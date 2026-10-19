using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The chrome a phone app wears: the tab bar along the bottom, and the line in
/// the title bar that says where sync stands.
/// </summary>
public sealed class ShellTests
{
    private static readonly IReadOnlyList<NavItem> Tabs =
    [
        new("", "Inbox", Match: true),
        new("note", "Note"),
        new("tasks", "Tasks")
    ];

    [Fact]
    public void The_tab_bar_is_a_labelled_nav_of_one_link_per_destination()
    {
        using var context = new BunitContext();

        var bar = context.Render<TabBar>(parameters => parameters
            .Add(p => p.Items, Tabs)
            .Add(p => p.AriaLabel, "Sections")
            .Add(p => p.TestId, "tab-bar"));

        var nav = bar.Find("nav[data-testid='tab-bar']");
        Assert.Equal("Sections", nav.GetAttribute("aria-label"));

        var links = bar.FindAll("a.tab-bar__tab");
        Assert.Equal(["Inbox", "Note", "Tasks"], links.Select(link => link.TextContent));
        Assert.NotNull(bar.Find("[data-testid='tab-bar-note']"));
    }

    [Fact]
    public void Only_the_tab_for_the_current_route_is_lit_and_the_root_does_not_light_everywhere()
    {
        using var context = new BunitContext();
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("tasks");

        var bar = context.Render<TabBar>(parameters => parameters
            .Add(p => p.Items, Tabs)
            .Add(p => p.TestId, "tab-bar"));

        var active = bar.FindAll(".tab-bar__tab--active");
        Assert.Single(active);
        Assert.Equal("Tasks", active[0].TextContent);
        Assert.Equal("page", bar.Find("[data-testid='tab-bar-tasks']").GetAttribute("aria-current"));
        Assert.Null(bar.Find("[data-testid='tab-bar-inbox']").GetAttribute("aria-current"));
    }

    [Fact]
    public void A_tab_given_a_glyph_draws_it_above_its_word_hidden_so_the_word_stays_its_name()
    {
        using var context = new BunitContext();
        IReadOnlyList<NavItem> tabs =
        [
            new("", "Today", Match: true, Icon: Glyph<SunIcon>()),
            new("standup", "Stand-up", Icon: Glyph<TargetIcon>())
        ];

        var bar = context.Render<TabBar>(parameters => parameters
            .Add(p => p.Items, tabs)
            .Add(p => p.TestId, "tab-bar"));

        var standUp = bar.Find("[data-testid='tab-bar-stand-up']");
        var parts = standUp.Children.Select(child => child.ClassName).ToArray();
        Assert.Equal(["tab-bar__icon", "tab-bar__label"], parts);

        var glyph = standUp.QuerySelector(".tab-bar__icon svg.target-icon")!;
        Assert.Equal("true", glyph.GetAttribute("aria-hidden"));
        Assert.Equal("false", glyph.GetAttribute("focusable"));
        Assert.Equal("20", glyph.GetAttribute("width"));

        Assert.Equal("Stand-up", standUp.TextContent.Trim());
        Assert.Equal(["Today", "Stand-up"], bar.FindAll("a.tab-bar__tab").Select(link => link.TextContent.Trim()));
        Assert.NotNull(bar.Find("[data-testid='tab-bar-today'] .tab-bar__icon svg.sun-icon"));
    }

    private static RenderFragment Glyph<TIcon>() where TIcon : IComponent => builder =>
    {
        builder.OpenComponent<TIcon>(0);
        builder.AddAttribute(1, "Size", "20");
        builder.CloseComponent();
    };

    [Fact]
    public void A_tab_without_a_glyph_is_its_word_alone()
    {
        using var context = new BunitContext();

        var bar = context.Render<TabBar>(parameters => parameters
            .Add(p => p.Items, Tabs)
            .Add(p => p.TestId, "tab-bar"));

        Assert.Empty(bar.FindAll(".tab-bar__icon"));
        Assert.Empty(bar.FindAll(".tab-bar__label"));
        Assert.Empty(bar.Find("[data-testid='tab-bar-note']").Children);
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(-30, "just now")]
    [InlineData(59, "just now")]
    [InlineData(3 * 60, "3m ago")]
    [InlineData(2 * 3600 + 5, "2h ago")]
    [InlineData(4 * 86400, "4d ago")]
    public void Elapsed_time_is_said_in_the_unit_a_glance_wants(int seconds, string expected) =>
        Assert.Equal(expected, SyncStatusLine.Relative(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Each_reading_has_its_own_words()
    {
        var now = new DateTimeOffset(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);

        Assert.Equal("Not paired", SyncStatusLine.Describe(SyncStatusReading.NotPaired, now));
        Assert.Equal("Synced 3m ago", SyncStatusLine.Describe(SyncStatusReading.Synced(now.AddMinutes(-3)), now));
        Assert.Equal("Not synced yet", SyncStatusLine.Describe(SyncStatusReading.Synced(null), now));
        Assert.Equal("Offline — 2 waiting", SyncStatusLine.Describe(SyncStatusReading.Offline(2), now));
    }

    [Fact]
    public void The_status_line_is_a_polite_status_region_whose_words_carry_the_state()
    {
        using var context = new BunitContext();

        var line = context.Render<SyncStatusLine>(parameters => parameters
            .Add(p => p.Reading, SyncStatusReading.Offline(1))
            .Add(p => p.TestId, "sync-status"));

        var region = line.Find("[data-testid='sync-status']");
        Assert.Equal("status", region.GetAttribute("role"));
        Assert.Equal("polite", region.GetAttribute("aria-live"));
        Assert.Equal("offline", region.GetAttribute("data-sync-state"));
        Assert.Contains("sync-status--offline", region.ClassList);
        Assert.Equal("Offline — 1 waiting", line.Find(".sync-status__text").TextContent);
        Assert.Equal("true", line.Find(".sync-status__dot").GetAttribute("aria-hidden"));
    }
}
