using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.UI;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// How the Events list words when an event runs — one day reads once, whatever the end carries
/// on that same date, and only a different day draws a range — and how a row, a pin and the
/// venue hint say where the event is on the map.
/// </summary>
public sealed class EventPresentationTests
{
    private static readonly TimeSpan Cet = TimeSpan.FromHours(1);
    private static readonly DateOnly Friday = new(2026, 11, 27);

    [Fact]
    public void A_same_day_end_without_a_time_reads_as_the_start_day()
    {
        var start = new EventDateDto(Friday, new TimeOnly(16, 30), Cet);

        Assert.Equal("27 Nov 2026 16:30", EventPresentation.When(start, new EventDateDto(Friday)));
    }

    [Fact]
    public void A_same_day_end_at_the_start_time_reads_as_the_start_day()
    {
        var start = new EventDateDto(Friday, new TimeOnly(16, 30), Cet);

        Assert.Equal("27 Nov 2026 16:30", EventPresentation.When(start, new EventDateDto(Friday, new TimeOnly(16, 30), Cet)));
        Assert.Equal("27 Nov 2026 16:30", EventPresentation.When(start, new EventDateDto(Friday, new TimeOnly(16, 30))));
        Assert.Equal("27 Nov 2026 16:30", EventPresentation.When(start, new EventDateDto(Friday, new TimeOnly(16, 30), TimeSpan.Zero)));
    }

    [Fact]
    public void A_same_day_end_at_a_later_time_reads_the_day_once_with_both_times()
    {
        var start = new EventDateDto(Friday, new TimeOnly(16, 30), Cet);
        var end = new EventDateDto(Friday, new TimeOnly(21, 0), Cet);

        Assert.Equal("27 Nov 2026 16:30 – 21:00", EventPresentation.When(start, end));
    }

    [Fact]
    public void A_same_day_end_with_a_time_after_a_start_without_one_reads_the_day_once()
    {
        var end = new EventDateDto(Friday, new TimeOnly(21, 0), Cet);

        Assert.Equal("27 Nov 2026 – 21:00", EventPresentation.When(new EventDateDto(Friday), end));
    }

    [Fact]
    public void A_same_day_end_without_times_reads_the_day_once()
    {
        Assert.Equal("27 Nov 2026", EventPresentation.When(new EventDateDto(Friday), new EventDateDto(Friday)));
    }

    [Fact]
    public void An_end_on_another_day_reads_as_a_range()
    {
        var start = new EventDateDto(Friday, new TimeOnly(16, 30), Cet);
        var end = new EventDateDto(new DateOnly(2026, 11, 28));

        Assert.Equal("27 Nov 2026 16:30 – 28 Nov 2026", EventPresentation.When(start, end));
    }

    [Fact]
    public void No_end_reads_as_the_start_day_and_no_start_as_not_known()
    {
        Assert.Equal("27 Nov 2026", EventPresentation.When(new EventDateDto(Friday), null));
        Assert.Equal("Date not known", EventPresentation.When(null, new EventDateDto(Friday)));
    }

    private static readonly AddressDto Antwerp = new("Groenendaallaan 394", "2030", "Antwerpen", "Belgium");

    private static KnowledgeEventSummaryDto Summary(
        KnowledgeEventState state = KnowledgeEventState.Spotted, PlaceDto? place = null, int? number = null) =>
        new(Guid.NewGuid(), "Techorama Belgium", state, new EventDateDto(new DateOnly(2027, 5, 18)), null, "Techorama", place, number);

    private static PlaceDto Placed(string? venue = "Kinepolis") =>
        new(venue, false, Antwerp, AddressLookup.Placed, new MapPointDto(51.23, 4.42));

    [Fact]
    public void A_row_date_is_short_and_carries_its_year_only_outside_this_one()
    {
        var today = new DateOnly(2026, 11, 10);

        Assert.Equal("27 Nov", EventPresentation.ShortDate(new EventDateDto(Friday), today));
        Assert.Equal("18 May 2027", EventPresentation.ShortDate(new EventDateDto(new DateOnly(2027, 5, 18)), today));
        Assert.Equal("No date", EventPresentation.ShortDate(null, today));
    }

    [Fact]
    public void The_place_line_says_why_an_event_is_not_on_the_map_in_four_words()
    {
        Assert.Equal("Online · not on the map", EventPresentation.PlaceLine(new PlaceDto(null, true)));
        Assert.Equal("No venue yet · not on the map", EventPresentation.PlaceLine(null));
        Assert.Equal("No venue yet · not on the map", EventPresentation.PlaceLine(new PlaceDto("Jaarbeurs", false)));
        Assert.Equal("Address not found · not on the map", EventPresentation.PlaceLine(new PlaceDto(null, false, Antwerp, AddressLookup.NotFound)));
        Assert.Equal("Address not looked up yet · not on the map", EventPresentation.PlaceLine(new PlaceDto(null, false, Antwerp, AddressLookup.NotLookedUpYet)));
    }

    [Fact]
    public void An_event_on_the_map_reads_its_venue_else_its_address_on_one_line()
    {
        Assert.Equal("Kinepolis", EventPresentation.PlaceLine(Placed()));
        Assert.Equal("Groenendaallaan 394, 2030 Antwerpen, Belgium", EventPresentation.PlaceLine(Placed(venue: null)));
        Assert.Equal("Antwerpen", EventPresentation.AddressLine(new AddressDto(null, null, "Antwerpen", null)));
    }

    [Fact]
    public void A_mark_carries_the_number_in_the_tone_of_the_state_and_section()
    {
        Assert.Equal(new EventMark(EventMarkTone.Filled, "1"), EventPresentation.Mark(Summary(KnowledgeEventState.Going, Placed(), 1), EventListSection.Upcoming));
        Assert.Equal(new EventMark(EventMarkTone.Dashed, "2"), EventPresentation.Mark(Summary(KnowledgeEventState.Considering, Placed(), 2), EventListSection.Upcoming));
        Assert.Equal(new EventMark(EventMarkTone.Grey, "3"), EventPresentation.Mark(Summary(KnowledgeEventState.Attended, Placed(), 3), EventListSection.Upcoming));
        Assert.Equal(new EventMark(EventMarkTone.Grey, null), EventPresentation.Mark(Summary(KnowledgeEventState.Going, Placed()), EventListSection.Past));
        Assert.Equal(new EventMark(EventMarkTone.Grey, null), EventPresentation.Mark(Summary(KnowledgeEventState.Skipped, Placed()), EventListSection.Upcoming));
        Assert.Equal(new EventMark(EventMarkTone.Online, null), EventPresentation.Mark(Summary(place: new PlaceDto(null, true)), EventListSection.Upcoming));
        Assert.Equal(new EventMark(EventMarkTone.Off, null), EventPresentation.Mark(Summary(), EventListSection.Upcoming));
    }

    [Fact]
    public void A_pin_is_named_by_its_number_title_state_date_and_venue_and_its_tooltip_reads_title_and_date()
    {
        var summary = Summary(KnowledgeEventState.Spotted, Placed(), 2);

        Assert.Equal("2, Techorama Belgium, Spotted, 18 May 2027, Kinepolis", EventPresentation.PinName(summary));
        Assert.Equal("Techorama Belgium · 18 May 2027", EventPresentation.PinTooltip(summary));
    }

    [Fact]
    public void The_venue_hint_says_the_number_or_why_the_event_is_not_on_the_map()
    {
        Assert.Equal("On the map as 2.", EventPresentation.VenueHint(Placed(), 2, inView: true));
        Assert.Equal("On the map as 2.", EventPresentation.VenueHint(Placed(), 2, inView: null));
        Assert.Equal("Outside the map's view.", EventPresentation.VenueHint(Placed(), 3, inView: false));
        Assert.Equal("Online: not on the map.", EventPresentation.VenueHint(new PlaceDto(null, true), null, null));
        Assert.Equal("Add an address to put it on the map.", EventPresentation.VenueHint(new PlaceDto("Jaarbeurs", false), null, null));
        Assert.Equal("Add an address to put it on the map.", EventPresentation.VenueHint(null, null, null));
        Assert.Equal("Address not found: check it.", EventPresentation.VenueHint(new PlaceDto(null, false, Antwerp, AddressLookup.NotFound), null, null));
        Assert.Equal(
            "Not looked up yet: it will be looked up when you save again, or Look up now.",
            EventPresentation.VenueHint(new PlaceDto(null, false, Antwerp, AddressLookup.NotLookedUpYet), null, null));
        Assert.Null(EventPresentation.VenueHint(Placed(), null, null));
        Assert.Equal("On the map, in grey.", EventPresentation.VenueHint(Placed(), null, null, past: true));
        Assert.Equal("Skipped: not on the map.", EventPresentation.VenueHint(Placed(), null, null, skipped: true));
        Assert.Equal("Skipped: not on the map.", EventPresentation.VenueHint(Placed(), null, null, skipped: true, past: true));
        Assert.Equal("Add an address to put it on the map.", EventPresentation.VenueHint(new PlaceDto("Jaarbeurs", false), null, null, skipped: true));
        Assert.Equal(EventPresentation.LookingUpHint, EventPresentation.VenueHint(new PlaceDto(null, false, Antwerp, AddressLookup.NotLookedUpYet), null, null, lookingUp: true));
        Assert.Equal("Online: not on the map.", EventPresentation.VenueHint(new PlaceDto(null, true), null, null, lookingUp: true));
    }
}
