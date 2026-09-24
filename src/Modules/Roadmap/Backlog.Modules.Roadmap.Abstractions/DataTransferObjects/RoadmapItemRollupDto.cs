namespace Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

/// <summary>
/// How a gathered thing came to be under a roadmap item: named outright, found by
/// its tag, or both.
/// <para>
/// <see cref="Both"/> is a real state and not a rounding of the other two: an entry
/// can be the item's direct <c>TaskId</c> <em>and</em> carry its tag, and a
/// reader deciding whether the link is safe to remove needs to know it is held by
/// two threads rather than one.
/// </para>
/// </summary>
public enum RollupOrigin
{
    /// <summary>Named outright — the item's <c>TaskId</c>, or one of its
    /// <c>KnowledgeRefs</c>.</summary>
    Direct,

    /// <summary>Found because it carries the item's tag — a backlog entry tagged
    /// with it, or a knowledge chapter whose <c>roadmap</c> list names it.</summary>
    Tag,

    /// <summary>Reached both ways at once.</summary>
    Both
}

/// <summary>
/// How far along a gathered thing is, in the roadmap's own words.
/// <para>
/// Roadmap's vocabulary and not the backlog's, deliberately. The four members read
/// like the backlog's statuses because they describe the same journey, but this
/// context may not reference the module that owns that journey
/// (<c>ModuleBoundaryTests.A_module_never_references_another_module</c>), and a
/// timeline that coloured its steps by another module's enum would be reading a
/// vocabulary it has no say in. The translation happens once, in the one adapter
/// allowed to see both.
/// </para>
/// <para>
/// Four members, not five: a backlog entry that has been archived has finished its
/// work, and the roadmap has no fifth colour for "finished, then filed away". What
/// the roadmap draws is progress, and archiving is not a step back from done.
/// </para>
/// </summary>
public enum RoadmapProgress
{
    /// <summary>Not yet picked up, and not yet declared ready to be.</summary>
    Planned,

    /// <summary>Ready to be started.</summary>
    Ready,

    /// <summary>Being worked on now.</summary>
    InProgress,

    /// <summary>The work is over.</summary>
    Done
}

/// <summary>
/// One thing a roadmap item has gathered: a backlog entry or a knowledge chapter,
/// with how it was reached, what it registered as effort, how far along it is, and
/// what it waits on.
/// </summary>
/// <param name="Key">A stable identity for de-duplication — a backlog entry's id,
/// or a knowledge chapter's <c>&lt;path&gt;#&lt;slug&gt;</c> reference. The same
/// thing reached both directly and by tag shares one key and is counted once.</param>
/// <param name="Title">What to show for it.</param>
/// <param name="Effort">The story points it registered, or <see langword="null"/>
/// when it registered none. <c>null</c> is "not estimated" and contributes nothing
/// to a total; <c>0</c> is a real estimate that contributes zero.</param>
/// <param name="Origin">How it was reached.</param>
/// <param name="Progress">How far along it is, or <see langword="null"/> when it
/// registered no progress at all — which is the honest answer for a knowledge
/// chapter, because a chapter has no status to read. <c>null</c> is to
/// <see cref="RoadmapProgress"/> exactly what it is to <paramref name="Effort"/>:
/// nothing registered, and so nothing counted. Calling an unstarted-looking thing
/// <see cref="RoadmapProgress.Planned"/> would be inventing a state, which is the
/// one thing a rollup does not do.</param>
/// <param name="DependsOn">The <em>gathered</em> keys this one waits on — the
/// subset of what it named that this same item also gathered, so a step's chain
/// survives into the roadmap without the roadmap holding a dependency of its own.
/// Empty for anything that waits on nothing here. An id naming something outside
/// the item is dropped rather than kept dangling: it still blocks the work, but it
/// is not an edge between two steps drawn in this bar, and a key that resolves to
/// no step would order nothing.</param>
/// <param name="RepositoryIds">The repositories the work is filed against, as the
/// backlog stores them (<c>owner/name</c>, or a name Import could not resolve).
/// Opaque here: a reader matches them against the repositories it knows, which is
/// how an item spanning several repositories is drawn as one part per repository.
/// Empty for a knowledge chapter and for work filed nowhere.</param>
public sealed record RoadmapGatheredLink(
    string Key,
    string Title,
    int? Effort,
    RollupOrigin Origin,
    RoadmapProgress? Progress = null,
    IReadOnlyList<string>? DependsOn = null,
    IReadOnlyList<string>? RepositoryIds = null)
{
    /// <summary>The gathered keys this waits on, never null.</summary>
    public IReadOnlyList<string> Waits => DependsOn ?? [];

    /// <summary>The repositories the work is filed against, never null.</summary>
    public IReadOnlyList<string> Repositories => RepositoryIds ?? [];

    /// <summary>Whether its work is over. Something that registered no progress is
    /// not done — it is unknown, and unknown never counts as finished.</summary>
    public bool IsDone => Progress is RoadmapProgress.Done;
}

/// <summary>
/// Everything a roadmap item gathers, and the arithmetic over the effort those
/// things registered.
/// <para>
/// The two lists are already de-duplicated and merged: a thing reached both
/// directly and by tag appears once, wearing <see cref="RollupOrigin.Both"/>. The
/// totals are read off those lists, so <see cref="TotalEffort"/> and
/// <see cref="UnestimatedCount"/> never disagree with what is drawn.
/// </para>
/// <para>
/// It is arithmetic over registered values and nothing more — no estimate is
/// inferred for something that registered none, and no progress is inferred for
/// something that registered none. That is the whole reason
/// <see cref="UnestimatedCount"/> exists beside <see cref="TotalEffort"/>: a total
/// that silently dropped the unestimated work would read as smaller than the work
/// actually is. <see cref="DoneCount"/> sits beside <see cref="DoneEffort"/> for
/// the same reason and is the same guard one level in — a progress fill drawn from
/// effort alone would read as further along than the plan is the moment finished
/// work turned out to be unsized.
/// </para>
/// </summary>
public sealed record RoadmapItemRollupDto(
    IReadOnlyList<RoadmapGatheredLink> BacklogEntries,
    IReadOnlyList<RoadmapGatheredLink> KnowledgeChapters)
{
    /// <summary>Nothing gathered — the honest state of an item nothing points at
    /// yet.</summary>
    public static RoadmapItemRollupDto Empty { get; } = new([], []);

    private IEnumerable<RoadmapGatheredLink> All => BacklogEntries.Concat(KnowledgeChapters);

    /// <summary>How many things were gathered in total, backlog and knowledge
    /// together. Each is counted once regardless of how it was reached.</summary>
    public int GatheredCount => BacklogEntries.Count + KnowledgeChapters.Count;

    /// <summary>The sum of the story points every gathered thing registered.
    /// Things that registered none contribute nothing; <c>0</c> contributes
    /// zero.</summary>
    public int TotalEffort => All.Where(link => link.Effort is not null).Sum(link => link.Effort!.Value);

    /// <summary>How many gathered things registered an estimate at all —
    /// <c>0</c> counts, absence does not.</summary>
    public int EstimatedCount => All.Count(link => link.Effort is not null);

    /// <summary>How many gathered things registered no estimate. The figure the
    /// total is not allowed to hide.</summary>
    public int UnestimatedCount => All.Count(link => link.Effort is null);

    /// <summary>The sum of the story points registered by the gathered things whose
    /// work is over — the numerator of a progress fill over
    /// <see cref="TotalEffort"/>. Finished work that registered no estimate adds
    /// nothing here, because there is nothing to add; it shows up in
    /// <see cref="DoneCount"/> instead.</summary>
    public int DoneEffort => All
        .Where(link => link.IsDone && link.Effort is not null)
        .Sum(link => link.Effort!.Value);

    /// <summary>How many gathered things have finished, estimated or not. The
    /// figure the fill is not allowed to hide, the way
    /// <see cref="UnestimatedCount"/> is the figure the total is not allowed to
    /// hide.</summary>
    public int DoneCount => All.Count(link => link.IsDone);

    /// <summary>Whether nothing at all was gathered.</summary>
    public bool IsEmpty => GatheredCount == 0;
}

/// <summary>
/// Combines the candidates a roadmap item gathers into one list per source, so a
/// thing reached both directly and by tag is counted once, and orders them the way
/// the work itself is ordered.
/// <para>
/// The rule the merge keeps is the one the total depends on: identity is the
/// candidate's key, order is first appearance, and an origin seen more than one way
/// becomes <see cref="RollupOrigin.Both"/>. Both functions here are pure functions
/// of their input so the "counted once" guarantee and the ordering can be asserted
/// without a store behind them.
/// </para>
/// </summary>
public static class RoadmapRollup
{
    /// <summary>
    /// De-duplicates gathered candidates on <see cref="RoadmapGatheredLink.Key"/>,
    /// keeping the first title, effort, progress and dependencies seen for a key and
    /// merging the origins of every candidate that shares it. Keys compare
    /// case-insensitively; order is the order a key first appears.
    /// </summary>
    public static IReadOnlyList<RoadmapGatheredLink> Merge(IEnumerable<RoadmapGatheredLink> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var order = new List<string>();
        var byKey = new Dictionary<string, RoadmapGatheredLink>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (!byKey.TryGetValue(candidate.Key, out var existing))
            {
                order.Add(candidate.Key);
                byKey[candidate.Key] = candidate;
                continue;
            }

            // The first title, effort, progress and dependencies win — the same
            // thing reached twice registered one of each. Only the origin can
            // genuinely differ, and two ways of reaching it read as Both.
            byKey[candidate.Key] = existing with { Origin = Combine(existing.Origin, candidate.Origin) };
        }

        return [.. order.Select(key => byKey[key])];
    }

    /// <summary>
    /// Orders gathered links so that nothing comes before something it waits on —
    /// the order a plan's steps are drawn in.
    /// <para>
    /// Ties go to first appearance: two links that are equally free to go next come
    /// out in the order they arrived, so the caller decides what "first" means by
    /// deciding what it hands in, and this function invents no ordering of its own.
    /// </para>
    /// <para>
    /// A cycle is tolerated rather than refused. Tasks allows one to be written, so
    /// a drawing that threw on one would be a surface a person could break by typing
    /// an <c>after:</c>; instead the deadlock is broken by releasing the
    /// earliest-appearing link still waiting, and the pass continues. Work that sits
    /// downstream of the cycle therefore still lands after it, and a set that is
    /// nothing but one cycle degrades exactly to appearance order.
    /// </para>
    /// <para>
    /// A dependency naming a key not in the input is not an edge here. It is a real
    /// dependency and it really does block the work, but it is on something this
    /// list does not contain, so there is no position it could order against.
    /// </para>
    /// </summary>
    public static IReadOnlyList<RoadmapGatheredLink> InDependencyOrder(IEnumerable<RoadmapGatheredLink> links)
    {
        ArgumentNullException.ThrowIfNull(links);

        var ordered = links.ToList();
        if (ordered.Count < 2) return ordered;

        // Position by key, so an edge can be followed to an index. A duplicate key
        // keeps its first position, matching Merge — which has normally already run.
        var positionOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < ordered.Count; index++) positionOf.TryAdd(ordered[index].Key, index);

        // Only edges that land inside this list count, and only once each: a link
        // naming the same predecessor twice waits on it once.
        var waitsFor = new List<HashSet<int>>(ordered.Count);
        var blocks = new List<List<int>>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            waitsFor.Add([]);
            blocks.Add([]);
        }

        for (var index = 0; index < ordered.Count; index++)
        {
            foreach (var dependency in ordered[index].Waits)
            {
                if (string.IsNullOrEmpty(dependency)) continue;
                if (!positionOf.TryGetValue(dependency, out var target)) continue;
                if (target == index) continue; // A link waiting on itself blocks only itself.

                if (waitsFor[index].Add(target)) blocks[target].Add(index);
            }
        }

        var emitted = new bool[ordered.Count];
        var remaining = waitsFor.Select(set => set.Count).ToArray();
        var result = new List<RoadmapGatheredLink>(ordered.Count);

        while (result.Count < ordered.Count)
        {
            var next = FirstFree(emitted, remaining);

            // Nothing is free and something is left: every one of them is waiting on
            // another that is also waiting. Release the earliest and carry on.
            if (next < 0) next = FirstUnemitted(emitted);

            emitted[next] = true;
            result.Add(ordered[next]);

            foreach (var blocked in blocks[next]) remaining[blocked]--;
        }

        return result;
    }

    /// <summary>The earliest link that has nothing left to wait for, or
    /// <c>-1</c>.</summary>
    private static int FirstFree(bool[] emitted, int[] remaining)
    {
        for (var index = 0; index < emitted.Length; index++)
        {
            if (!emitted[index] && remaining[index] <= 0) return index;
        }

        return -1;
    }

    private static int FirstUnemitted(bool[] emitted)
    {
        for (var index = 0; index < emitted.Length; index++)
        {
            if (!emitted[index]) return index;
        }

        return -1;
    }

    private static RollupOrigin Combine(RollupOrigin first, RollupOrigin second) =>
        first == second ? first : RollupOrigin.Both;
}
