using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Modules.Backlog.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Backlog.Features.ImportPlan;

/// <summary>
/// Brings in a plan and turns it into backlog entries in one step.
/// <para>
/// Per ADR 0004 a plan is not a file format of its own — it is ordinary entry
/// text with more than one top-level heading. This handler does exactly what a
/// hand-typed multi-entry paste does (<see cref="EntryTextParser.SplitSegments"/>,
/// <see cref="EntryTextParser.Parse"/>) and adds only what a single paste cannot
/// already offer: resolving <c>after:</c> against a same-document <c>id:</c>
/// before any entry the document describes has a real id
/// (`.design/content-editing.md#scheduling-and-dependency-tokens`), and
/// upserting by <c>(import_plan_id, import_item_id)</c> so a later version of a
/// plan already brought in updates its entries instead of duplicating them.
/// </para>
/// </summary>
/// <param name="RawText">The plan's raw text, pasted or read from an uploaded
/// file.</param>
/// <param name="DefaultRepo">The Import dialog's optional "Target repository"
/// field. Applied as an entry's <c>repo:</c> value only when that entry's own
/// text names none — the per-entry token stays the power-user override this
/// never touches. See ADR 0004's `repo:` resolution.</param>
public sealed record ImportPlanCommand(string RawText, string? DefaultRepo = null);

public sealed class ImportPlanCommandHandler(ITaskRepository entries)
    : ICommandHandler<ImportPlanCommand, Result<ImportPlanResultDto>>
{
    /// <summary>Nothing in the pasted or uploaded text parsed to an entry with a
    /// title. Not a parse failure — an entry with no title is an ordinary
    /// half-typed state elsewhere in this module — but a plan that produces
    /// nothing is not something Import can act on.</summary>
    public static readonly Error EmptyPlan = Error.Validation(
        "import.empty_plan",
        "Nothing in that text parsed to an entry with a title.");

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

        if (parsedEntries.Count == 0) return EmptyPlan;

        var planId = SharedTag(parsedEntries);
        var existing = await entries.ListAsync(cancellationToken);

        // Pass 1: resolve each parsed entry's identity against what is already
        // stored, before anything about the batch is written. None of the
        // entries has a real id yet — see ADR 0004 — so this is the only place
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
                // Whichever version of the plan first introduced the prompt.
                outcomes.Add(Outcome.ForCreate(parsed, TaskEntryFields.CreateFrom(parsed, nextOrder++)));
            }
            else if (match.Status is EntryStatus.Done or EntryStatus.Archived)
            {
                // A later plan version does not reopen finished work.
                outcomes.Add(Outcome.ForSkip(parsed, match));
            }
            else
            {
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
                created++;
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

        return new ImportPlanResultDto(created, updated, skipped, resultEntries);
    }

    /// <summary>The one <c>#tag</c> every parsed entry has in common, or null
    /// when there is none. Per ADR 0004 this becomes <c>import_plan_id</c> —
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
