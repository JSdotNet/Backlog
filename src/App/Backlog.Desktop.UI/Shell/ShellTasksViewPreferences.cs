using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.FileSystem;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The Board's columns, kept per device in the shell's own navigation file beside the
/// view it reopens on — the Tasks views' <see cref="ITasksViewPreferences"/>. The
/// Calendar's range is kept beside them by <see cref="ShellCalendarViewPreferences"/>.
/// <para>
/// The file keeps each by its enum member's name, below the module that owns the
/// vocabulary. A name this build does not define — a newer build's, or a number — reads
/// as the default, and is kept in the file by the store.
/// </para>
/// </summary>
public sealed class ShellTasksViewPreferences(ShellNavigationStore store) : ITasksViewPreferences
{
    public TaskBoardGrouping BoardGrouping => Remembered(store.BoardColumns, TaskBoardGrouping.Status);

    public void SetBoardGrouping(TaskBoardGrouping grouping) => store.SetBoardColumns(grouping.ToString());

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
