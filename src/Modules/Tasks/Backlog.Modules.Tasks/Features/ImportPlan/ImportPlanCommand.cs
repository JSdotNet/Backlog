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
/// <param name="SourceInboxId">The inbox item this plan was drafted from, or
/// null for a plan pasted or uploaded by hand. Stamped on the entries this run
/// <em>creates</em> and on nothing else: an entry already under way keeps the
/// provenance it was born with, because the aggregate holds the field as
/// constructor-only and a re-import is not a second birth. The entries it is
/// stamped on are born Draft whatever their text says — see
/// <c>CreateEntry</c> for why a model's plan about captured content gets no
/// Ready without a person's look.</param>
/// <param name="LayOutOnRoadmap">The Import dialog's "Lay out on the roadmap", off by
/// default: for a document with task entries and no <c>plan</c> entry, lay out one
/// roadmap item per plan tag its tasks carry where none exists yet (ADR 0013,
/// ruling 3). Off, a plan re-imported for its tasks grows no item nobody asked
/// for.</param>
public sealed record ImportPlanCommand(
    string RawText,
    string? DefaultRepo = null,
    IReadOnlyDictionary<string, string>? RepoMatches = null,
    string? SourceInboxId = null,
    bool LayOutOnRoadmap = false);

/// <param name="roadmap">Where <c>plan</c> entries cross to the roadmap. Optional
/// because a host may compose Tasks without Roadmap; a document with <c>plan</c>
/// entries then imports its tasks and reports that nothing was laid out.</param>
public sealed class ImportPlanCommandHandler(
    ITaskRepository entries,
    IRepositoryDirectory repositories,
    IRoadmapPlanIntake? roadmap = null)
    : ICommandHandler<ImportPlanCommand, Result<ImportPlanResultDto>>
{
    /// <summary>The refusal a host with no roadmap reports for a document that asked
    /// for one.</summary>
    public const string RoadmapUnavailable = "The roadmap is not available here, so nothing was laid out on it.";

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

        var parsedAll = EntryTextParser.SplitSegments(command.RawText)
            .Select(EntryTextParser.Parse)
            .Where(parsed => !string.IsNullOrWhiteSpace(parsed.Title))
            .ToList();

        // One document, two kinds (ADR 0013, ruling 3). Import is the one door a
        // `plan` entry may come through, and it never becomes a task: it is handed
        // to Roadmap through IRoadmapPlanIntake once the task entries are down.
        // Everything below about tasks — the shared tag, clear-then-write, the
        // two-pass after: — runs over the task entries alone.
        var planEntries = parsedAll.Where(parsed => parsed.Kind == EntryKind.Plan).ToList();
        var parsedEntries = parsedAll
            .Where(parsed => parsed.Kind != EntryKind.Plan)
            .Select(parsed => ApplyDefaultRepo(parsed, command.DefaultRepo))
            .ToList();

        // Both refusals come before repository resolution, because resolution
        // registers: a name the registry has never seen is added to it, which is a
        // write to the workspace exactly as creating an entry is. A plan Import
        // will not act on must not leave one behind for somebody to go and delete,
        // so nothing about it is resolved until it is known to be a plan at all.
        if (parsedAll.Count == 0) return EmptyPlan;
        if (FirstDuplicateItemId(parsedEntries) is { } duplicate) return DuplicateItemId(duplicate);

        // Each level resolves after: against its own ids only. A task naming a
        // `plan` entry would otherwise be stored as a dependency on an id no task
        // has; it is dropped and reported instead.
        var levels = LocalIds.Of(parsedEntries, planEntries);
        var unresolvedTaskDependencies = new List<ImportUnresolvedDependencyDto>();
        parsedEntries = [.. parsedEntries.Select(parsed => DropCrossLevel(parsed, levels, unresolvedTaskDependencies))];

        // One resolver for the whole run, because the memo it holds is a
        // within-run answer: a plan that names the same repository in ten entries
        // is one question about one repository, and asking the registry ten times
        // is how an unrecognized name would get offered for registration ten
        // times.
        var resolver = new RepositoryIdResolver(repositories);
        parsedEntries = [.. parsedEntries.Select(parsed => ResolveRepos(parsed, resolver, command.RepoMatches))];

        // Tasks first, then the roadmap, so placement reads the effort this import
        // just gathered rather than the previous version's.
        var tasks = parsedEntries.Count == 0
            ? TaskHalf.None
            : await ImportTasksAsync(parsedEntries, command.SourceInboxId, cancellationToken);

        var roadmap = await LayOutOnRoadmapAsync(planEntries, parsedEntries, levels, command, cancellationToken);

        return new ImportPlanResultDto(
            tasks.Created,
            tasks.Replaced,
            tasks.Updated,
            tasks.Skipped,
            tasks.Removed,
            tasks.Entries,
            roadmap,
            unresolvedTaskDependencies.Count == 0 ? null : unresolvedTaskDependencies);
    }

    /// <summary>
    /// The task half, exactly as ADR 0007 has it: the shared tag, clear-then-write,
    /// and the two-pass <c>after:</c>.
    /// </summary>
    private async Task<TaskHalf> ImportTasksAsync(
        List<EntryTextParser.ParsedEntry> parsedEntries,
        string? sourceInboxId,
        CancellationToken cancellationToken)
    {
        var sharedTag = SharedTag(parsedEntries);
        string? PlanIdOf(EntryTextParser.ParsedEntry parsed) => sharedTag ?? OwnPlanTag(parsed);

        var existing = await entries.ListAsync(cancellationToken);

        var cleared = new List<TaskItem>();
        foreach (var id in parsedEntries.Select(PlanIdOf).OfType<string>().Distinct(StringComparer.Ordinal))
        {
            cleared.AddRange(await ClearNotStartedAsync(id, existing, cancellationToken));
        }

        if (cleared.Count > 0) existing = [.. existing.Except(cleared)];

        // Which of the cleared entries this version can still be recognized as
        // having rewritten, rather than simply dropped. Read before pass 1 so
        // the counts can tell a replacement from a create — ImportPlanResultDto
        // says why that distinction is worth keeping. Keyed by plan as well, since
        // two plans in one document may each own a step of the same id.
        var clearedItemIds = cleared
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ImportItemId))
            .Select(entry => (entry.ImportPlanId, entry.ImportItemId!))
            .ToHashSet();

        // Pass 1: resolve each parsed entry's identity against what is left
        // standing, before anything about the batch is written. None of the
        // entries has a real id yet — see ADR 0007 — so this is the only place
        // that link can be made.
        var outcomes = new List<Outcome>(parsedEntries.Count);
        var nextOrder = existing.Count;

        foreach (var parsed in parsedEntries)
        {
            TaskItem? match = null;
            var planId = PlanIdOf(parsed);
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
                outcomes.Add(Outcome.ForCreate(parsed, CreateEntry(parsed, nextOrder++, sourceInboxId)));
            }
            else if (match.IsCompleted || match.Status is EntryStatus.Done or EntryStatus.Archived)
            {
                // A later plan version does not reopen finished work — neither
                // an entry the person has ticked off nor one whose status says the
                // work is over. Both, because they are two facts now: a Done
                // entry left unticked is still finished work, and an entry ticked
                // while still In progress is still off the person's list.
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

        // What a value the document itself does not name may still mean: a step
        // an earlier import of this plan already created. Read once for the run
        // — the store does not change under pass 2 — and over what is left
        // standing, so a step this version just cleared cannot be resolved to.
        var stored = existing
            .Select(entry => new DependencyResolution.Candidate(entry.Id, entry.ImportItemId, entry.ImportPlanId))
            .ToList();

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
            // rewritten to the real id it resolved to. Anything else is asked of
            // the store next — the `id:` of a step this plan brought in before
            // (see DependencyResolution for the order it is asked in) — and only
            // what neither knows is written through as a real, already-existing
            // backlog_item_id, unchanged from ordinary `after:` behaviour.
            var planId = PlanIdOf(outcome.Parsed);
            var resolvedDependsOn = DependencyResolution.ResolveAll(
                (outcome.Parsed.DependsOn ?? [])
                    .Select(id => localIds.TryGetValue(id, out var real) ? real.ToString() : id),
                stored,
                planId,
                outcome.Parsed.Tags);

            var entry = outcome.Entry;

            if (outcome.Kind is OutcomeKind.Create)
            {
                entry.SetDependsOn(resolvedDependsOn);
                entry.SetImportPlanId(planId);

                await entries.SaveAsync(entry, cancellationToken);
                resultEntries.Add(entry.ToDto());

                if (outcome.Parsed.ImportItemId is { } itemId && clearedItemIds.Contains((planId, itemId))) replaced++;
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
        return new TaskHalf(created, replaced, updated, skipped, cleared.Count - replaced, resultEntries);
    }

    /// <summary>
    /// The roadmap half (ADR 0013, ruling 3): the document's <c>plan</c> entries, or —
    /// with "Lay out on the roadmap" on and none written — one made-up entry per plan
    /// tag its tasks carry, handed across with the effort now gathered under every tag
    /// either kind names. A task-only import with plan tags crosses too, with no
    /// entries, so an item still placed by effort is re-lengthened (ruling 5).
    /// <para>
    /// Null when nothing crossed and there is nothing to say, so an ordinary task
    /// import reports exactly what it always did.
    /// </para>
    /// </summary>
    private async Task<RoadmapIntakeResultDto?> LayOutOnRoadmapAsync(
        List<EntryTextParser.ParsedEntry> planEntries,
        List<EntryTextParser.ParsedEntry> taskEntries,
        LocalIds levels,
        ImportPlanCommand command,
        CancellationToken cancellationToken)
    {
        var skipped = new List<string>();
        var effortIgnored = new List<string>();
        var unresolved = new List<ImportUnresolvedDependencyDto>();
        var entriesToLayOut = new List<RoadmapPlanEntryDto>();

        foreach (var plan in planEntries)
        {
            // Exactly one plan tag: it is the one thing a later task-level import
            // finds the item by, so none is guessed (ruling 2).
            var tags = PlanTags(plan.Tags);
            if (tags.Count != 1)
            {
                skipped.Add(plan.Title.Trim());
                continue;
            }

            if (plan.Effort is not null) effortIgnored.Add(plan.Title.Trim());

            var after = new List<string>();
            foreach (var value in plan.DependsOn ?? [])
            {
                if (levels.IsTaskOnly(value)) unresolved.Add(new ImportUnresolvedDependencyDto(plan.ImportItemId ?? tags[0], value));
                else after.Add(value);
            }

            entriesToLayOut.Add(new RoadmapPlanEntryDto(
                plan.Title.Trim(),
                tags[0],
                plan.ImportItemId,
                MatchRepos(plan.RepoIds, command.RepoMatches),
                plan.Priority,
                plan.DueOn,
                after,
                string.IsNullOrWhiteSpace(plan.Body) ? null : plan.Body));
        }

        var taskTags = taskEntries
            .SelectMany(entry => PlanTags(entry.Tags))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Only for a document that wrote no `plan` entry: one that did has said which
        // items it means, and a made-up one beside them would be a guess.
        var layOutIfMissing = command.LayOutOnRoadmap && planEntries.Count == 0
            ? [.. taskTags.Select(tag => Synthesise(tag, taskEntries))]
            : new List<RoadmapPlanEntryDto>();

        RoadmapIntakeResultDto laidOut;
        if (entriesToLayOut.Count == 0 && layOutIfMissing.Count == 0 && taskTags.Count == 0)
        {
            laidOut = RoadmapIntakeResultDto.Empty;
        }
        else if (roadmap is null)
        {
            // A host that composed no roadmap. The tasks are in; say what was not.
            laidOut = entriesToLayOut.Count > 0 || layOutIfMissing.Count > 0
                ? RoadmapIntakeResultDto.Refused(RoadmapUnavailable)
                : RoadmapIntakeResultDto.Empty;
        }
        else
        {
            var gathered = await GatherEffortAsync(entriesToLayOut.Select(entry => entry.Tag).Concat(taskTags), cancellationToken);
            laidOut = await roadmap.LayOutAsync(
                new RoadmapPlanIntakeRequestDto(entriesToLayOut, layOutIfMissing, gathered),
                cancellationToken);
        }

        var result = laidOut with
        {
            Skipped = [.. skipped, .. laidOut.Skipped],
            EffortIgnored = [.. effortIgnored, .. laidOut.EffortIgnored],
            UnresolvedDependencies = [.. unresolved, .. laidOut.UnresolvedDependencies]
        };

        return IsQuiet(result) ? null : result;
    }

    /// <summary>
    /// What the stored tasks register under each tag, read after this import wrote
    /// its own — the same gather the roadmap's rollup makes: by tag with the plan
    /// sigil lifted, ignoring case, finished work included, tombstones not.
    /// </summary>
    private async Task<List<RoadmapPlanEffortDto>> GatherEffortAsync(
        IEnumerable<string> tags,
        CancellationToken cancellationToken)
    {
        var stored = await entries.ListAsync(cancellationToken);

        return
        [
            .. tags
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(tag =>
                {
                    var slug = Bare(tag);
                    var under = stored
                        .Where(entry => entry.Tags.Any(carried => string.Equals(Bare(carried), slug, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    return new RoadmapPlanEffortDto(tag, under.Sum(entry => entry.Effort ?? 0), under.Count(entry => entry.Effort is null));
                })
        ];
    }

    /// <summary>The one entry "Lay out on the roadmap" makes up for a plan tag: titled
    /// from the tag, its repository scope the union of the <c>repo:</c> values of the
    /// document's tasks carrying it.</summary>
    private static RoadmapPlanEntryDto Synthesise(string tag, List<EntryTextParser.ParsedEntry> taskEntries)
    {
        var repos = taskEntries
            .Where(entry => entry.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .SelectMany(entry => entry.RepoIds ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RoadmapPlanEntryDto(TitleFromTag(tag), tag, null, repos, null, null, [], null);
    }

    /// <summary><c>+roadmap-imported-plans</c> reads as "Roadmap imported plans".</summary>
    internal static string TitleFromTag(string tag)
    {
        var words = Bare(tag).Replace('-', ' ').Replace('_', ' ').Trim();
        return words.Length == 0 ? tag : char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>A plan entry's <c>repo:</c> values as the reader matched them in the
    /// dialog, else as written. Never resolved against the registry and never
    /// registered: an item needs no <c>repo_id</c>, and an alias the registry does not
    /// hold is the roadmap's ordinary unresolved state (ruling 2).</summary>
    private static List<string> MatchRepos(IReadOnlyList<string>? names, IReadOnlyDictionary<string, string>? matches) =>
    [
        .. (names ?? [])
            .Select(name => matches is not null && matches.TryGetValue(name, out var matched) && !string.IsNullOrWhiteSpace(matched)
                ? matched.Trim()
                : name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
    ];

    /// <summary>The rule the Import dialog's preview counts by, so the two cannot
    /// disagree.</summary>
    private static IReadOnlyList<string> PlanTags(IEnumerable<string> tags) => ImportPlanPreview.PlanTags(tags);

    private static string Bare(string tag) => tag.StartsWith('+') ? tag[1..] : tag;

    private static bool IsQuiet(RoadmapIntakeResultDto result) =>
        result is { Created: 0, Updated: 0, Relengthened: 0, Refusal: null }
        && result.Skipped.Count == 0
        && result.EffortIgnored.Count == 0
        && result.AmbiguousTags.Count == 0
        && result.UnresolvedDependencies.Count == 0;

    /// <summary>Drops a task-level <c>after:</c> that names a <c>plan</c> entry of this
    /// document and no task, reporting it.</summary>
    private static EntryTextParser.ParsedEntry DropCrossLevel(
        EntryTextParser.ParsedEntry parsed,
        LocalIds levels,
        List<ImportUnresolvedDependencyDto> unresolved)
    {
        if ((parsed.DependsOn?.Count ?? 0) == 0) return parsed;

        var kept = new List<string>();
        foreach (var value in parsed.DependsOn!)
        {
            if (levels.IsPlanOnly(value)) unresolved.Add(new ImportUnresolvedDependencyDto(parsed.ImportItemId ?? parsed.Title.Trim(), value));
            else kept.Add(value);
        }

        return kept.Count == parsed.DependsOn!.Count ? parsed : parsed with { DependsOn = kept };
    }

    /// <summary>
    /// The names each level of the document answers to in <c>after:</c>: a task entry
    /// by its <c>id:</c>; a <c>plan</c> entry by its <c>id:</c> and by its tag, which is
    /// what its id defaults to. Ordinal, as every id comparison in Import is.
    /// </summary>
    private sealed class LocalIds
    {
        private readonly HashSet<string> _tasks = new(StringComparer.Ordinal);
        private readonly HashSet<string> _plans = new(StringComparer.Ordinal);

        public static LocalIds Of(
            IEnumerable<EntryTextParser.ParsedEntry> taskEntries,
            IEnumerable<EntryTextParser.ParsedEntry> planEntries)
        {
            var ids = new LocalIds();

            foreach (var task in taskEntries)
            {
                if (!string.IsNullOrWhiteSpace(task.ImportItemId)) ids._tasks.Add(task.ImportItemId);
            }

            foreach (var plan in planEntries)
            {
                if (!string.IsNullOrWhiteSpace(plan.ImportItemId)) ids._plans.Add(plan.ImportItemId);
                foreach (var tag in PlanTags(plan.Tags)) ids._plans.Add(Bare(tag));
            }

            return ids;
        }

        public bool IsPlanOnly(string value) => _plans.Contains(value.Trim()) && !_tasks.Contains(value.Trim());

        public bool IsTaskOnly(string value) => _tasks.Contains(value.Trim()) && !_plans.Contains(value.Trim());
    }

    /// <summary>What the task half wrote, as the five counts the result reports.</summary>
    private sealed record TaskHalf(int Created, int Replaced, int Updated, int Skipped, int Removed, IReadOnlyList<TaskItemDto> Entries)
    {
        public static TaskHalf None { get; } = new(0, 0, 0, 0, 0, []);
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
                && entry.Status is EntryStatus.Draft or EntryStatus.Ready
                // An entry the person has ticked off is finished work whatever
                // its status says, and finished work is kept, not cleared.
                && !entry.IsCompleted)
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
    /// </para>
    /// <para>
    /// <b>Except for a plan drafted from an inbox item</b>, which is born
    /// Draft whatever its text says. Nobody agreed that plan: a model wrote it
    /// about captured content — an article, a forwarded mail, a page somebody
    /// else made — and an entry born Ready is a prompt an agent may pick up
    /// without a person having read it. The provenance is the signal, so the
    /// rule keys on <paramref name="sourceInboxId"/> and on nothing the text
    /// can say; the drafter is asked for <c>!draft</c> too, so the two agree,
    /// but the prompt is a request and this is the guarantee.
    /// </para></summary>
    private static TaskItem CreateEntry(EntryTextParser.ParsedEntry parsed, int order, string? sourceInboxId)
    {
        var entry = TaskEntryFields.CreateFrom(parsed, order, sourceInboxId);

        if (sourceInboxId is not null) entry.SetStatus(EntryStatus.Draft);
        else if (parsed.Status is null) entry.SetStatus(EntryStatus.Ready);

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

    /// <summary>The one plan tag every parsed entry has in common — the
    /// <c>+tag</c> written with its sigil, or for a legacy plan the bare
    /// <c>#tag</c> — or null when there is none. Per ADR 0007 this becomes
    /// <c>import_plan_id</c> —
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

        if (shared is null) return null;

        // The plan tag is the one written with its sigil, and it is the plan's
        // identity whichever other tags every entry happens to share: a batch
        // whose entries all carry `+release-q4` and `#deploy` is the release-q4
        // plan, not the deploy one. A batch with no sigilled tag is a legacy
        // plan, identified by the bare tag it was written with.
        var candidates = shared.ToList();
        return candidates.FirstOrDefault(tag => tag.StartsWith('+')) ?? candidates.FirstOrDefault();
    }

    /// <summary>An entry's own plan when the document shares no tag: the one
    /// <c>+tag</c> it carries, or null when it carries none or several.
    /// <para>
    /// A roadmap document holds several plans and the task entries under them, so
    /// nothing is common to every entry — and without this each of those entries had
    /// no plan id, and every re-import of the document wrote all of them again. The
    /// sigil is what makes the fallback safe: a <c>+tag</c> names a plan, so an entry
    /// wearing exactly one belongs to that plan unambiguously. A general <c>#tag</c>
    /// is only a plan id when every entry shares it, as ADR 0007 always had it.
    /// </para></summary>
    private static string? OwnPlanTag(EntryTextParser.ParsedEntry parsed)
    {
        var planTags = parsed.Tags.Where(tag => tag.StartsWith('+')).Distinct(StringComparer.Ordinal).ToList();

        return planTags.Count == 1 ? planTags[0] : null;
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
