using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Events;
using Backlog.Mobile.UI.Notes;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Services;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's Events screens as a person meets them
/// (<c>.devbook/domain/events/requirements.md#events-on-the-phone</c>): the tab that appears once
/// a desktop has sent an event, the upcoming list soonest first, the read-only detail with every
/// field the phone shows, and attaching a Talk note to an event from the Notes tab.
/// </summary>
public sealed class EventScreenTests
{
    /// <summary>Wednesday 21 October 2026, on a phone whose local zone is UTC — the fake clock's.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 41, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Pushed = Now.AddHours(-1);

    private static (ShellHost Host, ScriptedEventService Events) Phone(
        ScriptedTaskService? tasks = null,
        ScriptedNotificationService? notifications = null,
        IReadOnlyList<KnowledgeEventReplicaDto>? events = null,
        IDeviceStore? store = null)
    {
        var service = new ScriptedEventService();
        foreach (var knowledgeEvent in events ?? []) service.Publish(knowledgeEvent);

        return (ShellHost.Paired(store: store, clock: new FakeTimeProvider(Now), tasks: tasks, notifications: notifications, events: service), service);
    }

    private static List<string> Tabs(IRenderedComponent<Routes> app) =>
        [.. app.FindAll("[data-testid='tab-bar'] a").Select(tab => tab.TextContent.Trim())];

    [Fact]
    public void The_tab_shows_before_notes_once_the_phone_holds_an_event()
    {
        var (host, _) = Phone(events: [TestEvents.Event("Techorama 2026", Pushed, new DateOnly(2026, 10, 26))]);
        using var disposing = host;

        var app = host.Open("");

        app.WaitForAssertion(() => Assert.Contains("Events", Tabs(app)));
        Assert.Equal(["Today", "Inbox", "Events", "Notes"], Tabs(app));
        Assert.NotNull(app.Find("[data-testid='tab-bar'] a[href='events'] .calendar-icon"));
    }

    [Fact]
    public void The_tab_follows_notifications_when_both_are_held()
    {
        var notifications = new ScriptedNotificationService();
        notifications.Publish(TestNotifications.Question("Run the migration?", Now.AddMinutes(-2)), Now.AddMinutes(-1));
        var (host, _) = Phone(
            notifications: notifications,
            events: [TestEvents.Event("Techorama 2026", Pushed, new DateOnly(2026, 10, 26))]);
        using var disposing = host;

        var app = host.Open("");

        app.WaitForAssertion(() => Assert.Equal(["Today", "Inbox", "Notifications", "Events", "Notes"], Tabs(app)));
    }

    [Fact]
    public void No_event_means_no_tab_even_with_a_series_held()
    {
        var (host, events) = Phone();
        using var disposing = host;
        events.Publish(TestEvents.Series("Techorama", Pushed));

        var app = host.Open("");

        app.WaitForAssertion(() => Assert.True(events.Pulls > 0));
        Assert.DoesNotContain("Events", Tabs(app));
    }

    [Fact]
    public void The_list_shows_upcoming_events_soonest_first_and_leaves_skipped_and_past_ones_off()
    {
        var far = TestEvents.Event("NDC Porto", Pushed, new DateOnly(2026, 11, 20));
        var near = TestEvents.Event("Techorama 2026", Pushed, new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 28));
        var endsToday = TestEvents.Event("DevDays", Pushed, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 21), KnowledgeEventState.Attended);
        var undated = TestEvents.Event("Build 2027", Pushed, state: KnowledgeEventState.Spotted);
        var past = TestEvents.Event("Update Conference", Pushed, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 20));
        var skipped = TestEvents.Event("Cloud Summit", Pushed, new DateOnly(2026, 11, 2), state: KnowledgeEventState.Skipped);
        var (host, _) = Phone(events: [far, past, undated, skipped, near, endsToday]);
        using var disposing = host;

        var app = host.Open("events");

        app.WaitForAssertion(() => Assert.Equal(4, app.FindAll("[data-testid='event-row']").Count));
        var rows = app.FindAll("[data-testid='event-row']");
        Assert.Equal(
            ["DevDays", "Techorama 2026", "NDC Porto", "Build 2027"],
            rows.Select(row => row.QuerySelector("[data-testid='event-row-title']")!.TextContent));
        Assert.Equal(
            ["Attended", "Going", "Going", "Spotted"],
            rows.Select(row => row.QuerySelector("[data-testid='event-row-when']")!.TextContent));
        Assert.Equal("26 Oct – 28 Oct 2026 · Kinepolis Utrecht", rows[1].QuerySelector("[data-testid='event-row-snippet']")!.TextContent);
        Assert.Equal("Date not known · Kinepolis Utrecht", rows[3].QuerySelector("[data-testid='event-row-snippet']")!.TextContent);
        Assert.Equal($"events/{near.Id:D}", rows[1].GetAttribute("href"));
        Assert.Equal("4 upcoming events", app.Find("[data-testid='events-lede']").TextContent);
        Assert.Contains("tab-bar__tab--active", app.Find("[data-testid='tab-bar-events']").ClassList);
    }

    [Fact]
    public void A_held_event_that_is_over_leaves_the_list_saying_nothing_is_coming_up()
    {
        var (host, _) = Phone(events: [TestEvents.Event("Update Conference", Pushed, new DateOnly(2026, 10, 1))]);
        using var disposing = host;

        var app = host.Open("events");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='events-none']")));
        Assert.Empty(app.FindAll("[data-testid='event-row']"));
        Assert.Equal("Nothing coming up", app.Find("[data-testid='events-lede']").TextContent);
    }

    [Fact]
    public void The_detail_shows_every_field_and_nothing_that_edits()
    {
        var seriesId = Guid.NewGuid();
        var techorama = TestEvents.Event("Techorama 2026", Pushed, new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 28), seriesId: seriesId) with
        {
            Start = new EventDateReplicaDto(new DateOnly(2026, 10, 26), new TimeOnly(8, 30), TimeSpan.FromHours(1)),
            Online = true,
            KeyDates =
            [
                new EventKeyDateReplicaDto(new DateOnly(2026, 9, 15), "Call for papers closes"),
                new EventKeyDateReplicaDto(new DateOnly(2026, 6, 30), "Early bird ends"),
            ],
            WhatToLearn = "Aspire in production\nHow others run Blazor on phones",
        };
        var (host, events) = Phone(events: [techorama]);
        using var disposing = host;
        events.Publish(TestEvents.Series("Techorama", Pushed, seriesId));

        var app = host.Open($"events/{techorama.Id:D}");

        app.WaitForAssertion(() => Assert.Equal("Techorama 2026", app.Find("[data-testid='event-title']").TextContent));
        Assert.Equal("Going", app.Find("[data-testid='event-state']").TextContent.Trim());
        Assert.Equal("Techorama", app.Find("[data-testid='event-series']").TextContent);
        Assert.Equal("Mon 26 Oct 2026, 08:30", app.Find("[data-testid='event-start']").TextContent);
        Assert.Equal("Wed 28 Oct 2026", app.Find("[data-testid='event-end']").TextContent);
        Assert.Equal("Kinepolis Utrecht · also online", app.Find("[data-testid='event-where']").TextContent);

        var link = app.Find("[data-testid='event-link']");
        Assert.Equal("https://techorama.nl/2026", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("noopener", link.GetAttribute("rel"), StringComparison.Ordinal);

        Assert.Equal(
            ["Tue 30 Jun 2026 Early bird ends", "Tue 15 Sep 2026 Call for papers closes"],
            app.FindAll("[data-testid='event-key-date']").Select(row => string.Join(' ', row.Children.Select(part => part.TextContent))));
        Assert.Contains("How others run Blazor on phones", app.Find("[data-testid='event-what-to-learn']").TextContent, StringComparison.Ordinal);
        Assert.NotNull(app.Find("[data-testid='event-notes-none']"));

        // Read only: no field, no button, and nothing of the event leaves the phone.
        Assert.Empty(app.FindAll(
            "[data-testid='event-page'] input, [data-testid='event-page'] textarea, [data-testid='event-page'] select, [data-testid='event-page'] button"));
        Assert.Empty(host.Outbox.Entries);
    }

    [Fact]
    public void A_link_that_is_not_a_web_address_is_shown_and_never_followed()
    {
        var odd = TestEvents.Event("Techorama 2026", Pushed, new DateOnly(2026, 10, 26)) with { Link = "javascript:alert(1)" };
        var (host, _) = Phone(events: [odd]);
        using var disposing = host;

        var app = host.Open($"events/{odd.Id:D}");

        app.WaitForAssertion(() => Assert.Equal("javascript:alert(1)", app.Find("[data-testid='event-link-text']").TextContent));
        Assert.Empty(app.FindAll("[data-testid='event-link']"));
    }

    [Fact]
    public void A_talk_note_the_phone_holds_opens_and_a_brain_note_shows_its_title_only()
    {
        var keynote = Note("Keynote", Now.AddDays(-1));
        var tasks = new ScriptedTaskService(keynote);
        var techorama = TestEvents.Event(
            "Techorama 2026",
            Pushed,
            new DateOnly(2026, 10, 26),
            notes:
            [
                new EventNoteLinkReplicaDto(NoteLinkKind.Brain, "Talks/aspire-in-production.md", "Aspire in production"),
                new EventNoteLinkReplicaDto(NoteLinkKind.Talk, keynote.Id.ToString("D"), "Keynote"),
                new EventNoteLinkReplicaDto(NoteLinkKind.Talk, Guid.NewGuid().ToString("D"), "Hallway track"),
            ]);
        var (host, _) = Phone(tasks: tasks, events: [techorama]);
        using var disposing = host;
        var app = host.Open("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));

        host.Navigation.NavigateTo($"events/{techorama.Id:D}");

        app.WaitForAssertion(() => Assert.Equal(3, app.FindAll("[data-testid='event-note']").Count));
        var notes = app.FindAll("[data-testid='event-note']");

        var brain = notes[0];
        Assert.Equal("brain", brain.GetAttribute("data-kind"));
        Assert.Empty(brain.QuerySelectorAll("a"));
        Assert.Equal("Aspire in production", brain.QuerySelector("[data-testid='event-note-title']")!.TextContent);

        var held = Assert.Single(notes[1].QuerySelectorAll("a"));
        Assert.Equal($"notes/{keynote.Id:D}", held.GetAttribute("href"));
        Assert.Equal("Keynote", held.TextContent);

        // A Talk note this phone does not hold has no page to open.
        Assert.Empty(notes[2].QuerySelectorAll("a"));
        Assert.Equal("Hallway track", notes[2].QuerySelector("[data-testid='event-note-title']")!.TextContent);
    }

    [Fact]
    public async Task Attaching_a_note_queues_one_request_waits_then_reads_attached_once_the_desktop_links_it()
    {
        var keynote = Note("Keynote", Now.AddDays(-1));
        var tasks = new ScriptedTaskService(keynote);
        var techorama = TestEvents.Event("Techorama 2026", Pushed, new DateOnly(2026, 10, 26));
        var past = TestEvents.Event("Update Conference", Pushed, new DateOnly(2026, 10, 1));
        var (host, events) = Phone(tasks: tasks, events: [past, techorama]);
        using var disposing = host;
        var app = host.Open("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));

        // No signal from here: the request waits in the outbox.
        tasks.State = InboxServiceState.Unreachable;
        host.Navigation.NavigateTo($"notes/{keynote.Id:D}");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-event-select'] select")));

        // Upcoming first, then the ones that are over.
        Assert.Equal(
            ["Choose an event", "Techorama 2026 · 26 Oct 2026", "Update Conference · 1 Oct 2026"],
            app.FindAll("[data-testid='note-event-select'] option").Select(option => option.TextContent.Trim()));
        Assert.True(app.Find("[data-testid='note-event-attach']").HasAttribute("disabled"));

        app.Find("[data-testid='note-event-select'] select").Change(techorama.Id.ToString("D"));
        app.Find("[data-testid='note-event-attach']").Click();

        app.WaitForAssertion(() => Assert.Contains(
            "Waiting to send",
            app.Find("[data-testid='note-event-pending']").TextContent,
            StringComparison.Ordinal));
        Assert.Contains("Techorama 2026", app.Find("[data-testid='note-event-pending']").TextContent, StringComparison.Ordinal);
        var entry = Assert.Single(host.Outbox.Entries);
        Assert.Equal(EventAttachmentRequestOutboxKind.Token, entry.Kind);
        Assert.Equal(techorama.Id, host.Service<EventNoteAttachments>().PendingEventFor(keynote.Id));

        // Delivered: the phone still says it was sent while the desktop has not pushed the event back.
        tasks.State = InboxServiceState.Answering;
        await host.Outbox.ResumeAsync(TestContext.Current.CancellationToken);
        app.WaitForAssertion(() => Assert.StartsWith(
            "Sent.",
            app.Find("[data-testid='note-event-pending']").TextContent.Trim(),
            StringComparison.Ordinal));
        Assert.Empty(host.Outbox.Entries);
        Assert.Contains(tasks.Pushed, change => change.Id == entry.Id);

        // A desktop applied it: the event's notes name the note, and the page says attached.
        events.Publish(techorama with
        {
            Notes = [new EventNoteLinkReplicaDto(NoteLinkKind.Talk, keynote.Id.ToString("D"), "Keynote")],
            UpdatedAt = Now,
        });
        host.Service<AppLifecycle>().Resume();

        app.WaitForAssertion(() => Assert.Equal(
            "Attached to Techorama 2026",
            app.Find("[data-testid='note-event-attached']").TextContent.Trim()));
        Assert.Empty(app.FindAll("[data-testid='note-event-pending']"));

        // It is not offered again; the other event still is.
        Assert.DoesNotContain(
            "Techorama 2026 · 26 Oct 2026",
            app.FindAll("[data-testid='note-event-select'] option").Select(option => option.TextContent.Trim()));
    }

    [Fact]
    public void A_note_offers_no_event_while_the_phone_holds_none()
    {
        var keynote = Note("Keynote", Now.AddDays(-1));
        var (host, events) = Phone(tasks: new ScriptedTaskService(keynote));
        using var disposing = host;
        var app = host.Open("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));
        app.WaitForAssertion(() => Assert.True(events.Pulls > 0));

        host.Navigation.NavigateTo($"notes/{keynote.Id:D}");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-editor']")));
        Assert.Empty(app.FindAll("[data-testid='note-event']"));
    }

    [Fact]
    public void A_note_shows_no_event_section_while_every_event_the_phone_holds_is_skipped()
    {
        var keynote = Note("Keynote", Now.AddDays(-1));
        var skipped = TestEvents.Event("Cloud Summit", Pushed, new DateOnly(2026, 11, 2), state: KnowledgeEventState.Skipped);
        var (host, _) = Phone(tasks: new ScriptedTaskService(keynote), events: [skipped]);
        using var disposing = host;
        var app = host.Open("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));
        app.WaitForAssertion(() => Assert.Contains("Events", Tabs(app)));

        host.Navigation.NavigateTo($"notes/{keynote.Id:D}");

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-editor']")));
        Assert.Empty(app.FindAll("[data-testid='note-event']"));
    }

    [Fact]
    public async Task A_request_the_outbox_parked_says_why_and_goes_on_a_tap()
    {
        var keynote = Note("Keynote", Now.AddDays(-1));
        var tasks = new ScriptedTaskService(keynote);
        var techorama = TestEvents.Event("Techorama 2026", Pushed, new DateOnly(2026, 10, 26));
        var store = new InMemoryDeviceStore();
        var parked = Request(techorama.Id, keynote.Id) with { Attempts = DeviceOutbox.MaxAttempts, LastError = "No such host is known." };
        await store.AddAsync(parked, TestContext.Current.CancellationToken);
        var (host, _) = Phone(tasks: tasks, events: [techorama], store: store);
        using var disposing = host;
        var app = host.Open("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));

        host.Navigation.NavigateTo($"notes/{keynote.Id:D}");

        app.WaitForAssertion(() => Assert.Equal(
            "Not sent yet: this note will be attached to Techorama 2026. No such host is known.",
            app.Find("[data-testid='note-event-pending']").FirstChild!.TextContent.Trim()));

        app.Find("[data-testid='note-event-retry']").Click();

        app.WaitForAssertion(() => Assert.Empty(host.Outbox.Entries));
        Assert.Contains(tasks.Pushed, change => change.Id == parked.Id);
        app.WaitForAssertion(() => Assert.Empty(app.FindAll("[data-testid='note-event-pending']")));
    }

    [Fact]
    public async Task A_request_the_service_refused_says_why_and_attaching_again_sends_a_new_one()
    {
        var keynote = Note("Keynote", Now.AddDays(-1));
        var tasks = new ScriptedTaskService(keynote);
        var techorama = TestEvents.Event("Techorama 2026", Pushed, new DateOnly(2026, 10, 26));
        var store = new InMemoryDeviceStore();
        var refused = Request(techorama.Id, keynote.Id) with { Refused = true, LastError = "The request names no event." };
        await store.AddAsync(refused, TestContext.Current.CancellationToken);
        var (host, _) = Phone(tasks: tasks, events: [techorama], store: store);
        using var disposing = host;
        var app = host.Open("notes");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-row']")));

        // No signal from here: the new request waits in the outbox.
        tasks.State = InboxServiceState.Unreachable;
        host.Navigation.NavigateTo($"notes/{keynote.Id:D}");

        app.WaitForAssertion(() => Assert.StartsWith(
            "Techorama 2026 could not take this note: The request names no event.",
            app.Find("[data-testid='note-event-pending']").TextContent.Trim(),
            StringComparison.Ordinal));
        Assert.Empty(app.FindAll("[data-testid='note-event-retry']"));

        app.Find("[data-testid='note-event-select'] select").Change(techorama.Id.ToString("D"));
        app.Find("[data-testid='note-event-attach']").Click();

        app.WaitForAssertion(() => Assert.StartsWith(
            "Waiting to send",
            app.Find("[data-testid='note-event-pending']").TextContent.Trim(),
            StringComparison.Ordinal));
        Assert.Equal(2, host.Outbox.Entries.Count(entry => entry.Kind == EventAttachmentRequestOutboxKind.Token));
    }

    [Fact]
    public async Task Turning_to_another_note_while_attach_saves_sends_no_request_for_either()
    {
        var keynote = Note("Keynote", Now.AddDays(-1));
        var hallway = Note("Hallway track", Now.AddDays(-1));
        var tasks = new ScriptedTaskService(keynote, hallway);
        var techorama = TestEvents.Event("Techorama 2026", Pushed, new DateOnly(2026, 10, 26));
        var store = new HeldDeviceStore();
        var (host, _) = Phone(tasks: tasks, events: [techorama], store: store);
        using var disposing = host;
        var app = host.Open("notes");
        app.WaitForAssertion(() => Assert.Equal(2, app.FindAll("[data-testid='note-row']").Count));
        tasks.State = InboxServiceState.Unreachable;

        host.Navigation.NavigateTo($"notes/{keynote.Id:D}");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='note-event-select'] select")));

        // An edit still waiting to be saved: Attach saves it first, and the save is held.
        app.Find("[data-testid='note-editor-title'] input").Input("Keynote, revised");
        app.Find("[data-testid='note-event-select'] select").Change(techorama.Id.ToString("D"));
        var release = store.HoldNextAdd();
        var attaching = app.Find("[data-testid='note-event-attach']").ClickAsync(new());

        host.Navigation.NavigateTo($"notes/{hallway.Id:D}");
        app.WaitForAssertion(() => Assert.Equal("Hallway track", app.Find("[data-testid='note-editor-title'] input").GetAttribute("value")));

        release.SetResult();
        await attaching;

        Assert.DoesNotContain(host.Outbox.Entries, entry => entry.Kind == EventAttachmentRequestOutboxKind.Token);
        Assert.Null(host.Service<EventNoteAttachments>().PendingEventFor(hallway.Id));
    }

    private static TaskChange Note(string title, DateTimeOffset at) =>
        NoteViewProjection.NewNote(Guid.CreateVersion7(), title, "Taken in the main hall.", [], at);

    /// <summary>An attachment request as the phone queued it before this visit.</summary>
    private static OutboxEntry Request(Guid eventId, Guid noteId)
    {
        var requestId = Guid.CreateVersion7();
        var change = EventAttachmentReplicaDocuments.ToChange(new EventAttachmentRequestDto(requestId, eventId, noteId, "Keynote"), Pushed);
        return new OutboxEntry(requestId, EventAttachmentRequestOutboxKind.Token, TaskOutboxKind.Write(change), 0, null, Pushed);
    }

    /// <summary>A store whose next add waits until the test lets it finish, so a save is still
    /// on its way while the test turns the page.</summary>
    private sealed class HeldDeviceStore : IDeviceStore
    {
        private readonly InMemoryDeviceStore _inner = new();
        private TaskCompletionSource? _hold;

        public TaskCompletionSource HoldNextAdd() =>
            _hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<OutboxEntry> ReadOutbox() => _inner.ReadOutbox();

        public async Task AddAsync(OutboxEntry entry, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _hold, null) is { } hold) await hold.Task;
            await _inner.AddAsync(entry, cancellationToken);
        }

        public Task UpdateAsync(OutboxEntry entry, CancellationToken cancellationToken = default) => _inner.UpdateAsync(entry, cancellationToken);

        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) => _inner.RemoveAsync(id, cancellationToken);

        public CachedInbox? ReadInbox() => _inner.ReadInbox();

        public Task SaveInboxAsync(CachedInbox inbox, CancellationToken cancellationToken = default) => _inner.SaveInboxAsync(inbox, cancellationToken);
    }
}
