namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// The dashboard tab a Needs-you item opens.
/// </summary>
/// <remarks>
/// The members name the tabs that hold a Needs-you source, and only those: an item can
/// point at nothing else. <see cref="NeedsYouItem.TabId"/> spells each as the pane's own
/// tab id, so the Overview links without a mapping of its own.
/// </remarks>
public enum NeedsYouTab
{
    /// <summary>The Devbook tab, where the drift lives.</summary>
    Devbook,

    /// <summary>The Tasks tab, where the roadmap lives.</summary>
    Tasks,

    /// <summary>The Cost tab, where the month-end projection lives.</summary>
    Cost,

    /// <summary>The Productivity tab, where the rework lives.</summary>
    Productivity
}

/// <summary>Which rule raised a Needs-you item. The rules run in this order, and so do
/// the items.</summary>
public enum NeedsYouRule
{
    /// <summary>A devbook unit whose drift issue carries <c>sync-failed</c>, or whose last
    /// verdict is <c>conflict</c>.</summary>
    Drift,

    /// <summary>A roadmap item whose outlook is <see cref="PlanOutlook.Behind"/>.</summary>
    RoadmapBehind,

    /// <summary>A provider projected past its monthly budget by the month's end.</summary>
    PastBudget,

    /// <summary>Pull requests that came back after review are more than a quarter of
    /// those merged in the window.</summary>
    Rework
}

/// <summary>
/// One thing on the dashboard that needs the person: what it is, one line on why, and
/// the tab that shows it.
/// </summary>
/// <param name="Rule">The rule that raised it.</param>
/// <param name="Title">What needs the person, as a short heading.</param>
/// <param name="Detail">One line on why — never more than one.</param>
/// <param name="Tab">The tab it links to.</param>
public sealed record NeedsYouItem(NeedsYouRule Rule, string Title, string Detail, NeedsYouTab Tab)
{
    /// <summary>The tab's id as the dashboard pane names it — <c>dashboard-devbook</c>,
    /// <c>dashboard-tasks</c>, <c>dashboard-cost</c>, <c>dashboard-productivity</c>.</summary>
    public string TabId => "dashboard-" + Tab.ToString().ToLowerInvariant();
}

/// <summary>
/// The Needs-you items for one scope, in rule order, and the sources that could not be
/// read for them.
/// </summary>
/// <remarks>
/// A source that is unavailable is skipped, never an error: its rule raises nothing and
/// its reason is in <see cref="Skipped"/>, so an empty list over a skipped source can say
/// it is a floor rather than an all-clear.
/// </remarks>
/// <param name="Items">What needs the person, rule by rule.</param>
/// <param name="Skipped">Why each source that could not be read, or not read whole, was
/// skipped or is a floor, in rule order. Empty when every source answered in full.</param>
public sealed record NeedsYouInsight(IReadOnlyList<NeedsYouItem> Items, IReadOnlyList<string> Skipped)
{
    /// <summary>What an empty result means, in the words the Overview shows.</summary>
    public const string NothingNeedsYou = "Nothing needs you.";

    /// <summary>The share of merged pull requests that came back after review above
    /// which rework needs the person. Exactly this share does not.</summary>
    public const decimal ReworkThreshold = 0.25m;

    /// <summary>True when nothing needs the person.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>Nothing needs the person and every source answered.</summary>
    public static NeedsYouInsight Empty { get; } = new([], []);
}
