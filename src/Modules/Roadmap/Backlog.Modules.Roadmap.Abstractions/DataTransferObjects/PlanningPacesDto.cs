using Backlog.SharedKernel;

namespace Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

/// <summary>
/// Every pace the roadmap can place a plan by, and which one it is using.
/// <para>
/// A measured pace is <c>null</c> when nothing with an estimate was finished in its
/// stretch: a pace of zero has no length to draw, so it is not offered rather than
/// offered as nothing. A measured pace is used whenever there is one — the chosen
/// stretch, the last two weeks when none was chosen, otherwise the first stretch that
/// measured something — and <see cref="Manual"/>, the one typed pace at the top of
/// the roadmap, only when none did; <see cref="FellBack"/> says so.
/// </para>
/// </summary>
/// <param name="Manual">The pace the reader typed, in story points a week — the
/// fallback every scope shares. Always positive.</param>
/// <param name="LastTwoWeeks">Effort finished in the last 14 days, over the working hours
/// in them, times the hours of a working week (local ADR 0019) — the effort over 2 weeks.</param>
/// <param name="LastFourWeeks">The same over the last 28 days and 4 weeks.</param>
/// <param name="LastEightWeeks">The same over the last 56 days and 8 weeks.</param>
/// <param name="Source">The stretch the reader chose. <see cref="PaceSource.Manual"/>
/// is no stretch chosen, which reads as the last two weeks; <see cref="PaceSource.Set"/>
/// is the scope's own pace, set by hand (<see cref="Own"/>).</param>
public sealed record PlanningPacesDto(
    decimal Manual,
    decimal? LastTwoWeeks,
    decimal? LastFourWeeks,
    decimal? LastEightWeeks,
    PaceSource Source)
{
    private decimal? _own;

    /// <summary>The working week these paces are counted in — a week of the pace is the
    /// hours of this week (local ADR 0019). The default week when the reader never set
    /// one; a week with no working hours reads as the default too.</summary>
    public WorkingHours Week
    {
        get;
        init => field = (value ?? WorkingHours.Default).Effective;
    } = WorkingHours.Default;

    /// <summary>The scope's own typed pace, set on its slider — what
    /// <see cref="PaceSource.Set"/> stands for. The global scope's is the heading's
    /// typed pace, and so is a repository's that has none of its own, so it equals
    /// <see cref="Manual"/> unless a scope set one. Always positive.</summary>
    public decimal Own
    {
        get => _own ?? Manual;
        init => _own = value;
    }

    /// <summary>The pace a source stands for, or <c>null</c> when it measured
    /// nothing.</summary>
    public decimal? Of(PaceSource source) => source switch
    {
        PaceSource.LastTwoWeeks => LastTwoWeeks,
        PaceSource.LastFourWeeks => LastFourWeeks,
        PaceSource.LastEightWeeks => LastEightWeeks,
        PaceSource.Set => Own,
        _ => Manual
    };

    /// <summary>The stretches, shortest first — the order a stretch is looked for in
    /// when the chosen one measured nothing.</summary>
    public static IReadOnlyList<PaceSource> Stretches { get; } =
        [PaceSource.LastTwoWeeks, PaceSource.LastFourWeeks, PaceSource.LastEightWeeks];

    /// <summary>The pace placement reads: the scope's own pace when it was set by
    /// hand, otherwise the chosen stretch when it measured something, otherwise the
    /// first stretch that did, otherwise the typed pace. No stretch chosen reads as
    /// the last two weeks.</summary>
    public PaceSource InEffect
    {
        get
        {
            if (Source == PaceSource.Set) return PaceSource.Set;

            var chosen = Source == PaceSource.Manual ? PaceSource.LastTwoWeeks : Source;
            if (Of(chosen) is not null) return chosen;

            foreach (var stretch in Stretches)
            {
                if (Of(stretch) is not null) return stretch;
            }

            return PaceSource.Manual;
        }
    }

    /// <summary>What placement divides by. Always positive.</summary>
    public decimal InUse => Of(InEffect) ?? Manual;

    /// <summary>No stretch measured anything, so <see cref="Manual"/> is in use. Never
    /// so for a pace set by hand: that is a choice, not a fallback.</summary>
    public bool FellBack => InEffect == PaceSource.Manual;
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
    /// <summary>The working week every pace here is counted in, and every window it
    /// sizes is counted through (local ADR 0019). The default week when the reader never
    /// set one; a week with no working hours reads as the default too.</summary>
    public WorkingHours Week
    {
        get;
        init => field = (value ?? WorkingHours.Default).Effective;
    } = WorkingHours.Default;

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
