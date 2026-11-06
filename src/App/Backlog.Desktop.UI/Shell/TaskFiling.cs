namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// What the window's task list says of one Backlog entry, for <see cref="NotificationRepositories"/>:
/// not read yet (<see cref="NotRead"/>, the default — the notification stays unresolved and is
/// asked about again), read and not holding it (<see cref="NotHeld"/> — the other rules decide),
/// or filed under a repository (<see cref="Under"/>, null for none: the Brain's).
/// </summary>
public readonly record struct TaskFiling(bool ListRead, bool Held, string? Repository)
{
    /// <summary>The task list has not been read yet.</summary>
    public static TaskFiling NotRead => default;

    /// <summary>The task list is read and does not hold the entry.</summary>
    public static TaskFiling NotHeld => new(true, false, null);

    /// <summary>The entry is filed under <paramref name="repository"/>; null for none.</summary>
    public static TaskFiling Under(string? repository) => new(true, true, repository);
}
