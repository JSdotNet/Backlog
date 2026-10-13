using Backlog.Desktop.UI.Tasks;
using Backlog.UI.Components.Buttons;
using Backlog.UI.Components.Menus;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// How many times the shell, and the Tasks pane inside it, drew themselves.
/// <para>
/// Read off a child each one draws rather than off the component itself, because
/// bUnit's <c>RenderCount</c> on a component counts every render under it: a
/// keystroke that redraws only a row of the Tasks pane moves the shell's count too.
/// Each child here is drawn by its owner and by nothing else, and is drawn again on
/// every render of its owner, because a parameter it takes is one Blazor cannot
/// compare — the view option's label is a fragment, the entry menu's items a fresh
/// list.
/// </para>
/// </summary>
internal static class ShellRenderCount
{
    /// <summary>Renders of <see cref="Home"/> itself: its Tasks view option.</summary>
    public static int Shell(IRenderedComponent<Home> home) =>
        home.FindComponents<ToggleButton>().Single(option => option.Instance.TestId == "tasks-view-option").RenderCount;

    /// <summary>Renders of <see cref="TasksPane"/> itself: its entry menu.</summary>
    public static int TasksPane(IRenderedComponent<Home> home) =>
        home.FindComponent<TasksPane>().FindComponents<ContextMenu>().Single(menu => menu.Instance.TestId == "entry-menu").RenderCount;
}
