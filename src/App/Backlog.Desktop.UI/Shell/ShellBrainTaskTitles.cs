using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// A Brain note's task titles, answered from the window's own task list — what the
/// shell handed the Devbook pane as <c>TaskTitleFor</c> before the Devbook became a
/// registered surface (local ADR 0041 §1).
/// </summary>
public sealed class ShellBrainTaskTitles(TasksDesktopState state) : IBrainTaskTitles
{
    public string? TitleFor(string taskId) =>
        Guid.TryParse(taskId, out var id) && state.Rows.FirstOrDefault(row => row.Id == id) is { } row
            ? row.PreviewTitle
            : null;
}
