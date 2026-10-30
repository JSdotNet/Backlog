using Backlog.Desktop.UI.Devbook;
using Backlog.Desktop.UI.Tasks;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The Devbook references of every task in the window's backlog filed to one
/// repository — the entry's own repository, else the one the list is anchored to, as
/// the task's chips read them — which the Domain view shows on the features and
/// requirements they name. What the shell handed the Devbook pane as
/// <c>TaskReferencesFor</c> before the Devbook became a registered surface (local ADR
/// 0043 §1).
/// </summary>
public sealed class ShellDevbookTaskReferences(TasksDesktopState state) : IDevbookTaskReferences
{
    public IReadOnlyList<DevbookTaskReferences> For(string repositoryAlias) =>
        [.. state.Rows
            .Where(row => row.Id is not null && row.DevbookReferences.Count > 0
                && string.Equals(state.DevbookRepositoryAliasFor(row), repositoryAlias, StringComparison.OrdinalIgnoreCase))
            .Select(row => new DevbookTaskReferences(row.TaskId, row.DevbookReferences))];
}
