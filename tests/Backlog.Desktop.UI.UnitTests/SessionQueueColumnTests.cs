using System.Globalization;

using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The session queue docked beside the Tasks list (local ADR 0062 §6;
/// <c>.devbook/domain/sessions/features.md#start-a-session-from-an-entry</c>): a column on
/// the right in Plans and in Flat, with the Manager's state and Pause at its head, then
/// Needs you, Running, Up next, Landed today and the long-running agents; folded back into
/// the bar's popover at 48rem or narrower; and an item selected filters the list to its
/// entry or plan.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class SessionQueueColumnTests
{
    private const string Backlog = "JSdotNet/Backlog";
    private const string BacklogClone = @"D:\Repos\Backlog";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static string Token(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record Seeded(
        TasksPaneHost Host,
        TasksSessionRequestTests.FakeEntrySessionRequests Queue,
        TasksPlanSessionRequestTests.FakePlanTagSessionRequests Plans,
        FakeAttention Attention,
        FakeTimeProvider Clock,
        EntryRow Export,
        EntryRow Import,
        EntryRow Report);

    private static async Task<Seeded> SeedAsync()
    {
        var host = await TasksPaneHost.CreateAsync(Backlog);
        host.GitHub.Settings.SetCloneDirectory("backlog", BacklogClone);

        var export = await host.WriteEntryAsync($"# Add the export\n`prompt` `!ready` `repo:{Backlog}`\n");
        var import = await host.WriteEntryAsync($"# Add the import\n`prompt` `!ready` `repo:{Backlog}`\n");
        var report = await host.WriteEntryAsync($"# Fix the report\n`prompt` `!in-progress` `repo:{Backlog}`\n");
        await host.WriteEntryAsync("# 1 - Start it +alpha\n`prompt` `!ready`\n");
        await host.State.SelectAsync(null);

        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var queue = new TasksSessionRequestTests.FakeEntrySessionRequests
        {
            DispatcherContact = new DispatcherContactReading(clock.GetUtcNow(), IsSilent: false)
        };
        var plans = new TasksPlanSessionRequestTests.FakePlanTagSessionRequests();
        plans.Repositories["alpha"] = ["backlog"];
        var attention = new FakeAttention();

        host.Context.Services.AddSingleton<IEntrySessionRequests>(queue);
        host.Context.Services.AddSingleton<ISessionQueueOrder>(queue.Order);
        host.Context.Services.AddSingleton<IPlanTagSessionRequests>(plans);
        host.Context.Services.AddSingleton<ISessionQueueAttention>(attention);
        host.Context.Services.AddSingleton<TimeProvider>(clock);

        return new Seeded(host, queue, plans, attention, clock, export, import, report);
    }

    private static IRenderedComponent<TasksPane> Render(TasksPaneHost host, TaskListGrouping? grouping, Action<string>? openSession = null) =>
        host.Context.Render<TasksPane>(parameters =>
        {
            if (grouping is { } picked) parameters.Add(pane => pane.ListGrouping, picked);
            if (openSession is not null) parameters.Add(pane => pane.OnOpenSession, openSession);
        });

    private static AngleSharp.Dom.IElement Column(IRenderedComponent<TasksPane> pane) =>
        pane.WaitForElement("[data-testid='session-queue-column']");

    private static IReadOnlyList<string> Titles(IRenderedComponent<TasksPane> pane) =>
        [.. pane.FindAll("[data-testid='entry-list'] .task-item__title").Select(title => title.TextContent.Trim())];

    private static string Normalise(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // --- Where it stands ----------------------------------------------------------

    [Theory]
    [InlineData(TaskListGrouping.Flat)]
    [InlineData(TaskListGrouping.Plans)]
    public async Task The_queue_is_a_column_on_the_right_of_the_list_and_the_bar_carries_no_popover(TaskListGrouping grouping)
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;

        var pane = Render(seeded.Host, grouping);

        var column = Column(pane);
        Assert.Equal("aside", column.TagName, ignoreCase: true);
        Assert.Equal(TasksPane.SessionQueueColumnHeadingId, column.GetAttribute("aria-labelledby"));
        Assert.Equal("Queue", pane.Find($"#{TasksPane.SessionQueueColumnHeadingId}").TextContent.Trim());

        // The column is the list grid's last child, after the scroller: on the right.
        var list = column.ParentElement!;
        Assert.Contains("backlog-list--queue", list.ClassList);
        Assert.Equal(grouping == TaskListGrouping.Plans, list.ClassList.Contains("backlog-list--plans"));
        Assert.Equal("session-queue-column", list.Children[^1].GetAttribute("data-testid"));
        Assert.Equal("backlog-list-scroll", list.Children[^2].GetAttribute("data-testid"));

        Assert.Empty(pane.FindAll("[data-testid='session-queue-button']"));
        Assert.NotNull(column.QuerySelector("[data-testid='session-queue-agents']"));
    }

    [Fact]
    public async Task At_48rem_or_narrower_the_column_folds_back_into_the_popover_trigger_and_comes_back_wider()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        var now = seeded.Clock.GetUtcNow();
        seeded.Queue.Open[seeded.Export.Id!.Value] = new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Requested, Backlog, now.AddMinutes(-3), 0);

        var pane = Render(seeded.Host, TaskListGrouping.Flat);
        Column(pane);

        await pane.InvokeAsync(() => pane.Instance.ListNarrowChangedAsync(true));

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='session-queue-column']"));
            var trigger = pane.Find("[data-testid='session-queue-button']");
            Assert.Equal("Session queue, 1 waiting", trigger.GetAttribute("aria-label"));
            Assert.DoesNotContain("backlog-list--queue", pane.Find(".backlog-list").ClassList);
        });

        await pane.InvokeAsync(() => pane.Instance.ListNarrowChangedAsync(false));

        pane.WaitForAssertion(() =>
        {
            Assert.NotNull(pane.Find("[data-testid='session-queue-column']"));
            Assert.Empty(pane.FindAll("[data-testid='session-queue-button']"));
        });
    }

    [Fact]
    public async Task A_host_that_picks_no_grouping_keeps_today_s_popover()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;

        var pane = Render(seeded.Host, grouping: null);

        pane.WaitForElement("[data-testid='session-queue-button']");
        Assert.Empty(pane.FindAll("[data-testid='session-queue-column']"));
    }

    [Fact]
    public async Task With_the_session_requests_flag_off_there_is_neither_column_nor_popover()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        seeded.Queue.IsOffered = false;
        seeded.Plans.IsOffered = false;

        var pane = Render(seeded.Host, TaskListGrouping.Flat);

        Assert.Empty(pane.FindAll("[data-testid='session-queue-column']"));
        Assert.Empty(pane.FindAll("[data-testid='session-queue-button']"));
    }

    // --- What it holds ------------------------------------------------------------

    [Fact]
    public async Task Top_to_bottom_it_reads_Needs_you_Running_Up_next_Landed_today_and_the_agents()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        var now = seeded.Clock.GetUtcNow();
        seeded.Attention.Answer = new([new SessionQueueQuestion(Guid.NewGuid(), "session-1", "Offer the client accounts too?", now.AddMinutes(-4))], new SessionQueueSlots(1, 3));
        seeded.Report.PullRequestStates = new Dictionary<EntryPullRequestLink, LinkedPullRequestState> { [new(Backlog, 232)] = LinkedPullRequestState.Draft };
        var landed = await seeded.Host.WriteEntryAsync($"# Ship the tray\n`prompt` `!done` `completed:{Token(Today)}`\n");
        landed.PullRequestStates = new Dictionary<EntryPullRequestLink, LinkedPullRequestState> { [new(Backlog, 1841)] = LinkedPullRequestState.Merged };
        await seeded.Host.State.SelectAsync(null);
        seeded.Queue.Open[seeded.Report.Id!.Value] = new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Claimed, Backlog, now.AddMinutes(-9));
        seeded.Queue.Open[seeded.Export.Id!.Value] = new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Requested, Backlog, now.AddMinutes(-5), 0);
        seeded.Queue.Open[seeded.Import.Id!.Value] = new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Requested, Backlog, now.AddMinutes(-2), 1);

        var pane = Render(seeded.Host, TaskListGrouping.Flat);

        pane.WaitForAssertion(() =>
        {
            var column = Column(pane);
            Assert.Equal(
                ["session-queue-needs", "session-queue-running", "session-queue-next", "session-queue-landed", "session-queue-agents"],
                column.QuerySelectorAll("section[data-testid]").Select(section => section.GetAttribute("data-testid")));
            Assert.Equal("Manager online · 1 of 3 slots", column.QuerySelector("[data-testid='session-queue-manager']")!.TextContent);
        });

        var needs = pane.Find("[data-testid='session-queue-needs']");
        Assert.Equal("Needs you · 2", needs.QuerySelector("h3")!.TextContent.Trim());
        Assert.Contains("Offer the client accounts too?", needs.QuerySelector("[data-testid='session-queue-question']")!.TextContent, StringComparison.Ordinal);
        Assert.NotNull(needs.QuerySelector("[data-testid='session-queue-answer']"));
        var draft = needs.QuerySelector("[data-testid='session-queue-draft']")!;
        Assert.Equal("Fix the report · draft PR #232", Normalise(draft.QuerySelector("[data-testid='session-queue-draft-select']")!.TextContent));
        Assert.Equal($"https://github.com/{Backlog}/pull/232", draft.QuerySelector("[data-testid='session-queue-validate']")!.GetAttribute("href"));
        Assert.NotNull(draft.QuerySelector("[data-testid='session-queue-dismiss']"));

        // Running: the Claimed one, offering no Cancel and no move.
        var running = pane.Find("[data-testid='session-queue-running']").QuerySelectorAll("[data-testid='session-queue-row']");
        Assert.Equal("Fix the report", Assert.Single(running).QuerySelector("[data-testid='session-queue-jump']")!.TextContent.Trim());
        Assert.Null(running[0].QuerySelector("[data-testid='session-queue-cancel']"));
        Assert.Null(running[0].QuerySelector("[data-testid='session-queue-order']"));

        // Up next, in queue order, numbered, each with its moves and Cancel.
        var next = pane.Find("[data-testid='session-queue-next']").QuerySelectorAll("[data-testid='session-queue-row']");
        Assert.Equal(["Add the export", "Add the import"], next.Select(row => row.QuerySelector("[data-testid='session-queue-jump']")!.TextContent.Trim()));
        Assert.Equal(["1", "2"], next.Select(row => row.QuerySelector(".session-queue-column__place")!.TextContent));
        Assert.All(next, row => Assert.NotNull(row.QuerySelector("[data-testid='session-queue-order']")));
        Assert.All(next, row => Assert.NotNull(row.QuerySelector("[data-testid='session-queue-cancel']")));
        Assert.Equal($"Entry · {Backlog} · Requested · 5 min ago", Normalise(next[0].QuerySelector("[data-testid='session-queue-meta']")!.TextContent));

        Assert.Equal("Ship the tray · PR #1841 merged", Normalise(pane.Find("[data-testid='session-queue-landed-select']").TextContent));
    }

    [Fact]
    public async Task A_started_request_runs_with_its_Cancel_and_Cancel_withdraws_it()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        var requestId = Guid.NewGuid();
        seeded.Queue.Open[seeded.Report.Id!.Value] = new EntrySessionRequest(requestId, EntrySessionRequestState.Started, Backlog, seeded.Clock.GetUtcNow().AddMinutes(-9));

        var pane = Render(seeded.Host, TaskListGrouping.Plans);
        var cancel = pane.WaitForElement("[data-testid='session-queue-running'] [data-testid='session-queue-cancel']");
        Assert.NotNull(cancel.Closest(".session-queue-column__row"));

        cancel.Click();

        pane.WaitForAssertion(() => Assert.Equal(requestId, Assert.Single(seeded.Queue.Cancelled)));
    }

    [Fact]
    public async Task Alt_arrows_and_the_Move_controls_reorder_Up_next_as_they_do_in_the_popover()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        var now = seeded.Clock.GetUtcNow();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        seeded.Queue.Open[seeded.Export.Id!.Value] = new EntrySessionRequest(first, EntrySessionRequestState.Requested, Backlog, now.AddMinutes(-5), 0);
        seeded.Queue.Open[seeded.Import.Id!.Value] = new EntrySessionRequest(second, EntrySessionRequestState.Requested, Backlog, now.AddMinutes(-2), 1);

        var pane = Render(seeded.Host, TaskListGrouping.Flat);
        var rows = pane.WaitForElements("[data-testid='session-queue-next'] [data-testid='session-queue-row']");

        await rows[1].TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });
        pane.WaitForAssertion(() => Assert.Equal((second, 0), Assert.Single(seeded.Queue.Order.Moved)));

        // The fake moved it, so the import heads Up next now, and its Move up has nowhere to go.
        var head = pane.Find("[data-testid='session-queue-next'] [data-testid='session-queue-move-up']");
        Assert.Equal("Move Add the import up", head.GetAttribute("aria-label"));
        Assert.True(head.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Pause_in_the_header_pauses_the_whole_queue()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;

        var pane = Render(seeded.Host, TaskListGrouping.Flat);
        var pause = pane.WaitForElement(".session-queue-column__header [data-testid='session-queue-pause']");
        Assert.Equal("Pause queue", pause.TextContent.Trim());

        pause.Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal([true], seeded.Queue.PauseCalls);
            Assert.Equal("Resume queue", pane.Find(".session-queue-column__header [data-testid='session-queue-pause']").TextContent.Trim());
            Assert.NotNull(pane.Find("[data-testid='session-queue-column'] [data-testid='session-queue-paused']"));
        });
    }

    [Fact]
    public async Task Answer_opens_the_session_that_asked_and_Dismiss_takes_a_draft_off_Needs_you()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        seeded.Attention.Answer = new([new SessionQueueQuestion(Guid.NewGuid(), "session-7", "Which one first?", seeded.Clock.GetUtcNow())], null);
        seeded.Report.PullRequestStates = new Dictionary<EntryPullRequestLink, LinkedPullRequestState> { [new(Backlog, 232)] = LinkedPullRequestState.Draft };
        string? opened = null;

        var pane = Render(seeded.Host, TaskListGrouping.Flat, id => opened = id);
        pane.WaitForElement("[data-testid='session-queue-answer']").Click();
        Assert.Equal("session-7", opened);

        pane.Find("[data-testid='session-queue-dismiss']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='session-queue-draft']"));
            Assert.Equal("Needs you · 1", pane.Find("[data-testid='session-queue-needs'] h3").TextContent.Trim());
        });
    }

    [Fact]
    public async Task The_header_says_when_the_Manager_is_silent_or_not_heard_yet()
    {
        Assert.Equal("Manager silent", SessionQueue.ManagerLabel(new DispatcherContactReading(DateTimeOffset.UtcNow.AddMinutes(-5), IsSilent: true), new SessionQueueSlots(1, 3)));
        Assert.Equal("Manager not heard yet", SessionQueue.ManagerLabel(DispatcherContactReading.Unknown, null));
        Assert.Equal("Manager online", SessionQueue.ManagerLabel(new DispatcherContactReading(DateTimeOffset.UtcNow, IsSilent: false), null));

        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        seeded.Queue.DispatcherContact = DispatcherContactReading.Unknown;

        var pane = Render(seeded.Host, TaskListGrouping.Flat);

        Assert.Equal("Manager not heard yet", pane.WaitForElement("[data-testid='session-queue-manager']").TextContent);
        Assert.Contains("session-queue-column__dot--unheard", pane.Find(".session-queue-column__dot").ClassList);
    }

    // --- Selecting an item filters the list -----------------------------------------

    [Fact]
    public async Task Selecting_an_entry_request_filters_the_list_to_that_entry_and_Show_all_lets_go()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        var now = seeded.Clock.GetUtcNow();
        seeded.Queue.Open[seeded.Export.Id!.Value] = new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Requested, Backlog, now.AddMinutes(-5), 0);
        seeded.Queue.Open[seeded.Import.Id!.Value] = new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Requested, Backlog, now.AddMinutes(-2), 1);

        var pane = Render(seeded.Host, TaskListGrouping.Flat);
        pane.WaitForAssertion(() => Assert.Contains("Add the import", Titles(pane)));

        var jump = pane.WaitForElements("[data-testid='session-queue-next'] [data-testid='session-queue-jump']")[1];
        Assert.Equal("false", jump.GetAttribute("aria-pressed"));
        await jump.ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["Add the import"], Titles(pane));
            Assert.Equal(seeded.Import.Id, seeded.Host.State.QueueFocusEntry);
            Assert.Equal("true", pane.FindAll("[data-testid='session-queue-next'] [data-testid='session-queue-jump']")[1].GetAttribute("aria-pressed"));
            Assert.Contains("Showing Add the import", pane.Find("[data-testid='session-queue-focus']").TextContent, StringComparison.Ordinal);
        });

        pane.Find("[data-testid='session-queue-focus-clear']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Contains("Add the export", Titles(pane));
            Assert.Contains("Add the import", Titles(pane));
            Assert.Null(seeded.Host.State.QueueFocusEntry);
            Assert.Empty(pane.FindAll("[data-testid='session-queue-focus']"));
        });
    }

    [Fact]
    public async Task Selecting_a_draft_filters_the_list_to_its_entry()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        seeded.Report.PullRequestStates = new Dictionary<EntryPullRequestLink, LinkedPullRequestState> { [new(Backlog, 232)] = LinkedPullRequestState.Draft };

        var pane = Render(seeded.Host, TaskListGrouping.Flat);
        await pane.WaitForElement("[data-testid='session-queue-draft-select']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Equal(["Fix the report"], Titles(pane)));
    }

    [Fact]
    public async Task Selecting_a_plan_request_in_Flat_switches_to_Plans_and_selects_that_plan()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        seeded.Plans.Open["alpha"] = new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Requested, Backlog, seeded.Clock.GetUtcNow().AddMinutes(-1), 0);
        var picked = new List<TaskListGrouping>();

        var pane = seeded.Host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.ListGrouping, TaskListGrouping.Flat)
            .Add(p => p.ListGroupingChanged, picked.Add));

        var jump = pane.WaitForElement("[data-key='plan-alpha'] [data-testid='session-queue-jump']");
        Assert.Equal("+alpha", jump.TextContent.Trim());
        await jump.ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal([TaskListGrouping.Plans], picked);
            Assert.Equal("+alpha", seeded.Host.State.SelectedPlan);
            Assert.Equal("true", pane.Find("[data-key='plan-alpha'] [data-testid='session-queue-jump']").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public async Task Selecting_a_plan_entry_in_Flat_shows_its_plan_in_Plans_rather_than_an_empty_list()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        var planEntry = seeded.Host.State.Rows.Single(row => row.PreviewTitle.Contains("Start it", StringComparison.Ordinal));
        planEntry.PullRequestStates = new Dictionary<EntryPullRequestLink, LinkedPullRequestState> { [new(Backlog, 300)] = LinkedPullRequestState.Draft };
        var picked = new List<TaskListGrouping>();

        var pane = seeded.Host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.ListGrouping, TaskListGrouping.Flat)
            .Add(p => p.ListGroupingChanged, picked.Add));
        await pane.WaitForElement("[data-testid='session-queue-draft-select']").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal([TaskListGrouping.Plans], picked);
            Assert.Equal("+alpha", seeded.Host.State.SelectedPlan);
            Assert.Null(seeded.Host.State.QueueFocusEntry);
            Assert.Equal(planEntry.Id, seeded.Host.State.SelectedRow?.Id);
            Assert.Equal("true", pane.Find("[data-testid='session-queue-draft-select']").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public async Task Selecting_a_landed_entry_in_Flat_shows_it_though_Flat_opens_on_the_open_entries()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        var landed = await seeded.Host.WriteEntryAsync($"# Ship the tray\n`prompt` `!done` `completed:{Token(Today)}`\n");
        landed.PullRequestStates = new Dictionary<EntryPullRequestLink, LinkedPullRequestState> { [new(Backlog, 1841)] = LinkedPullRequestState.Merged };
        await seeded.Host.State.SelectAsync(null);

        var pane = Render(seeded.Host, TaskListGrouping.Flat);
        pane.WaitForAssertion(() => Assert.DoesNotContain("Ship the tray", Titles(pane)));
        await pane.WaitForElement("[data-testid='session-queue-landed-select']").ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["Ship the tray"], seeded.Host.State.FilteredRows.Select(row => row.PreviewTitle));
            // Filtered, not opened: the open entry would take the width the column needs.
            Assert.Null(seeded.Host.State.SelectedRow);
        });
    }

    [Fact]
    public async Task Folded_the_queue_keeps_the_entry_it_filtered_the_list_to_with_Show_all_on_the_bar()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        seeded.Queue.Open[seeded.Import.Id!.Value] = new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Requested, Backlog, seeded.Clock.GetUtcNow(), 0);

        var pane = Render(seeded.Host, TaskListGrouping.Flat);
        await pane.WaitForElement("[data-testid='session-queue-next'] [data-testid='session-queue-jump']").ClickAsync(new());
        pane.WaitForAssertion(() => Assert.Equal(["Add the import"], Titles(pane)));

        await pane.InvokeAsync(() => pane.Instance.ListNarrowChangedAsync(true));

        var chip = pane.WaitForElement("[data-testid='session-queue-focus-bar']");
        Assert.Equal("Showing Add the import · Show all", Normalise(chip.TextContent));
        Assert.Equal(["Add the import"], Titles(pane));

        chip.Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Null(seeded.Host.State.QueueFocusEntry);
            Assert.Contains("Add the export", Titles(pane));
            Assert.Empty(pane.FindAll("[data-testid='session-queue-focus-bar']"));
        });
    }

    [Fact]
    public async Task Showing_a_plan_from_the_roadmap_lets_go_of_the_queue_s_entry()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        seeded.Host.State.SetQueueFocusEntry(seeded.Import.Id);

        seeded.Host.State.ShowPlan("+alpha");

        Assert.Null(seeded.Host.State.QueueFocusEntry);
    }

    [Fact]
    public async Task Two_entries_recording_one_draft_each_list_it_without_a_duplicate_key()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        var shared = new EntryPullRequestLink(Backlog, 232);
        seeded.Export.PullRequestStates = new Dictionary<EntryPullRequestLink, LinkedPullRequestState> { [shared] = LinkedPullRequestState.Draft };
        seeded.Import.PullRequestStates = new Dictionary<EntryPullRequestLink, LinkedPullRequestState> { [shared] = LinkedPullRequestState.Draft };

        var pane = Render(seeded.Host, TaskListGrouping.Flat);

        pane.WaitForAssertion(() => Assert.Equal(2, pane.FindAll("[data-testid='session-queue-draft']").Count));
    }

    [Fact]
    public async Task Cancelling_the_last_row_hands_the_focus_to_the_column_s_heading()
    {
        var seeded = await SeedAsync();
        using var _host = seeded.Host;
        var requestId = Guid.NewGuid();
        seeded.Queue.Open[seeded.Export.Id!.Value] = new EntrySessionRequest(requestId, EntrySessionRequestState.Requested, Backlog, seeded.Clock.GetUtcNow(), 0);

        var pane = Render(seeded.Host, TaskListGrouping.Flat);
        pane.WaitForElement("[data-testid='session-queue-cancel']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(requestId, Assert.Single(seeded.Queue.Cancelled));
            Assert.Contains(
                seeded.Host.Context.JSInterop.Invocations["backlogFocus"],
                call => Equals(call.Arguments[0], TasksPane.SessionQueueColumnHeadingId));
        });
    }

    /// <summary>The reader, in memory: what a test says needs the person.</summary>
    private sealed class FakeAttention : ISessionQueueAttention
    {
        public SessionQueueAttention Answer { get; set; } = SessionQueueAttention.None;

        public Task<SessionQueueAttention> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Answer);
    }
}
