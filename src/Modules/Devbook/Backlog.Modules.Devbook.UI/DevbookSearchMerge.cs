using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// One devbook a search asks: the Brain, or one configured repository
/// (<c>.devbook/domain/devbook/features.md#unified-search</c>).
/// </summary>
/// <param name="Key">The scope member it stands for: <see cref="BrainScope.Value"/>
/// for the Brain, else the repository's alias — the value every search port
/// already takes as its repository alias.</param>
/// <param name="Label">What its chip and its group heading say.</param>
public sealed record DevbookSearchSource(string Key, string Label)
{
    /// <summary>The Brain, by the name every scope calls it.</summary>
    public static DevbookSearchSource Brain { get; } = new(BrainScope.Value, BrainScope.Name);

    /// <summary>A configured repository, labelled with its alias.</summary>
    public static DevbookSearchSource Repository(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        return new DevbookSearchSource(alias, alias);
    }

    public bool IsBrain => BrainScope.Is(Key);
}

/// <summary>
/// What one source answered: its hits, best first, or the sentence saying why it
/// could not be searched.
/// </summary>
public sealed record DevbookSearchGroup(DevbookSearchSource Source, IReadOnlyList<DevbookSearchHit> Hits, string? UnavailableMessage)
{
    public static DevbookSearchGroup For(DevbookSearchSource source, IReadOnlyList<DevbookSearchHit> hits) => new(source, hits, null);

    public static DevbookSearchGroup NotSearchable(DevbookSearchSource source, string message) => new(source, [], message);

    /// <summary>Whether this source has no database to search, rather than no
    /// chapter that matched.</summary>
    public bool IsUnavailable => UnavailableMessage is not null;
}

/// <summary>
/// How one search across several devbooks puts its answer together: within a
/// source, the words and the meaning answers merged into one list; across them,
/// one group per source with the Brain's first.
/// </summary>
public static class DevbookSearchMerge
{
    /// <summary>
    /// The constant of reciprocal-rank fusion: a hit at place <c>r</c> in a list
    /// earns <c>1 / (RankConstant + r)</c>. Sixty is the value the method was
    /// published with; it flattens the head of each list enough that a chapter
    /// both tiers placed second beats one only one tier placed first.
    /// </summary>
    public const int RankConstant = 60;

    /// <summary>
    /// The words and the meaning answers of one source as one list, best first.
    /// <para>
    /// By place, never by score. A full-text rank and a cosine are on different
    /// scales — <see cref="DevbookSearchHit.Score"/> is comparable only within one
    /// answer — so reciprocal-rank fusion is used: each list votes by where it put
    /// a chapter, and a chapter in both is one row with both votes. Equal votes
    /// keep the words first, because the words carry the excerpt that shows the
    /// reader why a row is there; a chapter found by both keeps that excerpt.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DevbookSearchHit> Merge(
        IReadOnlyList<DevbookSearchHit> words,
        IReadOnlyList<DevbookSearchHit> meaning,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(meaning);
        if (limit <= 0) return [];

        var fused = new Dictionary<string, (DevbookSearchHit Hit, double Score, int Order)>(StringComparer.Ordinal);

        Vote(words);
        Vote(meaning);

        return
        [
            .. fused.Values
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.Order)
                .Take(limit)
                .Select(entry => entry.Hit with { Score = entry.Score })
        ];

        void Vote(IReadOnlyList<DevbookSearchHit> hits)
        {
            for (var place = 0; place < hits.Count; place++)
            {
                var hit = hits[place];
                var key = hit.Chapter.Reference;
                var vote = 1d / (RankConstant + place + 1);

                if (fused.TryGetValue(key, out var seen))
                {
                    var kept = string.IsNullOrWhiteSpace(seen.Hit.Excerpt) && !string.IsNullOrWhiteSpace(hit.Excerpt)
                        ? hit
                        : seen.Hit;
                    fused[key] = (kept, seen.Score + vote, seen.Order);
                }
                else
                {
                    fused[key] = (hit, vote, fused.Count);
                }
            }
        }
    }

    /// <summary>
    /// The sources' answers in the order they are shown: the Brain's first, then
    /// the repositories in the order they were asked — their configured order.
    /// A source that answered and matched nothing has no group; one that could
    /// not be searched keeps its group, so it is named rather than silently left
    /// out.
    /// </summary>
    public static IReadOnlyList<DevbookSearchGroup> Group(IEnumerable<DevbookSearchGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        return
        [
            .. groups
                .Where(group => group.IsUnavailable || group.Hits.Count > 0)
                .Select((group, order) => (group, order))
                .OrderBy(entry => entry.group.Source.IsBrain ? 0 : 1)
                .ThenBy(entry => entry.order)
                .Select(entry => entry.group)
        ];
    }
}

/// <summary>
/// A result the reader pressed: which devbook it came from and its address there.
/// The pane opens it in place when that devbook is the one it shows, and hands it
/// to the shell to switch to when it is not.
/// </summary>
public sealed record DevbookSearchOpen(DevbookSearchSource Source, string Reference);
