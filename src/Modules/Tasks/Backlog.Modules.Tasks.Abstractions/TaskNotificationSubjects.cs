namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// How the Tasks context names a task a notification can be about, and the reason it
/// closes with when the task ends (<c>.devbook/domain/notifications/domain.md#subject</c>).
/// <para>
/// Plain strings, because this project may not see the Notifications context's
/// abstractions (<c>ModuleBoundaryTests</c>). The adapter that does — the task store in
/// <c>Backlog.Infrastructure.Sqlite</c>, which every write passes through — builds the
/// subject from these, and so does whatever publishes about a task.
/// </para>
/// </summary>
public static class TaskNotificationSubjects
{
    /// <summary>The subject context a task is named under.</summary>
    public const string Context = "tasks";

    /// <summary>A task, by its id in the <c>D</c> format.</summary>
    public const string Task = "task";

    /// <summary>Why a notification about a task closes: it was marked Done, archived, or
    /// deleted.</summary>
    public const string EndedReason = "Task done";

    /// <summary>A task's subject id.</summary>
    public static string Id(Guid taskId) => taskId.ToString("D");

    /// <summary>Whether a task in <paramref name="status"/>, deleted or not, has ended: Done
    /// and Archived are the statuses past the work, and a deleted task is gone.</summary>
    public static bool HasEnded(EntryStatus status, bool deleted) =>
        deleted || status is EntryStatus.Done or EntryStatus.Archived;
}
