using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The Inbox as a list a person can read on a phone in a conference hall: a
/// capture made with no network is on screen at once and says it is waiting, a
/// replica still warming up shows the last list rather than nothing, and a row
/// opens into the whole capture.
/// </summary>
public sealed class InboxListTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_capture_made_offline_is_in_the_list_at_once_marked_waiting()
    {
        var inbox = new ScriptedInboxService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-pull-notice']")));

        app.Find("[data-testid='capture-field'] input").Input("Ask the speaker for the slides");
        app.Find("[data-testid='capture-submit']").Click();

        app.WaitForAssertion(() =>
        {
            var row = Assert.Single(app.FindAll("[data-testid='inbox-row']"));
            Assert.Equal("true", row.GetAttribute("data-waiting"));
            Assert.Contains("Ask the speaker for the slides", row.TextContent);
            Assert.Equal("Waiting", row.QuerySelector("[data-testid='inbox-waiting']")!.TextContent);
            Assert.Null(row.QuerySelector("[data-testid='inbox-dismiss']"));
        });

        // Kept, not lost: the field is clear because the capture is safe.
        Assert.Equal(string.Empty, app.Find("[data-testid='capture-field'] input").GetAttribute("value") ?? string.Empty);
        Assert.Single(host.Outbox.Entries);
        Assert.Contains("Offline — 1 waiting", app.Find("[data-testid='sync-status']").TextContent);
    }

    [Fact]
    public void A_waiting_capture_becomes_an_ordinary_row_once_the_service_takes_it()
    {
        var clock = new FakeTimeProvider(Now);
        var inbox = new ScriptedInboxService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(inbox, clock: clock);
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-pull-notice']")));
        app.Find("[data-testid='capture-field'] input").Input("Ask the speaker for the slides");
        app.Find("[data-testid='capture-submit']").Click();
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-waiting']")));

        inbox.State = InboxServiceState.Answering;
        clock.Advance(TimeSpan.FromSeconds(2));

        app.WaitForAssertion(() =>
        {
            var row = Assert.Single(app.FindAll("[data-testid='inbox-row']"));
            Assert.Equal("false", row.GetAttribute("data-waiting"));
            Assert.Equal("Dismiss", row.QuerySelector("[data-testid='inbox-dismiss']")!.TextContent.Trim());
            Assert.Empty(app.FindAll("[data-testid='inbox-pull-notice']"));
        });

        Assert.Equal(1, inbox.Created);
    }

    /// <summary>
    /// The race behind the conference-day nudges: a pull is out when the outbox
    /// delivers, and it answers with the list from before the capture landed. The
    /// delivery's own refresh must not be lost to the pull in flight, and the row
    /// must not vanish while the next pull is on its way.
    /// </summary>
    [Fact]
    public void A_capture_delivered_while_a_pull_is_out_stops_waiting_and_stays_listed()
    {
        var clock = new FakeTimeProvider(Now);
        var inbox = new ScriptedInboxService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(inbox, clock: clock);
        var app = CaptureOffline(host, "Ask the speaker for the slides");

        PullThenDeliver(app, host, inbox, clock);
        inbox.ReleasePulls();

        app.WaitForAssertion(() =>
        {
            var row = Assert.Single(app.FindAll("[data-testid='inbox-row']"));
            Assert.Equal("false", row.GetAttribute("data-waiting"));
            Assert.Contains("Ask the speaker for the slides", row.TextContent);
        });

        // The delivery's refresh waited for the pull in flight rather than being
        // dropped by it: the first pull failed offline, the second was held, and
        // a third asked the service after the capture landed.
        app.WaitForAssertion(() => Assert.Equal(3, inbox.Pulls));
    }

    /// <summary>After a restart the next pull can fail too — the token it carries
    /// was signed by the service that stopped. A delivered capture still reads
    /// delivered: the outbox has the service's word for it.</summary>
    [Fact]
    public void A_delivered_capture_stays_listed_when_the_pull_after_it_fails()
    {
        var clock = new FakeTimeProvider(Now);
        var inbox = new ScriptedInboxService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(inbox, clock: clock);
        var app = CaptureOffline(host, "Ask the speaker for the slides");

        PullThenDeliver(app, host, inbox, clock);
        inbox.State = InboxServiceState.Unauthorized;
        inbox.ReleasePulls();

        app.WaitForAssertion(() =>
        {
            Assert.Equal(3, inbox.Pulls);
            Assert.NotNull(app.Find("[data-testid='inbox-pull-notice']"));
        });
        var row = Assert.Single(app.FindAll("[data-testid='inbox-row']"));
        Assert.Equal("false", row.GetAttribute("data-waiting"));
        Assert.Contains("Ask the speaker for the slides", row.TextContent);
    }

    private static IRenderedComponent<Routes> CaptureOffline(ShellHost host, string title)
    {
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-pull-notice']")));
        app.Find("[data-testid='capture-field'] input").Input(title);
        app.Find("[data-testid='capture-submit']").Click();
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-waiting']")));

        return app;
    }

    /// <summary>The service is back. A pull reads its list before the capture
    /// reaches it and is held there; then the outbox's retry delivers.</summary>
    private static void PullThenDeliver(IRenderedComponent<Routes> app, ShellHost host, ScriptedInboxService inbox, FakeTimeProvider clock)
    {
        inbox.State = InboxServiceState.Answering;
        inbox.HoldPulls();
        app.Find("[data-testid='inbox-refresh']").Click();
        app.WaitForAssertion(() => Assert.Equal(1, inbox.HeldPulls));

        clock.Advance(DeviceOutbox.FirstRetryDelay);
        app.WaitForAssertion(() =>
        {
            Assert.Equal(1, inbox.Created);
            Assert.Empty(host.Outbox.Entries);
        });
    }

    /// <summary>While the Cosmos emulator warms up the service answers 503
    /// <c>sync.replica_unavailable</c>. The phone shows what it pulled last, and
    /// a line saying why it is not newer — never a blank screen.</summary>
    [Fact]
    public void A_replica_still_warming_up_shows_the_cached_list_and_says_so()
    {
        var cached = new CachedInbox(
            [new InboxItem(Guid.CreateVersion7(), "Book the train", "mobile", Now.AddHours(-2))],
            Now.AddMinutes(-5));
        var inbox = new ScriptedInboxService { State = InboxServiceState.WarmingUp };
        using var host = ShellHost.Paired(inbox, new InMemoryDeviceStore(cached), new FakeTimeProvider(Now));
        var app = host.Open();

        app.WaitForAssertion(() => Assert.Contains(
            "Cloud sync is still starting up. Showing the inbox as it was 5m ago.",
            app.Find("[data-testid='inbox-pull-notice']").TextContent));

        var row = Assert.Single(app.FindAll("[data-testid='inbox-row']"));
        Assert.Contains("Book the train", row.TextContent);
        Assert.Equal("status", app.Find("[data-testid='inbox-pull-notice']").GetAttribute("role"));
    }

    /// <summary>A row says when, what kind, and the first line of the body; a tap
    /// opens the whole of it — body, source, tags and person.</summary>
    [Fact]
    public void A_row_reads_at_a_glance_and_opens_into_the_whole_capture()
    {
        var item = new InboxItem(
            Guid.CreateVersion7(),
            "Ask about the offsite",
            "mobile",
            Now.AddMinutes(-3),
            "Dates, budget, who drives.\nAnd whether partners come.",
            ["planning", "team"],
            "alex");
        using var host = ShellHost.Paired(new ScriptedInboxService(item), clock: new FakeTimeProvider(Now));
        var app = host.Open();

        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='inbox-row']")));

        Assert.Equal("3m ago", app.Find("[data-testid='inbox-time']").TextContent);
        Assert.Equal("Dates, budget, who drives.", app.Find("[data-testid='inbox-preview']").TextContent);
        Assert.NotNull(app.Find(".inbox__meta .capture-kind-marker--text"));
        Assert.Equal("Dismiss", app.Find("[data-testid='inbox-dismiss']").TextContent.Trim());
        Assert.DoesNotContain("Triage", app.Markup, StringComparison.Ordinal);

        app.Find("[data-testid='inbox-open']").Click();

        app.WaitForAssertion(() => Assert.Equal("true", app.Find("[data-testid='inbox-sheet']").GetAttribute("data-open")));
        Assert.Contains("And whether partners come.", app.Find("[data-testid='inbox-sheet-body']").TextContent);
        Assert.Equal("mobile", app.Find("[data-testid='inbox-sheet-source']").TextContent);
        Assert.Equal("@alex", app.Find("[data-testid='inbox-sheet-person']").TextContent);
        Assert.Contains("planning", app.Find("[data-testid='inbox-sheet-tags']").TextContent);
        Assert.Contains("team", app.Find("[data-testid='inbox-sheet-tags']").TextContent);
    }
}
