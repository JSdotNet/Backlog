namespace Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

/// <summary>
/// Every pace the roadmap can place a plan by, and which one it is using.
/// <para>
/// A measured pace is <c>null</c> when nothing with an estimate was finished in its
/// stretch: a pace of zero has no length to draw, so it is not offered rather than
/// offered as nothing. Choosing one that is empty places by <see cref="Manual"/>
/// instead, and <see cref="FellBack"/> says so.
/// </para>
/// </summary>
/// <param name="Manual">The pace the reader typed, in story points a week. Always
/// positive.</param>
/// <param name="LastTwoWeeks">Effort finished in the last 14 days, over 2 weeks.</param>
/// <param name="LastFourWeeks">Effort finished in the last 28 days, over 4 weeks.</param>
/// <param name="LastEightWeeks">Effort finished in the last 56 days, over 8 weeks.</param>
/// <param name="Source">The pace the reader chose.</param>
public sealed record PlanningPacesDto(
    decimal Manual,
    decimal? LastTwoWeeks,
    decimal? LastFourWeeks,
    decimal? LastEightWeeks,
    PaceSource Source)
{
    /// <summary>The pace a source stands for, or <c>null</c> when it measured
    /// nothing.</summary>
    public decimal? Of(PaceSource source) => source switch
    {
        PaceSource.LastTwoWeeks => LastTwoWeeks,
        PaceSource.LastFourWeeks => LastFourWeeks,
        PaceSource.LastEightWeeks => LastEightWeeks,
        _ => Manual
    };

    /// <summary>What placement divides by. Always positive.</summary>
    public decimal InUse => Of(Source) ?? Manual;

    /// <summary>The chosen pace measured nothing, so <see cref="Manual"/> is in
    /// use in its place.</summary>
    public bool FellBack => Of(Source) is null;
}

/// <summary>A finished backlog entry's estimate, the day it was finished, and the
/// repositories it was filed under — all a measured pace needs to know about it.</summary>
public sealed record CompletedEffortDto(DateOnly CompletedOn, int Effort)
{
    /// <summary>The aliases of the repositories the entry was filed under; empty for
    /// an unfiled one. An entry under two repositories counts in full toward both
    /// of their measured paces, and toward the global one once.</summary>
    public IReadOnlyList<string> RepositoryAliases { get; init; } = [];
}

/// <summary>
/// The pace in use for every scope placement can be asked about, read at one moment:
/// the global one, and one per configured repository.
/// </summary>
/// <param name="Global">The pace a plan filed under no configured repository is
/// placed at. Always positive.</param>
/// <param name="ByRepository">Per configured repository alias, compared without
/// regard to case, the pace in use for it — its own, or the global typed pace and
/// choice measured over its own work when the reader never set one for it. Every
/// value is positive.</param>
public sealed record PacesInUseDto(decimal Global, IReadOnlyDictionary<string, decimal> ByRepository)
{
    /// <summary>
    /// The pace an item filed under <paramref name="repositoryAliases"/> is placed at.
    /// <para>
    /// No alias is the global pace. Otherwise each alias stands for its repository's
    /// pace — or the global one, for an alias no configured repository answers to — and
    /// the lowest wins: work that has to land in several repositories goes at the pace
    /// of the slowest, which is the longest bar and the honest one.
    /// </para>
    /// </summary>
    public decimal For(IEnumerable<string>? repositoryAliases)
    {
        decimal? lowest = null;

        foreach (var alias in repositoryAliases ?? [])
        {
            if (string.IsNullOrWhiteSpace(alias)) continue;

            var pace = ByRepository.TryGetValue(alias.Trim(), out var own) ? own : Global;
            lowest = lowest is { } known ? Math.Min(known, pace) : pace;
        }

        return lowest ?? Global;
    }
}
