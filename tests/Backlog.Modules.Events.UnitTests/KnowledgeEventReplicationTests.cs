using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Events.DomainModels;
using Backlog.Modules.Events.Extensions;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The published replication port (local ADR 0050 §2): every event and series this device
/// changed since a watermark, as the whole document that travels, and another device's copy
/// taken only when it is later — keeping its stamp — and refused when it does not read as an
/// event at all. Resolved through <c>AddEventsModule()</c> over the host's replica stores.
/// </summary>
public sealed class KnowledgeEventReplicationTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Amsterdam = TimeSpan.FromHours(1);

    private readonly InMemoryKnowledgeEventReplicaStore _events = new();
    private readonly InMemoryEventSeriesReplicaStore _series = new();
    private readonly ServiceProvider _provider;
    private readonly IKnowledgeEventReplication _replication;

    public KnowledgeEventReplicationTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IKnowledgeEventReplicaStore>(_events);
        services.AddSingleton<IEventSeriesReplicaStore>(_series);

        _provider = services.AddEventsModule().BuildServiceProvider();
        _replication = _provider.GetRequiredService<IKnowledgeEventReplication>();
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A Going event with every field set through the aggregate's own rules.</summary>
    private static KnowledgeEvent Techorama(Guid series)
    {
        var knowledgeEvent = KnowledgeEvent.Create(Guid.NewGuid(), "Techorama 2026").Value;

        knowledgeEvent.SetSeries(series);
        Assert.True(knowledgeEvent.SetDates(
            EventDate.Create(new DateOnly(2026, 10, 26), new TimeOnly(8, 30), Amsterdam).Value,
            EventDate.Create(new DateOnly(2026, 10, 28), null, null).Value,
            DateSource.EventPage).IsSuccess);
        knowledgeEvent.SetPlace(Place.AtVenue("Kinepolis Utrecht").Value);
        knowledgeEvent.SetLink("https://techorama.nl/2026");
        knowledgeEvent.SetWhatToLearn("Aspire in production");
        knowledgeEvent.SetKeyDates([KeyDate.Create(new DateOnly(2026, 6, 30), "Early bird ends").Value]);
        knowledgeEvent.SetTicket(TicketState.Bought);
        knowledgeEvent.SetCost(Cost.Create(1295.50m, "EUR").Value);
        Assert.True(knowledgeEvent.LinkNote(NoteLink.Create(NoteLinkKind.Brain, "knowledge/aspire.md", "Aspire")).IsSuccess);
        Assert.True(knowledgeEvent.LinkNote(NoteLink.Create(NoteLinkKind.Talk, "6f1c2d3e-0000-0000-0000-000000000042", "Keynote")).IsSuccess);
        Assert.True(knowledgeEvent.LinkTask(EventTaskLink.Create("task-hotel", TaskPurpose.Prep)).IsSuccess);
        Assert.True(knowledgeEvent.MoveTo(KnowledgeEventState.Going).IsSuccess);

        return knowledgeEvent;
    }

    private static KnowledgeEventReplicaDto Copy(Guid id, string title, DateTimeOffset at) =>
        new(id, title, KnowledgeEventState.Spotted, null, null, null, null, false, null, [], null, null, [], null, null, null, [], null, at);

    [Fact]
    public async Task A_saved_event_travels_whole_with_its_stamp()
    {
        var seriesId = Guid.NewGuid();
        var saved = _events.Seed(Techorama(seriesId), Noon);

        var copy = Assert.Single(await _replication.ListEventsChangedSinceAsync(Noon.AddTicks(-1), Cancellation));

        Assert.Equal(saved.Id, copy.Id);
        Assert.Equal("Techorama 2026", copy.Title);
        Assert.Equal(KnowledgeEventState.Going, copy.State);
        Assert.Equal(new EventDateReplicaDto(new DateOnly(2026, 10, 26), new TimeOnly(8, 30), Amsterdam), copy.Start);
        Assert.Equal(new EventDateReplicaDto(new DateOnly(2026, 10, 28), null, null), copy.End);
        Assert.Equal(DateSource.EventPage, copy.DateSource);
        Assert.Equal("Kinepolis Utrecht", copy.Venue);
        Assert.False(copy.Online);
        Assert.Equal("https://techorama.nl/2026", copy.Link);
        Assert.Equal([new EventKeyDateReplicaDto(new DateOnly(2026, 6, 30), "Early bird ends")], copy.KeyDates);
        Assert.Equal("Aspire in production", copy.WhatToLearn);
        Assert.Equal(seriesId, copy.SeriesId);
        Assert.Equal(
            [
                new EventNoteLinkReplicaDto(NoteLinkKind.Brain, "knowledge/aspire.md", "Aspire"),
                new EventNoteLinkReplicaDto(NoteLinkKind.Talk, "6f1c2d3e-0000-0000-0000-000000000042", "Keynote"),
            ],
            copy.Notes);
        Assert.Equal(TicketState.Bought, copy.Ticket);
        Assert.Equal(new EventCostReplicaDto(1295.50m, "EUR"), copy.Cost);
        Assert.Equal([new EventTaskLinkReplicaDto("task-hotel", TaskPurpose.Prep)], copy.Tasks);
        Assert.Null(copy.SkipReason);
        Assert.Null(copy.Verdict);
        Assert.Equal(Noon, copy.UpdatedAt);

        Assert.Empty(await _replication.ListEventsChangedSinceAsync(Noon, Cancellation));
    }

    /// <summary>What one desktop sends, another takes field for field — and sends on at the
    /// same stamp, so the replica sees the version it already holds.</summary>
    [Fact]
    public async Task A_copy_from_another_desktop_is_taken_whole_at_its_own_stamp()
    {
        var sent = Assert.Single(await Sender().ListEventsChangedSinceAsync(DateTimeOffset.MinValue, Cancellation));

        Assert.Equal(KnowledgeEventReplicaOutcome.Taken, await _replication.ApplyEventAsync(sent, Cancellation));

        var held = _events.Find(sent.Id)!;
        Assert.Equal(Noon.AddHours(-2), held.UpdatedAt);
        Assert.Equal(KnowledgeEventState.Going, held.Event.State);
        Assert.Equal("Kinepolis Utrecht", held.Event.Place!.Venue);
        Assert.Equal(new TimeOnly(8, 30), held.Event.Start!.Time);
        Assert.Equal(NoteLinkKind.Talk, held.Event.Notes[1].Kind);
        Assert.Equal("Keynote", held.Event.Notes[1].Title);

        var resent = Assert.Single(await _replication.ListEventsChangedSinceAsync(DateTimeOffset.MinValue, Cancellation));
        Assert.Equal(sent.UpdatedAt, resent.UpdatedAt);
        Assert.Equal(sent.Start, resent.Start);
        Assert.Equal(sent.Cost, resent.Cost);
        Assert.Equal(sent.Notes, resent.Notes);
        Assert.Equal(sent.KeyDates, resent.KeyDates);
        Assert.Equal(sent.Tasks, resent.Tasks);
    }

    [Fact]
    public async Task A_later_copy_replaces_the_event_and_one_no_later_changes_nothing()
    {
        var id = _events.Seed(KnowledgeEvent.Create(Guid.NewGuid(), "dotNET Friday").Value, Noon).Id;

        Assert.Equal(KnowledgeEventReplicaOutcome.NotLater, await _replication.ApplyEventAsync(Copy(id, "Older", Noon.AddMinutes(-1)), Cancellation));
        Assert.Equal(KnowledgeEventReplicaOutcome.NotLater, await _replication.ApplyEventAsync(Copy(id, "Same stamp", Noon), Cancellation));
        Assert.Equal("dotNET Friday", _events.Find(id)!.Event.Title);

        Assert.Equal(KnowledgeEventReplicaOutcome.Taken, await _replication.ApplyEventAsync(Copy(id, "dotNET Friday Q4", Noon.AddMinutes(1)), Cancellation));
        Assert.Equal("dotNET Friday Q4", _events.Find(id)!.Event.Title);
    }

    public static TheoryData<string> UnreadableCopies() =>
    [
        "no id",
        "blank title",
        "a state outside the set",
        "a time with no offset",
        "a venue that is also online",
        "a note link with no reference",
        "a note link of no known kind",
        "a key date with no label",
        "a negative cost",
        "a task link with no task",
    ];

    [Theory]
    [MemberData(nameof(UnreadableCopies))]
    public async Task A_copy_that_does_not_read_as_an_event_is_refused_and_writes_nothing(string fault)
    {
        var id = Guid.NewGuid();
        var good = Copy(id, "Techorama 2026", Noon);

        var copy = fault switch
        {
            "no id" => good with { Id = Guid.Empty },
            "blank title" => good with { Title = "  " },
            "a state outside the set" => good with { State = (KnowledgeEventState)42 },
            "a time with no offset" => good with
            {
                Start = new EventDateReplicaDto(new DateOnly(2026, 10, 26), new TimeOnly(9, 0), null),
                DateSource = DateSource.Typed,
            },
            "a venue that is also online" => good with { Venue = "Kinepolis", Online = true },
            "a note link with no reference" => good with { Notes = [new EventNoteLinkReplicaDto(NoteLinkKind.Talk, " ", "Keynote")] },
            "a note link of no known kind" => good with { Notes = [new EventNoteLinkReplicaDto((NoteLinkKind)7, "x", "Keynote")] },
            "a key date with no label" => good with { KeyDates = [new EventKeyDateReplicaDto(new DateOnly(2026, 6, 30), "")] },
            "a negative cost" => good with { Cost = new EventCostReplicaDto(-1m, "EUR") },
            "a task link with no task" => good with { Tasks = [new EventTaskLinkReplicaDto("", TaskPurpose.Prep)] },
            _ => throw new ArgumentOutOfRangeException(nameof(fault), fault, null),
        };

        Assert.Equal(KnowledgeEventReplicaOutcome.Unreadable, await _replication.ApplyEventAsync(copy, Cancellation));
        Assert.Empty(await _replication.ListEventsChangedSinceAsync(DateTimeOffset.MinValue, Cancellation));
    }

    /// <summary>A document from a build whose lists the serializer left out reads as an event
    /// with none.</summary>
    [Fact]
    public async Task A_copy_whose_lists_are_absent_reads_as_one_with_none()
    {
        var copy = Copy(Guid.NewGuid(), "A meetup", Noon) with { KeyDates = null!, Notes = null!, Tasks = null! };

        Assert.Equal(KnowledgeEventReplicaOutcome.Taken, await _replication.ApplyEventAsync(copy, Cancellation));
        Assert.Empty(_events.Find(copy.Id)!.Event.Notes);
    }

    [Fact]
    public async Task Series_travel_and_are_taken_on_the_same_terms()
    {
        var series = _series.Seed(EventSeries.Create(Guid.NewGuid(), "Techorama", "Techorama BV", "https://techorama.nl").Value, Noon);

        Assert.Equal(
            new EventSeriesReplicaDto(series.Id, "Techorama", "Techorama BV", "https://techorama.nl", Noon),
            Assert.Single(await _replication.ListSeriesChangedSinceAsync(DateTimeOffset.MinValue, Cancellation)));

        Assert.Equal(
            KnowledgeEventReplicaOutcome.NotLater,
            await _replication.ApplySeriesAsync(new EventSeriesReplicaDto(series.Id, "Older", null, null, Noon), Cancellation));
        Assert.Equal(
            KnowledgeEventReplicaOutcome.Unreadable,
            await _replication.ApplySeriesAsync(new EventSeriesReplicaDto(series.Id, " ", null, null, Noon.AddDays(1)), Cancellation));
        Assert.Equal(
            KnowledgeEventReplicaOutcome.Taken,
            await _replication.ApplySeriesAsync(new EventSeriesReplicaDto(series.Id, "Techorama NL", null, null, Noon.AddDays(1)), Cancellation));

        var held = _series.Find(series.Id)!;
        Assert.Equal("Techorama NL", held.Series.Name);
        Assert.Equal(Noon.AddDays(1), held.UpdatedAt);
    }

    /// <summary>A head that composed the module without the replica stores has nothing to send
    /// and keeps nothing it is sent.</summary>
    [Fact]
    public async Task A_head_without_the_replica_stores_sends_nothing_and_takes_nothing()
    {
        using var provider = new ServiceCollection().AddEventsModule().BuildServiceProvider();
        var replication = provider.GetRequiredService<IKnowledgeEventReplication>();

        Assert.Empty(await replication.ListEventsChangedSinceAsync(DateTimeOffset.MinValue, Cancellation));
        Assert.Empty(await replication.ListSeriesChangedSinceAsync(DateTimeOffset.MinValue, Cancellation));
        Assert.Equal(KnowledgeEventReplicaOutcome.Unreadable, await replication.ApplyEventAsync(Copy(Guid.NewGuid(), "Lost", Noon), Cancellation));
    }

    /// <summary>Another desktop's port over its own store, holding the full Techorama event.</summary>
    private static IKnowledgeEventReplication Sender()
    {
        var store = new InMemoryKnowledgeEventReplicaStore();
        store.Seed(Techorama(Guid.NewGuid()), Noon.AddHours(-2));

        var services = new ServiceCollection();
        services.AddSingleton<IKnowledgeEventReplicaStore>(store);

        return services.AddEventsModule().BuildServiceProvider().GetRequiredService<IKnowledgeEventReplication>();
    }

    public void Dispose() => _provider.Dispose();
}
