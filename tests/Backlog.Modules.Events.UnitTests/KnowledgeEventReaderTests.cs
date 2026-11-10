using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.DomainModels;
using Backlog.Modules.Events.Services;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The events read contract (local ADR 0050 §7, <c>.devbook/domain/context-map.md</c>): the
/// decided events — Considering, Going, Attended and Harvested — with their days, their place
/// and their key dates, as another context may read them. Spotted and Skipped stay off it, so
/// nothing reaches the Calendar unasked
/// (<c>.devbook/domain/events/requirements.md#events-on-the-calendar</c>, "Spotted stays off").
/// <para>
/// Over an in-memory repository: the port is the module's own boundary to storage.
/// </para>
/// </summary>
public sealed class KnowledgeEventReaderTests
{
    [Fact]
    public async Task Only_decided_events_are_read_in_the_order_they_were_saved()
    {
        var reader = ReaderOver(
            Event("Harvested talk", KnowledgeEventState.Harvested),
            Event("Spotted meetup", KnowledgeEventState.Spotted),
            Event("Going conference", KnowledgeEventState.Going),
            Event("Skipped webinar", KnowledgeEventState.Skipped),
            Event("Considering workshop", KnowledgeEventState.Considering),
            Event("Attended meetup", KnowledgeEventState.Attended));

        var read = await reader.ListDecidedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Harvested talk", "Going conference", "Considering workshop", "Attended meetup"],
            read.Select(knowledgeEvent => knowledgeEvent.Title));
        Assert.Equal(
            [KnowledgeEventState.Harvested, KnowledgeEventState.Going, KnowledgeEventState.Considering, KnowledgeEventState.Attended],
            read.Select(knowledgeEvent => knowledgeEvent.State));
    }

    [Fact]
    public async Task A_decided_event_carries_its_days_its_venue_and_its_key_dates_in_order()
    {
        var id = Guid.NewGuid();
        var reader = ReaderOver(Event(
            "Techorama",
            KnowledgeEventState.Going,
            id: id,
            start: new DateOnly(2026, 5, 12),
            end: new DateOnly(2026, 5, 14),
            place: Place.AtVenue("Antwerp").Value,
            keyDates:
            [
                KeyDate.Create(new DateOnly(2026, 3, 3), "Early bird ends").Value,
                KeyDate.Create(new DateOnly(2026, 2, 1), "Call for speakers closes").Value
            ]));

        var techorama = Assert.Single(await reader.ListDecidedAsync(TestContext.Current.CancellationToken));

        Assert.Equal(id, techorama.Id);
        Assert.Equal(new DateOnly(2026, 5, 12), techorama.Start);
        Assert.Equal(new DateOnly(2026, 5, 14), techorama.End);
        Assert.Equal("Antwerp", techorama.Venue);
        Assert.False(techorama.Online);
        Assert.Equal(
            [
                new KeyDateDto(new DateOnly(2026, 3, 3), "Early bird ends"),
                new KeyDateDto(new DateOnly(2026, 2, 1), "Call for speakers closes")
            ],
            techorama.KeyDates);
    }

    [Fact]
    public async Task An_event_with_a_start_and_no_end_reads_with_no_end()
    {
        var reader = ReaderOver(Event("dotNET Friday", KnowledgeEventState.Going, start: new DateOnly(2026, 11, 20)));

        var friday = Assert.Single(await reader.ListDecidedAsync(TestContext.Current.CancellationToken));

        Assert.Equal(new DateOnly(2026, 11, 20), friday.Start);
        Assert.Null(friday.End);
    }

    [Fact]
    public async Task An_online_event_with_no_dates_reads_online_with_no_venue_and_no_days()
    {
        var reader = ReaderOver(Event("Build", KnowledgeEventState.Considering, place: Place.Online));

        var build = Assert.Single(await reader.ListDecidedAsync(TestContext.Current.CancellationToken));

        Assert.True(build.Online);
        Assert.Null(build.Venue);
        Assert.Null(build.Start);
        Assert.Null(build.End);
        Assert.Empty(build.KeyDates);
    }

    [Fact]
    public async Task An_event_with_no_place_reads_with_no_venue_and_not_online()
    {
        var reader = ReaderOver(Event("Local meetup", KnowledgeEventState.Attended));

        var meetup = Assert.Single(await reader.ListDecidedAsync(TestContext.Current.CancellationToken));

        Assert.Null(meetup.Venue);
        Assert.False(meetup.Online);
    }

    private static KnowledgeEventReader ReaderOver(params KnowledgeEvent[] events) => new(new InMemoryEvents(events));

    private static KnowledgeEvent Event(
        string title,
        KnowledgeEventState state,
        Guid? id = null,
        DateOnly? start = null,
        DateOnly? end = null,
        Place? place = null,
        IEnumerable<KeyDate>? keyDates = null) =>
        KnowledgeEvent.Restore(
            id ?? Guid.NewGuid(),
            title,
            series: null,
            start: start is { } first ? EventDate.Create(first, time: null, utcOffset: null).Value : null,
            end: end is { } last ? EventDate.Create(last, time: null, utcOffset: null).Value : null,
            dateSource: start is null ? null : DateSource.Typed,
            place: place,
            link: null,
            state: state,
            skipReason: state == KnowledgeEventState.Skipped ? "Too far" : null,
            whatToLearn: null,
            keyDates: keyDates ?? [],
            ticket: null,
            cost: null,
            notes: [],
            tasks: [],
            verdict: null);

    private sealed class InMemoryEvents(IReadOnlyList<KnowledgeEvent> events) : IKnowledgeEventRepository
    {
        public Task<KnowledgeEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(events.FirstOrDefault(knowledgeEvent => knowledgeEvent.Id == id));

        public Task<IReadOnlyList<KnowledgeEvent>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(events);

        public Task SaveAsync(KnowledgeEvent knowledgeEvent, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The reader writes nothing.");
    }
}
