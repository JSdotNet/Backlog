using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Events.DomainModels;
using Backlog.Modules.Events.Extensions;
using Backlog.SharedKernel.Results;

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
        var knowledgeEvent = Assert.IsType<Ok<KnowledgeEvent>>(KnowledgeEvent.Create(Guid.NewGuid(), "Techorama 2026").Value).Value;

        knowledgeEvent.SetSeries(series);
        Assert.IsType<Ok>(knowledgeEvent.SetDates(
            Assert.IsType<Ok<EventDate>>(EventDate.Create(new DateOnly(2026, 10, 26), new TimeOnly(8, 30), Amsterdam).Value).Value,
            Assert.IsType<Ok<EventDate>>(EventDate.Create(new DateOnly(2026, 10, 28), null, null).Value).Value,
            DateSource.EventPage).Value);
        knowledgeEvent.SetPlace(Assert.IsType<Ok<Place>>(Place.AtVenue("Kinepolis Utrecht").Value).Value);
        knowledgeEvent.SetLink("https://techorama.nl/2026");
        knowledgeEvent.SetWhatToLearn("Aspire in production");
        knowledgeEvent.SetKeyDates([Assert.IsType<Ok<KeyDate>>(KeyDate.Create(new DateOnly(2026, 6, 30), "Early bird ends").Value).Value]);
        knowledgeEvent.SetTicket(TicketState.Bought);
        knowledgeEvent.SetCost(Assert.IsType<Ok<Cost>>(Cost.Create(1295.50m, "EUR").Value).Value);
        Assert.IsType<Ok>(knowledgeEvent.LinkNote(NoteLink.Create(NoteLinkKind.Brain, "knowledge/aspire.md", "Aspire")).Value);
        Assert.IsType<Ok>(knowledgeEvent.LinkNote(NoteLink.Create(NoteLinkKind.Talk, "6f1c2d3e-0000-0000-0000-000000000042", "Keynote")).Value);
        Assert.IsType<Ok>(knowledgeEvent.LinkTask(EventTaskLink.Create("task-hotel", TaskPurpose.Prep)).Value);
        Assert.IsType<Ok>(knowledgeEvent.MoveTo(KnowledgeEventState.Going).Value);

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
        var id = _events.Seed(Assert.IsType<Ok<KnowledgeEvent>>(KnowledgeEvent.Create(Guid.NewGuid(), "dotNET Friday").Value).Value, Noon).Id;

        Assert.Equal(KnowledgeEventReplicaOutcome.NotLater, await _replication.ApplyEventAsync(Copy(id, "Older", Noon.AddMinutes(-1)), Cancellation));
        Assert.Equal(KnowledgeEventReplicaOutcome.NotLater, await _replication.ApplyEventAsync(Copy(id, "Same stamp", Noon), Cancellation));
        Assert.Equal("dotNET Friday", _events.Find(id)!.Event.Title);

        Assert.Equal(KnowledgeEventReplicaOutcome.Taken, await _replication.ApplyEventAsync(Copy(id, "dotNET Friday Q4", Noon.AddMinutes(1)), Cancellation));
        Assert.Equal("dotNET Friday Q4", _events.Find(id)!.Event.Title);
    }

    /// <summary>A removal is a write like any other: the tombstone is listed marked removed, and
    /// a removed copy another desktop sends is stored removed.</summary>
    [Fact]
    public async Task A_removed_event_travels_marked_removed()
    {
        var removed = Assert.IsType<Ok<KnowledgeEvent>>(KnowledgeEvent.Create(Guid.NewGuid(), "Techorama 2026").Value).Value;
        Assert.IsType<Ok>(removed.Remove().Value);
        _events.Seed(removed, Noon);
        var held = _events.Seed(Assert.IsType<Ok<KnowledgeEvent>>(KnowledgeEvent.Create(Guid.NewGuid(), "dotNET Friday").Value).Value, Noon).Id;

        var listed = await _replication.ListEventsChangedSinceAsync(DateTimeOffset.MinValue, Cancellation);
        var taken = await _replication.ApplyEventAsync(Copy(held, "dotNET Friday", Noon.AddMinutes(1)) with { Removed = true }, Cancellation);

        Assert.True(Assert.Single(listed, copy => copy.Id == removed.Id).Removed);
        Assert.False(Assert.Single(listed, copy => copy.Id == held).Removed);
        Assert.Equal(KnowledgeEventReplicaOutcome.Taken, taken);
        Assert.True(_events.Find(held)!.Event.IsRemoved);
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
        "an online copy with an address",
        "an address with no part",
        "a lookup outside the set",
        "a lookup with no address",
        "a point off the globe",
        "a point on an address not placed",
        "a placed address with no point",
        "a point with no lookup",
        "a point with no longitude",
        "a point with no latitude",
        "a point with no degrees",
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
            "an online copy with an address" => good with { Online = true, Address = Utrecht, Lookup = AddressLookup.NotLookedUpYet },
            "an address with no part" => good with { Address = new EventAddressReplicaDto(" ", null, null, null), Lookup = AddressLookup.NotLookedUpYet },
            "a lookup outside the set" => good with { Address = Utrecht, Lookup = (AddressLookup)9 },
            "a lookup with no address" => good with { Venue = "Kinepolis", Lookup = AddressLookup.NotFound },
            "a point off the globe" => good with { Address = Utrecht, Lookup = AddressLookup.Placed, Point = new EventMapPointReplicaDto(52.09, 181) },
            "a point on an address not placed" => good with { Address = Utrecht, Lookup = AddressLookup.NotFound, Point = new EventMapPointReplicaDto(52.09, 5.12) },
            "a placed address with no point" => good with { Address = Utrecht, Lookup = AddressLookup.Placed },
            "a point with no lookup" => good with { Address = Utrecht, Point = new EventMapPointReplicaDto(52.09, 5.12) },
            "a point with no longitude" => good with { Address = Utrecht, Lookup = AddressLookup.Placed, Point = new EventMapPointReplicaDto(52.09, null) },
            "a point with no latitude" => good with { Address = Utrecht, Lookup = AddressLookup.Placed, Point = new EventMapPointReplicaDto(null, 5.12) },
            "a point with no degrees" => good with { Address = Utrecht, Lookup = AddressLookup.Placed, Point = new EventMapPointReplicaDto(null, null) },
            _ => throw new ArgumentOutOfRangeException(nameof(fault), fault, null),
        };

        Assert.Equal(KnowledgeEventReplicaOutcome.Unreadable, await _replication.ApplyEventAsync(copy, Cancellation));
        Assert.Empty(await _replication.ListEventsChangedSinceAsync(DateTimeOffset.MinValue, Cancellation));
    }

    private static readonly EventAddressReplicaDto Utrecht = new("Jaarbeursplein 6", "3521 AL", "Utrecht", "Netherlands");

    /// <summary>An in-person event at the Jaarbeurs whose address the geocoder placed.</summary>
    private static KnowledgeEvent PlacedAtJaarbeurs()
    {
        var knowledgeEvent = Assert.IsType<Ok<KnowledgeEvent>>(KnowledgeEvent.Create(Guid.NewGuid(), "Techorama 2026").Value).Value;
        var address = Assert.IsType<Ok<Address>>(Address.Create("Jaarbeursplein 6", "3521 AL", "Utrecht", "Netherlands").Value).Value;
        knowledgeEvent.SetPlace(Assert.IsType<Ok<Place>>(Place.Create("Jaarbeurs", online: false, address, AddressLookup.NotLookedUpYet, point: null).Value).Value);
        Assert.IsType<Ok>(knowledgeEvent.RecordLookup(
            address, AddressLookup.Placed, Assert.IsType<Ok<MapPoint>>(MapPoint.Create(52.08842, 5.11553).Value).Value).Value);

        return knowledgeEvent;
    }

    [Fact]
    public async Task A_placed_address_travels_with_its_lookup_and_point()
    {
        _events.Seed(PlacedAtJaarbeurs(), Noon);

        var copy = Assert.Single(await _replication.ListEventsChangedSinceAsync(DateTimeOffset.MinValue, Cancellation));

        Assert.Equal("Jaarbeurs", copy.Venue);
        Assert.False(copy.Online);
        Assert.Equal(Utrecht, copy.Address);
        Assert.Equal(AddressLookup.Placed, copy.Lookup);
        Assert.Equal(new EventMapPointReplicaDto(52.08842, 5.11553), copy.Point);
    }

    [Fact]
    public async Task A_copy_with_a_placed_address_is_taken_with_its_lookup_and_point()
    {
        var sent = PlacedAtJaarbeurs();
        var copy = Copy(sent.Id, "Techorama 2026", Noon) with
        {
            Venue = "Jaarbeurs",
            Address = Utrecht,
            Lookup = AddressLookup.Placed,
            Point = new EventMapPointReplicaDto(52.08842, 5.11553),
        };

        Assert.Equal(KnowledgeEventReplicaOutcome.Taken, await _replication.ApplyEventAsync(copy, Cancellation));

        Assert.Equal(sent.Place, _events.Find(copy.Id)!.Event.Place);
    }

    [Theory]
    [InlineData(AddressLookup.NotFound)]
    [InlineData(AddressLookup.NotLookedUpYet)]
    public async Task A_copy_with_an_address_and_no_venue_name_is_taken_without_a_point(AddressLookup lookup)
    {
        var copy = Copy(Guid.NewGuid(), "A meetup", Noon) with { Address = Utrecht, Lookup = lookup };

        Assert.Equal(KnowledgeEventReplicaOutcome.Taken, await _replication.ApplyEventAsync(copy, Cancellation));

        var place = _events.Find(copy.Id)!.Event.Place!;
        Assert.Null(place.Venue);
        Assert.Equal("Utrecht", place.Address!.Town);
        Assert.Equal(lookup, place.Lookup);
        Assert.Null(place.Point);
    }

    /// <summary>A copy that holds an address and leaves its lookup out reads as an address not
    /// looked up yet — what any new address is.</summary>
    [Fact]
    public async Task A_copy_with_an_address_and_no_lookup_reads_as_not_looked_up_yet()
    {
        var copy = Copy(Guid.NewGuid(), "A meetup", Noon) with { Address = Utrecht };

        Assert.Equal(KnowledgeEventReplicaOutcome.Taken, await _replication.ApplyEventAsync(copy, Cancellation));

        Assert.Equal(AddressLookup.NotLookedUpYet, _events.Find(copy.Id)!.Event.Place!.Lookup);
    }

    /// <summary>A copy from an older desktop build, which knows none of the address fields,
    /// holds the venue alone, and reads as that venue.</summary>
    [Fact]
    public async Task A_copy_without_the_address_fields_reads_as_its_venue()
    {
        var copy = Copy(Guid.NewGuid(), "Techorama 2026", Noon) with { Venue = "Kinepolis Utrecht" };

        Assert.Equal(KnowledgeEventReplicaOutcome.Taken, await _replication.ApplyEventAsync(copy, Cancellation));

        Assert.Equal(Assert.IsType<Ok<Place>>(Place.AtVenue("Kinepolis Utrecht").Value).Value, _events.Find(copy.Id)!.Event.Place);
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
        var series = _series.Seed(Assert.IsType<Ok<EventSeries>>(EventSeries.Create(Guid.NewGuid(), "Techorama", "Techorama BV", "https://techorama.nl").Value).Value, Noon);

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
