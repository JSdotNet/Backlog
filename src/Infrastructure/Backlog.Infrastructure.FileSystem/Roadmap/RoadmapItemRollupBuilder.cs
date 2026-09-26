using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Turns what a roadmap item reaches — its direct links and everything carrying its
/// tag — into the two de-duplicated lists a rollup shows.
/// <para>
/// A pure function of the item, the backlog and the knowledge nodes, deliberately
/// apart from the adapter that reads those. Everything worth asserting about a
/// rollup — that a thing linked and tagged is counted once, that a direct link and
/// a tag match merge, that <c>null</c> effort is left out of the sum but kept in the
/// count — is decided here, where it can be checked without a store or a file.
/// </para>
/// <para>
/// It is also the one place the two vocabularies meet. Roadmap may not reference
/// Tasks, so a backlog entry's <see cref="EntryStatus"/> becomes a
/// <see cref="RoadmapProgress"/> here and its <c>after:</c> ids become gathered
/// keys here, and nothing downstream ever sees the Tasks-side shapes. An
/// infrastructure adapter is allowed to see both; that is what it is for.
/// </para>
/// </summary>
public static class RoadmapItemRollupBuilder
{
    /// <param name="item">The roadmap item to gather for.</param>
    /// <param name="backlog">Every backlog entry.</param>
    /// <param name="knowledge">Every knowledge node.</param>
    /// <param name="sessions">The AI sessions an entry may be linked to, by session
    /// id — read only to date an entry that carries no date of its own. Null, or a
    /// linked session missing from it, leaves the entry's dates as Tasks recorded
    /// them.</param>
    public static RoadmapItemRollupDto Build(
        RoadmapItemDto item,
        IReadOnlyList<TaskItemDto> backlog,
        IReadOnlyList<KnowledgeGraphNode> knowledge,
        IReadOnlyDictionary<string, RoadmapSessionActivity>? sessions = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(backlog);
        ArgumentNullException.ThrowIfNull(knowledge);

        return Build(item, backlog, knowledge, DependencyCandidates(backlog), sessions ?? NoSessions);
    }

    /// <summary>
    /// One rollup per item of a plan, off one read of each source.
    /// <para>
    /// Not a loop over <see cref="Build(RoadmapItemDto, IReadOnlyList{TaskItemDto},
    /// IReadOnlyList{KnowledgeGraphNode}, IReadOnlyDictionary{string, RoadmapSessionActivity})"/> for one reason worth stating: the
    /// dependency candidates are derived from the whole backlog, not from one item's
    /// share of it, so deriving them once here is both cheaper and the only way the
    /// answer stays the same for every item.
    /// </para>
    /// <para>
    /// Two items of one plan may gather the same entry — a tag is not exclusive —
    /// and each gets its own rollup naming it. De-duplication is within an item, not
    /// across a plan: an entry reached by two items is two bars' worth of drawing,
    /// and deciding which bar may claim it is a question for whoever draws them.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<Guid, RoadmapItemRollupDto> BuildPlan(
        RoadmapPlanDto plan,
        IReadOnlyList<TaskItemDto> backlog,
        IReadOnlyList<KnowledgeGraphNode> knowledge,
        IReadOnlyDictionary<string, RoadmapSessionActivity>? sessions = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(backlog);
        ArgumentNullException.ThrowIfNull(knowledge);

        var candidates = DependencyCandidates(backlog);
        var rollups = new Dictionary<Guid, RoadmapItemRollupDto>();

        foreach (var item in plan.Items)
        {
            // Last write wins on a repeated id rather than throwing. A plan holding
            // one id twice is already broken, and refusing to draw any of it is a
            // worse answer than drawing it once.
            rollups[item.Id] = Build(item, backlog, knowledge, candidates, sessions ?? NoSessions);
        }

        return rollups;
    }

    private static RoadmapItemRollupDto Build(
        RoadmapItemDto item,
        IReadOnlyList<TaskItemDto> backlog,
        IReadOnlyList<KnowledgeGraphNode> knowledge,
        IReadOnlyCollection<DependencyResolution.Candidate> candidates,
        IReadOnlyDictionary<string, RoadmapSessionActivity> sessions) =>
        new(
            RoadmapRollup.Merge(BacklogCandidates(item, backlog, candidates, sessions)),
            RoadmapRollup.Merge(KnowledgeCandidates(item, knowledge)));

    private static readonly IReadOnlyDictionary<string, RoadmapSessionActivity> NoSessions =
        new Dictionary<string, RoadmapSessionActivity>();

    /// <summary>Whether <paramref name="item"/> gathers <paramref name="entry"/> at all —
    /// by its direct link or by carrying its tag. The adapter asks it to learn which
    /// entries' sessions are worth reading before any rollup is built.</summary>
    public static bool Gathers(RoadmapItemDto item, TaskItemDto entry)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(entry);

        return IsDirect(item, entry) || IsTagged(item, entry);
    }

    /// <summary>
    /// Whether a session could date <paramref name="entry"/>: it names at least one
    /// session, and it lacks a date a session can stand in for — a start, or, once
    /// its work is over, a completion.
    /// </summary>
    public static bool NeedsSessionDates(TaskItemDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return (entry.StartedOn is null || (IsOver(entry.Status) && entry.CompletedOn is null))
            && SessionIds(entry).Any();
    }

    /// <summary>The ids of the AI sessions linked to <paramref name="entry"/>.</summary>
    public static IEnumerable<string> SessionIds(TaskItemDto entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Projections
            .Where(projection => string.Equals(projection.TargetType, EntryProjectionDto.SessionTargetType, StringComparison.OrdinalIgnoreCase))
            .Select(projection => projection.ExternalId)
            .Where(id => !string.IsNullOrWhiteSpace(id));
    }

    /// <summary>Every live entry an <c>after:</c> value may name, for the resolver
    /// below. Built from the whole backlog because a plan-local id is resolved
    /// against the store, not against one item's gatherings.</summary>
    private static IReadOnlyCollection<DependencyResolution.Candidate> DependencyCandidates(
        IReadOnlyList<TaskItemDto> backlog) =>
        [.. backlog.Select(entry => new DependencyResolution.Candidate(entry.Id, entry.ImportItemId, entry.ImportPlanId))];

    private static IEnumerable<RoadmapGatheredLink> BacklogCandidates(
        RoadmapItemDto item,
        IReadOnlyList<TaskItemDto> backlog,
        IReadOnlyCollection<DependencyResolution.Candidate> candidates,
        IReadOnlyDictionary<string, RoadmapSessionActivity> sessions)
    {
        var gathered = new List<(TaskItemDto Entry, bool Direct, bool Tagged)>();
        foreach (var entry in backlog)
        {
            var direct = IsDirect(item, entry);
            var tagged = IsTagged(item, entry);

            if (direct || tagged) gathered.Add((entry, direct, tagged));
        }

        // Which keys an edge may point at is only known once the whole gathering is
        // known, so the links are built in a second pass over the first pass's result
        // rather than streamed out of one.
        var keys = new HashSet<string>(gathered.Select(found => found.Entry.Id.ToString()), StringComparer.OrdinalIgnoreCase);

        foreach (var (entry, direct, tagged) in gathered)
        {
            var (startedOn, completedOn) = Dates(entry, sessions);

            yield return new RoadmapGatheredLink(
                entry.Id.ToString(),
                entry.Title,
                entry.Effort,
                Origin(direct, tagged),
                Progress(entry.Status),
                GatheredDependencies(entry, keys, candidates),
                entry.RepoIds ?? [],
                startedOn,
                completedOn,
                entry.CreatedAt is { } createdAt ? DateOnly.FromDateTime(createdAt.LocalDateTime) : null);
        }
    }

    /// <summary>
    /// When an entry's work ran: the dates Tasks recorded, and where one is missing,
    /// the sessions linked to the entry.
    /// <para>
    /// A session-derived start is the earliest instant any linked session is known to
    /// have been working. It comes ahead of the created-on fallback a reader applies
    /// later, because a session is the work itself and the day an entry was filed is
    /// not. A session-derived completion is the last activity of the latest linked
    /// session, and only for work that is over: a session ending says nothing about
    /// open work being finished.
    /// </para>
    /// <para>
    /// The entry's own dates always win, and nothing is written back — this is read
    /// off the gathering each time, like the rest of the rollup.
    /// </para>
    /// </summary>
    private static (DateOnly? StartedOn, DateOnly? CompletedOn) Dates(
        TaskItemDto entry,
        IReadOnlyDictionary<string, RoadmapSessionActivity> sessions)
    {
        var startedOn = entry.StartedOn;
        var completedOn = entry.CompletedOn;
        if (sessions.Count == 0 || !NeedsSessionDates(entry)) return (startedOn, completedOn);

        var worked = SessionIds(entry)
            .Select(id => sessions.TryGetValue(id, out var session) ? session : null)
            .OfType<RoadmapSessionActivity>()
            .ToList();
        if (worked.Count == 0) return (startedOn, completedOn);

        startedOn ??= LocalDay(worked.Min(session => session.EarliestAt));
        if (IsOver(entry.Status)) completedOn ??= LocalDay(worked.Max(session => session.LastActivityAt));

        return (startedOn, completedOn);
    }

    private static DateOnly LocalDay(DateTimeOffset instant) => DateOnly.FromDateTime(instant.LocalDateTime);

    private static bool IsDirect(RoadmapItemDto item, TaskItemDto entry) =>
        item.TaskId is { } linked && entry.Id == linked;

    private static bool IsTagged(RoadmapItemDto item, TaskItemDto entry) =>
        !string.IsNullOrWhiteSpace(item.Tag) && entry.Tags.Any(candidate => CarriesPlanTag(candidate, item.Tag!));

    private static bool IsOver(EntryStatus status) => Progress(status) == RoadmapProgress.Done;

    /// <summary>
    /// The gathered keys one entry waits on.
    /// <para>
    /// Resolved before it is filtered, and that order matters. A stored
    /// <c>after:</c> is whatever was written — a real id for an entry Import
    /// resolved, a plan-local slug for one it could not — and a slug compared
    /// against a list of ids matches nothing. A plan imported over two sittings
    /// would silently lose every edge between the two halves, which is the one
    /// reading this exists to give. <see cref="DependencyResolution"/> is the rule
    /// Import and the tasks pane already share, so the chain the roadmap draws is
    /// the chain those two agree on.
    /// </para>
    /// <para>
    /// What survives the filter is only what this item also gathered. An id naming
    /// work outside the item still blocks that entry and is still shown as a
    /// dependency where dependencies are shown — it is simply not an edge between
    /// two things drawn in one bar.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> GatheredDependencies(
        TaskItemDto entry,
        HashSet<string> keys,
        IReadOnlyCollection<DependencyResolution.Candidate> candidates)
    {
        var declared = entry.DependsOn;
        if (declared is null || declared.Count == 0) return [];

        var resolved = DependencyResolution.ResolveAll(declared, candidates, entry.ImportPlanId, entry.Tags);

        return [.. resolved.Where(keys.Contains)];
    }

    private static IEnumerable<RoadmapGatheredLink> KnowledgeCandidates(
        RoadmapItemDto item,
        IReadOnlyList<KnowledgeGraphNode> knowledge)
    {
        var byId = new Dictionary<string, KnowledgeGraphNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in knowledge) byId.TryAdd(node.Id, node);

        // Direct references first, so their order is the person's reading order — the
        // tag matches that follow keep whatever order the graph lists them in.
        foreach (var reference in item.Knowledge)
        {
            if (string.IsNullOrWhiteSpace(reference)) continue;

            if (byId.TryGetValue(reference, out var node))
            {
                yield return new RoadmapGatheredLink(node.Id, node.Label, node.Effort, RollupOrigin.Direct);
            }
            else
            {
                // A reference to a chapter the graph does not know is an ordinary
                // state, not an error: it is shown as itself, with no effort to sum.
                yield return new RoadmapGatheredLink(reference, reference, null, RollupOrigin.Direct);
            }
        }

        var tag = item.Tag;
        if (string.IsNullOrWhiteSpace(tag)) yield break;

        foreach (var node in knowledge)
        {
            if (!node.Roadmap.Contains(tag, StringComparer.OrdinalIgnoreCase)) continue;
            yield return new RoadmapGatheredLink(node.Id, node.Label, node.Effort, RollupOrigin.Tag);
        }
    }

    /// <summary>
    /// Whether a backlog entry's stored tag names the roadmap item's slug.
    /// <para>
    /// A plan tag is stored as <c>+slug</c>, and an entry filed before the sigil
    /// existed carries the bare <c>slug</c>; both roll up, or every entry already
    /// on disk would drop off its item the day the sigil arrived. What must not roll
    /// up is a person: <c>@release-q4</c> names somebody, and only the <c>+</c> is
    /// lifted before the compare so it stays a mismatch.
    /// </para>
    /// </summary>
    private static bool CarriesPlanTag(string candidate, string slug)
    {
        var bare = candidate.StartsWith('+') ? candidate[1..] : candidate;

        return string.Equals(bare, slug, StringComparison.OrdinalIgnoreCase);
    }

    private static RollupOrigin Origin(bool direct, bool tagged) =>
        direct && tagged ? RollupOrigin.Both : direct ? RollupOrigin.Direct : RollupOrigin.Tag;

    /// <summary>
    /// A backlog status read in the roadmap's four words.
    /// <para>
    /// <see cref="EntryStatus.Archived"/> is <see cref="RoadmapProgress.Done"/>, and
    /// that is a reading rather than a rounding: the aggregate's own words are that
    /// "Done and Archived say the work is over", and the roadmap draws whether the
    /// work is over. Filing a finished entry away is not progress running backwards.
    /// </para>
    /// <para>
    /// <c>CompletedOn</c> is deliberately not consulted. It says the person has
    /// ticked the entry off their list, which is a different fact from the work
    /// being over — the two were split apart on purpose — and it is the second one a
    /// plan's progress fill is about.
    /// </para>
    /// </summary>
    private static RoadmapProgress Progress(EntryStatus status) => status switch
    {
        EntryStatus.Draft => RoadmapProgress.Planned,
        EntryStatus.Ready => RoadmapProgress.Ready,
        EntryStatus.InProgress => RoadmapProgress.InProgress,
        EntryStatus.Done => RoadmapProgress.Done,
        EntryStatus.Archived => RoadmapProgress.Done,
        _ => RoadmapProgress.Planned
    };
}
