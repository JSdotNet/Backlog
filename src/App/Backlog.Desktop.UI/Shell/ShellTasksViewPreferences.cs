using Backlog.Modules.Work.UI.Tasks;
using Backlog.Infrastructure.FileSystem;

using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The Board's columns and the Tasks list's Plans / Flat grouping, kept in the shell's own
/// navigation file beside the view it reopens on — the Tasks views'
/// <see cref="ITasksViewPreferences"/>. The Calendar's range is kept beside them by
/// <see cref="ShellCalendarViewPreferences"/>.
/// <para>
/// The Board's columns are per device. Plans / Flat is per window (local ADR 0072 §1,
/// ADR 0043 §5): the main window keeps its choice in the file, and a popped-out window
/// opens on what the main window remembered and keeps its own choice in memory, never
/// writing it back. One instance per window, beside the window's
/// <see cref="CurrentShellWindow"/> and <see cref="NavigationManager"/>: a window on the
/// <see cref="SurfaceWindowRoute"/> route, or with a key, is popped out; without either
/// it stands for the main window.
/// </para>
/// <para>
/// The file keeps each by its enum member's name, below the module that owns the
/// vocabulary. A name this build does not define — a newer build's, or a number — reads
/// as the default, and is kept in the file by the store.
/// </para>
/// </summary>
public sealed class ShellTasksViewPreferences(
    ShellNavigationStore store,
    CurrentShellWindow? window = null,
    NavigationManager? navigation = null) : ITasksViewPreferences
{
    /// <summary>A popped-out window's Plans / Flat, taken from the main window's at its
    /// first read and its own from then on.</summary>
    private TaskListGrouping? _windowGrouping;

    public TaskBoardGrouping BoardGrouping => Remembered(store.BoardColumns, TaskBoardGrouping.Status);

    public void SetBoardGrouping(TaskBoardGrouping grouping) => store.SetBoardColumns(grouping.ToString());

    // The window's key is read here rather than at construction: Routes writes it as the
    // window's root renders, which may follow this service's construction.
    public TaskListGrouping ListGrouping =>
        IsMainWindow ? MainWindowGrouping : _windowGrouping ??= MainWindowGrouping;

    public void SetListGrouping(TaskListGrouping grouping)
    {
        if (IsMainWindow) store.SetTasksGrouping(grouping.ToString());
        else _windowGrouping = grouping;
    }

    // Popped out by the route, as SurfaceWindow restores its scope, or by a key: the
    // harness and the desktop app's served /window/ pages render with no key, and the
    // route is known before the window's root writes one.
    private bool IsMainWindow =>
        window?.Key is null && (navigation is null || !SurfaceWindowRoute.IsCurrent(navigation));

    private TaskListGrouping MainWindowGrouping => Remembered(store.TasksGrouping, TaskListGrouping.Plans);

    /// <summary>
    /// A choice stored by its enum name, read back as that member, or
    /// <paramref name="fallback"/> when the name is not one this build defines.
    /// <see cref="Enum.TryParse{TEnum}(string?, out TEnum)"/> also takes a number, so a
    /// stored name must start with a letter and name a defined member.
    /// </summary>
    internal static TEnum Remembered<TEnum>(string? stored, TEnum fallback) where TEnum : struct, Enum =>
        !string.IsNullOrEmpty(stored)
        && char.IsLetter(stored[0])
        && Enum.TryParse<TEnum>(stored, out var value)
        && Enum.IsDefined(value)
            ? value
            : fallback;
}
