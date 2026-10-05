using Backlog.UI.Components.Badges;
using Backlog.UI.Components.Tasks;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Where a linked task came from (ADR 0020 §2 and §3). What is worth holding: the
/// badge is drawn from what the host hands in and nothing else, so a connector
/// added later needs no change; a connector this build does not know still shows
/// its key, under the neutral tone; local work draws nothing at all; and a
/// descriptor is never trusted to write CSS or a link the browser should not follow.
/// </summary>
public sealed class SourceBadgeTests
{
    private static readonly TaskSource GitHub =
        new("#412", "https://example.com/issues/412", "GitHub", "github", "color-primary-light");

    [Fact]
    public void A_known_connector_shows_its_name_and_the_key_and_opens_the_item_at_the_source()
    {
        using var context = new BunitContext();

        var badge = context.Render<SourceBadge>(p => p.Add(b => b.Source, GitHub).Add(b => b.TestId, "source"));
        var link = badge.Find("a[data-testid='source']");

        Assert.Equal("https://example.com/issues/412", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Equal("GitHub", link.QuerySelector(".badge__connector")!.TextContent);
        Assert.Equal("#412", link.QuerySelector(".badge__key")!.TextContent);
        Assert.Equal("GitHub #412", link.TextContent.Trim());
        Assert.Equal("Open GitHub #412 at the source", link.GetAttribute("title"));
        Assert.Contains("badge--linked", link.ClassList);
        Assert.DoesNotContain("badge--linked-neutral", link.ClassList);

        // The descriptor's token is the tone, as a custom property the class reads.
        Assert.Equal("--source-badge-tone: var(--color-primary-light)", link.GetAttribute("style"));

        // And its icon, when it names a provider mark this library draws.
        Assert.NotNull(link.QuerySelector("svg"));
    }

    [Fact]
    public void An_unknown_connector_shows_its_stored_key_under_the_neutral_badge()
    {
        using var context = new BunitContext();

        var badge = context.Render<SourceBadge>(p => p.Add(b => b.Source, new TaskSource("JIRA-301", "https://example.com/browse/JIRA-301")));
        var link = badge.Find("a");

        Assert.Contains("badge--linked-neutral", link.ClassList);
        Assert.Null(link.QuerySelector(".badge__connector"));
        Assert.Equal("JIRA-301", link.TextContent.Trim());
        Assert.Null(link.GetAttribute("style"));
        Assert.Null(link.QuerySelector("svg"));
    }

    [Fact]
    public void Local_work_draws_nothing()
    {
        using var context = new BunitContext();

        var badge = context.Render<SourceBadge>(p => p.Add(b => b.Source, (TaskSource?)null));

        Assert.Equal(string.Empty, badge.Markup.Trim());
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public void A_link_that_is_not_http_is_not_followed(string url)
    {
        using var context = new BunitContext();

        var badge = context.Render<SourceBadge>(p => p.Add(b => b.Source, GitHub with { Url = url }));

        Assert.Empty(badge.FindAll("a"));
        Assert.Equal("Linked to GitHub #412", badge.Find("span.badge").GetAttribute("title"));
    }

    [Theory]
    [InlineData("red; background: url(x)")]
    [InlineData("color-primary)")]
    public void A_token_that_is_not_a_plain_name_is_dropped_rather_than_written_into_the_style(string token)
    {
        using var context = new BunitContext();

        var badge = context.Render<SourceBadge>(p => p.Add(b => b.Source, GitHub with { ColorToken = token }));

        Assert.Null(badge.Find("a").GetAttribute("style"));
    }

    [Fact]
    public void A_row_draws_the_badge_outside_its_open_button_and_the_flags_on_its_metadata_line()
    {
        using var context = new BunitContext();
        var row = new TaskRow("t1", "Review the spacing tokens", Source: GitHub with
        {
            Blocked = true,
            BlockedReason = "Waiting on the design review",
            SeveralPlanLabels = true,
        });

        var item = context.Render<TaskItem>(p => p.Add(t => t.Task, row).Add(t => t.TestId, "row"));

        var badge = item.Find("[data-testid='row-source']");
        Assert.Equal("A", badge.TagName);
        Assert.Null(badge.Closest("button"));

        var line = item.Find("[data-testid='row-open']");
        var blocked = line.QuerySelector(".task-item__detail--sourceblocked")!;
        Assert.Contains("Waiting on the design review", blocked.TextContent);
        Assert.Contains("Blocked at the source", blocked.TextContent);
        Assert.NotNull(line.QuerySelector(".task-item__detail--sourceplanlabels"));
    }

    [Fact]
    public void A_local_row_draws_no_badge_slot()
    {
        using var context = new BunitContext();

        var item = context.Render<TaskItem>(p => p.Add(t => t.Task, new TaskRow("t1", "Local")).Add(t => t.TestId, "row"));

        Assert.Empty(item.FindAll(".task-item__badges"));
        Assert.Empty(item.FindAll("[data-testid='row-source']"));
    }

    [Fact]
    public void The_flags_read_in_the_order_a_reader_acts_on_them()
    {
        var source = GitHub with { Blocked = true, RemovedAtSource = true, DoneHereOpenAtSource = true, SeveralPlanLabels = true };

        Assert.Equal(
            [TaskDetailKind.SourceBlocked, TaskDetailKind.SourceRemoved, TaskDetailKind.SourceOpen, TaskDetailKind.SourcePlanLabels],
            source.Flags.Select(flag => flag.Kind));
        Assert.Equal("Blocked", Assert.Single((GitHub with { Blocked = true }).Flags).Text);
    }

    [Fact]
    public void The_panel_shows_the_badge_on_its_heading_line_and_the_flags_under_it()
    {
        using var context = new BunitContext();

        var panel = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Review the spacing tokens")
            .Add(t => t.Source, GitHub with { DoneHereOpenAtSource = true })
            .Add(t => t.TestId, "panel"));

        Assert.NotNull(panel.Find("[data-testid='panel-source']").Closest(".task-panel__header"));
        var flags = panel.Find("[data-testid='panel-source-flags']");
        Assert.Contains("Open at source", flags.TextContent);
        Assert.Null(flags.Closest(".task-panel__header"));
    }

    [Fact]
    public void A_panel_with_no_flags_draws_no_flag_list()
    {
        using var context = new BunitContext();

        var panel = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Ship it")
            .Add(t => t.Source, GitHub)
            .Add(t => t.TestId, "panel"));

        Assert.Empty(panel.FindAll("[data-testid='panel-source-flags']"));
    }
}
