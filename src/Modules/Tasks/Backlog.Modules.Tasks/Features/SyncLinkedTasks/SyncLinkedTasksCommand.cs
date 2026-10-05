using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Tasks.Features.SyncLinkedTasks;

/// <summary>
/// Brings one connected target's items in as linked tasks: creates the task an
/// item has not become yet, updates the fields the source owns on the ones it
/// has, and archives the task of an item that is gone.
/// <para>
/// The title and the assignee follow the source on every sync. The status follows
/// the source's <em>state moves</em>: it is walked to the map's answer when the
/// normalised state differs from the one the last sync recorded, and otherwise left
/// where the person put it (ADR 0020 §4, as amended at implementation).
/// </para>
/// <para>
/// <c>.devbook/arc42/adr/0020-external-items-arrive-as-linked-tasks.md</c> is the
/// design, §2 to §6 and §9. The handler names no connector: it finds the one whose
/// descriptor carries <see cref="ConnectorId"/> among those the host registered.
/// </para>
/// </summary>
public sealed record SyncLinkedTasksCommand(string ConnectorId, string Target);

/// <summary>What one sync did, counted per task.</summary>
/// <param name="Created">Items that became a task.</param>
/// <param name="Updated">Tasks a source field, the status or a flag changed on.</param>
/// <param name="Unchanged">Tasks the sync read and left alone.</param>
/// <param name="SkippedTombstoned">Items whose task the person deleted; never made
/// again.</param>
/// <param name="SkippedUntouched">Items older than the target's first-sync cut-off,
/// with no task yet.</param>
/// <param name="Vanished">Tasks archived because their item was gone.</param>
public sealed record LinkedTaskSyncSummary(
    int Created,
    int Updated,
    int Unchanged,
    int SkippedTombstoned,
    int SkippedUntouched,
    int Vanished);

public sealed class SyncLinkedTasksCommandHandler(
    IEnumerable<ITaskConnector> connectors,
    ITaskRepository tasks,
    IConnectedTargets targets,
    TimeProvider time,
    ILogger<SyncLinkedTasksCommandHandler>? log = null)
    : ICommandHandler<SyncLinkedTasksCommand, Result<LinkedTaskSyncSummary>>
{
    public static readonly Error ConnectorNotFound = Error.NotFound(
        "linked_tasks.connector_not_found",
        "That source is not available in this app.");

    public static readonly Error TargetNotFound = Error.NotFound(
        "linked_tasks.target_not_found",
        "That repository or product is not connected.");

    public static readonly Error TargetDisabled = Error.Validation(
        "linked_tasks.target_disabled",
        "Syncing is switched off for that repository or product.");

    private readonly ILogger _log = log ?? NullLogger<SyncLinkedTasksCommandHandler>.Instance;

    public async Task<Result<LinkedTaskSyncSummary>> Handle(
        SyncLinkedTasksCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var connector = connectors.FirstOrDefault(candidate =>
            string.Equals(candidate.Descriptor.Id, command.ConnectorId, StringComparison.Ordinal));
        if (connector is null) return ConnectorNotFound;

        var target = targets.Get(command.ConnectorId, command.Target);
        if (target is null) return TargetNotFound;
        if (!target.Enabled) return TargetDisabled;

        // Read before the fetch, so the next fetch's "closed since" overlaps this
        // one rather than leaving a gap for an item that closed while it ran.
        var fetchStartedAt = time.GetUtcNow();

        // Fixed at the first sync and kept: a window that slid with every run would
        // drop an item the day it turned old, which is not what the setting asks.
        // Derived at the first sync only — a target that has synced before has no
        // first sync left, so an age set afterwards skips nothing.
        var cutoff = target.IgnoreUntouchedBefore
            ?? (target.LastSyncedAt is null && target.SkipUntouchedOlderThan is { } age ? fetchStartedAt - age : null);

        IReadOnlyList<SourceItem> items;
        try
        {
            items = await connector.FetchAsync(target.Target, target.LastSyncedAt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing is written on a failed fetch, and nothing is archived as
            // vanished: an answer from a source that could not be reached is not an
            // empty source.
            _log.LogWarning(ex, "Fetching {Connector} items for {Target} failed.", connector.Descriptor.Id, target.Target);
            return Error.Unexpected(
                "linked_tasks.fetch_failed",
                $"{connector.Descriptor.DisplayName} could not be reached for {target.Target}.");
        }

        var connectorId = connector.Descriptor.Id;
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var seen = new HashSet<Guid>();
        int created = 0, updated = 0, unchanged = 0, tombstoned = 0, untouched = 0, vanished = 0;

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ExternalId)) continue;

            var id = LinkedTaskIds.For(connectorId, item.ExternalId);
            if (!seen.Add(id)) continue;

            var existing = await tasks.GetIncludingDeletedAsync(id, cancellationToken);

            // A linked task the person deleted stays deleted. The tombstone is what
            // says so, and the sync never makes the id again.
            if (existing?.DeletedAt is not null)
            {
                tombstoned++;
                continue;
            }

            if (existing is null)
            {
                if (cutoff is { } before && item.UpdatedAt < before)
                {
                    untouched++;
                    continue;
                }

                await tasks.SaveAsync(Create(id, connectorId, target, item, fetchStartedAt, today), cancellationToken);
                created++;
                continue;
            }

            // A quiet sync stays quiet: a task nothing changed on is not saved, so its
            // UpdatedAt does not move and an idle machine never wins last-write-wins
            // over a real edit made elsewhere (ADR 0020, §5).
            if (Update(existing, connectorId, target, item, today))
            {
                await tasks.SaveAsync(existing, cancellationToken);
                updated++;
            }
            else
            {
                unchanged++;
            }
        }

        // The fetch is every open item plus what closed since the last sync, so it
        // can only say "gone" about an item it would otherwise have returned. A first
        // sync — no last sync — answers the open items only, and a task that reached
        // this device through the replica with its item closed meanwhile is simply
        // not in it; nothing is archived on that answer.
        var vanishable = target.LastSyncedAt is null
            ? []
            : await tasks.ListAsync(cancellationToken);

        foreach (var task in vanishable)
        {
            if (!HasVanished(task, connectorId, target, seen)) continue;

            WalkTo(task, EntryStatus.Archived, today);
            task.SetSourceRef(task.SourceRef!.WithFlag(LinkedTaskFlags.Vanished, set: true));
            await tasks.SaveAsync(task, cancellationToken);
            vanished++;
        }

        RecordSync(target, fetchStartedAt, cutoff);

        return new LinkedTaskSyncSummary(created, updated, unchanged, tombstoned, untouched, vanished);
    }

    // --- The status map -----------------------------------------------------

    /// <summary>The one map from a normalised state to a task status, the same for
    /// every connector (ADR 0020, §4).</summary>
    internal static EntryStatus StatusFor(NormalisedSourceState state) => state switch
    {
        NormalisedSourceState.Open => EntryStatus.Ready,
        NormalisedSourceState.Active => EntryStatus.InProgress,
        NormalisedSourceState.Done => EntryStatus.Done,
        NormalisedSourceState.Dropped => EntryStatus.Archived,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Not a normalised source state."),
    };

    /// <summary>
    /// The shortest way from one status to another along the lifecycle graph's
    /// edges, excluding the start: Ready to Done is In progress then Done, because
    /// the graph has no edge from Ready to Done and the sync never sets a status
    /// past it. Empty when the two are the same.
    /// <para>
    /// A breadth-first search over <see cref="EntryStatusFlow.NextFrom"/> rather than
    /// a table of paths, so the walk follows the graph if the graph ever changes.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<EntryStatus> PathBetween(EntryStatus from, EntryStatus to)
    {
        if (from == to) return [];

        var cameFrom = new Dictionary<EntryStatus, EntryStatus>();
        var queue = new Queue<EntryStatus>([from]);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in EntryStatusFlow.NextFrom(current))
            {
                if (next == from || !cameFrom.TryAdd(next, current)) continue;
                if (next == to) return Unwind(cameFrom, from, to);
                queue.Enqueue(next);
            }
        }

        throw new InvalidOperationException($"The lifecycle has no path from {from} to {to}.");

        static IReadOnlyList<EntryStatus> Unwind(Dictionary<EntryStatus, EntryStatus> cameFrom, EntryStatus from, EntryStatus to)
        {
            var path = new List<EntryStatus>();
            for (var step = to; step != from; step = cameFrom[step]) path.Add(step);
            path.Reverse();
            return path;
        }
    }

    /// <summary>
    /// Moves the task to <paramref name="target"/> one permitted edge at a time, and
    /// keeps its two day stamps true to where it ends up rather than to the edges it
    /// crossed. Answers whether it moved.
    /// <para>
    /// <b>The start.</b> <see cref="TaskItem.ChangeStatus"/> stamps
    /// <see cref="TaskItem.StartedOn"/> the first time a task enters In progress, and
    /// a walk from Ready to Done crosses In progress only because the graph has no
    /// shorter edge. Nobody started that work today, so a start stamped in transit is
    /// taken back; it stays when In progress is where the walk lands.
    /// </para>
    /// <para>
    /// <b>The tick.</b> An item the source finished is work that is over, which is what
    /// the tick says in the app: ticking an entry off moves it to Done. So a walk that
    /// lands on Done ticks the task off today unless it already was, and a walk that
    /// takes a task out of Done or Archived back into open work unticks it, so a
    /// reopened item does not sit under Completed. Sub-items are not cascaded:
    /// <see cref="TaskItem.ChangeStatus"/> does not cascade them either.
    /// </para>
    /// </summary>
    private static bool WalkTo(TaskItem task, EntryStatus target, DateOnly today)
    {
        var from = task.Status;
        if (from == target) return false;

        var startedBefore = task.StartedOn;
        foreach (var step in PathBetween(from, target)) task.ChangeStatus(step, today);

        if (target != EntryStatus.InProgress && task.StartedOn != startedBefore) task.SetStartedOn(startedBefore);

        if (target == EntryStatus.Done && task.CompletedOn is null)
        {
            task.SetCompletedOn(today);
        }
        else if (from is EntryStatus.Done or EntryStatus.Archived
            && target is EntryStatus.Draft or EntryStatus.Ready or EntryStatus.InProgress
            && task.CompletedOn is not null)
        {
            task.SetCompletedOn(null);
        }

        return true;
    }

    // --- Creating and updating ------------------------------------------------

    private static TaskItem Create(
        Guid id,
        string connectorId,
        ConnectedTarget target,
        SourceItem item,
        DateTimeOffset createdAt,
        DateOnly today)
    {
        var (tags, multiplePlanTags) = TagsFrom(item.Labels ?? []);

        var task = new TaskItem(
            id,
            TitleOf(item),
            // Stored as prose: the source's text is not this grammar, and a heading,
            // checklist line or open fence in it would otherwise become the entry's
            // structure — see EntryTextParser.AsProse.
            EntryTextParser.AsProse(item.Body),
            EntryType.Task,
            EntryStatus.Draft,
            Priority.Medium,
            repoIds: null,
            tags,
            sourceInboxId: null,
            createdAt);

        // Copied once, here, and never again: the person owns the body, the tags,
        // the effort and the due date from now on (ADR 0020, §4).
        if (item.Effort is >= 0) task.SetEffort(item.Effort);
        if (item.DueOn is { } dueOn) task.SetDueOn(dueOn);

        WalkTo(task, StatusFor(item.State), today);

        var flags = multiplePlanTags ? new[] { LinkedTaskFlags.MultiplePlanTags } : [];
        task.SetSourceRef(ReferenceTo(connectorId, target.Target, item, flags));

        return task;
    }

    /// <summary>
    /// Writes what the source owns onto a task the item already became: the title,
    /// the reference with its assignee and state, and the status — the status only
    /// when the source's state moved (ADR 0020 §4, as amended at implementation).
    /// <para>
    /// The title follows the source only while the person has not renamed the task
    /// (ADR 0020 §9): it is overwritten when the target's title follows the source and
    /// the task's title still equals the source title the last sync recorded. A
    /// title that differs was renamed here and stops following; the reference still
    /// records the source's new title. A reference that recorded none — an older one —
    /// follows, once.
    /// </para>
    /// <para>
    /// The source owns the state, not the status a person gives the task between
    /// two of its moves. So a task the person started, or archived, while the source
    /// still says Open keeps that status on every sync until the source's normalised
    /// state changes; then the status is walked to the map's answer. "Moved" means
    /// the state differs from the one the held reference recorded, or the reference
    /// recorded none — an older one, walked once and recorded — or the task carries
    /// <see cref="LinkedTaskFlags.Vanished"/>, because the sync archived it and its
    /// item's return is a move however the state reads.
    /// </para>
    /// <para>
    /// Each mutator is called only when its value differs, so a task the source
    /// said nothing new about comes out of here untouched. Answers whether anything
    /// changed.
    /// </para>
    /// </summary>
    private static bool Update(TaskItem task, string connectorId, ConnectedTarget target, SourceItem item, DateOnly today)
    {
        var changed = false;
        var held = task.SourceRef;

        var title = TitleOf(item);
        var renamedLocally = held?.SourceTitle is { } lastSourceTitle
            && !string.Equals(task.Title, lastSourceTitle, StringComparison.Ordinal);
        if (target.TitleFollowsSource && !renamedLocally && !string.Equals(task.Title, title, StringComparison.Ordinal))
        {
            task.Rename(title);
            changed = true;
        }

        var wasVanished = held?.HasFlag(LinkedTaskFlags.Vanished) == true;
        var wasDoneLocally = held?.HasFlag(LinkedTaskFlags.DoneLocally) == true;
        var sourceOpen = item.State is NormalisedSourceState.Open or NormalisedSourceState.Active;
        var stateMoved = held?.NormalisedState is not { } last || last != item.State || wasVanished;

        if (stateMoved)
        {
            // A local Done is never reopened. The person's finish is information the
            // source does not have yet, so the task stays Done — and a task that
            // vanished while it was in that state comes back to it. A Done the source
            // itself brought (the last sync recorded Done) is not local: the source
            // reopening the item reopens the task.
            var localDone = task.Status == EntryStatus.Done && held?.NormalisedState != NormalisedSourceState.Done;
            var keepDone = sourceOpen && (localDone || (wasVanished && wasDoneLocally));
            var status = keepDone ? EntryStatus.Done : StatusFor(item.State);

            // Archived is past Done on the closing side: a task the person archived
            // is not walked back out to Done when its item closes. A vanished task is
            // the exception, because the sync archived it, not the person.
            if (status == EntryStatus.Done && task.Status == EntryStatus.Archived && !wasVanished) status = EntryStatus.Archived;

            changed |= WalkTo(task, status, today);
        }

        // Decided from where the task is now on every sync, moved or not, so the
        // mismatch is flagged the sync after the person finishes a task the source
        // still holds open, and cleared once either side catches up.
        var doneLocally = sourceOpen && task.Status == EntryStatus.Done;

        // Every flag the task carried survives except the two this sync decides
        // afresh — including one a newer build set that this one has no name for.
        var flags = (held?.Flags ?? [])
            .Where(flag => flag is not (LinkedTaskFlags.Vanished or LinkedTaskFlags.DoneLocally))
            .ToList();
        if (doneLocally) flags.Add(LinkedTaskFlags.DoneLocally);

        // The spelling the task was linked under is kept when the settings name the
        // same target in another case: it is the same target (ConnectedTarget.Is
        // compares it that way), and rewriting the spelling would restamp every
        // task for no change.
        var targetSpelling = held is not null && string.Equals(held.Target, target.Target, StringComparison.OrdinalIgnoreCase)
            ? held.Target
            : target.Target;
        var reference = ReferenceTo(connectorId, targetSpelling, item, flags);

        if (!Equals(reference, held))
        {
            task.SetSourceRef(reference);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Whether a live task is one this target's fetch would have returned had its
    /// item still been there, and is open enough that archiving it means something.
    /// <para>
    /// The fetch is every open item plus what closed since the last sync, so only an
    /// item the last sync saw <em>open</em> is one the fetch must return. The held
    /// normalised state is what says so: Open or Active, and the task is vanished if
    /// it is still open work here or carries <see cref="LinkedTaskFlags.DoneLocally"/>.
    /// Done or Dropped, and its absence is the fetch leaving out an item that closed
    /// earlier — never vanishing, whatever the person did to the task since. A
    /// reference too old to record a state is vanished only while the task itself is
    /// open work.
    /// </para>
    /// </summary>
    private static bool HasVanished(TaskItem task, string connectorId, ConnectedTarget target, HashSet<Guid> seen)
    {
        if (task.SourceRef is not { } source) return false;
        if (!string.Equals(source.ConnectorId, connectorId, StringComparison.Ordinal)) return false;
        if (!string.Equals(source.Target, target.Target, StringComparison.OrdinalIgnoreCase)) return false;
        if (seen.Contains(task.Id) || source.HasFlag(LinkedTaskFlags.Vanished)) return false;

        var openWork = task.Status is EntryStatus.Draft or EntryStatus.Ready or EntryStatus.InProgress;

        return source.NormalisedState switch
        {
            NormalisedSourceState.Open or NormalisedSourceState.Active => openWork || source.HasFlag(LinkedTaskFlags.DoneLocally),
            null => openWork,
            _ => false,
        };
    }

    private static SourceRef ReferenceTo(string connectorId, string target, SourceItem item, IEnumerable<string> flags) =>
        new(
            connectorId,
            target,
            item.ExternalId,
            item.Url,
            item.DisplayKey,
            item.Assignee,
            item.SourceStateName,
            item.UpdatedAt,
            flags,
            item.State,
            TitleOf(item));

    /// <summary>The title a task is given. An item with none is named by its key, so
    /// it still has a row a person can read.</summary>
    private static string TitleOf(SourceItem item) =>
        !string.IsNullOrWhiteSpace(item.Title) ? item.Title.Trim()
        : !string.IsNullOrWhiteSpace(item.DisplayKey) ? item.DisplayKey.Trim()
        : item.ExternalId;

    // --- Labels ---------------------------------------------------------------

    /// <summary>
    /// Labels as tags, by Backlog's own sigil rule (ADR 0020, §6): a label starting
    /// with <c>+</c> is the plan tag of that name, stored <c>+slug</c>; any other is
    /// a general tag, stored bare. Only the first plan label becomes the plan tag, so
    /// no task counts in two plans; answers whether there were more.
    /// </summary>
    internal static (IReadOnlyList<string> Tags, bool MultiplePlanTags) TagsFrom(IEnumerable<string> labels)
    {
        var tags = new List<string>();
        var plans = new HashSet<string>(StringComparer.Ordinal);

        foreach (var label in labels)
        {
            var trimmed = (label ?? string.Empty).Trim();
            var isPlan = trimmed.StartsWith('+');
            if (Slug(isPlan ? trimmed[1..] : trimmed) is not { } slug) continue;

            if (isPlan)
            {
                if (plans.Add(slug) && plans.Count == 1) tags.Add("+" + slug);
            }
            else if (!tags.Contains(slug, StringComparer.Ordinal))
            {
                tags.Add(slug);
            }
        }

        return (tags, plans.Count > 1);
    }

    /// <summary>
    /// A label's name as a tag: lower case, each run of spaces a hyphen, anything a
    /// tag cannot hold dropped. Null when nothing tag-shaped is left — the tag
    /// grammar wants a letter first, and a tag the next save would discard is
    /// better not written.
    /// </summary>
    internal static string? Slug(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        var pendingHyphen = false;

        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(character) || character == '-')
            {
                pendingHyphen = builder.Length > 0;
                continue;
            }

            if (!char.IsLetterOrDigit(character) && character != '_') continue;

            if (pendingHyphen) builder.Append('-');
            pendingHyphen = false;
            builder.Append(character);
        }

        return builder.Length > 0 && char.IsAsciiLetter(builder[0]) ? builder.ToString() : null;
    }

    // --- Bookkeeping ----------------------------------------------------------

    /// <summary>Records where this sync got to through
    /// <see cref="IConnectedTargets.Update"/>, which applies the two bookkeeping
    /// values to whatever the store holds at that moment, under its own lock — so a
    /// setting the person changed during the fetch, or while this ran, is never
    /// written back over, and a target removed meanwhile stays removed.</summary>
    private void RecordSync(ConnectedTarget target, DateTimeOffset fetchStartedAt, DateTimeOffset? cutoff)
    {
        var error = targets.Update(target.ConnectorId, target.Target, latest => latest with
        {
            LastSyncedAt = fetchStartedAt,
            IgnoreUntouchedBefore = latest.IgnoreUntouchedBefore ?? cutoff,
        });

        // The tasks are written either way. A sync whose bookkeeping did not stick
        // fetches more next time than it had to, which costs time and loses nothing.
        if (error is not null)
        {
            _log.LogWarning("The sync of {Connector} {Target} could not record its progress: {Error}", target.ConnectorId, target.Target, error);
        }
    }
}
