using Microsoft.AspNetCore.Components;

// The component's published namespace, not the folder's: see the note at the top
// of Settings.razor for why the two must differ.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// What every app-wide page of the Settings screen is given by the screen: what
/// the pages share, and whether the page is the one on screen.
/// <para>
/// A page is kept alive once opened, so its state survives a tab switch, and it is
/// handed <see cref="SettingsScreenContext"/>, so it is redrawn whenever the screen
/// is. Together those would redraw every page ever opened on every tab switch -
/// the Repositories page alone takes the better part of a second - where the
/// screen used to draw only the page on screen. So a page that is not on screen
/// does not redraw; it is drawn again, from what it holds, when it is opened.
/// </para>
/// </summary>
public abstract class SettingsPageBase : ComponentBase
{
    /// <summary>What the pages of the screen share; see
    /// <see cref="SettingsScreenContext"/>.</summary>
    [Parameter, EditorRequired]
    public SettingsScreenContext Screen { get; set; } = default!;

    /// <summary>Whether this page is the one on screen.</summary>
    [Parameter]
    public bool Active { get; set; } = true;

    protected override bool ShouldRender() => Active;
}
