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

/// <summary>A finished backlog entry's estimate and the day it was finished — all a
/// measured pace needs to know about it.</summary>
public sealed record CompletedEffortDto(DateOnly CompletedOn, int Effort);
