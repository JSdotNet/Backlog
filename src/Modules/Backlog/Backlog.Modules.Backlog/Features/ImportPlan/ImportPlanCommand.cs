using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.Abstractions.Services;
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

    public async Task<Result<ImportPlanResultDto>> Handle(
        ImportPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // One memo for the whole run, keyed on the name exactly as the plan wrote
        // it. A plan that names the same repository in ten entries is one
        // question about one repository, and asking the registry ten times is
        // how an unrecognized name would get offered for registration ten times.
        var resolvedRepos = new Dictionary<string, string>(StringComparer.Ordinal);

        var parsedEntries = EntryTextParser.SplitSegments(command.RawText)
            .Select(EntryTextParser.Parse)
            .Where(parsed => !string.IsNullOrWhiteSpace(parsed.Title))
            .Select(parsed => ApplyDefaultRepo(parsed, command.DefaultRepo))
            .Select(parsed => ResolveRepos(parsed, command.RepoMatches, resolvedRepos))
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

    /// <summary>
    /// Turns the repository names an entry wrote into the aliases the workspace
    /// actually knows, registering any it does not.
    /// <para>
    /// Runs over every entry, not only the ones the dialog flagged. A name that
    /// already matches a configured repository costs one lookup and changes
    /// nothing, which is what keeps the ordinary single-repository import exactly
    /// as fast as it was; the interesting cases are only ever the leftovers.
    /// </para>
    /// <para>
    /// Two names can resolve to one repository — "Widgets" matched to
    /// <c>widgets</c> beside a literal <c>widgets</c> — so the result is
    /// de-duplicated. The parser already guarantees an entry names each
    /// repository once, and resolution should not be the step that breaks it.
    /// </para>
    /// </summary>
    private EntryTextParser.ParsedEntry ResolveRepos(
        EntryTextParser.ParsedEntry parsed,
        IReadOnlyDictionary<string, string>? matches,
        Dictionary<string, string> resolved)
    {
        if ((parsed.RepoIds?.Count ?? 0) == 0) return parsed;

        return parsed with
        {
            RepoIds =
            [
                .. parsed.RepoIds!
                    .Select(name => ResolveRepo(name, matches, resolved))
                    .Distinct(StringComparer.Ordinal)
            ]
        };
    }

    /// <summary>One name, resolved once per run. The reader's own answer from the
    /// dialog first, then the registry, and only a name neither knows is
    /// registered — the leniency ADR 0004 grants Import and nothing else.</summary>
    private string ResolveRepo(
        string name,
        IReadOnlyDictionary<string, string>? matches,
        Dictionary<string, string> resolved)
    {
        if (resolved.TryGetValue(name, out var already)) return already;

        var alias = matches is not null
            && matches.TryGetValue(name, out var matched)
            && !string.IsNullOrWhiteSpace(matched)
                ? matched
                : (repositories.Resolve(name) ?? repositories.Register(name)).Alias;

        resolved[name] = alias;
        return alias;
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
