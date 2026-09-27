namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>
/// The stretch a pace was measured over, or <see cref="Manual"/> for the pace the
/// person typed because no stretch measured anything.
/// <para>
/// A mirror of the roadmap's own choice rather than that type: nothing in this module
/// may name a Roadmap type, and the section needs to say which stretch it is quoting so
/// a figure it shows can be recognised on the roadmap.
/// </para>
/// </summary>
public enum PlanPaceBasis
{
    LastTwoWeeks,
    LastFourWeeks,
    LastEightWeeks,
    Manual
}

/// <summary>
/// The pace the roadmap places work at for the scope asked: points per week, and the
/// stretch it was measured over.
/// </summary>
/// <param name="PointsPerWeek">Story points a week. Positive whenever the roadmap is
/// on, because the roadmap falls back to the typed pace rather than to nothing.</param>
/// <param name="Basis">Where the figure came from, so a reader can tell a measured pace
/// from a typed one.</param>
public sealed record PlanPace(decimal PointsPerWeek, PlanPaceBasis Basis);

/// <summary>
/// One roadmap item the window shows, reduced to what an outlook is worked out from.
/// </summary>
/// <param name="Start">First day, inclusive — as planned, not as drawn.</param>
/// <param name="End">Last day, inclusive — as planned, which is what the outlook judges
/// the projection against.</param>
/// <param name="RepositoryAliases">The repositories it is filed under. Empty for a
/// plan-wide item, which every repository scope keeps.</param>
/// <param name="TotalEffort">The story points everything it gathered registered. An
/// unestimated thing adds nothing here and is counted in <paramref name="Unestimated"/>,
/// so the total can say it is a floor.</param>
/// <param name="LastCompletedOn">The day its last entry was ticked off, when one says —
/// where a finished item actually ended.</param>
/// <param name="PacePointsPerWeek">The item's own pace: the lowest across its
/// repositories, the global one when it names none. Per item, not the reading's, because
/// the roadmap places each item at its own and the outlook has to agree with the bar.</param>
/// <param name="PlacedByEffort">Whether its window is still the importer's, sized from
/// effort. Such a window moves with the pace by itself, so there is nothing to be
/// behind.</param>
public sealed record PlanItemProgress(
    Guid Id,
    string Title,
    DateOnly Start,
    DateOnly End,
    IReadOnlyList<string> RepositoryAliases,
    int GatheredCount,
    int DoneCount,
    int TotalEffort,
    int DoneEffort,
    int Unestimated,
    bool IsFinished,
    DateOnly? LastCompletedOn,
    decimal PacePointsPerWeek,
    bool PlacedByEffort);

/// <summary>
/// What the roadmap says about the window: whether it is on at all, the pace in use for
/// the scope, and the items the window shows.
/// </summary>
/// <param name="RoadmapEnabled">False when the roadmap feature is off. Said rather than
/// answered with no items, so the section can tell "nothing planned" from "no plan".</param>
public sealed record PlanReading(bool RoadmapEnabled, PlanPace Pace, IReadOnlyList<PlanItemProgress> Items)
{
    /// <summary>The reading when the roadmap is switched off: nothing was read.</summary>
    public static PlanReading Off { get; } = new(false, new PlanPace(0m, PlanPaceBasis.Manual), []);
}

/// <summary>
/// PORT — the roadmap's items and their progress, for the dashboard's tasks section.
/// <para>
/// A window in the signature, unlike <see cref="ICompletedTaskSource"/>'s horizon: what
/// an item gathered is worked out per item and costs a backlog walk each, so the items
/// are narrowed to the window before their work is gathered rather than after. The
/// repository aliases only choose the pace to quote; narrowing the items by repository
/// stays a derivation in this module, as it is for the completed tasks. Nothing in this
/// contract names a Roadmap type; the adapter that answers it may see both contexts,
/// nothing in this module may.
/// </para>
/// </summary>
public interface IPlanProgressSource
{
    /// <summary>
    /// The items whose own start–end overlaps <paramref name="from"/> to
    /// <paramref name="to"/>, both inclusive, with their work gathered — and the pace
    /// the roadmap places work at for <paramref name="repositoryAliases"/>: that
    /// repository's when exactly one is named, the global one otherwise.
    /// </summary>
    Task<PlanReading> ReadAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<string> repositoryAliases,
        CancellationToken cancellationToken = default);
}
