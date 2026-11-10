using Backlog.Modules.Events.Abstractions;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>Deciding, editing and the series, through the pane's port.</summary>
public sealed class EventsPaneDecideTests : EventsPaneFixture
{
    // --- AC-2, AC-3 Forward moves -------------------------------------------------------

    [Fact]
    public async Task A_spotted_event_marked_going_is_going()
    {
        var id = await Add("Techorama Belgium");

        var moved = await Pane.MoveToAsync(id, KnowledgeEventState.Going, Token);

        Assert.Equal(KnowledgeEventState.Going, moved.Value.State);
        Assert.Equal(KnowledgeEventState.Going, (await Pane.GetAsync(id, Token)).Value.State);
    }

    [Fact]
    public async Task A_going_event_offers_no_way_back_and_refuses_one()
    {
        var id = await Add("Techorama Belgium");
        var going = (await Pane.MoveToAsync(id, KnowledgeEventState.Going, Token)).Value;

        var back = await Pane.MoveToAsync(id, KnowledgeEventState.Considering, Token);

        Assert.Equal([KnowledgeEventState.Attended, KnowledgeEventState.Harvested], going.Moves.Forward);
        Assert.Equal("knowledge_event.state_backward", back.Error.Code);
        Assert.Equal(KnowledgeEventState.Going, (await Pane.GetAsync(id, Token)).Value.State);
    }

    [Fact]
    public async Task A_harvested_event_moves_nowhere()
    {
        var id = await Add("Techorama Belgium");
        await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        var moved = await Pane.MoveToAsync(id, KnowledgeEventState.Harvested, Token);

        Assert.Equal("knowledge_event.state_final", moved.Error.Code);
    }

    // --- AC-4, AC-5 Skipping ------------------------------------------------------------

    [Fact]
    public async Task A_considering_event_skipped_with_a_reason_shows_the_reason()
    {
        var id = await Add("Update Conference");
        await Pane.MoveToAsync(id, KnowledgeEventState.Considering, Token);

        var skipped = await Pane.SkipAsync(id, "Clashes with the release", Token);

        Assert.Equal(KnowledgeEventState.Skipped, skipped.Value.State);
        Assert.Equal("Clashes with the release", skipped.Value.SkipReason);
    }

    [Fact]
    public async Task A_blank_skip_reason_is_refused()
    {
        var id = await Add("Update Conference");

        var skipped = await Pane.SkipAsync(id, " ", Token);

        Assert.Equal("knowledge_event.skip_reason_missing", skipped.Error.Code);
        Assert.Equal(KnowledgeEventState.Spotted, (await Pane.GetAsync(id, Token)).Value.State);
    }

    [Fact]
    public async Task A_going_event_cannot_be_skipped()
    {
        var id = await Add("Techorama Belgium");
        var going = (await Pane.MoveToAsync(id, KnowledgeEventState.Going, Token)).Value;

        var skipped = await Pane.SkipAsync(id, "Too busy", Token);

        Assert.False(going.Moves.CanSkip);
        Assert.Equal("knowledge_event.cannot_skip", skipped.Error.Code);
    }

    // --- AC-13 The editor ---------------------------------------------------------------

    [Fact]
    public async Task Dates_the_person_types_are_saved_as_typed()
    {
        var id = await Add("dotNET Friday");
        var start = new EventDayDto(new DateOnly(2026, 11, 27), new TimeOnly(16, 30), Cet);

        var dated = await Pane.SetDatesAsync(id, start, null, Token);

        Assert.Equal(start, dated.Value.Start);
        Assert.Equal(DateSource.Typed, dated.Value.DateSource);
    }

    [Fact]
    public async Task Clearing_the_dates_clears_their_source()
    {
        var id = await Dated("dotNET Friday", new DateOnly(2026, 11, 27));

        var cleared = await Pane.SetDatesAsync(id, null, null, Token);

        Assert.Null(cleared.Value.Start);
        Assert.Null(cleared.Value.DateSource);
    }

    [Fact]
    public async Task An_end_before_the_start_is_refused()
    {
        var id = await Add("Techorama Belgium");

        var dated = await Pane.SetDatesAsync(id, new EventDayDto(new DateOnly(2026, 10, 28)), new EventDayDto(new DateOnly(2026, 10, 26)), Token);

        Assert.Equal("knowledge_event.dates_out_of_order", dated.Error.Code);
        Assert.Null((await Pane.GetAsync(id, Token)).Value.Start);
    }

    [Fact]
    public async Task A_time_with_no_offset_is_refused()
    {
        var id = await Add("dotNET Friday");

        var dated = await Pane.SetDatesAsync(id, new EventDayDto(new DateOnly(2026, 11, 27), new TimeOnly(16, 30)), null, Token);

        Assert.Equal("event_date.offset_missing", dated.Error.Code);
    }

    [Fact]
    public async Task The_details_round_trip()
    {
        var id = await Add("Techorama Belgium");

        await Pane.SetPlaceAsync(id, new PlaceDto(" Kinepolis Antwerp ", false), Token);
        await Pane.SetLinkAsync(id, "https://techorama.be", Token);
        await Pane.SetWhatToLearnAsync(id, "Aspire in production", Token);
        await Pane.SetKeyDatesAsync(id, [new KeyDateDto(new DateOnly(2026, 3, 1), "Early bird ends")], Token);
        await Pane.SetTicketAsync(id, TicketState.Bought, Token);
        var read = (await Pane.SetCostAsync(id, new CostDto(899m, " eur "), Token)).Value;

        Assert.Equal(new PlaceDto("Kinepolis Antwerp", false), read.Place);
        Assert.Equal("https://techorama.be", read.Link);
        Assert.Equal("Aspire in production", read.WhatToLearn);
        Assert.Equal([new KeyDateDto(new DateOnly(2026, 3, 1), "Early bird ends")], read.KeyDates);
        Assert.Equal(TicketState.Bought, read.Ticket);
        Assert.Equal(new CostDto(899m, "EUR"), read.Cost);
    }

    [Fact]
    public async Task An_online_event_reads_as_online()
    {
        var id = await Add("dotNET Friday");

        var read = await Pane.SetPlaceAsync(id, new PlaceDto(null, true), Token);

        Assert.Equal(new PlaceDto(null, true), read.Value.Place);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("Kinepolis", true)]
    public async Task A_place_that_is_neither_or_both_is_refused(string? venue, bool online)
    {
        var id = await Add("Techorama Belgium");

        var read = await Pane.SetPlaceAsync(id, new PlaceDto(venue, online), Token);

        Assert.Equal("place.invalid", read.Error.Code);
    }

    [Fact]
    public async Task A_key_date_without_a_label_is_refused_and_keeps_the_key_dates()
    {
        var id = await Add("Techorama Belgium");
        await Pane.SetKeyDatesAsync(id, [new KeyDateDto(new DateOnly(2026, 3, 1), "Early bird ends")], Token);

        var read = await Pane.SetKeyDatesAsync(id, [new KeyDateDto(new DateOnly(2026, 4, 1), " ")], Token);

        Assert.Equal("key_date.label_missing", read.Error.Code);
        Assert.Equal("Early bird ends", Assert.Single((await Pane.GetAsync(id, Token)).Value.KeyDates).Label);
    }

    [Fact]
    public async Task A_negative_cost_is_refused()
    {
        var id = await Add("Techorama Belgium");

        var read = await Pane.SetCostAsync(id, new CostDto(-1m, "EUR"), Token);

        Assert.Equal("cost.invalid", read.Error.Code);
    }

    // --- AC-14 The verdict --------------------------------------------------------------

    [Fact]
    public async Task A_verdict_waits_until_the_event_is_attended()
    {
        var id = await Add("Techorama Belgium");
        var going = (await Pane.MoveToAsync(id, KnowledgeEventState.Going, Token)).Value;

        var early = await Pane.SetVerdictAsync(id, "Worth it", Token);
        await Pane.MoveToAsync(id, KnowledgeEventState.Attended, Token);
        var attended = await Pane.SetVerdictAsync(id, "Worth it", Token);

        Assert.False(going.VerdictOffered);
        Assert.Equal("knowledge_event.verdict_too_early", early.Error.Code);
        Assert.True(attended.Value.VerdictOffered);
        Assert.Equal("Worth it", attended.Value.Verdict);
    }

    // --- AC-12 Series -------------------------------------------------------------------

    [Fact]
    public async Task A_new_series_needs_a_name()
    {
        var started = await Pane.StartSeriesAsync(" ", cancellationToken: Token);

        Assert.Equal("event_series.name_missing", started.Error.Code);
        Assert.Empty(await Pane.ListSeriesAsync(Token));
    }

    [Fact]
    public async Task An_event_added_with_its_series_lists_the_series_other_editions()
    {
        var series = (await Pane.StartSeriesAsync("Techorama", "Techorama vzw", "https://techorama.be", Token)).Value;
        var last = (await Pane.AddAsync("Techorama 2025", series.Id, Token)).Value;
        await Pane.AddAsync("Unrelated", cancellationToken: Token);

        var added = await Pane.AddAsync("Techorama 2026", series.Id, Token);

        Assert.Equal(series, added.Value.Series);
        var edition = Assert.Single(added.Value.OtherEditions);
        Assert.Equal(last.Id, edition.Id);
        Assert.Equal("Techorama", Assert.Single(Assert.Single(await Pane.ListAsync(Token)).Events, e => e.Id == added.Value.Id).SeriesName);
    }

    [Fact]
    public async Task An_unknown_series_is_refused()
    {
        var id = await Add("Techorama 2026");

        var set = await Pane.SetSeriesAsync(id, Guid.NewGuid(), Token);
        var added = await Pane.AddAsync("Techorama 2027", Guid.NewGuid(), Token);

        Assert.Equal("event_series.not_found", set.Error.Code);
        Assert.Equal("event_series.not_found", added.Error.Code);
    }

    [Fact]
    public async Task Series_list_in_name_order()
    {
        await Pane.StartSeriesAsync("Techorama", cancellationToken: Token);
        await Pane.StartSeriesAsync("dotNET Friday", cancellationToken: Token);

        Assert.Equal(["dotNET Friday", "Techorama"], (await Pane.ListSeriesAsync(Token)).Select(s => s.Name));
    }
}
