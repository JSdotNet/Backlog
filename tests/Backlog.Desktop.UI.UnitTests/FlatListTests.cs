using System.Globalization;

using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Tasks list's <b>Flat</b> view (local ADR 0062 §3;
/// <c>.devbook/domain/tasks/features.md#plans-view</c>): the entries in no plan, plus a
/// plan entry in today's My Day wearing "☀ My Day · &lt;plan&gt;" on its second line,
/// under quiet text tabs, with one line of text actions while rows are picked and a
/// footer saying where the plan entries went.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class FlatListTests
{
    private const string Backlog = "JSdotNet/Backlog";
    private const string BacklogClone = @"D:\Repos\Backlog";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static string Token(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Seeded(
        TasksPaneHost Host,
        TasksSessionRequestTests.FakeEntrySessionRequests Queue,
        EntryRow Runbook,
        EntryRow Certificate,
        EntryRow Prompt,
        EntryRow PlanEntry,
        EntryRow PlanEntryOnMyDay,
        EntryRow Done);

    private static async Task<Seeded> SeedAsync()
    {
        var host = await TasksPaneHost.CreateAsync(Backlog);
        host.GitHub.Settings.SetCloneDirectory("backlog", BacklogClone);

        var runbook = await host.WriteEntryAsync("# Write the runbook\n`task` `!ready` `#ops`\n");
        var certificate = await host.WriteEntryAsync($"# Renew the certificate\n`task` `!draft` `myday:{Token(Today)}`\n");
        var prompt = await host.WriteEntryAsync($"# Add the export\n`prompt` `!ready` `repo:{Backlog}`\n");
        var planEntry = await host.WriteEntryAsync("# 14 - Issue device-bound tokens +zta\n`prompt` `!ready`\n");
        var planEntryOnMyDay = await host.WriteEntryAsync($"# 16 - Keep sessions alive +zta\n`prompt` `!in-progress` `myday:{Token(Today)}`\n");
        var done = await host.WriteEntryAsync($"# Filed it\n`task` `!done` `completed:{Token(Today.AddDays(-3))}`\n");
        await host.State.SelectAsync(null);

        var queue = new TasksSessionRequestTests.FakeEntrySessionRequests();
        host.Context.Services.AddSingleton<IEntrySessionRequests>(queue);
        host.Context.Services.AddSingleton<ISessionQueueOrder>(queue.Order);
        host.Context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));

        return new Seeded(host, queue, runbook, certificate, prompt, planEntry, planEntryOnMyDay, done);
    }

    private static IRenderedComponent<TasksPane> RenderFlat(TasksPaneHost host) =>
        host.Context.Render<TasksPane>(parameters => parameters.Add(pane => pane.ListGrouping, TaskListGrouping.Flat));

    private static string RowTestId(EntryRow row) => $"entry-list-{row.TaskId}";

    private static IReadOnlyList<string> Titles(IRenderedComponent<TasksPane> pane) =>
        [.. pane.FindAll("[data-testid='entry-list'] .task-item__title").Select(title => title.TextContent.Trim())];

    private static Task TickAsync(IRenderedComponent<TasksPane> pane, EntryRow row) =>
        pane.Find($"[data-testid='{RowTestId(row)}-select'] input").ClickAsync(new());

    private static string Text(AngleSharp.Dom.IElement element) => element.TextContent.Trim();

    // --- What Flat lists ------------------------------------------------------

    [Fact]
    public async Task Flat_lists_the_unplanned_entries_and_leaves_the_plan_entries_under_Plans()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);

        pane.WaitForAssertion(() =>
        {
            var titles = Titles(pane);
            Assert.Contains("Write the runbook", titles);
            Assert.Contains("Renew the certificate", titles);
            Assert.Contains("Add the export", titles);
            Assert.DoesNotContain("14 - Issue device-bound tokens +zta", titles);
        });
    }

    [Fact]
    public async Task A_plan_entry_in_todays_My_Day_stays_in_Flat_and_names_its_plan_on_its_second_line()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);

        pane.WaitForAssertion(() =>
        {
            Assert.Contains("16 - Keep sessions alive +zta", Titles(pane));
            var myDay = pane.Find($"[data-testid='{RowTestId(seeded.PlanEntryOnMyDay)}'] .task-item__detail--myday");
            Assert.Contains("☀", myDay.TextContent, StringComparison.Ordinal);
            Assert.EndsWith("My Day · zta", Text(myDay), StringComparison.Ordinal);
        });

        // An unplanned entry in My Day keeps its plain fact: it names no plan.
        Assert.EndsWith("My Day", Text(pane.Find($"[data-testid='{RowTestId(seeded.Certificate)}'] .task-item__detail--myday")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pane_with_no_host_choice_still_lists_every_entry_under_the_chip_bar()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = host.Render();

        pane.WaitForAssertion(() => Assert.Contains("14 - Issue device-bound tokens +zta", Titles(pane)));
        Assert.NotEmpty(pane.FindAll("[data-testid='myday-filter-option']"));
        Assert.Empty(pane.FindAll("[data-testid='flat-quick-views']"));
    }

    // --- The quiet bar --------------------------------------------------------

    [Fact]
    public async Task The_bar_draws_quiet_tabs_in_place_of_the_chip_rows()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='flat-quick-view']")));

        Assert.Equal(
            ["Everything", "My Day 2", "In the queue 0", "Waiting 0"],
            pane.FindAll("[data-testid='flat-quick-view']").Select(tab => string.Join(' ', Text(tab).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));
        Assert.Equal("true", pane.Find("[data-testid='flat-quick-view'][data-view='Everything']").GetAttribute("aria-checked"));
        Assert.Equal(["Open", "Draft", "Ready", "In progress", "Done"], pane.FindAll("[data-testid='flat-status-tab']").Select(Text));
        Assert.Equal("true", pane.Find("[data-testid='flat-status-tab'][data-status='open']").GetAttribute("aria-checked"));
        Assert.NotNull(pane.Find("[data-testid='flat-type-menu']"));
        Assert.NotNull(pane.Find("[data-testid='flat-source-menu']"));
        Assert.NotNull(pane.Find("[data-testid='flat-tag-menu']"));
        Assert.Equal("Sort: status", Text(pane.Find("[data-testid='sort-by-status']")));

        // The chip rows are gone: no scope chips, no status chips, no tag strip.
        Assert.Empty(pane.FindAll("[data-testid='myday-filter-option']"));
        Assert.Empty(pane.FindAll("[data-testid='readyinplan-filter-option']"));
        Assert.Empty(pane.FindAll("[data-testid='status-filter-option']"));
        Assert.Empty(pane.FindAll("[data-testid='tag-filter-row']"));
        Assert.Empty(pane.FindAll("[data-testid='bulk-select-toggle']"));
    }

    [Fact]
    public async Task My_Day_narrows_Flat_to_todays_entries_and_Everything_widens_it_again()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='flat-quick-view']")));

        await pane.Find("[data-testid='flat-quick-view'][data-view='MyDay']").ClickAsync(new());

        pane.WaitForAssertion(() =>
            Assert.Equal(["Renew the certificate", "16 - Keep sessions alive +zta"], Titles(pane).OrderByDescending(title => title.StartsWith('R')).ToList()));
        Assert.Equal("true", pane.Find("[data-testid='flat-quick-view'][data-view='MyDay']").GetAttribute("aria-checked"));

        await pane.Find("[data-testid='flat-quick-view'][data-view='Everything']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Contains("Write the runbook", Titles(pane)));
        Assert.Null(host.State.MyDayOn);
    }

    [Fact]
    public async Task The_status_tabs_open_on_Open_and_Done_lists_the_finished_ones()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);
        pane.WaitForAssertion(() => Assert.DoesNotContain("Filed it", Titles(pane)));

        await pane.Find("[data-testid='flat-status-tab'][data-status='done']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Equal(["Filed it"], host.State.FilteredRows.Select(row => row.PreviewTitle)));
    }

    // --- The footer -----------------------------------------------------------

    [Fact]
    public async Task The_footer_counts_the_unplanned_entries_the_bar_leaves_out()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);
        pane.WaitForAssertion(() => Assert.Equal("Plan entries live under Plans", Text(pane.Find("[data-testid='flat-footer']"))));

        await pane.Find("[data-testid='flat-quick-view'][data-view='MyDay']").ClickAsync(new());

        // Runbook and the export are open, unplanned and out of today's My Day.
        pane.WaitForAssertion(() => Assert.Equal("2 more unplanned · plan entries live under Plans", Text(pane.Find("[data-testid='flat-footer']"))));
    }

    [Fact]
    public async Task The_heading_counts_the_open_unplanned_entries()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);

        // Runbook, the certificate and the export; the finished one is not counted.
        pane.WaitForAssertion(() => Assert.Equal("Unplanned 3 · plan entries only from My Day", Text(pane.Find("[data-testid='flat-heading']"))));
    }

    // --- Flat is its own view, whatever the rail had selected --------------------

    [Fact]
    public async Task A_plan_selected_on_the_rail_does_not_narrow_Flat()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = host.Context.Render<TasksPane>(parameters => parameters.Add(p => p.ListGrouping, TaskListGrouping.Plans));
        host.State.SelectRailItem(PlanRailItem.Plan, "+zta", Today);
        pane.WaitForAssertion(() => Assert.Equal("+zta", host.State.SelectedPlan));

        pane.Render(parameters => parameters.Add(p => p.ListGrouping, TaskListGrouping.Flat));

        pane.WaitForAssertion(() =>
        {
            var titles = Titles(pane);
            Assert.Contains("Write the runbook", titles);
            Assert.Contains("Renew the certificate", titles);
            Assert.Contains("Add the export", titles);
            Assert.Contains("16 - Keep sessions alive +zta", titles);
            Assert.DoesNotContain("14 - Issue device-bound tokens +zta", titles);
            Assert.Equal("Plan entries live under Plans", Text(pane.Find("[data-testid='flat-footer']")));
        });
        Assert.Null(host.State.SelectedPlan);
    }

    [Fact]
    public async Task Unplanned_selected_on_the_rail_keeps_the_plan_entry_on_My_Day_in_Flat()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = host.Context.Render<TasksPane>(parameters => parameters.Add(p => p.ListGrouping, TaskListGrouping.Plans));
        host.State.SelectRailItem(PlanRailItem.Unplanned, null, Today);
        pane.WaitForAssertion(() => Assert.True(host.State.IsRailItemSelected(PlanRailItem.Unplanned)));

        pane.Render(parameters => parameters.Add(p => p.ListGrouping, TaskListGrouping.Flat));

        pane.WaitForAssertion(() =>
        {
            var titles = Titles(pane);
            Assert.Contains("Write the runbook", titles);
            Assert.Contains("16 - Keep sessions alive +zta", titles);
        });
        Assert.False(host.State.IsRailItemSelected(PlanRailItem.Unplanned));
    }

    // --- Selection ------------------------------------------------------------

    [Fact]
    public async Task Picking_rows_shows_one_line_of_text_actions()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);
        pane.WaitForAssertion(() => Assert.Contains("Write the runbook", Titles(pane)));
        Assert.Empty(pane.FindAll("[data-testid='flat-selection']"));

        await TickAsync(pane, seeded.Runbook);

        pane.WaitForAssertion(() =>
        {
            var line = pane.Find("[data-testid='flat-selection']");
            Assert.Equal("1 selected", Text(line.QuerySelector("[data-testid='flat-selection-count']")!));
            Assert.NotNull(line.QuerySelector("[data-testid='flat-make-plan']"));
            Assert.NotNull(line.QuerySelector("[data-testid='flat-add-to-plan']"));
            Assert.NotNull(line.QuerySelector("[data-testid='flat-queue-each']"));
            Assert.NotNull(line.QuerySelector("[data-testid='flat-selection-status']"));
            Assert.NotNull(line.QuerySelector("[data-testid='flat-selection-clear']"));
        });
        Assert.Empty(pane.FindAll("[data-testid='bulk-bar']"));
    }

    [Fact]
    public async Task Clear_lets_go_of_the_selection_and_the_line_goes()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);
        pane.WaitForAssertion(() => Assert.Contains("Write the runbook", Titles(pane)));
        await TickAsync(pane, seeded.Runbook);
        pane.WaitForAssertion(() => pane.Find("[data-testid='flat-selection']"));

        await pane.Find("[data-testid='flat-selection-clear']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='flat-selection']")));
        Assert.Equal(0, host.State.SelectionCount);
    }

    [Fact]
    public async Task Status_sets_one_status_across_the_picked_rows()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);
        pane.WaitForAssertion(() => Assert.Contains("Write the runbook", Titles(pane)));
        await TickAsync(pane, seeded.Runbook);
        await TickAsync(pane, seeded.Certificate);

        await pane.Find("[data-testid='flat-selection-status'] button").ClickAsync(new());
        await pane.Find("[data-testid='flat-selection-status-option'][data-status='InProgress']").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(EntryStatus.InProgress, seeded.Runbook.PreviewStatus);
            Assert.Equal(EntryStatus.InProgress, seeded.Certificate.PreviewStatus);
        });
        Assert.Equal(EntryStatus.Ready, seeded.Prompt.PreviewStatus);
    }

    [Fact]
    public async Task Add_to_plan_files_the_picked_rows_under_the_plan_and_they_leave_Flat()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);
        pane.WaitForAssertion(() => Assert.Contains("Write the runbook", Titles(pane)));
        await TickAsync(pane, seeded.Runbook);

        await pane.Find("[data-testid='flat-add-to-plan'] button").ClickAsync(new());
        await pane.Find("[data-testid='flat-add-to-plan-option'][data-plan='+zta']").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Contains("+zta", seeded.Runbook.PreviewTags);
            Assert.DoesNotContain("Write the runbook", Titles(pane));
        });
        Assert.Contains("ops", seeded.Runbook.PreviewTags);
    }

    [Fact]
    public async Task Make_a_plan_files_the_picked_rows_under_a_new_plan()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);
        pane.WaitForAssertion(() => Assert.Contains("Write the runbook", Titles(pane)));
        await TickAsync(pane, seeded.Runbook);
        await TickAsync(pane, seeded.Certificate);

        await pane.Find("[data-testid='flat-make-plan'] button").ClickAsync(new());
        Assert.True(pane.Find("[data-testid='flat-make-plan-submit']").HasAttribute("disabled"));
        await pane.Find("[data-testid='flat-make-plan-name'] input").InputAsync(new() { Value = "Ops hygiene" });
        await pane.Find("[data-testid='flat-make-plan-submit']").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Contains("+ops-hygiene", seeded.Runbook.PreviewTags);
            Assert.Contains("+ops-hygiene", seeded.Certificate.PreviewTags);
        });
        Assert.DoesNotContain("Write the runbook", Titles(pane));
        // Renew the certificate is in today's My Day, so it stays — now naming its plan.
        Assert.Contains("Renew the certificate", Titles(pane));
    }

    [Fact]
    public async Task Queue_each_asks_for_a_session_per_picked_prompt_and_passes_over_the_rest()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);
        pane.WaitForAssertion(() => Assert.Contains("Add the export", Titles(pane)));

        // A task is not a prompt: alone, nothing can be queued.
        await TickAsync(pane, seeded.Runbook);
        pane.WaitForAssertion(() => Assert.True(pane.Find("[data-testid='flat-queue-each']").HasAttribute("disabled")));

        await TickAsync(pane, seeded.Prompt);
        pane.WaitForAssertion(() => Assert.False(pane.Find("[data-testid='flat-queue-each']").HasAttribute("disabled")));

        await pane.Find("[data-testid='flat-queue-each']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Equal((seeded.Prompt.Id!.Value, Backlog, BacklogClone), Assert.Single(seeded.Queue.Asked)));
    }

    [Fact]
    public async Task A_plan_entry_on_My_Day_is_never_queued_on_its_own_by_Queue_each()
    {
        var seeded = await SeedAsync();
        using var host = seeded.Host;

        var pane = RenderFlat(host);
        pane.WaitForAssertion(() => Assert.Contains("16 - Keep sessions alive +zta", Titles(pane)));

        await TickAsync(pane, seeded.PlanEntryOnMyDay);
        await TickAsync(pane, seeded.Prompt);
        pane.WaitForAssertion(() => Assert.False(pane.Find("[data-testid='flat-queue-each']").HasAttribute("disabled")));

        await pane.Find("[data-testid='flat-queue-each']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Equal(seeded.Prompt.Id!.Value, Assert.Single(seeded.Queue.Asked).EntryId));
    }

    // --- The state's rule -----------------------------------------------------

    [Theory]
    [InlineData("zta", "+zta")]
    [InlineData("  ", null)]
    [InlineData("Ops hygiene", "+ops-hygiene")]
    [InlineData("+Zero trust  auth", "+zero-trust-auth")]
    public void A_typed_plan_name_becomes_one_plan_tag(string typed, string? expected) =>
        Assert.Equal(expected, TasksPane.PlanTagFor(typed));
}
