namespace Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

/// <summary>
/// The roadmap's plans in the Calendar's words: Tasks' own shape, so nothing of
/// Roadmap's crosses into this module.
/// </summary>
/// <param name="Windows">Every Roadmap Item, with the window the roadmap draws it with
/// today, in the plan's order.</param>
/// <param name="Milestones">Every milestone, in the plan's order.</param>
/// <param name="Shelf">The imported plans no Roadmap Item carries the tag of yet — the
/// roadmap's shelf — in the order the backlog holds them.</param>
/// <param name="Available">Whether there is a roadmap to show at all; false while it is
/// switched off, when the Calendar offers no plans.</param>
public sealed record CalendarPlansDto(
    IReadOnlyList<CalendarPlanWindowDto> Windows,
    IReadOnlyList<CalendarMilestoneDto> Milestones,
    IReadOnlyList<CalendarShelfPlanDto> Shelf,
    bool Available = true)
{
    /// <summary>Nothing planned and nothing waiting.</summary>
    public static CalendarPlansDto Empty { get; } = new([], [], []);

    /// <summary>No roadmap to read: it is switched off.</summary>
    public static CalendarPlansDto Off { get; } = new([], [], [], Available: false);
}

/// <summary>One Roadmap Item's planned window.</summary>
/// <param name="Start">First day, inclusive.</param>
/// <param name="End">Last day, inclusive.</param>
/// <param name="Tag">The plan tag, bare — no <c>+</c>.</param>
/// <param name="RepositoryAliases">The repositories it is filed under, by alias; the
/// first gives the bar its colour.</param>
/// <param name="DonePoints">The points of the gathered work that is finished.</param>
/// <param name="TotalPoints">The points all the gathered work registered.</param>
public sealed record CalendarPlanWindowDto(
    Guid Id,
    string Title,
    string Tag,
    DateOnly Start,
    DateOnly End,
    IReadOnlyList<string> RepositoryAliases,
    int DonePoints,
    int TotalPoints);

/// <summary>One milestone: a single day the plan is read against.</summary>
public sealed record CalendarMilestoneDto(
    Guid Id,
    string Title,
    DateOnly On);

/// <summary>One plan on the roadmap's shelf: imported work with no window yet.</summary>
/// <param name="Tag">The plan tag, bare — what a drop names it by.</param>
/// <param name="Title">What the roadmap would call the item it becomes.</param>
/// <param name="TaskCount">How many tasks carry the plan.</param>
/// <param name="TotalEffort">The points those tasks registered.</param>
/// <param name="UnestimatedCount">How many registered none.</param>
public sealed record CalendarShelfPlanDto(
    string Tag,
    string Title,
    IReadOnlyList<string> RepositoryAliases,
    int TaskCount,
    int TotalEffort,
    int UnestimatedCount);
