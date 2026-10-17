namespace Backlog.Modules.Progress.Abstractions;

/// <summary>How long each sprint of a series lasts
/// (<c>.devbook/domain/progress/domain.md#sprint-length</c>). The two values are the
/// only lengths a series may have.</summary>
public enum SprintLength
{
    /// <summary>Seven days.</summary>
    OneWeek = 7,

    /// <summary>Fourteen days.</summary>
    TwoWeeks = 14
}

/// <summary>One way a series picks a sprint's goal tasks
/// (<c>.devbook/domain/progress/domain.md#goal-rule-kind</c>).</summary>
public enum GoalRuleKind
{
    /// <summary>The members brought in by the chosen plans.</summary>
    InPlans,

    /// <summary>The members whose sprint item is marked as a goal task.</summary>
    MarkedAsGoal,

    /// <summary>The members carrying one of the chosen task tags.</summary>
    Tagged,

    /// <summary>Every member of the sprint.</summary>
    Everything
}

/// <summary>What closing a sprint did with one unfinished member
/// (<c>.devbook/domain/progress/domain.md#close-outcome</c>). None of the three
/// changes the task.</summary>
public enum CloseOutcome
{
    /// <summary>The task joins the next sprint, as a directly added member.</summary>
    Carry,

    /// <summary>The task leaves the sprint and joins no other.</summary>
    BackToBacklog,

    /// <summary>The task is recorded as dropped in this sprint only.</summary>
    Drop
}

/// <summary>A member task's status, in the words Tasks uses for it. Progress
/// reads it and never writes it.</summary>
public enum SprintTaskStatus
{
    Draft,
    Ready,
    InProgress,
    Done,
    Archived
}

/// <summary>
/// Where one member stands in the sprint, for the counts: the first that applies
/// of <see cref="Done"/>, <see cref="Blocked"/>, <see cref="InProgress"/> and
/// <see cref="NotStarted"/>.
/// </summary>
public enum SprintWorkState
{
    /// <summary>Draft or Ready, and not blocked.</summary>
    NotStarted,

    /// <summary>In progress, and not blocked.</summary>
    InProgress,

    /// <summary>Marked blocked by hand, and not done.</summary>
    Blocked,

    /// <summary>Done by <c>.devbook/domain/progress/domain.md#done</c>: the task
    /// is Done, or every pull request linked to it is merged. An archived task that is
    /// neither counts as not started.</summary>
    Done
}

/// <summary>The state of a pull request linked to a member task. <see cref="Unknown"/>
/// until something reads it from GitHub.</summary>
public enum LinkedPullRequestState
{
    Unknown,
    Open,
    Draft,
    Merged,
    Closed
}

/// <summary>The three kinds of work a goal holds
/// (<c>.devbook/domain/progress/domain.md#goal-attachment</c>). Stored by name, so the
/// names are persisted values and never renamed.</summary>
public enum GoalAttachmentKind
{
    /// <summary>A plan, by its plan tag: every entry filed under the tag.</summary>
    Plan,

    /// <summary>A task, by its id, with the pull requests it links.</summary>
    Task,

    /// <summary>A pull request, by its repository and number.</summary>
    PullRequest
}

/// <summary>Where an impediment stands
/// (<c>.devbook/domain/progress/domain.md#impediment-state</c>). It moves forward only —
/// New, Reported, Escalated, Being handled — and any open state may move to Resolved.
/// Stored by name, so the names are persisted values and never renamed.</summary>
public enum ImpedimentState
{
    /// <summary>Raised, and not yet reported to anyone.</summary>
    New,

    /// <summary>Told to whoever can remove it.</summary>
    Reported,

    /// <summary>Taken higher because the report was not enough. A state a move may skip.</summary>
    Escalated,

    /// <summary>Someone is working on removing it. Shown as "Being handled".</summary>
    BeingHandled,

    /// <summary>No longer in the way. A resolved impediment moves to no other state.</summary>
    Resolved
}

/// <summary>How long the window is that a person reads their goals and stand-up in
/// (<c>.devbook/domain/progress/domain.md#iteration</c>,
/// <c>.devbook/domain/progress/context.md#iteration-length</c>). Stored as the setting's
/// values — <c>day</c>, <c>week</c>, <c>two-weeks</c> and <c>month</c> — mapped explicitly by
/// each store, never as the member names or numbers; a store reads a member name as a fallback.</summary>
public enum IterationLength
{
    /// <summary>One worked day of the person's working week.</summary>
    Day,

    /// <summary>Monday to Friday.</summary>
    Week,

    /// <summary>Fourteen days from the two-week start, or a whole number of fourteen days after it.</summary>
    TwoWeeks,

    /// <summary>One calendar month, from its first day to its last.</summary>
    Month
}

/// <summary>Whether a goal lands in time (<c>.devbook/domain/progress/domain.md#goal-forecast</c>).
/// Read afresh on every reading and never stored.</summary>
public enum GoalStanding
{
    /// <summary>It lands on or before its due date, nothing open holds it back, or it has no open item.</summary>
    OnTrack,

    /// <summary>It lands after its due date, has no landing date with items still open, or
    /// an open impediment holds back one of its items.</summary>
    AtRisk
}
