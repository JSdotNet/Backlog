using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.ImportPlan;

/// <summary>
/// Brings in a plan and turns it into backlog entries in one step.
/// <para>
/// Per ADR 0007 a plan is not a file format of its own — it is ordinary entry
/// text with more than one top-level heading. This handler does exactly what a
/// hand-typed multi-entry paste does (<see cref="EntryTextParser.SplitSegments"/>,
/// <see cref="EntryTextParser.Parse"/>) and adds only what a single paste cannot
/// already offer: resolving <c>after:</c> against a same-document <c>id:</c>
/// before any entry the document describes has a real id
/// (`.design/content-editing.md#scheduling-and-dependency-tokens`), and
/// replacing the previous version of the plan rather than adding to it: every
/// entry of this <c>import_plan_id</c> that nobody has started yet is cleared
/// before the new ones are written, so a plan brought in twice cannot leave two
/// copies of a prompt behind. Work already picked up, finished or archived is
/// matched by <c>(import_plan_id, import_item_id)</c> and kept.
/// </para>
/// </summary>
/// <param name="RawText">The plan's raw text, pasted or read from an uploaded
/// file.</param>
/// <param name="DefaultRepo">The Import dialog's optional "Target repository"
/// field. Applied as an entry's <c>repo:</c> value only when that entry's own
/// text names none — the per-entry token stays the power-user override this
/// never touches. See ADR 0007's `repo:` resolution.</param>
/// <param name="RepoMatches">What the reader said in the Import dialog about the
/// repository names the plan actually mentions: the name as the plan wrote it,
/// mapped to the alias of the known repository they meant. Only the names they
/// matched appear here — anything they left alone is resolved, and if need be
/// registered, the ordinary way. A person having looked at a name is the
/// strongest signal there is about what it means, which is why this is consulted
/// before the registry rather than after it.</param>
public sealed record ImportPlanCommand(
    string RawText,
    string? DefaultRepo = null,
    IReadOnlyDictionary<string, string>? RepoMatches = null);

public sealed class ImportPlanCommandHandler(ITaskRepository entries, IRepositoryDirectory repositories)
    : ICommandHandler<ImportPlanCommand, Result<ImportPlanResultDto>>
{
    /// <summary>Nothing in the pasted or uploaded text parsed to an entry with a
    /// title. Not a parse failure — an entry with no title is an ordinary
    /// half-typed state elsewhere in this module — but a plan that produces
    /// nothing is not something Import can act on.</summary>
    public static readonly Error EmptyPlan = Error.Validation(
        "import.empty_plan",
        "Nothing in that text parsed to an entry with a title.");

    /// <summary>Two entries in the document claim the same <c>id:</c>. ADR 0007
    /// reads an <c>id:</c> as the one name a prompt goes by inside its plan, and
    /// every use of it here depends on that: an <c>after:</c> naming a doubled id
    /// has two answers, and on a re-import both segments match the one stored
    /// entry and write over each other while the counts report two. Refused whole
    /// rather than resolved by a rule nobody wrote down — the person can see which
    /// id it was and say which prompt owns it.</summary>
    public static Error DuplicateItemId(string importItemId) => Error.Validation(
        "import.duplicate_item_id",
        $"Two entries both claim `id:{importItemId}` — an id names one prompt in a plan.");

    public async Task<Result<ImportPlanResultDto>> Handle(
        ImportPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var parsedEntries = EntryTextParser.SplitSegments(command.RawText)
            .Select(EntryTextParser.Parse)
            .Where(parsed => !string.IsNullOrWhiteSpace(parsed.Title))
            .Select(parsed => ApplyDefaultRepo(parsed, command.DefaultRepo))
            .ToList();

        // Both refusals come before repository resolution, because resolution
        // registers: a name the registry has never seen is added to it, which is a
        // write to the workspace exactly as creating an entry is. A plan Import
        // will not act on must not leave one behind for somebody to go and delete,
        // so nothing about it is resolved until it is known to be a plan at all.
        if (parsedEntries.Count == 0) return EmptyPlan;
        if (FirstDuplicateItemId(parsedEntries) is { } duplicate) return DuplicateItemId(duplicate);

        // One resolver for the whole run, because the memo it holds is a
        // within-run answer: a plan that names the same repository in ten entries
        // is one question about one repository, and asking the registry ten times
        // is how an unrecognized name would get offered for registration ten
        // times.
        var resolver = new RepositoryIdResolver(repositories);
        parsedEntries = [.. parsedEntries.Select(parsed => ResolveRepos(parsed, resolver, command.RepoMatches))];

        var planId = SharedTag(parsedEntries);
        var existing = await entries.ListAsync(cancellationToken);

        var cleared = await ClearNotStartedAsync(planId, existing, cancellationToken);
        if (cleared.Count > 0) existing = [.. existing.Except(cleared)];

        // Which of the cleared entries this version can still be recognized as
        // having rewritten, rather than simply dropped. Read before pass 1 so
        // the counts can tell a replacement from a create — ImportPlanResultDto
        // says why that distinction is worth keeping.
        var clearedItemIds = cleared
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ImportItemId))
            .Select(entry => entry.ImportItemId!)
            .ToHashSet(StringComparer.Ordinal);

        // Pass 1: resolve each parsed entry's identity against what is left
        // standing, before anything about the batch is written. None of the
        // entries has a real id yet — see ADR 0007 — so this is the only place
        // that link can be made.
        var outcomes = new List<Outcome>(parsedEntries.Count);
        var nextOrder = existing.Count;

        foreach (var parsed in parsedEntries)
        {
            TaskItem? match = null;
            if (planId is not null && !string.IsNullOrWhiteSpace(parsed.ImportItemId))
            {
                match = existing.FirstOrDefault(e =>
                    string.Equals(e.ImportPlanId, planId, StringComparison.Ordinal)
                    && string.Equals(e.ImportItemId, parsed.ImportItemId, StringComparison.Ordinal));
            }

            if (match is null)
            {
                // The prompt as this version of the plan writes it: either new,
                // or written again in place of the copy just cleared.
                outcomes.Add(Outcome.ForCreate(parsed, CreateEntry(parsed, nextOrder++)));
            }
            else if (match.Status is EntryStatus.Done or EntryStatus.Archived)
            {
                // A later plan version does not reopen finished work.
                outcomes.Add(Outcome.ForSkip(parsed, match));
            }
            else
            {
                // Work already under way — the one thing a match can be, now
                // that the entries nobody had started are gone. Brought up to
                // date in place rather than replaced out from under whoever
                // picked it up.
                outcomes.Add(Outcome.ForUpdate(parsed, match));
            }
        }

        // A dependency on an entry that this run skips or updates is still a
        // real dependency — the local id it names already resolved to a real
        // entry, one this run simply leaves untouched.
        var localIds = outcomes
            .Where(outcome => !string.IsNullOrWhiteSpace(outcome.Parsed.ImportItemId))
            .ToDictionary(outcome => outcome.Parsed.ImportItemId!, outcome => outcome.RealId, StringComparer.Ordinal);

        var created = 0;
        var replaced = 0;
        var updated = 0;
        var skipped = 0;
        var resultEntries = new List<TaskItemDto>();

        // Pass 2: resolve depends_on against the map just built, then persist.
        foreach (var outcome in outcomes)
        {
            if (outcome.Kind is OutcomeKind.Skip)
            {
                skipped++;
                continue;
            }

            // A value found in the map was a same-document local id and is
            // rewritten to the real id it resolved to; anything else is treated
            // as a real, already-existing backlog_item_id, unchanged from
            // ordinary `after:` behaviour.
            var resolvedDependsOn = (outcome.Parsed.DependsOn ?? [])
                .Select(id => localIds.TryGetValue(id, out var real) ? real.ToString() : id)
                .ToList();

            var entry = outcome.Entry;

            if (outcome.Kind is OutcomeKind.Create)
            {
                entry.SetDependsOn(resolvedDependsOn);
                entry.SetImportPlanId(planId);

                await entries.SaveAsync(entry, cancellationToken);
                resultEntries.Add(entry.ToDto());

                if (outcome.Parsed.ImportItemId is { } itemId && clearedItemIds.Contains(itemId)) replaced++;
                else created++;
            }
            else
            {
                TaskEntryFields.ApplyToExisting(entry, outcome.Parsed with { DependsOn = resolvedDependsOn });
                if (outcome.Parsed.Status is { } status) entry.SetStatus(status);

                await entries.SaveAsync(entry, cancellationToken);
                resultEntries.Add(entry.ToDto());
                updated++;
            }
        }

        // What was cleared and not written again: the prompts this version of
        // the plan has stopped asking for.
        return new ImportPlanResultDto(created, replaced, updated, skipped, cleared.Count - replaced, resultEntries);
    }

    /// <summary>
    /// Clears the previous version of this plan: every stored entry carrying the
    /// plan's id that is still waiting to be picked up — <see cref="EntryStatus.Draft"/>
    /// or <see cref="EntryStatus.Ready"/> — is tombstoned before the version
    /// being brought in is written.
    /// <para>
    /// The alternative was matching each stored entry to a parsed one and
    /// editing it in place, which is what this handler used to do. It cannot
    /// hold: an entry is only recognizable by its <c>id:</c>, a plan is free not
    /// to write one, and an entry with no id matches nothing and so arrives
    /// again on every import. Clearing first makes a duplicate impossible
    /// whatever the plan wrote, and says what a re-import means — this is the
    /// plan now, and the work nobody has started is whatever the latest version
    /// says it is.
    /// </para>
    /// <para>
    /// Bounded by <c>import_plan_id</c>, which only Import ever writes, so a
    /// hand-typed entry that happens to carry the plan's tag is not in scope. A
    /// plan with no shared tag has no identity to have a previous version of
    /// (see <see cref="SharedTag"/>) and clears nothing.
    /// </para>
    /// <para>
    /// Tombstoning rather than deleting the row, because that is what deletion
    /// is in this module — see <c>ITaskRepository</c> and ADR 0005: a row simply
    /// dropped here would come back on the next sync from another device.
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<TaskItem>> ClearNotStartedAsync(
        string? planId,
        IReadOnlyList<TaskItem> existing,
        CancellationToken cancellationToken)
    {
        if (planId is null) return [];

        var superseded = existing
            .Where(entry =>
                string.Equals(entry.ImportPlanId, planId, StringComparison.Ordinal)
                && entry.Status is EntryStatus.Draft or EntryStatus.Ready)
            .ToList();

        foreach (var entry in superseded)
        {
            entry.MarkDeleted();
            await entries.SaveAsync(entry, cancellationToken);
        }

        return superseded;
    }

    /// <summary>Constructs a new entry from a parsed segment the way every
    /// text-save does, then applies the one default that is Import's own: an
    /// entry whose metadata line says nothing about its readiness arrives at
    /// <see cref="EntryStatus.Ready"/>.
    /// <para>
    /// A hand-typed entry is born at Draft because it is being shaped as it is
    /// typed. A plan is the opposite case — a sequence of work already agreed
    /// and written down to be picked up — so leaving every entry at Draft only
    /// hands somebody a promotion per entry to click through before "What's
    /// next" shows any of it. An explicit <c>!draft</c> still means what it says;
    /// this fills a gap, it never overrides. Kept here rather than in
    /// <see cref="TaskEntryFields.CreateFrom"/> because that helper is shared
    /// with the hand-typed path, whose default stays Draft.
    /// </para></summary>
    private static TaskItem CreateEntry(EntryTextParser.ParsedEntry parsed, int order)
    {
        var entry = TaskEntryFields.CreateFrom(parsed, order);
        if (parsed.Status is null) entry.SetStatus(EntryStatus.Ready);
        return entry;
    }

    /// <summary>The first <c>id:</c> two entries in the document both claim, or
    /// null when every one of them is its own. Read before pass 1 rather than
    /// discovered by the dictionary that pass 2 needs, so a plan Import cannot act
    /// on is refused with nothing constructed and nothing saved.
    /// <para>
    /// Ordinal, matching how the same value is compared against a stored
    /// <c>import_item_id</c> further down: an id that would not match itself on a
    /// re-import is not the same id here either.
    /// </para></summary>
    private static string? FirstDuplicateItemId(IReadOnlyList<EntryTextParser.ParsedEntry> parsedEntries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parsed in parsedEntries)
        {
            if (string.IsNullOrWhiteSpace(parsed.ImportItemId)) continue;
            if (!seen.Add(parsed.ImportItemId)) return parsed.ImportItemId;
        }

        return null;
    }

    /// <summary>The one <c>#tag</c> every parsed entry has in common, or null
    /// when there is none. Per ADR 0007 this becomes <c>import_plan_id</c> —
    /// there is no separate plan-id field or wrapper document. An entry pasted
    /// without a shared tag still imports; it just cannot be matched by a later
    /// re-import.</summary>
    private static string? SharedTag(IReadOnlyList<EntryTextParser.ParsedEntry> parsedEntries)
    {
        IEnumerable<string>? shared = null;
        foreach (var parsed in parsedEntries)
        {
            shared = shared is null ? parsed.Tags : shared.Intersect(parsed.Tags, StringComparer.Ordinal);
        }

        return shared?.FirstOrDefault();
    }

    /// <summary>Applies the dialog's default repository to a parsed entry that
    /// named none of its own. The <c>repo:</c> token in the entry's own text is
    /// always the stronger signal — a plan mixing repositories still works —
    /// so this only fills a gap the parser left empty, it never overrides.</summary>
    private static EntryTextParser.ParsedEntry ApplyDefaultRepo(EntryTextParser.ParsedEntry parsed, string? defaultRepo)
    {
        if (string.IsNullOrWhiteSpace(defaultRepo) || (parsed.RepoIds?.Count ?? 0) > 0) return parsed;

        return parsed with { RepoIds = [defaultRepo] };
    }

    /// <summary>
    /// Turns the repository names an entry wrote into the ids the workspace
    /// actually stores, registering any the registry does not know.
    /// <para>
    /// Runs over every entry, not only the ones the dialog flagged. A name that
    /// already matches a configured repository costs one lookup and changes
    /// nothing, which is what keeps the ordinary single-repository import exactly
    /// as fast as it was; the interesting cases are only ever the leftovers.
    /// </para>
    /// <para>
    /// The rule itself lives in <see cref="RepositoryIdResolver"/>, shared with
    /// the ordinary text-save path so the two cannot disagree about what a
    /// <c>repo:</c> value means. What is left here is only Import's own concern:
    /// which entries to run it over, and that an entry naming none is left
    /// untouched rather than given an empty list.
    /// </para>
    /// </summary>
    private static EntryTextParser.ParsedEntry ResolveRepos(
        EntryTextParser.ParsedEntry parsed,
        RepositoryIdResolver resolver,
        IReadOnlyDictionary<string, string>? matches)
    {
        if ((parsed.RepoIds?.Count ?? 0) == 0) return parsed;

        return parsed with { RepoIds = resolver.ResolveOrRegister(parsed.RepoIds, matches) };
    }

    private enum OutcomeKind { Create, Update, Skip }

    /// <summary>What pass 1 decided about one parsed entry, and the real
    /// aggregate that decision resolved to — already loaded for an update or a
    /// skip, freshly constructed (with its real id already assigned) for a
    /// create.</summary>
    private sealed class Outcome
    {
        public required EntryTextParser.ParsedEntry Parsed { get; init; }
        public required OutcomeKind Kind { get; init; }
        public required TaskItem Entry { get; init; }

        public Guid RealId => Entry.Id;

        public static Outcome ForCreate(EntryTextParser.ParsedEntry parsed, TaskItem entry) =>
            new() { Parsed = parsed, Kind = OutcomeKind.Create, Entry = entry };

        public static Outcome ForUpdate(EntryTextParser.ParsedEntry parsed, TaskItem entry) =>
            new() { Parsed = parsed, Kind = OutcomeKind.Update, Entry = entry };

        public static Outcome ForSkip(EntryTextParser.ParsedEntry parsed, TaskItem entry) =>
            new() { Parsed = parsed, Kind = OutcomeKind.Skip, Entry = entry };
    }
}
