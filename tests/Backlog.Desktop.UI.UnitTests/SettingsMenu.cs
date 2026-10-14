using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Drives the Settings screen's menu the way a reader does: by the words on a link.
/// <para>
/// A menu item is a link to the page's own address, and a test renderer follows no
/// link. So <see cref="Open"/> reads the address off the link and hands it to the
/// screen as the router would - as the <c>PageId</c> route parameter on the same
/// instance - which is what proves the link goes where it says.
/// </para>
/// </summary>
internal static class SettingsMenu
{
    private const string Links = "nav[aria-label='Settings pages'] a[data-page]";

    private const string AddressPrefix = "settings/";

    /// <summary>The page's name on a link, without the badge beside it.</summary>
    private const string Name = ".settings-menu-group__name";

    /// <summary>Every page the menu lists, in menu order.</summary>
    public static string[] Pages(IRenderedComponent<Settings> component) =>
        [.. component.FindAll(Links).Select(Label)];

    /// <summary>The page the menu marks as the one on screen.</summary>
    public static string Current(IRenderedComponent<Settings> component) =>
        Label(component.Find(Links + "[aria-current='page']"));

    /// <summary>The badge the menu draws beside the page with id
    /// <paramref name="pageId"/>, or null when it draws none.</summary>
    public static string? Badge(IRenderedComponent<Settings> component, string pageId) =>
        component.Find($"{Links}[data-page='{pageId}']")
            .QuerySelector("[data-testid='settings-menu-badge']")?.TextContent.Trim();

    /// <summary>Follows the menu link labelled <paramref name="title"/>.</summary>
    public static void Open(IRenderedComponent<Settings> component, string title)
    {
        var href = component.FindAll(Links).Single(link => Label(link) == title).GetAttribute("href")!;

        Assert.StartsWith(AddressPrefix, href, StringComparison.Ordinal);
        Navigate(component, href[AddressPrefix.Length..]);
    }

    /// <summary>Moves the screen to <c>settings/<paramref name="pageId"/></c>, or to
    /// <c>settings</c> for null.</summary>
    public static void Navigate(IRenderedComponent<Settings> component, string? pageId) =>
        component.Render(parameters => parameters.Add(settings => settings.PageId, pageId));

    /// <summary>Opens the Repositories page and presses Edit on the row whose alias
    /// is <paramref name="alias"/> - the first row when null - so the repository's
    /// card is on screen under it.</summary>
    public static void OpenRepository(IRenderedComponent<Settings> component, string? alias = null)
    {
        Open(component, "Repositories");
        EditRepository(component, alias);
    }

    /// <summary>Presses Edit on a row of the Repositories table already on screen,
    /// by the button's accessible name - the first row's when
    /// <paramref name="alias"/> is null.</summary>
    public static void EditRepository(IRenderedComponent<Settings> component, string? alias = null)
    {
        var button = alias is null
            ? component.FindAll("[data-testid='repo-edit']")[0]
            : component.Find($"[data-testid='repo-edit'][aria-label='Edit {alias}']");

        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        button.Click();
    }

    private static string Label(AngleSharp.Dom.IElement link) =>
        (link.QuerySelector(Name) ?? link).TextContent.Trim();
}
