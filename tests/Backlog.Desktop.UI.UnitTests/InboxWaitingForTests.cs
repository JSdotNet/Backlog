using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.UI;
using Backlog.UI.Components.Feedback;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Waiting for (<c>w</c>) decision on the Inbox pane (local ADR 0054 §4;
/// <c>.devbook/domain/inbox/features.md#route-to-tasks</c>): on the decision bar,
/// in triage mode and from the keyboard, it asks who the work waits on —
/// required — and an optional follow-up date, then routes as Move to backlog
/// does with the waiting-for set, is undone as Move to backlog is, and is
/// listed in the Shortcuts dialog.
/// </summary>
public sealed class InboxWaitingForTests
{
    [Fact]
    public async Task W_in_triage_asks_who_and_when_then_routes_and_moves_on()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Quote for the roof", "Next one");
        var routed = new List<InboxRoutedDto>();
        harness.State.Routed += routed.Add;
        var pane = await harness.StartTriageAsync();

        await PressAsync(pane, "w");

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='inbox-waiting-for-dialog']")));
        Assert.True(pane.Find("[data-testid='inbox-waiting-for-confirm']").HasAttribute("disabled"));

        await pane.Find("[data-testid='inbox-waiting-for-who'] input").InputAsync(new() { Value = "Sam the roofer" });
        await pane.Find("[data-testid='inbox-waiting-for-follow-up'] input").InputAsync(new() { Value = "2026-12-01" });
        Assert.False(pane.Find("[data-testid='inbox-waiting-for-confirm']").HasAttribute("disabled"));
        await pane.Find("[data-testid='inbox-waiting-for-form']").SubmitAsync();

        pane.WaitForAssertion(() =>
        {
            var (id, waitingFor, _) = Assert.Single(harness.Inbox.WaitingForRoutes);
            Assert.Equal(items[0].Id, id);
            Assert.Equal(new InboxWaitingForDto("Sam the roofer", new DateOnly(2026, 12, 1)), waitingFor);
            Assert.Equal(RoutingDomain.Tasks, harness.Inbox.Find(items[0].Id)!.Routing!.Domain);
            Assert.Equal(items[1].Id, harness.State.SelectedItemId);
            Assert.Contains("Quote for the roof → Waiting for", pane.Find("[data-testid='inbox-triage-just-decided-line']").TextContent, StringComparison.Ordinal);
        });

        Assert.Empty(pane.FindAll("[data-testid='inbox-waiting-for-dialog']"));
        Assert.Equal(0, harness.Inbox.SingleRouteCalls);
        Assert.Single(routed);
        Assert.Contains(harness.Toasts.Visible, toast => toast.TestId == InboxDesktopState.WaitingForToastTestId && toast.Message.Contains("Sam the roofer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_no_follow_up_date_it_routes_with_who_alone()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Hear back from Ana");
        var pane = await harness.StartTriageAsync();

        await pane.Find("[data-testid='inbox-triage-decisions'] [data-testid='inbox-waiting-for']").ClickAsync(new());
        await pane.Find("[data-testid='inbox-waiting-for-who'] input").InputAsync(new() { Value = "Ana" });
        await pane.Find("[data-testid='inbox-waiting-for-form']").SubmitAsync();

        pane.WaitForAssertion(() => Assert.Equal(new InboxWaitingForDto("Ana"), Assert.Single(harness.Inbox.WaitingForRoutes).WaitingFor));
        Assert.NotNull(harness.Inbox.Find(items[0].Id)!.Routing);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("Sam `x`")]
    public async Task A_who_that_cannot_be_written_keeps_the_dialog_from_routing(string who)
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Quote for the roof");
        var pane = await harness.StartTriageAsync();

        await PressAsync(pane, "w");
        await pane.Find("[data-testid='inbox-waiting-for-who'] input").InputAsync(new() { Value = who });

        Assert.True(pane.Find("[data-testid='inbox-waiting-for-confirm']").HasAttribute("disabled"));
        await pane.Find("[data-testid='inbox-waiting-for-form']").SubmitAsync();

        Assert.Empty(harness.Inbox.WaitingForRoutes);
        Assert.Null(harness.Inbox.Find(items[0].Id)!.Routing);
        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-waiting-for-dialog']"));
    }

    [Fact]
    public async Task U_takes_waiting_for_back_as_it_takes_move_to_backlog_back()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Quote for the roof");
        var pane = await harness.StartTriageAsync();

        await pane.InvokeAsync(() => harness.State.WaitingForAsync("Sam", null));
        pane.WaitForAssertion(() => Assert.NotNull(harness.Inbox.Find(items[0].Id)!.Routing));

        await PressAsync(pane, "u");

        pane.WaitForAssertion(() =>
        {
            Assert.Null(harness.Inbox.Find(items[0].Id)!.Routing);
            Assert.Equal(InboxStatus.Unprocessed, harness.Inbox.Find(items[0].Id)!.Status);
        });
        // Move to backlog's undo: the tasks it made are deleted while unstarted.
        Assert.Contains(("return", items[0].Id), harness.Inbox.Undos);
    }

    [Fact]
    public async Task Refused_by_tasks_it_leaves_the_item_open_and_the_dialog_up()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Quote for the roof", "Next one");
        harness.Inbox.FailAct[("waiting-for", items[0].Id)] = InboxErrors.ItemNotFound;
        var pane = await harness.StartTriageAsync();

        await PressAsync(pane, "w");
        await pane.Find("[data-testid='inbox-waiting-for-who'] input").InputAsync(new() { Value = "Sam" });
        await pane.Find("[data-testid='inbox-waiting-for-form']").SubmitAsync();

        pane.WaitForAssertion(() => Assert.Single(harness.Inbox.WaitingForRoutes));
        Assert.Null(harness.Inbox.Find(items[0].Id)!.Routing);
        Assert.Equal(items[0].Id, harness.State.SelectedItemId);
        Assert.NotEmpty(pane.FindAll("[data-testid='inbox-waiting-for-dialog']"));
    }

    [Fact]
    public async Task The_item_detail_offers_waiting_for_and_routes_with_the_chosen_schedule()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Quote for the roof", "Still waiting");
        var pane = harness.Render();
        await pane.InvokeAsync(harness.State.InitializeAsync);

        await pane.Find($"[data-testid='inbox-item-{items[0].Id:D}']").ClickAsync(new());
        await pane.InvokeAsync(() => harness.State.SetRouteToMyDay(true));

        var button = pane.Find("[data-testid='inbox-detail-decisions'] [data-testid='inbox-waiting-for']");
        Assert.Contains("Waiting for", button.TextContent, StringComparison.Ordinal);
        Assert.Equal(InboxDesktopState.WaitingForTitle, button.GetAttribute("title"));
        await button.ClickAsync(new());

        await pane.Find("[data-testid='inbox-waiting-for-who'] input").InputAsync(new() { Value = "Sam" });
        await pane.Find("[data-testid='inbox-waiting-for-form']").SubmitAsync();

        pane.WaitForAssertion(() =>
        {
            var (_, waitingFor, schedule) = Assert.Single(harness.Inbox.WaitingForRoutes);
            Assert.Equal("Sam", waitingFor.Who);
            Assert.True(schedule?.AddToMyDay);
        });
    }

    [Fact]
    public async Task A_decided_item_is_not_routed_again_by_w()
    {
        using var harness = Harness.Create();
        var items = harness.SeedNewestFirst("Quote for the roof", "Still waiting");
        var pane = harness.Render();
        await pane.InvokeAsync(harness.State.InitializeAsync);
        await pane.Find($"[data-testid='inbox-item-{items[0].Id:D}']").ClickAsync(new());
        await pane.InvokeAsync(() => harness.State.WaitingForAsync("Sam", null));
        pane.WaitForAssertion(() => Assert.NotNull(harness.Inbox.Find(items[0].Id)!.Routing));

        await PressAsync(pane, "w");

        Assert.Empty(pane.FindAll("[data-testid='inbox-waiting-for-dialog']"));
        Assert.False(await pane.InvokeAsync(() => harness.State.WaitingForAsync("Sam", null)));
        Assert.Single(harness.Inbox.WaitingForRoutes);
    }

    [Fact]
    public void The_shortcuts_list_names_w()
    {
        Assert.Contains(InboxPane.Shortcuts, shortcut => shortcut.Keys == "w" && shortcut.Action.StartsWith("Waiting for", StringComparison.Ordinal));
    }

    [Fact]
    public void Components_js_hands_w_to_the_pane()
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"));
        var keys = script[script.IndexOf("const SHORTCUT_PANE_KEYS", StringComparison.Ordinal)..];
        keys = keys[..keys.IndexOf('\n', StringComparison.Ordinal)];

        Assert.Contains("'w'", keys, StringComparison.Ordinal);
    }

    private static Task PressAsync(IRenderedComponent<InboxPane> pane, string key) =>
        pane.InvokeAsync(() => pane.Instance.OnShortcutAsync(key));

    private sealed class Harness : IDisposable
    {
        private Harness(BunitContext context, FakeInboxItems inbox)
        {
            Context = context;
            Inbox = inbox;
        }

        public BunitContext Context { get; }

        public FakeInboxItems Inbox { get; }

        public InboxDesktopState State => Context.Services.GetRequiredService<InboxDesktopState>();

        public ToastChannel Toasts => Context.Services.GetRequiredService<ToastChannel>();

        public static Harness Create()
        {
            var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddSingleton(new Backlog.Infrastructure.GitHub.GitHubSettingsStore(
                Path.Combine(Path.GetTempPath(), "backlog-inbox-waiting-for-tests", Guid.NewGuid().ToString("n"), "github.json")));
            TasksTestHost.AddToastChannel(context.Services);
            var inbox = InboxTestHost.AddInboxState(context.Services);

            return new Harness(context, inbox);
        }

        public IReadOnlyList<InboxItemDto> SeedNewestFirst(params string[] titles) =>
            [.. titles.Select((title, index) => Inbox.Seed(title, capturedAt: Inbox.Now.AddMinutes(titles.Length - index)))];

        public IRenderedComponent<InboxPane> Render() => Context.Render<InboxPane>();

        public async Task<IRenderedComponent<InboxPane>> StartTriageAsync()
        {
            var pane = Render();
            await pane.InvokeAsync(State.InitializeAsync);
            await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("t"));
            Assert.True(State.TriageMode);
            return pane;
        }

        public void Dispose() => Context.Dispose();
    }
}
