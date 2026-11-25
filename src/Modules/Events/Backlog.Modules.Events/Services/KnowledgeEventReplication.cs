using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.Services;

/// <summary>
/// The published <see cref="IKnowledgeEventReplication"/> port: each event and each series one
/// whole document, the later stamp winning per id (local ADR 0050 §2, the devbook subject
/// feed's terms of local ADR 0044).
/// <para>
/// It maps between the aggregates and the travelling shape and decides whether a copy reads
/// as an event at all. Which copy wins is the store's, decided in the step that writes
/// (<see cref="IKnowledgeEventReplicaStore.SaveReplicatedAsync"/>), so a change the pane saves
/// while a page is merged is never overwritten by an older copy.
/// </para>
/// <para>
/// A copy is rehydrated as a stored event is — trusted, because the desktop that wrote it held
/// the aggregate's rules — except for what would make a value object its own rules refuse or a
/// link the aggregate cannot hold: those read as <see cref="KnowledgeEventReplicaOutcome.Unreadable"/>.
/// </para>
/// </summary>
internal sealed class KnowledgeEventReplication : IKnowledgeEventReplication
{
    private readonly IKnowledgeEventReplicaStore? _events;
    private readonly IEventSeriesReplicaStore? _series;

    /// <param name="events">Where this host keeps its events, or null on a head that keeps
    /// none — then there is nothing to send, and every copy reads as unreadable.</param>
    /// <param name="series">Where this host keeps its series, or null, on the same terms.</param>
    /// <param name="saved">The notice every local save raises, forwarded as
    /// <see cref="Changed"/>; null where nothing announces one.</param>
    public KnowledgeEventReplication(
        IKnowledgeEventReplicaStore? events = null,
        IEventSeriesReplicaStore? series = null,
        KnowledgeEventSaved? saved = null)
    {
        _events = events;
        _series = series;
        if (saved is not null) saved.Changed += () => Changed?.Invoke();
    }

    public event Action? Changed;

    public async Task<IReadOnlyList<KnowledgeEventReplicaDto>> ListEventsChangedSinceAsync(
        DateTimeOffset watermark,
        CancellationToken cancellationToken = default)
    {
        if (_events is null) return [];

        var changed = await _events.ListChangedSinceAsync(watermark, cancellationToken).ConfigureAwait(false);

        return [.. changed.Select(stored => ToCopy(stored.Event, stored.UpdatedAt))];
    }

    public async Task<IReadOnlyList<EventSeriesReplicaDto>> ListSeriesChangedSinceAsync(
        DateTimeOffset watermark,
        CancellationToken cancellationToken = default)
    {
        if (_series is null) return [];

        var changed = await _series.ListChangedSinceAsync(watermark, cancellationToken).ConfigureAwait(false);

        return [.. changed.Select(stored => new EventSeriesReplicaDto(
            stored.Series.Id, stored.Series.Name, stored.Series.Organiser, stored.Series.Link, stored.UpdatedAt))];
    }

    public async Task<KnowledgeEventReplicaOutcome> ApplyEventAsync(
        KnowledgeEventReplicaDto copy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(copy);

        if (_events is null || ToEvent(copy) is not { } knowledgeEvent) return KnowledgeEventReplicaOutcome.Unreadable;

        return await _events.SaveReplicatedAsync(knowledgeEvent, copy.UpdatedAt, cancellationToken).ConfigureAwait(false)
            ? KnowledgeEventReplicaOutcome.Taken
            : KnowledgeEventReplicaOutcome.NotLater;
    }

    public async Task<KnowledgeEventReplicaOutcome> ApplySeriesAsync(
        EventSeriesReplicaDto copy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(copy);

        if (_series is null || copy.Id == Guid.Empty || string.IsNullOrWhiteSpace(copy.Name))
            return KnowledgeEventReplicaOutcome.Unreadable;

        var series = EventSeries.Restore(copy.Id, copy.Name.Trim(), copy.Organiser, copy.Link);

        return await _series.SaveReplicatedAsync(series, copy.UpdatedAt, cancellationToken).ConfigureAwait(false)
            ? KnowledgeEventReplicaOutcome.Taken
            : KnowledgeEventReplicaOutcome.NotLater;
    }

    private static KnowledgeEventReplicaDto ToCopy(KnowledgeEvent e, DateTimeOffset updatedAt) => new(
        e.Id,
        e.Title,
        e.State,
        DateCopy(e.Start),
        DateCopy(e.End),
        e.DateSource,
        e.Place?.Venue,
        e.Place?.IsOnline ?? false,
        e.Link,
        [.. e.KeyDates.Select(keyDate => new EventKeyDateReplicaDto(keyDate.Date, keyDate.Label))],
        e.WhatToLearn,
        e.Series,
        [.. e.Notes.Select(note => new EventNoteLinkReplicaDto(note.Kind, note.Reference, note.Title))],
        e.SkipReason?.Text,
        e.Ticket,
        e.Cost is { } cost ? new EventCostReplicaDto(cost.Amount, cost.Currency) : null,
        [.. e.Tasks.Select(task => new EventTaskLinkReplicaDto(task.TaskId, task.Purpose))],
        e.Verdict?.Text,
        updatedAt,
        e.IsRemoved,
        e.Place?.Address is { } address
            ? new EventAddressReplicaDto(address.Street, address.Postcode, address.Town, address.Country)
            : null,
        e.Place?.Lookup,
        e.Place?.Point is { } point ? new EventMapPointReplicaDto(point.Latitude, point.Longitude) : null);

    private static EventDateReplicaDto? DateCopy(EventDate? date) =>
        date is null ? null : new EventDateReplicaDto(date.Date, date.Time, date.UtcOffset);

    /// <summary>The event a copy holds, or null when it does not read as one.</summary>
    private static KnowledgeEvent? ToEvent(KnowledgeEventReplicaDto copy)
    {
        if (copy.Id == Guid.Empty || string.IsNullOrWhiteSpace(copy.Title)) return null;
        if (!Enum.IsDefined(copy.State)
            || copy.DateSource is { } source && !Enum.IsDefined(source)
            || copy.Ticket is { } ticket && !Enum.IsDefined(ticket))
            return null;

        if (!TryDate(copy.Start, out var start) || !TryDate(copy.End, out var end)) return null;

        if (!TryPlace(copy, out var place)) return null;

        Cost? cost = null;
        if (copy.Cost is { } costCopy)
        {
            if (Cost.Create(costCopy.Amount, costCopy.Currency) is not Ok<Cost>(var priced)) return null;
            cost = priced;
        }

        var keyDates = new List<KeyDate>();
        foreach (var keyDate in copy.KeyDates ?? [])
        {
            if (keyDate is null || KeyDate.Create(keyDate.Date, keyDate.Label) is not Ok<KeyDate>(var made)) return null;
            keyDates.Add(made);
        }

        var notes = new List<NoteLink>();
        foreach (var note in copy.Notes ?? [])
        {
            if (note is null || string.IsNullOrWhiteSpace(note.Reference) || !Enum.IsDefined(note.Kind)) return null;
            notes.Add(NoteLink.Create(note.Kind, note.Reference, note.Title));
        }

        var tasks = new List<EventTaskLink>();
        foreach (var task in copy.Tasks ?? [])
        {
            if (task is null || string.IsNullOrWhiteSpace(task.TaskId) || !Enum.IsDefined(task.Purpose)) return null;
            tasks.Add(EventTaskLink.Create(task.TaskId, task.Purpose));
        }

        return KnowledgeEvent.Restore(
            copy.Id,
            copy.Title.Trim(),
            copy.SeriesId,
            start,
            end,
            copy.DateSource,
            place,
            copy.Link,
            copy.State,
            copy.SkipReason,
            copy.WhatToLearn,
            keyDates,
            copy.Ticket,
            cost,
            notes,
            tasks,
            copy.Verdict,
            removed: copy.Removed);
    }

    /// <summary>The place a copy holds, null when it holds none. An address whose lookup the
    /// copy leaves out reads as not looked up yet; a copy with neither (from an older desktop
    /// build) reads as its venue alone. A point that leaves out a degree does not read.</summary>
    private static bool TryPlace(KnowledgeEventReplicaDto copy, out Place? place)
    {
        place = null;
        if (copy.Venue is null && !copy.Online && copy.Address is null && copy.Lookup is null && copy.Point is null) return true;
        if (copy.Lookup is { } answer && !Enum.IsDefined(answer)) return false;

        Address? address = null;
        if (copy.Address is { } parts)
        {
            if (Address.Create(parts.Street, parts.Postcode, parts.Town, parts.Country) is not Ok<Address>(var made)) return false;
            address = made;
        }

        MapPoint? point = null;
        if (copy.Point is { } spot)
        {
            if (spot.Latitude is not { } latitude || spot.Longitude is not { } longitude
                || MapPoint.Create(latitude, longitude) is not Ok<MapPoint>(var made))
                return false;
            point = made;
        }

        var lookup = address is null ? copy.Lookup : copy.Lookup ?? AddressLookup.NotLookedUpYet;
        if (Place.Create(copy.Venue, copy.Online, address, lookup, point) is not Ok<Place>(var placed)) return false;

        place = placed;
        return true;
    }

    private static bool TryDate(EventDateReplicaDto? copy, out EventDate? date)
    {
        date = null;
        if (copy is null) return true;

        if (EventDate.Create(copy.Date, copy.Time, copy.UtcOffset) is not Ok<EventDate>(var made)) return false;

        date = made;
        return true;
    }
}
