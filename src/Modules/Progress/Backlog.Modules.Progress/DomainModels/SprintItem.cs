using Backlog.Modules.Progress.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.DomainModels;

/// <summary>
/// What a sprint records about one member task, by the task's id
/// (<c>.devbook/domain/progress/domain.md#sprint-item</c>). The note and the
/// open-question mark belong to the sprint, not to the task.
/// </summary>
public sealed class SprintItem
{
    private SprintItem(
        Guid taskId,
        bool addedDirectly,
        DateOnly? addedOn,
        bool isGoal,
        string? note,
        bool openQuestion,
        bool keptAtClose,
        CloseOutcome? outcome)
    {
        TaskId = taskId;
        AddedDirectly = addedDirectly;
        AddedOn = addedOn;
        IsGoal = isGoal;
        Note = note;
        OpenQuestion = openQuestion;
        KeptAtClose = keptAtClose;
        Outcome = outcome;
    }

    public Guid TaskId { get; }

    /// <summary>Added by a person, rather than only brought in by a linked plan.</summary>
    public bool AddedDirectly { get; private set; }

    /// <summary>The day the task was added directly; null for one only a plan brought in.</summary>
    public DateOnly? AddedOn { get; private set; }

    public bool IsGoal { get; private set; }

    public string? Note { get; private set; }

    public bool OpenQuestion { get; private set; }

    /// <summary>The task was a member when the sprint closed, so the closed sprint
    /// keeps it whatever its plans hold now.</summary>
    public bool KeptAtClose { get; private set; }

    public CloseOutcome? Outcome { get; private set; }

    /// <summary>Nothing recorded: an item a plan-brought task does not need.</summary>
    internal bool IsEmpty =>
        !AddedDirectly && !IsGoal && Note is null && !OpenQuestion && !KeptAtClose && Outcome is null;

    internal static SprintItem Direct(Guid taskId, DateOnly addedOn) =>
        new(taskId, addedDirectly: true, addedOn, isGoal: false, note: null, openQuestion: false, keptAtClose: false, outcome: null);

    internal static SprintItem Recorded(Guid taskId) =>
        new(taskId, addedDirectly: false, addedOn: null, isGoal: false, note: null, openQuestion: false, keptAtClose: false, outcome: null);

    /// <summary>Rehydration from storage.</summary>
    public static SprintItem Restore(
        Guid taskId,
        bool addedDirectly,
        DateOnly? addedOn,
        bool isGoal,
        string? note,
        bool openQuestion,
        bool keptAtClose,
        CloseOutcome? outcome) =>
        new(taskId, addedDirectly, addedOn, isGoal, note, openQuestion, keptAtClose, outcome);

    internal void MarkAddedDirectly(DateOnly today)
    {
        if (AddedDirectly) return;

        AddedDirectly = true;
        AddedOn = today;
    }

    internal void ClearAddedDirectly()
    {
        AddedDirectly = false;
        AddedOn = null;
    }

    internal Result MarkGoal(bool isGoal)
    {
        IsGoal = isGoal;
        return Result.Success();
    }

    /// <summary>A one-line note (<c>progress.note_not_one_line</c>); blank clears it.</summary>
    internal Result SetNote(string? note)
    {
        if (note is not null && (note.Contains('\n', StringComparison.Ordinal) || note.Contains('\r', StringComparison.Ordinal)))
        {
            return Result.Failure(ProgressErrors.NoteNotOneLine());
        }

        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return Result.Success();
    }

    internal Result SetOpenQuestion(bool openQuestion)
    {
        OpenQuestion = openQuestion;
        return Result.Success();
    }

    internal void KeepAtClose(CloseOutcome? outcome)
    {
        KeptAtClose = true;
        Outcome = outcome;
    }
}
