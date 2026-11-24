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

        host.Capture(app, "Ask the speaker for the slides");

        app.WaitForAssertion(() =>
        {
            var row = Assert.Single(app.FindAll("[data-testid='inbox-row']"));
            Assert.Equal("waiting", row.GetAttribute("data-state"));
            Assert.Contains("Ask the speaker for the slides", row.TextContent);
            Assert.Equal("Waiting", row.QuerySelector("[data-testid='inbox-row-state']")!.TextContent);
        });

        // Kept, not lost: the capture is safe on the phone.
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
        host.Capture(app, "Ask the speaker for the slides");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-row-state']")));

        inbox.State = InboxServiceState.Answering;
        clock.Advance(TimeSpan.FromSeconds(2));

        app.WaitForAssertion(() =>
        {
            var row = Assert.Single(app.FindAll("[data-testid='inbox-row']"));
            Assert.Equal("sent", row.GetAttribute("data-state"));
            Assert.Equal("Sent", row.QuerySelector("[data-testid='inbox-row-state']")!.TextContent.Trim());
            Assert.Empty(app.FindAll("[data-testid='inbox-pull-notice']"));
        });

        Assert.Equal(1, inbox.Created);
    }

    /// <summary>A talk note written with no network in the morning and flushed
    /// three hours later still reads as written in the morning — before it leaves
    /// the phone and after the service has it — because the capture carries the
    /// time it was made and the service keeps it.</summary>
    [Fact]
    public void A_capture_made_offline_keeps_its_time_once_the_service_takes_it()
    {
        var clock = new FakeTimeProvider(Now);
        var inbox = new ScriptedInboxService { State = InboxServiceState.Unreachable, Clock = clock };
        using var host = ShellHost.Paired(inbox, clock: clock);
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-pull-notice']")));
        host.Capture(app, "Ask the speaker for the slides");
        app.WaitForAssertion(() => Assert.Equal("waiting", app.Find("[data-testid='inbox-row']").GetAttribute("data-state")));

        inbox.State = InboxServiceState.Answering;
        clock.Advance(TimeSpan.FromHours(3));

        app.WaitForAssertion(() =>
        {
            var row = Assert.Single(app.FindAll("[data-testid='inbox-row']"));
            Assert.Equal("sent", row.GetAttribute("data-state"));
            Assert.Equal("3h ago", row.QuerySelector("[data-testid='inbox-row-time']")!.TextContent);
        });

        // Every attempt, the failed one included, says the same time.
        Assert.NotEmpty(inbox.Received);
        Assert.All(inbox.Received, capture => Assert.Equal(Now, capture.CapturedAt));
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
            Assert.Equal("sent", row.GetAttribute("data-state"));
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
        Assert.Equal("sent", row.GetAttribute("data-state"));
        Assert.Contains("Ask the speaker for the slides", row.TextContent);
    }

    private static IRenderedComponent<Routes> CaptureOffline(ShellHost host, string title)
    {
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-pull-notice']")));
        host.Capture(app, title);
        app.WaitForAssertion(() => Assert.Equal("waiting", app.Find("[data-testid='inbox-row']").GetAttribute("data-state")));

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

        Assert.Equal("3m ago", app.Find("[data-testid='inbox-row-time']").TextContent);
        Assert.Equal("Dates, budget, who drives.", app.Find("[data-testid='inbox-row-preview']").TextContent);
        Assert.NotNull(app.Find(".capture-row__meta .capture-kind-marker--text"));
        Assert.DoesNotContain("Triage", app.Markup, StringComparison.Ordinal);

        app.Find("[data-testid='inbox-row-open']").Click();

        app.WaitForAssertion(() => Assert.Equal("true", app.Find("[data-testid='inbox-sheet']").GetAttribute("data-open")));
        // The whole body, in the field it is edited in.
        Assert.Contains("And whether partners come.", app.Find("[data-testid='inbox-sheet-notes'] textarea").GetAttribute("value") ?? app.Find("[data-testid='inbox-sheet-notes'] textarea").TextContent);
        Assert.Equal("Ask about the offsite", app.Find("[data-testid='inbox-sheet-title'] input").GetAttribute("value"));
        Assert.Equal("mobile", app.Find("[data-testid='inbox-sheet-source']").TextContent);
        Assert.Equal("@alex", app.Find("[data-testid='inbox-sheet-person']").TextContent);
        Assert.Contains("planning", app.Find("[data-testid='inbox-sheet-tags']").TextContent);
        Assert.Contains("team", app.Find("[data-testid='inbox-sheet-tags']").TextContent);
    }

    /// <summary>The tab is the phone's own captures, counted in the header, and
    /// offers nothing that would triage one: that stays on the desktop.</summary>
    [Fact]
    public void The_header_counts_the_phones_own_captures_and_no_row_offers_triage()
    {
        var inbox = new ScriptedInboxService(
            new InboxItem(Guid.CreateVersion7(), "Ask about the offsite", "mobile", Now.AddMinutes(-3)),
            new InboxItem(Guid.CreateVersion7(), "Renew the domain", "mobile", Now.AddHours(-1)),
            new InboxItem(Guid.CreateVersion7(), "A video the monitor found", "youtube", Now.AddMinutes(-1)),
            new InboxItem(Guid.CreateVersion7(), "Captured on the desktop", "desktop", Now.AddMinutes(-2)));
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = host.Open();

        app.WaitForAssertion(() => Assert.Equal(2, app.FindAll("[data-testid='inbox-row']").Count));

        Assert.Equal("2 items to sort", app.Find("[data-testid='inbox-count']").TextContent);
        Assert.Equal(
            ["Ask about the offsite", "Renew the domain"],
            app.FindAll("[data-testid='inbox-row-title']").Select(title => title.TextContent));
        Assert.All(app.FindAll("[data-testid='inbox-row']"), row => Assert.Equal("sent", row.GetAttribute("data-state")));

        foreach (var triage in new[] { "Dismiss", "Later", "To note", "Delete" })
        {
            Assert.DoesNotContain(triage, app.Find("[data-testid='inbox-list']").TextContent, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_empty_inbox_says_nothing_is_left_to_sort()
    {
        using var host = ShellHost.Paired(new ScriptedInboxService(), clock: new FakeTimeProvider(Now));
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-empty']")));

        Assert.Equal("Nothing to sort", app.Find("[data-testid='inbox-count']").TextContent);
        Assert.Contains("Inbox clear", app.Find("[data-testid='inbox-empty']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void One_capture_is_one_item_to_sort()
    {
        var inbox = new ScriptedInboxService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = CaptureOffline(host, "Ask the speaker for the slides");

        Assert.Equal("1 item to sort", app.Find("[data-testid='inbox-count']").TextContent);
    }

    // --- Editing and deleting a capture ------------------------------------------

    private static TimeSpan SaveDelay => Backlog.Mobile.UI.Components.Pages.Inbox.SaveDelay;

    /// <summary>The sheet saves on its own once typing stops, as the Notes editor
    /// does: no Save button, one edit per pause, and the row reads the new words.</summary>
    [Fact]
    public void An_edit_in_the_sheet_reaches_the_service_after_a_pause_and_the_row_shows_it()
    {
        var clock = new FakeTimeProvider(Now);
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item);
        using var host = ShellHost.Paired(inbox, clock: clock);
        var app = OpenSheet(host);

        app.Find("[data-testid='inbox-sheet-title'] input").Input("Call the dentist");
        app.Find("[data-testid='inbox-sheet-title'] input").Input("Call the dentist about the crown");
        app.Find("[data-testid='inbox-sheet-notes'] textarea").Input("Ask for Thursday.");
        Assert.Empty(inbox.Edits);

        clock.Advance(SaveDelay);

        app.WaitForAssertion(() =>
        {
            var (id, edit) = Assert.Single(inbox.Edits);
            Assert.Equal(item.Id, id);
            Assert.Equal(new CaptureEditRequest("Call the dentist about the crown", "Ask for Thursday."), edit);
        });
        app.WaitForAssertion(() =>
        {
            Assert.Equal("Call the dentist about the crown", app.Find("[data-testid='inbox-row-title']").TextContent);
            Assert.Equal("sent", app.Find("[data-testid='inbox-row']").GetAttribute("data-state"));
        });
        Assert.Empty(app.FindAll("button[data-testid='inbox-sheet-save']"));
    }

    /// <summary>With no signal the edit is kept and shown at once, and the row says
    /// it is still on its way.</summary>
    [Fact]
    public void An_edit_made_offline_shows_on_the_row_at_once_marked_waiting()
    {
        var clock = new FakeTimeProvider(Now);
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item);
        using var host = ShellHost.Paired(inbox, clock: clock);
        var app = OpenSheet(host);
        inbox.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='inbox-sheet-title'] input").Input("Call the dentist about the crown");
        app.Find("[data-testid='inbox-sheet-title'] input").Blur();

        app.WaitForAssertion(() =>
        {
            Assert.Equal("Call the dentist about the crown", app.Find("[data-testid='inbox-row-title']").TextContent);
            Assert.Equal("waiting", app.Find("[data-testid='inbox-row']").GetAttribute("data-state"));
        });
        Assert.Equal(CaptureEditOutboxKind.Token, Assert.Single(host.Outbox.Entries).Kind);
    }

    /// <summary>Delete asks first, says what it does, and the row leaves the list
    /// at once; the service hears it as the capture's acknowledgement.</summary>
    [Fact]
    public void Delete_asks_first_and_takes_the_capture_off_the_list()
    {
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item);
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = OpenSheet(host);

        app.Find("[data-testid='inbox-sheet-delete']").Click();

        app.WaitForAssertion(() => Assert.Contains(
            "Removes it from this phone and archives it on the desktop.",
            app.Find("[data-testid='inbox-delete-confirm']").TextContent));
        Assert.Single(app.FindAll("[data-testid='inbox-row']"));

        app.Find("[data-testid='inbox-delete-confirm-yes']").Click();

        app.WaitForAssertion(() => Assert.Empty(app.FindAll("[data-testid='inbox-row']")));
        app.WaitForAssertion(() => Assert.Equal([item.Id], inbox.Acknowledged));
        Assert.Equal("false", app.Find("[data-testid='inbox-sheet']").GetAttribute("data-open"));
    }

    /// <summary>A capture deleted before it left the phone never reaches the
    /// service at all.</summary>
    [Fact]
    public async Task Deleting_a_capture_still_waiting_never_sends_it()
    {
        var inbox = new ScriptedInboxService { State = InboxServiceState.Unreachable };
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = CaptureOffline(host, "Ask the speaker for the slides");

        app.Find("[data-testid='inbox-row-open']").Click();
        app.WaitForAssertion(() => Assert.Equal("true", app.Find("[data-testid='inbox-sheet']").GetAttribute("data-open")));
        app.Find("[data-testid='inbox-sheet-delete']").Click();
        app.WaitForAssertion(() => app.Find("[data-testid='inbox-delete-confirm-yes']").Click());

        app.WaitForAssertion(() => Assert.Empty(app.FindAll("[data-testid='inbox-row']")));
        Assert.Empty(host.Outbox.Entries);

        // The signal comes back: there is nothing left to send, and nothing to tell.
        var attempts = inbox.Received.Count;
        inbox.State = InboxServiceState.Answering;
        await host.Outbox.ResumeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(attempts, inbox.Received.Count);
        Assert.Equal(0, inbox.Created);
        Assert.Empty(inbox.Acknowledged);
    }

    /// <summary>The desktop decided first: the edit is not lost silently, and not
    /// left waiting to be retried — one calm line says so.</summary>
    [Fact]
    public void An_edit_of_a_capture_the_desktop_already_decided_says_so_calmly()
    {
        var clock = new FakeTimeProvider(Now);
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item);
        using var host = ShellHost.Paired(inbox, clock: clock);
        var app = OpenSheet(host);
        inbox.Decide(item.Id);

        app.Find("[data-testid='inbox-sheet-title'] input").Input("Call the dentist about the crown");
        clock.Advance(SaveDelay);

        app.WaitForAssertion(() =>
        {
            var line = app.Find("[data-testid='inbox-dealt-with']");
            Assert.Equal("status", line.GetAttribute("role"));
            Assert.Contains("already dealt with", line.TextContent, StringComparison.Ordinal);
        });
        app.WaitForAssertion(() => Assert.Empty(host.Outbox.Entries));
        Assert.DoesNotContain("tap to retry", app.Markup, StringComparison.OrdinalIgnoreCase);
    }

    // --- An edit is never lost (the clock never reaches SaveDelay in these) -----

    [Fact]
    public void Closing_the_sheet_inside_the_wait_keeps_the_edit()
    {
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item);
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = OpenSheet(host);

        app.Find("[data-testid='inbox-sheet-title'] input").Input("Call the dentist about the crown");
        app.Find("[data-testid='inbox-sheet-close']").Click();

        app.WaitForAssertion(() => Assert.Equal("Call the dentist about the crown", Assert.Single(inbox.Edits).Edit.Title));
        app.WaitForAssertion(() => Assert.Equal("false", app.Find("[data-testid='inbox-sheet']").GetAttribute("data-open")));
    }

    [Fact]
    public void Going_to_the_background_inside_the_wait_keeps_the_edit()
    {
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item);
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = OpenSheet(host);

        app.Find("[data-testid='inbox-sheet-title'] input").Input("Call the dentist about the crown");
        host.Service<AppLifecycle>().Pause();

        app.WaitForAssertion(() => Assert.Equal("Call the dentist about the crown", Assert.Single(inbox.Edits).Edit.Title));
    }

    [Fact]
    public void Leaving_the_page_inside_the_wait_keeps_the_edit()
    {
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item);
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = OpenSheet(host);

        app.Find("[data-testid='inbox-sheet-title'] input").Input("Call the dentist about the crown");
        host.Navigation.NavigateTo("notes");

        app.WaitForAssertion(() => Assert.Equal("Call the dentist about the crown", Assert.Single(inbox.Edits).Edit.Title));
    }

    /// <summary>Leaving through the tab bar is a way out the sheet cannot hold: a
    /// blank title is not kept, but the notes typed with it are, under the title the
    /// capture last had.</summary>
    [Fact]
    public void Leaving_the_page_with_a_blank_title_keeps_the_notes_under_the_last_title()
    {
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item);
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = OpenSheet(host);

        app.Find("[data-testid='inbox-sheet-title'] input").Input("");
        app.Find("[data-testid='inbox-sheet-notes'] textarea").Input("Ask for Thursday.");
        host.Navigation.NavigateTo("notes");

        app.WaitForAssertion(() => Assert.Equal(
            new CaptureEditRequest("Call the dentist", "Ask for Thursday."),
            Assert.Single(inbox.Edits).Edit));
    }

    /// <summary>A capture cannot be without a title, so a blank one is not kept —
    /// and the sheet will not close on the notes typed with it: it stays, says why,
    /// and keeps them until a title is typed.</summary>
    [Fact]
    public void A_blank_title_keeps_the_sheet_open_with_the_reason_until_one_is_typed()
    {
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item);
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = OpenSheet(host);

        app.Find("[data-testid='inbox-sheet-title'] input").Input("");
        app.Find("[data-testid='inbox-sheet-notes'] textarea").Input("Ask for Thursday.");
        app.Find("[data-testid='inbox-sheet-close']").Click();

        app.WaitForAssertion(() => Assert.Contains("needs a title", app.Find("[data-testid='inbox-sheet-unsaved']").TextContent, StringComparison.Ordinal));
        Assert.Equal("true", app.Find("[data-testid='inbox-sheet']").GetAttribute("data-open"));
        Assert.Empty(inbox.Edits);

        app.Find("[data-testid='inbox-sheet-title'] input").Input("Call the dentist about the crown");
        app.Find("[data-testid='inbox-sheet-close']").Click();

        app.WaitForAssertion(() => Assert.Equal(
            new CaptureEditRequest("Call the dentist about the crown", "Ask for Thursday."),
            Assert.Single(inbox.Edits).Edit));
        app.WaitForAssertion(() => Assert.Equal("false", app.Find("[data-testid='inbox-sheet']").GetAttribute("data-open")));
    }

    /// <summary>The field's blur starts the save, and Close comes while it is still
    /// writing: the close waits for that save, and when it fails the sheet stays open
    /// with the reason rather than closing on words that were never kept.</summary>
    [Fact]
    public void Closing_while_a_save_is_still_writing_waits_for_it_and_stays_open_when_it_fails()
    {
        var store = new FailingAddStore();
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item);
        using var host = ShellHost.Paired(inbox, store: store, clock: new FakeTimeProvider(Now));
        var app = OpenSheet(host);

        app.Find("[data-testid='inbox-sheet-notes'] textarea").Input("Ask for Thursday.");
        store.HoldAndFail();
        app.Find("[data-testid='inbox-sheet-notes'] textarea").Blur();
        app.WaitForAssertion(() => Assert.True(store.AddEntered.IsCompleted));

        app.Find("[data-testid='inbox-sheet-close']").Click();
        store.Release();

        app.WaitForAssertion(() => Assert.Contains("Couldn't keep the change", app.Find("[data-testid='inbox-sheet-unsaved']").TextContent, StringComparison.Ordinal));
        Assert.Equal("true", app.Find("[data-testid='inbox-sheet']").GetAttribute("data-open"));
        Assert.Equal("Ask for Thursday.", app.Find("[data-testid='inbox-sheet-notes'] textarea").GetAttribute("value"));
    }

    [Fact]
    public void An_edit_that_cannot_be_kept_can_be_discarded_and_the_sheet_closed()
    {
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item);
        using var host = ShellHost.Paired(inbox, clock: new FakeTimeProvider(Now));
        var app = OpenSheet(host);

        app.Find("[data-testid='inbox-sheet-title'] input").Input("");
        app.Find("[data-testid='inbox-sheet-close']").Click();
        app.WaitForAssertion(() => app.Find("[data-testid='inbox-sheet-discard']").Click());

        // Discarding is the way out the person asked for: the sheet closes with it,
        // and the capture keeps the words it had.
        app.WaitForAssertion(() => Assert.Equal("false", app.Find("[data-testid='inbox-sheet']").GetAttribute("data-open")));
        Assert.Equal("Call the dentist", app.Find("[data-testid='inbox-row-title']").TextContent);
        Assert.Empty(inbox.Edits);
    }

    /// <summary>The desktop decided the capture open in the sheet: there is nothing
    /// left on this phone to save, so the sheet closes rather than keep an old header
    /// over "Saved", and the calm line says why.</summary>
    [Fact]
    public void The_sheet_closes_when_the_desktop_had_already_dealt_with_its_capture()
    {
        var clock = new FakeTimeProvider(Now);
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item);
        using var host = ShellHost.Paired(inbox, clock: clock);
        var app = OpenSheet(host);
        inbox.Decide(item.Id);

        app.Find("[data-testid='inbox-sheet-title'] input").Input("Call the dentist about the crown");
        clock.Advance(SaveDelay);

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='inbox-dealt-with']")));
        app.WaitForAssertion(() => Assert.Equal("false", app.Find("[data-testid='inbox-sheet']").GetAttribute("data-open")));
        Assert.Empty(host.Outbox.Entries);
    }

    /// <summary>The service has the edit, but the list the phone holds was read
    /// before it did: the row keeps the new words until a pull that began after
    /// the delivery answers — and that pull, not the phone, then has the last word.</summary>
    [Fact]
    public void A_delivered_edit_stays_on_the_row_until_a_pull_that_began_after_it()
    {
        var clock = new FakeTimeProvider(Now);
        var item = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var inbox = new ScriptedInboxService(item) { IgnoreEdits = true };
        using var host = ShellHost.Paired(inbox, clock: clock);
        var app = OpenSheet(host);

        inbox.HoldPulls();
        app.Find("[data-testid='inbox-refresh']").Click();
        app.WaitForAssertion(() => Assert.Equal(1, inbox.HeldPulls));

        app.Find("[data-testid='inbox-sheet-title'] input").Input("Call the dentist about the crown");
        clock.Advance(SaveDelay);
        app.WaitForAssertion(() => Assert.Single(inbox.Edits));
        app.WaitForAssertion(() => Assert.Empty(host.Outbox.Entries));

        // Delivered, and the outbox no longer holds it: the row still reads the edit.
        Assert.Equal("Call the dentist about the crown", app.Find("[data-testid='inbox-row-title']").TextContent);

        inbox.ReleasePulls();

        // The held pull was read before the delivery and does not count; the one
        // after it does, and this service kept the old words.
        app.WaitForAssertion(() => Assert.Equal("Call the dentist", app.Find("[data-testid='inbox-row-title']").TextContent));
    }

    /// <summary>After a Delete the focus goes to the row that took its place, not
    /// to the body: the row it was opened from and the button it was asked from
    /// are both gone.</summary>
    [Fact]
    public void After_a_delete_the_focus_goes_to_the_row_that_took_its_place()
    {
        var first = new InboxItem(Guid.CreateVersion7(), "Call the dentist", "mobile", Now.AddMinutes(-3));
        var second = new InboxItem(Guid.CreateVersion7(), "Renew the domain", "mobile", Now.AddMinutes(-9));
        using var host = ShellHost.Paired(new ScriptedInboxService(first, second), clock: new FakeTimeProvider(Now));
        var app = host.Open();
        app.WaitForAssertion(() => Assert.Equal(2, app.FindAll("[data-testid='inbox-row']").Count));

        app.FindAll("[data-testid='inbox-row-open']")[0].Click();
        app.WaitForAssertion(() => app.Find("[data-testid='inbox-sheet-delete']").Click());
        app.WaitForAssertion(() => app.Find("[data-testid='inbox-delete-confirm-yes']").Click());

        app.WaitForAssertion(() => Assert.Contains(
            host.JSInterop.Invocations,
            call => call.Identifier == "backlogFocusIfLost" && Equals(call.Arguments[0], $"inbox-row-{second.Id:N}")));
    }

    /// <summary>The device's file, whose adds can be made to wait and then fail —
    /// a disk that is full, a database that is locked.</summary>
    private sealed class FailingAddStore : IDeviceStore
    {
        private readonly InMemoryDeviceStore _inner = new();
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource? _held;
        private bool _failing;

        public Task AddEntered => _entered.Task;

        public void HoldAndFail()
        {
            _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _failing = true;
        }

        public void Release() => _held?.TrySetResult();

        public IReadOnlyList<OutboxEntry> ReadOutbox() => _inner.ReadOutbox();

        public async Task AddAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
        {
            if (_failing)
            {
                _entered.TrySetResult();
                if (_held is { } held) await held.Task.WaitAsync(cancellationToken);
                throw new IOException("The disk is full.");
            }

            await _inner.AddAsync(entry, cancellationToken);
        }

        public Task UpdateAsync(OutboxEntry entry, CancellationToken cancellationToken = default) => _inner.UpdateAsync(entry, cancellationToken);

        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) => _inner.RemoveAsync(id, cancellationToken);

        public CachedInbox? ReadInbox() => _inner.ReadInbox();

        public Task SaveInboxAsync(CachedInbox inbox, CancellationToken cancellationToken = default) => _inner.SaveInboxAsync(inbox, cancellationToken);
    }

    private static IRenderedComponent<Routes> OpenSheet(ShellHost host)
    {
        var app = host.Open();

        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='inbox-row']")));
        app.Find("[data-testid='inbox-row-open']").Click();
        app.WaitForAssertion(() => Assert.Equal("true", app.Find("[data-testid='inbox-sheet']").GetAttribute("data-open")));

        return app;
    }
}
