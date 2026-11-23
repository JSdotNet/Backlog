using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The icon-only control, as a button and as a link to a page of the app, named by its label
/// because its glyph names nothing (<c>.devbook/design/accessibility.md#iconography-accessibility</c>).
/// The samples link a bell to a notifications page.
/// </summary>
public sealed class IconButtonTests
{
    private static IRenderedComponent<IconButton> Bell(BunitContext context, string at, NavLinkMatch? match = null)
    {
        context.Services.GetRequiredService<NavigationManager>().NavigateTo(at);

        return context.Render<IconButton>(parameters =>
        {
            parameters
                .Add(b => b.AriaLabel, "Notifications")
                .Add(b => b.Href, "notifications")
                .Add(b => b.CssClass, "app-bar__notifications")
                .Add(b => b.TestId, "app-notifications")
                .AddChildContent("<svg class=\"bell-icon\"></svg>");
            if (match is { } value) parameters.Add(b => b.Match, value);
        });
    }

    [Fact]
    public void Without_an_href_it_is_still_the_button_it_was()
    {
        using var context = new BunitContext();

        var button = context.Render<IconButton>(parameters => parameters
            .Add(b => b.AriaLabel, "Settings")
            .Add(b => b.TestId, "app-settings"));

        var element = button.Find("[data-testid='app-settings']");
        Assert.Equal("BUTTON", element.TagName, ignoreCase: true);
        Assert.Equal("button", element.GetAttribute("type"));
        Assert.Equal("btn btn--icon", element.GetAttribute("class"));
        Assert.Empty(button.FindAll("a"));
    }

    [Fact]
    public void An_href_renders_a_link_with_the_same_name_classes_and_test_id()
    {
        using var context = new BunitContext();

        var bell = Bell(context, "");

        var link = bell.Find("[data-testid='app-notifications']");
        Assert.Equal("A", link.TagName, ignoreCase: true);
        Assert.Equal("notifications", link.GetAttribute("href"));
        Assert.Equal("Notifications", link.GetAttribute("aria-label"));
        Assert.Equal("Notifications", link.GetAttribute("title"));
        Assert.Equal(["btn", "btn--icon", "app-bar__notifications"], link.ClassList);
        Assert.Equal("true", link.QuerySelector("span")!.GetAttribute("aria-hidden"));
        Assert.NotNull(link.QuerySelector("span .bell-icon"));
        Assert.Empty(bell.FindAll("button"));
    }

    [Fact]
    public void A_link_away_from_its_page_is_not_current()
    {
        using var context = new BunitContext();

        var link = Bell(context, "inbox").Find("a");

        Assert.Null(link.GetAttribute("aria-current"));
        Assert.DoesNotContain("btn--icon-current", link.ClassList);
    }

    [Theory]
    [InlineData("notifications")]
    [InlineData("notifications/0f6e2c3a-1b4d-4e5f-8a9b-0c1d2e3f4a5b")]
    public void A_link_is_current_on_its_page_and_the_pages_under_it(string at)
    {
        using var context = new BunitContext();

        var link = Bell(context, at).Find("a");

        Assert.Equal("page", link.GetAttribute("aria-current"));
        Assert.Contains("btn--icon-current", link.ClassList);
    }

    [Fact]
    public void Matching_all_keeps_a_link_off_the_pages_under_it()
    {
        using var context = new BunitContext();

        var link = Bell(context, "notifications/0f6e2c3a-1b4d-4e5f-8a9b-0c1d2e3f4a5b", NavLinkMatch.All).Find("a");

        Assert.Null(link.GetAttribute("aria-current"));
    }

    [Fact]
    public void A_disabled_link_has_no_href_and_says_it_is_disabled()
    {
        using var context = new BunitContext();

        var link = context.Render<IconButton>(parameters => parameters
            .Add(b => b.AriaLabel, "Notifications")
            .Add(b => b.Href, "notifications")
            .Add(b => b.Disabled, true)).Find("a");

        Assert.False(link.HasAttribute("href"));
        Assert.Equal("true", link.GetAttribute("aria-disabled"));
        Assert.Equal("Notifications", link.GetAttribute("aria-label"));
    }
}
