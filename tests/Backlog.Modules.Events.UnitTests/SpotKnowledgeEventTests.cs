using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.Modules.Events.Services;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// <see cref="IKnowledgeEvents.SpotAsync"/>, the published way in for the Inbox's To Event route
/// (<c>.devbook/domain/events/requirements.md#to-event-route</c>,
/// <c>.devbook/domain/inbox/requirements.md#route-to-event</c>): a Spotted event from a title, a
/// link and what the capture found, in the series of its watched page or a new one.
/// </summary>
public sealed class SpotKnowledgeEventTests
{
    private readonly InMemoryEvents _events = new();
    private readonly InMemorySeries _series = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private KnowledgeEvents Events() => new(_events, _series);

    [Fact]
    public async Task A_routed_item_becomes_a_Spotted_event_with_its_title_and_link()
    {
        var spotted = await Events().SpotAsync(new SpotKnowledgeEventDto(
            "Techorama Netherlands 2026", "https://techorama.nl/2026", null, null, null, null, null), Ct);

        var made = Assert.Single(_events.Saved);
        Assert.Equal(spotted.Value.EventId, made.Id);
        Assert.Equal("Techorama Netherlands 2026", made.Title);
        Assert.Equal("https://techorama.nl/2026", made.Link);
        Assert.Equal(KnowledgeEventState.Spotted, made.State);
    }

    [Fact]
    public async Task Dates_read_from_the_page_travel_with_the_date_source_Event_page()
    {
        var offset = TimeSpan.FromHours(1);

        await Events().SpotAsync(new SpotKnowledgeEventDto(
            "dotNET Friday", null,
            new EventDateDto(new DateOnly(2026, 11, 27), new TimeOnly(16, 30), offset),
            null, "Nieuwegein", null, null), Ct);

        var made = Assert.Single(_events.Saved);
        Assert.Equal(new DateOnly(2026, 11, 27), made.Start!.Date);
        Assert.Equal(new TimeOnly(16, 30), made.Start.Time);
        Assert.Equal(offset, made.Start.UtcOffset);
        Assert.Null(made.End);
        Assert.Equal(DateSource.EventPage, made.DateSource);
        Assert.Equal("Nieuwegein", made.Place!.Venue);
    }

    [Fact]
    public async Task A_multi_day_edition_keeps_its_end()
    {
        await Events().SpotAsync(new SpotKnowledgeEventDto(
            "Techorama", null,
            new EventDateDto(new DateOnly(2026, 10, 26)), new EventDateDto(new DateOnly(2026, 10, 28)),
            null, null, null), Ct);

        var made = Assert.Single(_events.Saved);
        Assert.Equal(new DateOnly(2026, 10, 28), made.End!.Date);
        Assert.Null(made.Start!.Time);
    }

    [Fact]
    public async Task An_item_without_dates_still_arrives_with_none()
    {
        var spotted = await Events().SpotAsync(new SpotKnowledgeEventDto("Some meetup", null, null, null, null, null, null), Ct);

        Assert.True(spotted.IsSuccess);
        var made = Assert.Single(_events.Saved);
        Assert.Null(made.Start);
        Assert.Null(made.End);
        Assert.Null(made.DateSource);
        Assert.Null(made.Place);
    }

    [Fact]
    public async Task An_end_before_the_start_is_dropped_and_the_start_kept()
    {
        await Events().SpotAsync(new SpotKnowledgeEventDto(
            "Techorama", null,
            new EventDateDto(new DateOnly(2026, 10, 28)), new EventDateDto(new DateOnly(2026, 10, 26)),
            null, null, null), Ct);

        var made = Assert.Single(_events.Saved);
        Assert.Equal(new DateOnly(2026, 10, 28), made.Start!.Date);
        Assert.Null(made.End);
    }

    [Fact]
    public async Task A_start_with_a_time_and_no_offset_keeps_its_day_alone()
    {
        await Events().SpotAsync(new SpotKnowledgeEventDto(
            "Techorama", null, new EventDateDto(new DateOnly(2026, 10, 26), new TimeOnly(9, 0)), null, null, null, null), Ct);

        var made = Assert.Single(_events.Saved);
        Assert.Equal(new DateOnly(2026, 10, 26), made.Start!.Date);
        Assert.Null(made.Start.Time);
    }

    [Theory]
    [InlineData("Online")]
    [InlineData(" online ")]
    public async Task A_place_written_online_is_an_online_event(string place)
    {
        await Events().SpotAsync(new SpotKnowledgeEventDto("Webinar", null, null, null, place, null, null), Ct);

        Assert.True(Assert.Single(_events.Saved).Place!.IsOnline);
    }

    [Fact]
    public async Task The_event_joins_the_series_whose_page_is_the_watched_page()
    {
        var techorama = EventSeries.Create(Guid.NewGuid(), "Techorama Netherlands", null, "https://techorama.nl/").Value;
        await _series.SaveAsync(techorama, Ct);

        var spotted = await Events().SpotAsync(new SpotKnowledgeEventDto(
            "Techorama Netherlands 2026", null, null, null, null, "https://TECHORAMA.nl", "Techorama NL — home"), Ct);

        Assert.Equal(techorama.Id, Assert.Single(_events.Saved).Series);
        Assert.Equal(techorama.Id, spotted.Value.SeriesId);
        Assert.False(spotted.Value.SeriesStarted);
        Assert.Single(await _series.ListAsync(Ct));
    }

    [Fact]
    public async Task An_unknown_page_starts_a_series_named_after_it_and_linked_to_it()
    {
        var spotted = await Events().SpotAsync(new SpotKnowledgeEventDto(
            "dotNET Friday Q4", null, null, null, null, "https://dotnetfriday.nl", "dotNET Friday"), Ct);

        var series = Assert.Single(await _series.ListAsync(Ct));
        Assert.Equal("dotNET Friday", series.Name);
        Assert.Equal("https://dotnetfriday.nl", series.Link);
        Assert.Equal(series.Id, Assert.Single(_events.Saved).Series);
        Assert.True(spotted.Value.SeriesStarted);
    }

    [Fact]
    public async Task A_page_with_no_title_names_its_series_after_the_host()
    {
        await Events().SpotAsync(new SpotKnowledgeEventDto(
            "Edition", null, null, null, null, "https://www.dotnetfriday.nl/events", "  "), Ct);

        Assert.Equal("dotnetfriday.nl", Assert.Single(await _series.ListAsync(Ct)).Name);
    }

    [Fact]
    public async Task An_item_from_no_watched_page_starts_no_series()
    {
        await Events().SpotAsync(new SpotKnowledgeEventDto("Shared link", "https://example.org/talk", null, null, null, null, null), Ct);

        Assert.Empty(await _series.ListAsync(Ct));
        Assert.Null(Assert.Single(_events.Saved).Series);
    }

    [Fact]
    public async Task A_blank_title_is_refused_and_nothing_is_written()
    {
        var spotted = await Events().SpotAsync(new SpotKnowledgeEventDto(" ", null, null, null, null, "https://dotnetfriday.nl", "dotNET Friday"), Ct);

        Assert.Equal("knowledge_event.title_missing", spotted.Error.Code);
        Assert.Empty(_events.Saved);
        Assert.Empty(await _series.ListAsync(Ct));
    }

    private sealed class InMemoryEvents : IKnowledgeEventRepository
    {
        public List<KnowledgeEvent> Saved { get; } = [];

        public Task<KnowledgeEvent?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Saved.FirstOrDefault(e => e.Id == id));

        public Task<IReadOnlyList<KnowledgeEvent>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeEvent>>(Saved);

        public Task SaveAsync(KnowledgeEvent knowledgeEvent, CancellationToken cancellationToken = default)
        {
            Saved.RemoveAll(e => e.Id == knowledgeEvent.Id);
            Saved.Add(knowledgeEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySeries : IEventSeriesRepository
    {
        private readonly List<EventSeries> _saved = [];

        public Task<EventSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_saved.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<EventSeries>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EventSeries>>([.. _saved]);

        public Task SaveAsync(EventSeries series, CancellationToken cancellationToken = default)
        {
            _saved.RemoveAll(s => s.Id == series.Id);
            _saved.Add(series);
            return Task.CompletedTask;
        }
    }
}
