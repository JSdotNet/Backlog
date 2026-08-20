namespace Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

/// <summary>
/// How a gathered thing came to be under a roadmap item: named outright, found by
/// its tag, or both.
/// <para>
/// <see cref="Both"/> is a real state and not a rounding of the other two: an entry
/// can be the item's direct <c>BacklogEntryId</c> <em>and</em> carry its tag, and a
/// reader deciding whether the link is safe to remove needs to know it is held by
/// two threads rather than one.
/// </para>
/// </summary>
public enum RollupOrigin
{
    /// <summary>Named outright — the item's <c>BacklogEntryId</c>, or one of its
    /// <c>KnowledgeRefs</c>.</summary>
    Direct,

    /// <summary>Found because it carries the item's tag — a backlog entry tagged
    /// with it, or a knowledge chapter whose <c>roadmap</c> list names it.</summary>
    Tag,

    /// <summary>Reached both ways at once.</summary>
    Both
}

/// <summary>
/// One thing a roadmap item has gathered: a backlog entry or a knowledge chapter,
/// with how it was reached and what it registered as effort.
/// </summary>
/// <param name="Key">A stable identity for de-duplication — a backlog entry's id,
/// or a knowledge chapter's <c>&lt;path&gt;#&lt;slug&gt;</c> reference. The same
/// thing reached both directly and by tag shares one key and is counted once.</param>
/// <param name="Title">What to show for it.</param>
/// <param name="Effort">The story points it registered, or <see langword="null"/>
/// when it registered none. <c>null</c> is "not estimated" and contributes nothing
/// to a total; <c>0</c> is a real estimate that contributes zero.</param>
/// <param name="Origin">How it was reached.</param>
public sealed record RoadmapGatheredLink(string Key, string Title, int? Effort, RollupOrigin Origin);

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
/// inferred for something that registered none. That is the whole reason
/// <see cref="UnestimatedCount"/> exists beside <see cref="TotalEffort"/>: a total
/// that silently dropped the unestimated work would read as smaller than the work
/// actually is.
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

    /// <summary>Whether nothing at all was gathered.</summary>
    public bool IsEmpty => GatheredCount == 0;
}

/// <summary>
/// Combines the candidates a roadmap item gathers into one list per source, so a
/// thing reached both directly and by tag is counted once.
/// <para>
/// The rule the merge keeps is the one the total depends on: identity is the
/// candidate's key, order is first appearance, and an origin seen more than one way
/// becomes <see cref="RollupOrigin.Both"/>. It is a pure function of its input so
/// the "counted once" guarantee can be asserted without a store behind it.
/// </para>
/// </summary>
public static class RoadmapRollup
{
    /// <summary>
    /// De-duplicates gathered candidates on <see cref="RoadmapGatheredLink.Key"/>,
    /// keeping the first title and effort seen for a key and merging the origins of
    /// every candidate that shares it. Keys compare case-insensitively; order is the
    /// order a key first appears.
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

            // The first title and effort win — the same thing reached twice registered
            // one of each. Only the origin can genuinely differ, and two ways of
            // reaching it read as Both.
            byKey[candidate.Key] = existing with { Origin = Combine(existing.Origin, candidate.Origin) };
        }

        return [.. order.Select(key => byKey[key])];
    }

    private static RollupOrigin Combine(RollupOrigin first, RollupOrigin second) =>
        first == second ? first : RollupOrigin.Both;
}
