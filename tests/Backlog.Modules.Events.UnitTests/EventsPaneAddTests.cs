using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// <see cref="IEventsPane.AddAsync(NewKnowledgeEventDto, CancellationToken)"/>: the pane's Add
/// with every field at once, the act <c>add_knowledge_event</c> repeats over MCP. The event is
/// built whole on the aggregate and saved once, so a rule that refuses one field writes nothing
/// at all, not a half-filled Spotted event and not the series its name would have started.
/// </summary>
public sealed class EventsPaneAddTests : EventsPaneFixture
{
    private int _announced;

    public EventsPaneAddTests() => Announced.Changed += () => _announced++;

    private static readonly EventDateDto Start = new(new DateOnly(2027, 5, 18), new TimeOnly(9, 0), TimeSpan.FromHours(2));

    private static readonly EventDateDto End = new(new DateOnly(2027, 5, 19));

    [Fact]
    public async Task A_title_alone_adds_one_spotted_event_with_no_dates_in_one_announced_save()
    {
        var added = await Pane.AddAsync(new NewKnowledgeEventDto("dotNET Friday, 27 November"), Token);

        Assert.IsType<Ok<KnowledgeEventDto>>(added.Value);
        var read = Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.GetAsync(Assert.IsType<Ok<KnowledgeEventDto>>(added.Value).Value.Id, Token)).Value).Value;
        Assert.Equal(("dotNET Friday, 27 November", KnowledgeEventState.Spotted), (read.Title, read.State));
        Assert.Null(read.Start);
        Assert.Null(read.End);
        Assert.Null(read.DateSource);
        Assert.Null(read.Series);
        Assert.Equal(1, Store.Saves);
        Assert.Equal(1, _announced);
    }

    [Fact]
    public async Task Every_field_is_set_in_one_save_with_typed_dates_and_reads_back()
    {
        var added = await Pane.AddAsync(
            new NewKnowledgeEventDto(
                "Techorama Belgium 2027",
                SeriesName: "Techorama",
                Start: Start,
                End: End,
                Place: new PlaceDto("Kinepolis Antwerp", IsOnline: false),
                Link: "https://techorama.be",
                WhatToLearn: "Aspire in production",
                Ticket: TicketState.NotBought,
                Cost: new CostDto(749m, "eur")),
            Token);

        Assert.IsType<Ok<KnowledgeEventDto>>(added.Value);
        var read = Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.GetAsync(Assert.IsType<Ok<KnowledgeEventDto>>(added.Value).Value.Id, Token)).Value).Value;
        Assert.Equal(Start, read.Start);
        Assert.Equal(End, read.End);
        Assert.Equal(DateSource.Typed, read.DateSource);
        Assert.Equal(new PlaceDto("Kinepolis Antwerp", false), read.Place);
        Assert.Equal("https://techorama.be", read.Link);
        Assert.Equal("Aspire in production", read.WhatToLearn);
        Assert.Equal(TicketState.NotBought, read.Ticket);
        Assert.Equal(new CostDto(749m, "EUR"), read.Cost);
        Assert.Equal("Techorama", read.Series?.Name);
        Assert.Equal(KnowledgeEventState.Spotted, read.State);
        Assert.Equal(1, Store.Saves);
        Assert.Equal(1, _announced);
    }

    [Fact]
    public async Task An_end_before_the_start_is_refused_and_nothing_is_written()
    {
        var refused = await Pane.AddAsync(
            new NewKnowledgeEventDto("NDC Oslo", SeriesName: "NDC", Start: new EventDateDto(new DateOnly(2027, 6, 9)), End: new EventDateDto(new DateOnly(2027, 6, 7))),
            Token);

        Assert.Equal("knowledge_event.dates_out_of_order", Assert.IsType<Error>(refused.Value).Code);
        await AssertNothingWritten();
    }

    [Fact]
    public async Task A_time_with_no_offset_is_refused_and_nothing_is_written()
    {
        var refused = await Pane.AddAsync(
            new NewKnowledgeEventDto("dotNET Friday", Start: new EventDateDto(new DateOnly(2026, 11, 27), new TimeOnly(16, 30))),
            Token);

        Assert.Equal("event_date.offset_missing", Assert.IsType<Error>(refused.Value).Code);
        await AssertNothingWritten();
    }

    [Fact]
    public async Task A_venue_and_online_together_are_refused_and_nothing_is_written()
    {
        var refused = await Pane.AddAsync(new NewKnowledgeEventDto("dotNET Friday", Place: new PlaceDto("Nieuwegein", IsOnline: true)), Token);

        Assert.Equal("place.invalid", Assert.IsType<Error>(refused.Value).Code);
        await AssertNothingWritten();
    }

    [Theory]
    [InlineData(-1, "EUR")]
    [InlineData(10, " ")]
    public async Task A_negative_cost_or_one_with_no_currency_is_refused_and_nothing_is_written(int amount, string currency)
    {
        var refused = await Pane.AddAsync(new NewKnowledgeEventDto("Techorama", Cost: new CostDto(amount, currency)), Token);

        Assert.Equal("cost.invalid", Assert.IsType<Error>(refused.Value).Code);
        await AssertNothingWritten();
    }

    [Fact]
    public async Task A_blank_title_is_refused_and_nothing_is_written()
    {
        var refused = await Pane.AddAsync(new NewKnowledgeEventDto("  ", SeriesName: "Techorama"), Token);

        Assert.Equal("knowledge_event.title_missing", Assert.IsType<Error>(refused.Value).Code);
        await AssertNothingWritten();
    }

    [Fact]
    public async Task A_series_named_in_another_casing_is_joined_and_none_is_started()
    {
        var techorama = Assert.IsType<Ok<EventSeriesDto>>((await Pane.StartSeriesAsync("Techorama", cancellationToken: Token)).Value).Value;

        var added = await Pane.AddAsync(new NewKnowledgeEventDto("Techorama Belgium 2027", SeriesName: "  TECHORAMA "), Token);

        Assert.Equal(techorama.Id, Assert.IsType<Ok<KnowledgeEventDto>>(added.Value).Value.Series?.Id);
        Assert.Single(await Pane.ListSeriesAsync(Token));
    }

    [Fact]
    public async Task A_series_name_that_matches_none_starts_one_with_that_name_and_joins_it()
    {
        var added = await Pane.AddAsync(new NewKnowledgeEventDto("dotNET Friday, 27 November", SeriesName: " dotNET Friday "), Token);

        var started = Assert.Single(await Pane.ListSeriesAsync(Token));
        Assert.Equal("dotNET Friday", started.Name);
        Assert.Equal(started.Id, Assert.IsType<Ok<KnowledgeEventDto>>(added.Value).Value.Series?.Id);
        Assert.Equal(1, _announced);
    }

    [Fact]
    public async Task A_name_two_series_share_is_refused_and_nothing_is_written()
    {
        // Two series may share a name: Start series refuses only a blank one.
        await Series.SaveAsync(Assert.IsType<Ok<EventSeries>>(EventSeries.Create(Guid.NewGuid(), "Techorama", "Techorama BE", null).Value).Value, Token);
        await Series.SaveAsync(Assert.IsType<Ok<EventSeries>>(EventSeries.Create(Guid.NewGuid(), "techorama", "Techorama NL", null).Value).Value, Token);

        var refused = await Pane.AddAsync(new NewKnowledgeEventDto("Techorama 2027", SeriesName: "Techorama"), Token);

        Assert.Equal("event_series.ambiguous", Assert.IsType<Error>(refused.Value).Code);
        Assert.Empty(await Pane.ListAsync(Token));
        Assert.Equal(2, (await Pane.ListSeriesAsync(Token)).Count);
        Assert.Equal(0, _announced);
    }

    /// <summary>The series is saved before the event. A store that fails the event's save leaves
    /// the series it started, which the stores cannot take back, so it is announced like any
    /// other save and reaches the other devices rather than sitting here unsent.</summary>
    [Fact]
    public async Task A_failed_event_save_still_announces_the_series_it_started()
    {
        Store.FailNextSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Pane.AddAsync(new NewKnowledgeEventDto("Techorama Belgium 2027", SeriesName: "Techorama"), Token));

        Assert.Equal("Techorama", Assert.Single(await Pane.ListSeriesAsync(Token)).Name);
        Assert.Empty(await Pane.ListAsync(Token));
        Assert.Equal(1, _announced);
    }

    /// <summary>The series announce after a failed event save is best effort: a subscriber that
    /// throws on it does not replace the store's failure, which is the one the caller sees.</summary>
    [Fact]
    public async Task A_subscriber_that_throws_on_the_series_announce_does_not_hide_the_stores_failure()
    {
        Announced.Changed += () => throw new NotSupportedException("A subscriber failed.");
        Store.FailNextSave = true;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Pane.AddAsync(new NewKnowledgeEventDto("Techorama Belgium 2027", SeriesName: "Techorama"), Token));

        Assert.Equal("The events store could not be written.", thrown.Message);
        Assert.Single(await Pane.ListSeriesAsync(Token));
    }

    private async Task AssertNothingWritten()
    {
        Assert.Empty(await Pane.ListAsync(Token));
        Assert.Empty(await Pane.ListSeriesAsync(Token));
        Assert.Equal(0, Store.Saves);
        Assert.Equal(0, _announced);
    }
}
