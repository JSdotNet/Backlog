using System.Text.Json;

using Backlog.Mobile.UI.Events;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's knowledge events (<c>.devbook/domain/events/requirements.md#events-on-the-phone</c>,
/// local ADR 0050 §2): the owner's knowledge event feed pulled page by page and folded per id by
/// the later stamp, held while the phone holds an event, listed as the upcoming events — not
/// Skipped, last day today or later, soonest first, undated after — and read one at a time with
/// the name of its series. A service from before the feed reads as nothing yet.
/// </summary>
public sealed class EventViewProjectionTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 27, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 27);

    private readonly ScriptedEventService _service = new();
    private readonly InMemoryEventViewStore _store = new();
    private readonly FakeTimeProvider _clock = new(Morning);
    private readonly EventViewProjection _events;

    public EventViewProjectionTests() => _events = Projection();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private EventViewProjection Projection(bool paired = true) =>
        new(_store, _service.Client(), paired ? TestDevices.Paired() : TestDevices.Unpaired(), _clock);

    [Fact]
    public async Task A_phone_holding_no_event_shows_none_and_one_holding_an_event_shows_it()
    {
        Assert.False(_events.HoldsAny);

        _service.Publish(TestEvents.Series("Techorama", Morning));
        await _events.PullAsync(Cancellation);
        Assert.False(_events.HoldsAny);

        _service.Publish(TestEvents.Event("Techorama 2026", Morning, Today, Today.AddDays(1)));
        await _events.PullAsync(Cancellation);
        Assert.True(_events.HoldsAny);
    }

    [Fact]
    public async Task The_upcoming_list_leaves_out_skipped_and_past_events_and_lists_the_soonest_first_with_undated_ones_after()
    {
        _service.Publish(TestEvents.Event("Someday Summit", Morning, state: KnowledgeEventState.Spotted));
        _service.Publish(TestEvents.Event("dotNET Friday", Morning, new DateOnly(2026, 11, 6)));
        _service.Publish(TestEvents.Event("NDC Oslo", Morning, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3)));
        _service.Publish(TestEvents.Event("Yesterday's meetup", Morning, Today.AddDays(-1)));
        _service.Publish(TestEvents.Event("Tonight's meetup", Morning, Today));
        _service.Publish(TestEvents.Event("Techorama 2026", Morning, new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 28)));
        _service.Publish(TestEvents.Event("Skipped Conf", Morning, new DateOnly(2026, 11, 10), state: KnowledgeEventState.Skipped));
        _service.Publish(TestEvents.Event("Skipped someday", Morning, state: KnowledgeEventState.Skipped));
        _service.Publish(TestEvents.Event("Another someday", Morning, state: KnowledgeEventState.Considering));

        await _events.PullAsync(Cancellation);

        Assert.Equal(
            ["Techorama 2026", "Tonight's meetup", "dotNET Friday", "Another someday", "Someday Summit"],
            _events.Upcoming().Select(item => item.Title));
    }

    [Fact]
    public async Task The_upcoming_list_moves_with_the_day()
    {
        _service.Publish(TestEvents.Event("Tonight's meetup", Morning, Today));
        await _events.PullAsync(Cancellation);
        Assert.Single(_events.Upcoming());

        _clock.Advance(TimeSpan.FromDays(1));

        Assert.Empty(_events.Upcoming());
        Assert.True(_events.HoldsAny);
    }

    [Fact]
    public async Task A_later_copy_replaces_the_event_and_an_earlier_one_arriving_late_changes_nothing()
    {
        var id = Guid.NewGuid();
        _service.Publish(TestEvents.Event("Techorama 2026", Morning, Today, id: id));
        _service.Publish(TestEvents.Event("Techorama 2026 (moved)", Morning.AddHours(1), Today.AddDays(1), id: id));
        _service.Publish(TestEvents.Event("Techorama 2026 (stale)", Morning.AddMinutes(30), Today, id: id));

        await _events.PullAsync(Cancellation);

        var held = Assert.Single(_events.Upcoming());
        Assert.Equal("Techorama 2026 (moved)", held.Title);
        Assert.Equal(Today.AddDays(1), held.FirstDay);
    }

    [Fact]
    public async Task An_event_reads_whole_with_the_name_of_its_series()
    {
        var series = TestEvents.Series("Techorama", Morning);
        var knowledgeEvent = TestEvents.Event(
            "Techorama 2026",
            Morning,
            new DateOnly(2026, 10, 26),
            new DateOnly(2026, 10, 28),
            seriesId: series.Id,
            notes: [new EventNoteLinkReplicaDto(NoteLinkKind.Brain, "knowledge/aspire.md", "Aspire")]);
        _service.Publish(knowledgeEvent);
        _service.Publish(series);

        await _events.PullAsync(Cancellation);

        var found = _events.Find(knowledgeEvent.Id);
        Assert.NotNull(found);
        Assert.Equal("Techorama", found.SeriesName);
        Assert.Equal(
            (knowledgeEvent.Title, knowledgeEvent.State, knowledgeEvent.Venue, knowledgeEvent.Link, knowledgeEvent.WhatToLearn),
            (found.Title, found.State, found.Event.Venue, found.Event.Link, found.Event.WhatToLearn));
        Assert.Equal(knowledgeEvent.KeyDates, found.Event.KeyDates);
        Assert.Equal(knowledgeEvent.Notes, found.Event.Notes);
        Assert.Equal(new DateOnly(2026, 10, 26), found.FirstDay);
        Assert.Equal(new DateOnly(2026, 10, 28), found.LastDay);
        Assert.Null(_events.Find(Guid.NewGuid()));
    }

    [Fact]
    public async Task The_events_a_talk_note_is_attached_to_are_found_by_the_note()
    {
        var noteId = Guid.NewGuid();
        _service.Publish(TestEvents.Event("Techorama 2026", Morning, Today, notes: [new EventNoteLinkReplicaDto(NoteLinkKind.Talk, noteId.ToString("D"), "Keynote")]));
        _service.Publish(TestEvents.Event("dotNET Friday", Morning, Today.AddDays(9), notes: [new EventNoteLinkReplicaDto(NoteLinkKind.Brain, noteId.ToString("D"), "Same text, a Brain path")]));

        await _events.PullAsync(Cancellation);

        Assert.Equal("Techorama 2026", Assert.Single(_events.LinkingNote(noteId)).Title);
        Assert.Empty(_events.LinkingNote(Guid.NewGuid()));
    }

    [Fact]
    public async Task The_pull_follows_the_pages_until_the_feed_is_drained_and_resumes_from_the_cursor()
    {
        _service.PageSize = 2;
        for (var i = 0; i < 5; i++) _service.Publish(TestEvents.Event($"Meetup {i}", Morning, Today.AddDays(i)));

        await _events.PullAsync(Cancellation);
        Assert.Equal(5, _events.Upcoming().Count);
        Assert.Equal(3, _service.Pulls);

        _service.Publish(TestEvents.Event("Meetup 5", Morning, Today.AddDays(5)));
        await _events.PullAsync(Cancellation);

        Assert.Equal(6, _events.Upcoming().Count);
        Assert.Equal("c:5", _service.CursorsAsked[^1]);
    }

    [Fact]
    public async Task What_was_pulled_is_there_after_a_restart_before_any_pull()
    {
        _service.Publish(TestEvents.Event("Techorama 2026", Morning, Today));
        await _events.PullAsync(Cancellation);

        using var restarted = Projection();

        Assert.True(restarted.HoldsAny);
        Assert.Equal("Techorama 2026", Assert.Single(restarted.Upcoming()).Title);
    }

    [Fact]
    public async Task A_service_from_before_the_feed_reads_as_nothing_yet()
    {
        _service.State = EventFeedState.NotServedYet;

        await _events.PullAsync(Cancellation);

        Assert.False(_events.HoldsAny);
        Assert.Null(_store.ReadCursor());
    }

    [Fact]
    public async Task A_404_that_names_a_problem_is_a_failure_for_the_screen_to_word()
    {
        _service.State = EventFeedState.CodedNotFound;

        var failure = await Assert.ThrowsAsync<SyncUnavailableException>(() => _events.PullAsync(Cancellation));

        Assert.Equal("sync.device_not_found", failure.Code);
    }

    [Fact]
    public async Task A_cursor_the_service_no_longer_takes_is_dropped_and_the_pull_starts_again()
    {
        _service.Publish(TestEvents.Event("Techorama 2026", Morning, Today));
        await _events.PullAsync(Cancellation);
        _service.Publish(TestEvents.Event("dotNET Friday", Morning, Today.AddDays(9)));
        _service.State = EventFeedState.CursorExpiredOnce;

        await _events.PullAsync(Cancellation);

        Assert.Equal(2, _events.Upcoming().Count);
        Assert.Null(_service.CursorsAsked[^1]);
    }

    [Fact]
    public async Task A_document_this_build_cannot_read_is_passed_over()
    {
        using var unreadable = JsonDocument.Parse("""{"id":"not a guid"}""");
        _service.Store(new KnowledgeEventChange(Guid.NewGuid(), KnowledgeEventKinds.KnowledgeEvent, Morning, unreadable.RootElement.Clone()));
        _service.Publish(TestEvents.Event("Techorama 2026", Morning, Today));

        await _events.PullAsync(Cancellation);

        Assert.Equal("Techorama 2026", Assert.Single(_events.Upcoming()).Title);
    }

    [Fact]
    public async Task A_pull_that_brought_something_says_so_and_one_that_did_not_stays_quiet()
    {
        var raised = 0;
        _events.Changed += () => raised++;
        _service.Publish(TestEvents.Event("Techorama 2026", Morning, Today));

        await _events.PullAsync(Cancellation);
        await _events.PullAsync(Cancellation);

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task An_unpaired_phone_pulls_nothing()
    {
        _service.Publish(TestEvents.Event("Techorama 2026", Morning, Today));
        using var unpaired = Projection(paired: false);

        await unpaired.PullAsync(Cancellation);

        Assert.Equal(0, _service.Pulls);
        Assert.False(unpaired.HoldsAny);
    }

    public void Dispose() => _events.Dispose();
}
