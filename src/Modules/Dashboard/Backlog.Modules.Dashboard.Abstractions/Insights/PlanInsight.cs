using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// How a roadmap item's work stands against the window it was planned into.
/// </summary>
public enum PlanOutlook
{
    /// <summary>Every backlog entry it gathered is done.</summary>
    Finished,

    /// <summary>The effort left, each part at its own pace from today, lands by its end.</summary>
    OnTrack,

    /// <summary>The effort left, each part at its own pace from today, lands after its end.</summary>
    Behind,

    /// <summary>Its end has passed with work still open.</summary>
    Overdue,

    /// <summary>Nothing it gathered carries an estimate, so there is nothing to project.</summary>
    Unsized,

    /// <summary>No pace to project at.</summary>
    NoPace,

    /// <summary>Its window is still the importer's, sized from effort and moved with
    /// the pace, so judging it against itself would always read as on track.</summary>
    PlacedByEffort
}

/// <summary>
/// One roadmap item and how its work stands.
/// </summary>
/// <param name="ProjectedEnd">The day its work is expected to land: the last completion
/// for a finished item, the planned end for one placed by effort, the projection for
/// one judged against its end. Null where there is nothing to project.</param>
public sealed record PlanItemInsight(PlanItemProgress Item, PlanOutlook Outlook, DateOnly? ProjectedEnd);

/// <summary>
/// The roadmap items the window shows in the repositories in scope, with the pace in use
/// and how each item's work stands.
/// </summary>
/// <param name="RoadmapEnabled">False when the roadmap feature is off — the section
/// then says there is no plan rather than that nothing is planned.</param>
public sealed record PlanInsight(bool RoadmapEnabled, PlanPace Pace, IReadOnlyList<PlanItemInsight> Items)
{
    /// <summary>The first day of the window the items were read for, inclusive. The
    /// timeline draws one column per ISO week from the week this day falls in, so its
    /// axis is the one the items were narrowed by rather than a second reading of the
    /// clock. <c>default</c> when the insight was built without one.</summary>
    public DateOnly WindowFrom { get; init; }

    /// <summary>The last day of that window, inclusive — today.</summary>
    public DateOnly WindowTo { get; init; }

    /// <summary>The story points the items gathered, estimated work only.</summary>
    public int PlannedEffort => Items.Sum(item => item.Item.TotalEffort);

    /// <summary>The story points of the gathered work that is done.</summary>
    public int DoneEffort => Items.Sum(item => item.Item.DoneEffort);

    /// <summary>How many gathered things carry no estimate — the figure
    /// <see cref="PlannedEffort"/> is not allowed to hide.</summary>
    public int Unestimated => Items.Sum(item => item.Item.Unestimated);

    /// <summary>The insight when the roadmap is switched off.</summary>
    public static PlanInsight Off { get; } = new(false, new PlanPace(0m, PlanPaceBasis.Manual), []);
}
