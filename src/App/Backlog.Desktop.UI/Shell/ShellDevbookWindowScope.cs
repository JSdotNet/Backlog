using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The window's repository scope for a Devbook opened with no <c>scope</c> (local
/// ADR 0040 §6), answered from the window's own view state: its anchor, the one
/// repository the main window hands the Devbook as well.
/// </summary>
public sealed class ShellDevbookWindowScope(TasksDesktopState state) : IDevbookWindowScope
{
    public string RepositoryAlias => state.AnchorRepositoryAlias;
}
