using Backlog.UI.Components.Settings;
using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What the frame draws for the phone-width reflow. Which part is shown at which
/// width is a container query in the stylesheet, which a test renderer does not
/// lay out; what is testable here is the markup the query acts on - the mode class
/// the address sets, the way back to the list, and the row's badge and chevron.
/// The widths themselves are verified in a browser.
/// </summary>
public sealed class SettingsFrameNarrowTests
{
    [Fact]
    public void With_no_page_chosen_the_frame_is_the_index_and_goes_back_to_the_tasks()
    {
        using var context = new BunitContext();

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.Title, "Features"));

        var classes = frame.Find(".settings-frame").ClassList;
        Assert.Contains("settings-frame--index", classes);
        Assert.DoesNotContain("settings-frame--page", classes);
        Assert.Empty(frame.FindAll("[data-testid='back-to-menu-link']"));
        Assert.Equal("Back to your tasks", frame.Find("[data-testid='back-link']").GetAttribute("aria-label"));
    }

    [Fact]
    public void With_a_page_chosen_the_frame_offers_a_way_back_to_the_list()
    {
        using var context = new BunitContext();

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.Title, "Storage & backup")
            .Add(f => f.PageChosen, true));

        var classes = frame.Find(".settings-frame").ClassList;
        Assert.Contains("settings-frame--page", classes);
        Assert.DoesNotContain("settings-frame--index", classes);

        var toList = frame.Find("[data-testid='back-to-menu-link']");
        Assert.Equal("settings", toList.GetAttribute("href"));
        Assert.Equal("Back to all settings", toList.GetAttribute("aria-label"));
        Assert.Contains("settings-frame__back--menu", toList.ClassList);

        // The way back to the tasks stays in the markup; the stylesheet shows it
        // above phone width and the list's below.
        Assert.Contains("settings-frame__back--home", frame.Find("[data-testid='back-link']").ClassList);
    }

    [Fact]
    public void With_no_page_chosen_the_list_is_headed_Settings_with_no_eyebrow()
    {
        using var context = new BunitContext();

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.Title, "Features")
            .Add(f => f.Eyebrow, "Settings · Workspace"));

        // The wide layout's heading - the default page's - stays in the markup;
        // the stylesheet swaps it for the list's own below phone width.
        Assert.Equal("Features", frame.Find("[data-testid='settings-headline']").TextContent.Trim());

        var index = frame.Find(".settings-frame__heading--index");
        Assert.Equal("Settings", index.QuerySelector("h1[data-testid='settings-index-headline']")!.TextContent.Trim());
        Assert.Null(index.QuerySelector(".settings-frame__eyebrow"));
    }

    [Fact]
    public void With_a_page_chosen_the_frame_draws_no_list_heading()
    {
        using var context = new BunitContext();

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.Title, "Storage & backup")
            .Add(f => f.PageChosen, true));

        Assert.Empty(frame.FindAll(".settings-frame__heading--index"));
        Assert.Single(frame.FindAll("h1"));
    }

    [Fact]
    public void With_no_page_chosen_the_menu_marks_no_link_current()
    {
        using var context = new BunitContext();

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.Menu, MenuWithFeaturesCurrent()));

        Assert.Empty(frame.FindAll("a[aria-current]"));

        // The link keeps the raised surface and a screen-reader note for the wide
        // layout, where the default page sits beside the menu; the phone-width
        // stylesheet hides both.
        var features = frame.Find("a[data-page='features']");
        Assert.Contains("settings-menu-group__link--current", features.ClassList);
        Assert.NotNull(features.QuerySelector(".settings-menu-group__current-note.sr-only"));
    }

    [Fact]
    public void With_a_page_chosen_the_menu_marks_that_page_current()
    {
        using var context = new BunitContext();

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.PageChosen, true)
            .Add(f => f.Menu, MenuWithFeaturesCurrent()));

        var current = Assert.Single(frame.FindAll("a[aria-current='page']"));
        Assert.Equal("features", current.GetAttribute("data-page"));
        Assert.Empty(frame.FindAll(".settings-menu-group__current-note"));
    }

    [Fact]
    public void A_menu_group_outside_a_frame_marks_its_current_link()
    {
        using var context = new BunitContext();

        var group = context.Render<SettingsMenuGroup>(parameters => parameters
            .Add(g => g.Label, "Workspace")
            .Add(g => g.Items, new SettingsMenuItem[] { new("features", "Features", "settings/features", Current: true) }));

        Assert.Equal("page", group.Find("a").GetAttribute("aria-current"));
    }

    private static RenderFragment MenuWithFeaturesCurrent() => builder =>
    {
        builder.OpenComponent<SettingsMenuGroup>(0);
        builder.AddAttribute(1, nameof(SettingsMenuGroup.Label), "Workspace");
        builder.AddAttribute(2, nameof(SettingsMenuGroup.Items), (IReadOnlyList<SettingsMenuItem>)
        [
            new("features", "Features", "settings/features", Current: true),
            new("storage", "Storage & backup", "settings/storage")
        ]);
        builder.CloseComponent();
    };

    [Fact]
    public void The_host_names_where_the_list_is()
    {
        using var context = new BunitContext();

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.PageChosen, true)
            .Add(f => f.MenuHref, "app/settings"));

        Assert.Equal("app/settings", frame.Find("[data-testid='back-to-menu-link']").GetAttribute("href"));
    }

    [Fact]
    public void A_menu_item_draws_its_badge_only_when_it_has_a_value()
    {
        using var context = new BunitContext();

        var group = context.Render<SettingsMenuGroup>(parameters => parameters
            .Add(g => g.Label, "Workspace")
            .Add(g => g.Items, new SettingsMenuItem[]
            {
                new("features", "Features", "settings/features"),
                new("repositories", "Repositories", "settings/repositories", Badge: "4"),
                new("storage", "Storage & backup", "settings/storage", Badge: " ")
            }));

        var badges = group.FindAll("[data-testid='settings-menu-badge']");
        var badge = Assert.Single(badges);
        Assert.Equal("4", badge.TextContent.Trim());
        Assert.Equal("repositories", badge.Closest("a")!.GetAttribute("data-page"));
    }

    [Fact]
    public void Every_menu_row_carries_its_name_and_a_chevron_hidden_from_assistive_technology()
    {
        using var context = new BunitContext();

        var group = context.Render<SettingsMenuGroup>(parameters => parameters
            .Add(g => g.Label, "Workspace")
            .Add(g => g.Items, new SettingsMenuItem[]
            {
                new("features", "Features", "settings/features", Current: true),
                new("storage", "Storage & backup", "settings/storage")
            }));

        foreach (var link in group.FindAll("a"))
        {
            Assert.NotNull(link.QuerySelector(".settings-menu-group__name"));
            var chevron = link.QuerySelector(".settings-menu-group__chevron");
            Assert.NotNull(chevron);
            Assert.Equal("true", chevron!.GetAttribute("aria-hidden"));
        }

        Assert.Equal(
            ["Features", "Storage & backup"],
            group.FindAll(".settings-menu-group__name").Select(name => name.TextContent.Trim()));
    }
}
