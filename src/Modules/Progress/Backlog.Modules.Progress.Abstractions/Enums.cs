namespace Backlog.Modules.Progress.Abstractions;

/// <summary>A task's status as Progress reads it, in the words Tasks uses for it.
/// Progress reads it and never writes it.</summary>
public enum ProgressTaskStatus
{
    Draft,
    Ready,
    InProgress,
    Done,
    Archived
}

/// <summary>The state of a pull request a goal counts or a task links. <see cref="Unknown"/>
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

/// <summary>Which of an impediment's three fields an <see cref="ImpedimentEditDto"/> changes —
/// the only changes the phone may make (local ADR 0035 §8). Stored by name, so the names are
/// persisted values and never renamed.</summary>
public enum ImpedimentEditKind
{
    /// <summary>Moves it to <see cref="ImpedimentEditDto.State"/>, with
    /// <see cref="ImpedimentEditDto.With"/> as who it is with.</summary>
    State,

    /// <summary>Sets <see cref="ImpedimentEditDto.CheckAgain"/>, or clears it with null.</summary>
    CheckAgain,

    /// <summary>Replaces <see cref="ImpedimentEditDto.Note"/>, or clears it with null.</summary>
    Note
}
