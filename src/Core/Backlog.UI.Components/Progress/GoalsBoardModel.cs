using Backlog.UI.Components.Goals;

namespace Backlog.UI.Components.Progress;

/// <summary>The iteration lengths the Goals view's switch offers
/// (<c>.devbook/domain/progress/features.md#choosing-the-iteration</c>), in its order.</summary>
public enum GoalsIteration
{
    Day,
    Week,
    TwoWeeks,
    Month
}

/// <summary>An iteration window, its first and last day inclusive.</summary>
public sealed record GoalsWindow(DateOnly First, DateOnly Last);

/// <summary>A goal's items counted by status, and those done in the last window.</summary>
/// <param name="Done">Items done.</param>
/// <param name="Total">Every item the goal holds.</param>
/// <param name="Doing">Items in progress.</param>
/// <param name="Blocked">Items blocked.</param>
/// <param name="ToDo">Items not started.</param>
/// <param name="DoneInWindow">Items done in the last window: the <c>+n</c>.</param>
public sealed record GoalCounts(int Done, int Total, int Doing, int Blocked, int ToDo, int DoneInWindow);

/// <summary>A task's status as the goal's Tasks column dots it.</summary>
public enum GoalTaskStatus
{
    ToDo,
    Doing,
    Blocked,
    Done
}

/// <summary>A plan attached to a goal, with its own count.</summary>
/// <param name="Title">The plan's name as the host shows it.</param>
/// <param name="Subject">The plan as the host names it, handed back on detach; null draws the
/// row with no detach.</param>
public sealed record GoalPlanModel(string Title, int Done, int Total, GoalSubject? Subject = null);

/// <summary>A task attached to a goal.</summary>
/// <param name="Id">The host's id for the task.</param>
/// <param name="Subject">The task as the host names it, handed back on detach; null draws the
/// row with no detach.</param>
public sealed record GoalTaskModel(string Id, string Title, GoalTaskStatus Status, GoalSubject? Subject = null);

/// <summary>
/// One goal as the Goals view draws it
/// (<c>.devbook/domain/progress/features.md#the-goals-view</c>).
/// </summary>
/// <param name="Id">The host's id for the goal; handed back on attach.</param>
/// <param name="Title">The goal's title.</param>
/// <param name="DueDate">The day the goal is due.</param>
/// <param name="LandsOn">The day the forecast expects it to land; null with none.</param>
/// <param name="AtRisk">Whether the forecast reads At risk; On track otherwise.</param>
/// <param name="Counts">The goal's items by status.</param>
/// <param name="Plans">The plans attached to it.</param>
/// <param name="Tasks">The tasks attached to it.</param>
/// <param name="PullRequests">Its attached pull requests and those its tasks link.</param>
/// <param name="Outcome">The goal's one-line outcome; null with none.</param>
/// <param name="PullRequestSubjects">Each of <paramref name="PullRequests"/> as the host names
/// it, in its order, handed back on detach: null for one the goal holds only through a task,
/// which detaching the goal's own pull request would not remove.</param>
public sealed record GoalCardModel(
    string Id,
    string Title,
    DateOnly DueDate,
    DateOnly? LandsOn,
    bool AtRisk,
    GoalCounts Counts,
    IReadOnlyList<GoalPlanModel> Plans,
    IReadOnlyList<GoalTaskModel> Tasks,
    IReadOnlyList<PullRequestStatusModel> PullRequests,
    string? Outcome = null,
    IReadOnlyList<GoalSubject?>? PullRequestSubjects = null)
{
    /// <summary>The subject of the pull request at <paramref name="index"/> in
    /// <see cref="PullRequests"/>; null when it has none.</summary>
    public GoalSubject? PullRequestSubject(int index) =>
        PullRequestSubjects is { } subjects && index >= 0 && index < subjects.Count ? subjects[index] : null;
}

/// <summary>
/// A piece of work the view can attach to a goal: a match in the attach search, or a
/// row under No goal.
/// </summary>
/// <param name="Subject">The work as the host names it; handed back on attach. Its kind is the
/// item's kind label, and a pull request's number is drawn before its title.</param>
/// <param name="Title">What the row reads.</param>
public sealed record GoalsItem(GoalSubject Subject, string Title)
{
    /// <summary>Plan, task or pull request.</summary>
    public GoalSubjectKind Kind => Subject.Kind;

    /// <summary>A pull request's number; null for anything else.</summary>
    public int? Number => Subject.Kind == GoalSubjectKind.PullRequest ? Subject.Number : null;
}

/// <summary>An item in the stand-up's Finished or Planned list.</summary>
/// <param name="Title">The item's title.</param>
/// <param name="PullRequest">A pull request item's status line; null for anything else.</param>
/// <param name="Effort">The item's <c>effort:</c> in points, when it carries one; a planned
/// item shows it.</param>
public sealed record StandUpEntry(string Title, PullRequestStatusModel? PullRequest = null, int? Effort = null);

/// <summary>The items of one goal, or of No goal when <paramref name="GoalTitle"/> is null.</summary>
public sealed record StandUpGroup(string? GoalTitle, IReadOnlyList<StandUpEntry> Items);

/// <summary>How Finished and Planned order and count their groups, read by the panel and the
/// clipboard alike so the two cannot drift apart.</summary>
public static class StandUpGroups
{
    /// <summary>The goals' groups in the reading's order, then No goal last.</summary>
    public static IEnumerable<StandUpGroup> InOrder(IReadOnlyList<StandUpGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return groups.Where(group => group.GoalTitle is not null).Concat(groups.Where(group => group.GoalTitle is null));
    }

    /// <summary>Every item, across the groups: the block's count.</summary>
    public static int ItemCount(IReadOnlyList<StandUpGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return groups.Sum(group => group.Items.Count);
    }
}

/// <summary>An impediment's state, in the order it moves through them
/// (<c>.devbook/domain/progress/domain.md#impediment-state</c>).</summary>
public enum StandUpImpedimentState
{
    New,
    Reported,
    Escalated,
    BeingHandled,
    Resolved
}

/// <summary>One open impediment as the stand-up lists it.</summary>
/// <param name="Id">The host's id for the impediment; handed back on a move.</param>
/// <param name="Title">The impediment's title.</param>
/// <param name="State">The state it is stored in.</param>
/// <param name="Moves">The states it may move to, in order; the picker offers these and
/// <paramref name="State"/>.</param>
/// <param name="With">Who it is with; null when nobody is named yet.</param>
/// <param name="DaysOpen">Its age in working days.</param>
/// <param name="CheckAgain">The day to look at it again; null with none.</param>
/// <param name="GoalTitle">The goal it holds back; null with none.</param>
/// <param name="Note">The person's note on it; null with none.</param>
public sealed record StandUpImpediment(
    string Id,
    string Title,
    StandUpImpedimentState State,
    IReadOnlyList<StandUpImpedimentState> Moves,
    string? With,
    int DaysOpen,
    DateOnly? CheckAgain,
    string? GoalTitle,
    string? Note = null);

/// <summary>
/// The stand-up beside the goals (<c>.devbook/domain/progress/features.md#the-stand-up</c>).
/// </summary>
/// <param name="Finished">The items done in the last window, by goal, No goal last.</param>
/// <param name="Planned">The planned items for the next window, by goal, No goal last.</param>
/// <param name="PlannedEffort">The planned items' effort, added up.</param>
/// <param name="Capacity">The pace scaled to the next window; null without a pace.</param>
/// <param name="Impediments">Every open impediment.</param>
/// <param name="ResolvedInLast">How many were resolved in the last window.</param>
public sealed record StandUpModel(
    IReadOnlyList<StandUpGroup> Finished,
    IReadOnlyList<StandUpGroup> Planned,
    int PlannedEffort,
    int? Capacity,
    IReadOnlyList<StandUpImpediment> Impediments,
    int ResolvedInLast)
{
    /// <summary>A stand-up with nothing in it.</summary>
    public static StandUpModel Empty { get; } = new([], [], 0, null, [], 0);
}

/// <summary>
/// The whole Goals view in plain values
/// (<c>.devbook/domain/progress/features.md#the-goals-view</c>): the library carries no
/// domain, so the host turns its stand-up reading, its goals and its candidates into this,
/// and the board's callbacks back into its own calls.
/// </summary>
/// <param name="Iteration">The length the switch has checked.</param>
/// <param name="Last">The last window: what Finished covers.</param>
/// <param name="Next">The next window: what Planned covers.</param>
/// <param name="Goals">Every goal, in the order the view lists them.</param>
/// <param name="NoGoal">Work in progress in scope that no goal holds.</param>
/// <param name="StandUp">The stand-up.</param>
/// <param name="Candidates">Everything the attach search may match: plans, tasks and
/// pull requests.</param>
public sealed record GoalsBoardModel(
    GoalsIteration Iteration,
    GoalsWindow Last,
    GoalsWindow Next,
    IReadOnlyList<GoalCardModel> Goals,
    IReadOnlyList<GoalsItem> NoGoal,
    StandUpModel StandUp,
    IReadOnlyList<GoalsItem> Candidates);

/// <summary>Attach <paramref name="Subject"/> to the goal <paramref name="GoalId"/>.</summary>
public sealed record GoalAttachRequest(string GoalId, GoalSubject Subject);

/// <summary>Detach <paramref name="Subject"/> from the goal <paramref name="GoalId"/>.</summary>
public sealed record GoalDetachRequest(string GoalId, GoalSubject Subject);

/// <summary>A goal's title, outcome and due date as its open card hands them back, trimmed:
/// the three replace what the goal says (<c>.devbook/domain/progress/features.md#setting-a-goal</c>).</summary>
/// <param name="GoalId">The host's id for the goal.</param>
/// <param name="Outcome">The one-line outcome; null when blank.</param>
/// <param name="DueDate">The due date; null when blank or unreadable, which the module refuses.</param>
public sealed record GoalEditRequest(string GoalId, string Title, string? Outcome, DateOnly? DueDate);

/// <summary>Move the impediment <paramref name="ImpedimentId"/> to <paramref name="State"/>.</summary>
public sealed record ImpedimentMoveRequest(string ImpedimentId, StandUpImpedimentState State);

/// <summary>Replace the note of the impediment <paramref name="ImpedimentId"/> with
/// <paramref name="Note"/>, trimmed; null clears it.</summary>
public sealed record ImpedimentNoteRequest(string ImpedimentId, string? Note);

/// <summary>A new goal as the New goal dialog hands it back, trimmed.</summary>
/// <param name="Outcome">The one-line outcome; null when left blank.</param>
/// <param name="DueDate">The due date; null when left blank, which the module refuses.</param>
public sealed record NewGoalDraft(string Title, string? Outcome, DateOnly? DueDate);
