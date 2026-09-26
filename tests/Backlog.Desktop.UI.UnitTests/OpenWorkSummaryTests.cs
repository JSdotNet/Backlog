using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The open-work summary at the end of the filter bar, and the report it opens.
/// <para>
/// The numbers are taken over the rows the repository scope leaves in view —
/// the pool the chips count — so a status, tag, My Day or Not waiting filter
/// changes the list and never the total. <c>OpenWorkReportTests</c> covers the
/// arithmetic; this covers what the pane feeds it and what pressing it does.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class OpenWorkSummaryTests
{
    private const string Summary = "[data-testid='open-work-summary']";
    private const string Report = "[data-testid='open-work-report']";

    private static async Task<TasksPaneHost> SeededAsync()
    {
        var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");

        await host.WriteEntryAsync("# Provision the box\n`task` `!ready` `repo:backlog` `effort:3` `#infra`\n");
        await host.WriteEntryAsync("# Write the changelog\n`task` `!draft` `repo:docs` `effort:2`\n");
        await host.WriteEntryAsync("# Size me later\n`task` `!ready` `repo:backlog`\n");
        await host.WriteEntryAsync("# Already shipped\n`task` `!done` `repo:backlog` `effort:5` `completed:2026-09-20`\n");

        await host.State.SelectAsync(null);

        return host;
    }

    [Fact]
    public async Task The_summary_is_a_button_saying_what_is_open_in_scope()
    {
        using var host = await SeededAsync();

        var pane = host.Render();
        var button = pane.Find(Summary);

        Assert.Equal("BUTTON", button.TagName);
        Assert.Equal("3 open · 5 points · 1 unestimated", button.TextContent.Trim());
        Assert.NotNull(button.Closest(".filter-bar .filter-group--summary"));

        // Three parts, so a narrow column can drop the tail and then the noun and
        // still show the number.
        Assert.Equal("3", button.QuerySelector(".open-work-summary__number")!.TextContent);
        Assert.Equal(" open", button.QuerySelector(".open-work-summary__noun")!.TextContent);
        Assert.Equal(" · 5 points · 1 unestimated", button.QuerySelector(".open-work-summary__detail")!.TextContent);

        // The shorthand spelled out for whoever cannot see the layout.
        Assert.Equal("3 open tasks in scope, 5 points estimated, 1 not estimated. Open the report.", button.GetAttribute("aria-label"));
        Assert.Equal(button.GetAttribute("aria-label"), button.GetAttribute("title"));
    }

    [Fact]
    public async Task The_numbers_follow_the_repository_scope()
    {
        using var host = await SeededAsync();

        var pane = host.Render();
        host.State.SetRepositoryFilter("docs");
        pane.Render();

        Assert.Equal("1 open · 2 points", pane.Find(Summary).TextContent.Trim());
    }

    [Fact]
    public async Task No_filter_on_the_bar_moves_the_numbers()
    {
        using var host = await SeededAsync();

        var pane = host.Render();
        const string expected = "3 open · 5 points · 1 unestimated";

        host.State.SetStatusFilter("draft");
        pane.Render();
        Assert.Equal(expected, pane.Find(Summary).TextContent.Trim());

        host.State.SetStatusFilter(string.Empty);
        host.State.ToggleTagFilter("infra");
        pane.Render();
        Assert.Equal(expected, pane.Find(Summary).TextContent.Trim());

        host.State.ToggleTagFilter("infra");
        host.State.SetNotWaitingFilter(true);
        host.State.SetMyDayFilter(new DateOnly(2026, 9, 24));
        pane.Render();
        Assert.Equal(expected, pane.Find(Summary).TextContent.Trim());
    }

    [Fact]
    public async Task Pressing_it_opens_the_report_and_escape_closes_it()
    {
        using var host = await SeededAsync();

        var pane = host.Render();
        Assert.Empty(pane.FindAll(Report));

        await pane.Find(Summary).ClickAsync(new());

        var dialog = pane.Find(Report);
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Contains("Open work", pane.Find($"{Report} .modal__title").TextContent, StringComparison.Ordinal);
        Assert.Equal("3", pane.Find("[data-testid='open-work-tile-open'] .metric-tile__value").TextContent.Trim());

        await dialog.KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(pane.FindAll(Report));
    }

    [Fact]
    public async Task The_close_button_closes_the_report()
    {
        using var host = await SeededAsync();

        var pane = host.Render();
        await pane.Find(Summary).ClickAsync(new());
        await pane.Find($"{Report} .modal__close").ClickAsync(new());

        Assert.Empty(pane.FindAll(Report));
    }

    [Fact]
    public async Task Pressing_a_task_in_needs_attention_opens_it_and_closes_the_report()
    {
        using var host = await TasksPaneHost.CreateAsync();

        await host.WriteEntryAsync("# Write the runbook\n`task` `!draft`\n");
        var late = await host.WriteEntryAsync("# Renew the certificate\n`task` `!ready` `due:2020-01-01`\n");
        await host.State.SelectAsync(null);

        // A filter hiding the task is widened on the way in, as a followed
        // "Waiting for" name is.
        host.State.SetStatusFilter("draft");

        var pane = host.Render();
        await pane.Find(Summary).ClickAsync(new());

        await pane.Find("[data-testid='open-work-overdue'] [data-testid='open-work-task']").ClickAsync(new());

        Assert.Empty(pane.FindAll(Report));
        Assert.Same(late, host.State.SelectedRow);
    }

    /// <summary>Waiting work is a total — how many tasks and what they weigh —
    /// with no task named: each row's own "Waiting for" line names what it waits
    /// on.</summary>
    [Fact]
    public async Task Waiting_work_is_a_count_and_its_effort()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var open = await host.WriteEntryAsync("# Write the runbook\n`task` `!draft`\n");
        await host.WriteEntryAsync($"# Publish it\n`task` `!ready` `effort:3` `after:{open.TaskId}`\n");
        await host.WriteEntryAsync($"# Announce it\n`task` `!ready` `after:{open.TaskId}`\n");
        await host.State.SelectAsync(null);

        var pane = host.Render();
        await pane.Find(Summary).ClickAsync(new());

        var waiting = pane.Find("[data-testid='open-work-waiting']");
        Assert.Equal("2 tasks · 3 points · 1 unestimated", waiting.QuerySelector(".open-work-report__waiting")!.TextContent.Trim());
        Assert.Empty(waiting.QuerySelectorAll("[data-testid='open-work-task']"));
        Assert.DoesNotContain("Publish it", waiting.TextContent, StringComparison.Ordinal);
    }

    /// <summary>The plans table scrolls inside its own region, so a narrow window
    /// never widens the dialog; a scrolling region is reachable by keyboard.</summary>
    [Fact]
    public async Task The_plans_table_scrolls_in_its_own_region()
    {
        using var host = await SeededAsync();

        var pane = host.Render();
        await pane.Find(Summary).ClickAsync(new());

        var table = pane.Find("[data-testid='open-work-report-plans']");
        var region = table.ParentElement!;

        Assert.Contains("open-work-report__plans", region.ClassList);
        Assert.Equal("region", region.GetAttribute("role"));
        Assert.Equal("0", region.GetAttribute("tabindex"));
        Assert.Equal("open-work-plans-heading", region.GetAttribute("aria-labelledby"));
    }

    /// <summary>A task in Needs attention is a name to follow, drawn the way a
    /// row's "Waiting for" names are — not a small ghost button — with when it is
    /// due as a quieter line beneath it.</summary>
    [Fact]
    public async Task A_task_in_needs_attention_is_a_link_like_title_over_its_detail()
    {
        using var host = await TasksPaneHost.CreateAsync();

        await host.WriteEntryAsync("# Renew the certificate\n`task` `!ready` `due:2020-01-01`\n");
        await host.State.SelectAsync(null);

        var pane = host.Render();
        await pane.Find(Summary).ClickAsync(new());

        var title = pane.Find("[data-testid='open-work-overdue'] [data-testid='open-work-task']");

        Assert.Equal("open-work-report__task-title", title.ClassName);
        Assert.Equal("Renew the certificate", title.TextContent.Trim());

        var detail = title.NextElementSibling!;
        Assert.Equal("open-work-report__detail", detail.ClassName);
        Assert.StartsWith("Due ", detail.TextContent.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_nothing_open_the_report_says_so()
    {
        using var host = await TasksPaneHost.CreateAsync();

        await host.WriteEntryAsync("# Already shipped\n`task` `!done` `completed:2026-09-20`\n");
        await host.State.SelectAsync(null);

        var pane = host.Render();
        Assert.Equal("0 open · 0 points", pane.Find(Summary).TextContent.Trim());

        await pane.Find(Summary).ClickAsync(new());

        Assert.Single(pane.FindAll("[data-testid='open-work-report-empty']"));
        Assert.Empty(pane.FindAll("[data-testid='open-work-report-totals']"));
    }
}
