using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.UI;
using Backlog.UI.Components.Feedback;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// To Event on the Inbox pane (<c>.devbook/domain/inbox/features.md#route-to-event</c>,
/// <c>.devbook/domain/inbox/requirements.md#route-to-event</c>): beside Move to backlog and
/// To Brain, in triage and on the item, on the <b>e</b> key, only while the Events area is on.
/// </summary>
public sealed class InboxToEventTests
{
    /// <summary>The requirement's flag scenario: no route in triage, on the item or on e.</summary>
    [Fact]
    public async Task While_the_Events_area_is_off_To_Event_is_not_offered_anywhere()
    {
        using var harness = Harness.Create(eventsOn: false);
        var items = harness.SeedNewestFirst("Techorama 2026", "Next one");
        var pane = await harness.StartTriageAsync();

        Assert.Empty(pane.FindAll("[data-testid='inbox-to-event']"));

        await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("e"));

        Assert.Empty(harness.Inbox.EventRoutes);
        Assert.Null(harness.Inbox.Find(items[0].Id)!.Routing);
        Assert.Equal(items[0].Id, harness.State.SelectedItemId);
    }

    [Fact]
    public async Task To_Event_in_triage_routes_the_item_says_so_and_moves_on()
    {
        using var harness = Harness.Create(eventsOn: true);
        var items = harness.SeedNewestFirst("Techorama 2026", "Next one");
        var pane = await harness.StartTriageAsync();

        var button = pane.Find("[data-testid='inbox-triage-decisions'] [data-testid='inbox-to-event']");
        Assert.Contains("To Event", button.TextContent, StringComparison.Ordinal);
        await button.ClickAsync(new());

        pane.WaitForAssertion(() =>
        {
            Assert.Equal([items[0].Id], harness.Inbox.EventRoutes);
            Assert.Equal(RoutingDomain.Events, harness.Inbox.Find(items[0].Id)!.Routing!.Domain);
            Assert.Equal(items[1].Id, harness.State.SelectedItemId);
            Assert.Contains("Techorama 2026 → Event", pane.Find("[data-testid='inbox-triage-just-decided-line']").TextContent, StringComparison.Ordinal);
        });
        Assert.Contains(harness.Toasts.Visible, toast => toast.TestId == "inbox-event-routed" && toast.Message.Contains("Techorama 2026", StringComparison.Ordinal));
    }

    [Fact]
    public async Task E_routes_to_an_event_and_u_takes_it_back_leaving_the_event()
    {
        using var harness = Harness.Create(eventsOn: true);
        var items = harness.SeedNewestFirst("dotNET Friday");
        var pane = await harness.StartTriageAsync();

        await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("e"));
        pane.WaitForAssertion(() => Assert.NotNull(harness.Inbox.Find(items[0].Id)!.Routing));

        await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("u"));

        pane.WaitForAssertion(() => Assert.Equal(InboxStatus.Unprocessed, harness.Inbox.Find(items[0].Id)!.Status));
        Assert.Contains(("unlink", items[0].Id), harness.Inbox.Undos);
    }

    /// <summary>The requirement's last scenario, on screen: Events' words, and the item
    /// still waiting.</summary>
    [Fact]
    public async Task An_event_Events_did_not_make_is_said_and_the_item_stays()
    {
        using var harness = Harness.Create(eventsOn: true);
        var items = harness.SeedNewestFirst("Techorama 2026", "Next one");
        harness.Inbox.FailAct[("event", items[0].Id)] = InboxErrors.EventNotMade("disk full");
        var pane = await harness.StartTriageAsync();

        await pane.Find("[data-testid='inbox-to-event']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.Contains(
            harness.Toasts.Visible,
            toast => toast.TestId == "inbox-event-error" && toast.Message == "The event was not made: disk full"));
        Assert.Null(harness.Inbox.Find(items[0].Id)!.Routing);
        Assert.Equal(items[0].Id, harness.State.SelectedItemId);
    }

    [Fact]
    public async Task The_item_detail_offers_To_Event_and_says_where_the_item_went()
    {
        using var harness = Harness.Create(eventsOn: true);
        var items = harness.SeedNewestFirst("Techorama 2026", "Still waiting");
        var pane = harness.Render();
        await pane.InvokeAsync(harness.State.InitializeAsync);
        await pane.Find($"[data-testid='inbox-item-{items[0].Id:D}']").ClickAsync(new());

        await pane.Find("[data-testid='inbox-detail-decisions'] [data-testid='inbox-to-event']").ClickAsync(new());

        pane.WaitForAssertion(() => Assert.StartsWith(
            "Spotted as an event on",
            pane.Find("[data-testid='inbox-detail-state']").TextContent.Trim(),
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_shortcuts_dialog_lists_e_only_while_the_area_is_on()
    {
        Assert.Contains(("e", "To Event"), InboxPane.Shortcuts);

        foreach (var on in new[] { true, false })
        {
            using var harness = Harness.Create(eventsOn: on);
            var pane = harness.Render();
            await pane.InvokeAsync(harness.State.InitializeAsync);

            await pane.Find("[data-testid='inbox-pane-shortcuts']").ClickAsync(new());
            var keys = pane.FindAll("[data-testid='inbox-shortcuts-list'] kbd").Select(key => key.TextContent).ToList();

            Assert.Equal(on, keys.Contains("e"));
        }
    }

    /// <summary>The key reaches the pane only through the set components.js listens for.</summary>
    [Fact]
    public void Components_js_hands_the_pane_e()
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"));
        var line = script.Split('\n').Single(line => line.Contains("const SHORTCUT_PANE_KEYS", StringComparison.Ordinal));

        Assert.Contains("'e'", line, StringComparison.Ordinal);
    }

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

        public static Harness Create(bool eventsOn)
        {
            var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddSingleton(new Backlog.Infrastructure.GitHub.GitHubSettingsStore(
                Path.Combine(Path.GetTempPath(), "backlog-inbox-to-event-tests", Guid.NewGuid().ToString("n"), "github.json")));
            TasksTestHost.AddToastChannel(context.Services);
            var inbox = InboxTestHost.AddInboxState(context.Services);
            inbox.ToEventOffered = eventsOn;

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
