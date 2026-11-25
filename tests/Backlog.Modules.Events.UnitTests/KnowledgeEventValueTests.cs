using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// One test or more per value-object rule of
/// <c>.devbook/domain/events/domain.invariants.md#knowledge-event</c> — each enforced where the
/// value is made — and the value equality <c>domain.md</c> gives each of them.
/// </summary>
public sealed class KnowledgeEventValueTests
{
    private static readonly DateOnly MeetupDay = new(2026, 11, 20);
    private static readonly TimeSpan CentralEuropean = TimeSpan.FromHours(1);

    // --- A time carries its UTC offset --------------------------------------------------

    [Fact]
    public void A_time_without_its_utc_offset_is_refused()
    {
        var result = EventDate.Create(MeetupDay, new TimeOnly(16, 30), utcOffset: null);

        Assert.Equal("event_date.offset_missing", Assert.IsType<Error>(result.Value).Code);
    }

    [Fact]
    public void A_date_alone_is_an_event_date()
    {
        var date = Assert.IsType<Ok<EventDate>>(EventDate.Create(MeetupDay, time: null, utcOffset: null).Value).Value;

        Assert.Equal(MeetupDay, date.Date);
        Assert.Null(date.Time);
        Assert.Null(date.UtcOffset);
    }

    [Fact]
    public void A_date_with_a_time_and_its_offset_is_an_event_date()
    {
        var date = Assert.IsType<Ok<EventDate>>(EventDate.Create(MeetupDay, new TimeOnly(16, 30), CentralEuropean).Value).Value;

        Assert.Equal(new TimeOnly(16, 30), date.Time);
        Assert.Equal(TimeSpan.FromHours(1), date.UtcOffset);
    }

    [Fact]
    public void An_offset_without_a_time_is_kept()
    {
        var date = Assert.IsType<Ok<EventDate>>(EventDate.Create(MeetupDay, time: null, CentralEuropean).Value).Value;

        Assert.Null(date.Time);
        Assert.Equal(TimeSpan.FromHours(1), date.UtcOffset);
    }

    [Fact]
    public void Two_event_dates_with_the_same_parts_are_equal()
    {
        Assert.Equal(
            Assert.IsType<Ok<EventDate>>(EventDate.Create(MeetupDay, new TimeOnly(16, 30), CentralEuropean).Value).Value,
            Assert.IsType<Ok<EventDate>>(EventDate.Create(MeetupDay, new TimeOnly(16, 30), CentralEuropean).Value).Value);
        Assert.NotEqual(
            Assert.IsType<Ok<EventDate>>(EventDate.Create(MeetupDay, new TimeOnly(16, 30), CentralEuropean).Value).Value,
            Assert.IsType<Ok<EventDate>>(EventDate.Create(MeetupDay, new TimeOnly(17, 0), CentralEuropean).Value).Value);
    }

    // --- A place is a venue or online ---------------------------------------------------

    [Fact]
    public void A_place_that_is_both_a_venue_and_online_is_refused()
    {
        Assert.Equal("place.invalid", Assert.IsType<Error>(Place.Create("Kinepolis Antwerp", online: true).Value).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_place_that_is_neither_a_venue_nor_online_is_refused(string? venue)
    {
        Assert.Equal("place.invalid", Assert.IsType<Error>(Place.Create(venue, online: false).Value).Code);
    }

    [Fact]
    public void A_blank_venue_is_refused()
    {
        Assert.Equal("place.invalid", Assert.IsType<Error>(Place.AtVenue(" ").Value).Code);
    }

    [Fact]
    public void A_venue_is_a_place_with_its_name_trimmed()
    {
        var place = Assert.IsType<Ok<Place>>(Place.AtVenue("  Kinepolis Antwerp ").Value).Value;

        Assert.Equal("Kinepolis Antwerp", place.Venue);
        Assert.False(place.IsOnline);
        Assert.Equal(place, Assert.IsType<Ok<Place>>(Place.Create("Kinepolis Antwerp", online: false).Value).Value);
    }

    [Fact]
    public void Online_is_a_place_with_no_venue()
    {
        Assert.True(Place.Online.IsOnline);
        Assert.Null(Place.Online.Venue);
        Assert.Equal(Place.Online, Assert.IsType<Ok<Place>>(Place.Create(venue: null, online: true).Value).Value);
    }

    // --- An address has a part ---------------------------------------------------------

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("", " ", "\t", "  ")]
    public void An_address_with_no_part_is_refused(string? street, string? postcode, string? town, string? country)
    {
        Assert.Equal("address.empty", Assert.IsType<Error>(Address.Create(street, postcode, town, country).Value).Code);
    }

    [Fact]
    public void An_address_keeps_its_parts_trimmed_and_a_blank_part_as_none()
    {
        var kinepolis = Assert.IsType<Ok<Address>>(Address.Create(" Groenendaallaan 394 ", "2030 ", " Antwerpen", " ").Value).Value;

        Assert.Equal("Groenendaallaan 394", kinepolis.Street);
        Assert.Equal("2030", kinepolis.Postcode);
        Assert.Equal("Antwerpen", kinepolis.Town);
        Assert.Null(kinepolis.Country);
    }

    [Fact]
    public void A_town_alone_is_an_address()
    {
        var utrecht = Assert.IsType<Ok<Address>>(Address.Create(null, null, "Utrecht", null).Value).Value;

        Assert.Equal("Utrecht", utrecht.Town);
        Assert.Null(utrecht.Street);
    }

    [Fact]
    public void Two_addresses_are_equal_part_by_part()
    {
        Assert.Equal(AddressIn("Utrecht"), AddressIn(" Utrecht "));
        Assert.NotEqual(AddressIn("Utrecht"), AddressIn("Nieuwegein"));
    }

    // --- A map point lies on the globe --------------------------------------------------

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(52.0302, 5.0880)]
    public void A_point_on_the_globe_is_a_map_point(double latitude, double longitude)
    {
        var point = Assert.IsType<Ok<MapPoint>>(MapPoint.Create(latitude, longitude).Value).Value;

        Assert.Equal(latitude, point.Latitude);
        Assert.Equal(longitude, point.Longitude);
    }

    [Theory]
    [InlineData(90.0001, 0)]
    [InlineData(-90.0001, 0)]
    [InlineData(0, 180.0001)]
    [InlineData(0, -180.0001)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    public void A_point_off_the_globe_is_refused(double latitude, double longitude)
    {
        Assert.Equal("map_point.out_of_range", Assert.IsType<Error>(MapPoint.Create(latitude, longitude).Value).Code);
    }

    [Fact]
    public void Two_map_points_with_the_same_degrees_are_equal()
    {
        Assert.Equal(PointAt(52.0302, 5.0880), PointAt(52.0302, 5.0880));
        Assert.NotEqual(PointAt(52.0302, 5.0880), PointAt(51.2194, 4.4025));
    }

    // --- A place: in person with a venue name, an address or both; online with neither ---

    [Fact]
    public void An_address_alone_is_an_in_person_place()
    {
        var place = Assert.IsType<Ok<Place>>(Place.Create(venue: null, online: false, AddressIn("Utrecht"), AddressLookup.NotLookedUpYet, point: null).Value).Value;

        Assert.Null(place.Venue);
        Assert.False(place.IsOnline);
        Assert.Equal(AddressIn("Utrecht"), place.Address);
        Assert.Equal(AddressLookup.NotLookedUpYet, place.Lookup);
        Assert.Null(place.Point);
    }

    [Fact]
    public void A_placed_address_carries_its_point()
    {
        var place = Assert.IsType<Ok<Place>>(Place.Create(" 4Dotnet ", online: false, AddressIn("Nieuwegein"), AddressLookup.Placed, PointAt(52.0302, 5.0880)).Value).Value;

        Assert.Equal("4Dotnet", place.Venue);
        Assert.Equal(AddressLookup.Placed, place.Lookup);
        Assert.Equal(PointAt(52.0302, 5.0880), place.Point);
    }

    [Fact]
    public void A_venue_name_alone_has_no_address_no_lookup_and_no_point()
    {
        var place = Assert.IsType<Ok<Place>>(Place.AtVenue("Kinepolis Antwerp").Value).Value;

        Assert.Null(place.Address);
        Assert.Null(place.Lookup);
        Assert.Null(place.Point);
    }

    [Fact]
    public void A_place_with_neither_a_venue_name_nor_an_address_is_refused()
    {
        Assert.Equal("place.invalid", Assert.IsType<Error>(Place.Create(" ", online: false, address: null, lookup: null, point: null).Value).Code);
    }

    [Fact]
    public void An_online_place_with_an_address_is_refused()
    {
        Assert.Equal("place.invalid", Assert.IsType<Error>(Place.Create(venue: null, online: true, AddressIn("Utrecht"), AddressLookup.NotLookedUpYet, point: null).Value).Code);
    }

    [Fact]
    public void An_online_place_with_a_lookup_or_a_point_is_refused()
    {
        Assert.Equal("place.invalid", Assert.IsType<Error>(Place.Create(venue: null, online: true, address: null, AddressLookup.NotFound, point: null).Value).Code);
        Assert.Equal("place.invalid", Assert.IsType<Error>(Place.Create(venue: null, online: true, address: null, lookup: null, PointAt(52.0302, 5.0880)).Value).Code);
    }

    [Fact]
    public void An_address_without_its_lookup_is_refused()
    {
        Assert.Equal("place.lookup_mismatch", Assert.IsType<Error>(Place.Create("4Dotnet", online: false, AddressIn("Nieuwegein"), lookup: null, point: null).Value).Code);
    }

    [Fact]
    public void A_lookup_without_an_address_is_refused()
    {
        Assert.Equal("place.lookup_mismatch", Assert.IsType<Error>(Place.Create("4Dotnet", online: false, address: null, AddressLookup.NotLookedUpYet, point: null).Value).Code);
    }

    [Fact]
    public void A_placed_address_without_a_point_is_refused()
    {
        Assert.Equal("place.lookup_mismatch", Assert.IsType<Error>(Place.Create("4Dotnet", online: false, AddressIn("Nieuwegein"), AddressLookup.Placed, point: null).Value).Code);
    }

    [Theory]
    [InlineData(AddressLookup.NotFound)]
    [InlineData(AddressLookup.NotLookedUpYet)]
    public void A_point_on_an_address_that_is_not_placed_is_refused(AddressLookup lookup)
    {
        Assert.Equal("place.lookup_mismatch", Assert.IsType<Error>(Place.Create("4Dotnet", online: false, AddressIn("Nieuwegein"), lookup, PointAt(52.0302, 5.0880)).Value).Code);
    }

    [Fact]
    public void A_point_without_an_address_is_refused()
    {
        Assert.Equal("place.lookup_mismatch", Assert.IsType<Error>(Place.Create("4Dotnet", online: false, address: null, lookup: null, PointAt(52.0302, 5.0880)).Value).Code);
    }

    [Fact]
    public void Two_places_are_equal_by_venue_address_lookup_and_point()
    {
        Assert.Equal(
            Assert.IsType<Ok<Place>>(Place.Create("4Dotnet", online: false, AddressIn("Nieuwegein"), AddressLookup.Placed, PointAt(52.0302, 5.0880)).Value).Value,
            Assert.IsType<Ok<Place>>(Place.Create("4Dotnet", online: false, AddressIn("Nieuwegein"), AddressLookup.Placed, PointAt(52.0302, 5.0880)).Value).Value);
        Assert.NotEqual(
            Assert.IsType<Ok<Place>>(Place.Create("4Dotnet", online: false, AddressIn("Nieuwegein"), AddressLookup.NotFound, point: null).Value).Value,
            Assert.IsType<Ok<Place>>(Place.Create("4Dotnet", online: false, AddressIn("Nieuwegein"), AddressLookup.NotLookedUpYet, point: null).Value).Value);
    }

    private static Address AddressIn(string town) =>
        Assert.IsType<Ok<Address>>(Address.Create(street: null, postcode: null, town, country: null).Value).Value;

    private static MapPoint PointAt(double latitude, double longitude) =>
        Assert.IsType<Ok<MapPoint>>(MapPoint.Create(latitude, longitude).Value).Value;

    // --- A key date has a label ---------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_key_date_without_a_label_is_refused(string label)
    {
        Assert.Equal("key_date.label_missing", Assert.IsType<Error>(KeyDate.Create(new DateOnly(2026, 9, 1), label).Value).Code);
    }

    [Fact]
    public void A_key_date_keeps_its_date_and_its_label_trimmed()
    {
        var earlyBird = Assert.IsType<Ok<KeyDate>>(KeyDate.Create(new DateOnly(2026, 9, 1), " Early bird ends ").Value).Value;

        Assert.Equal(new DateOnly(2026, 9, 1), earlyBird.Date);
        Assert.Equal("Early bird ends", earlyBird.Label);
        Assert.Equal(earlyBird, Assert.IsType<Ok<KeyDate>>(KeyDate.Create(new DateOnly(2026, 9, 1), "Early bird ends").Value).Value);
    }

    // --- A cost is not negative and names its currency ----------------------------------

    [Fact]
    public void A_negative_cost_is_refused()
    {
        Assert.Equal("cost.invalid", Assert.IsType<Error>(Cost.Create(-0.01m, "EUR").Value).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_cost_without_a_currency_is_refused(string currency)
    {
        Assert.Equal("cost.invalid", Assert.IsType<Error>(Cost.Create(150m, currency).Value).Code);
    }

    [Fact]
    public void A_free_event_costs_zero()
    {
        Assert.Equal(0m, Assert.IsType<Ok<Cost>>(Cost.Create(0m, "EUR").Value).Value.Amount);
    }

    [Fact]
    public void A_cost_writes_its_currency_trimmed_and_upper_cased()
    {
        var ticket = Assert.IsType<Ok<Cost>>(Cost.Create(895m, " eur ").Value).Value;

        Assert.Equal("EUR", ticket.Currency);
        Assert.Equal(ticket, Assert.IsType<Ok<Cost>>(Cost.Create(895m, "EUR").Value).Value);
    }

    // --- Note and task links: equal when they name the same note or task ----------------

    [Fact]
    public void Two_note_links_naming_the_same_note_are_equal_whatever_their_titles()
    {
        Assert.Equal(
            NoteLink.Create(NoteLinkKind.Brain, "events/techorama-2026.md", "Techorama 2026"),
            NoteLink.Create(NoteLinkKind.Brain, "events/techorama-2026.md", "Techorama, renamed"));
    }

    [Fact]
    public void A_brain_note_and_a_talk_note_with_the_same_reference_are_different_notes()
    {
        Assert.NotEqual(
            NoteLink.Create(NoteLinkKind.Brain, "42", "Keynote"),
            NoteLink.Create(NoteLinkKind.Talk, "42", "Keynote"));
    }

    [Fact]
    public void A_note_link_without_a_reference_is_a_caller_bug()
    {
        Assert.Throws<ArgumentException>(() => NoteLink.Create(NoteLinkKind.Talk, " ", "Keynote"));
    }

    [Fact]
    public void Two_task_links_naming_the_same_task_are_equal_whatever_their_purposes()
    {
        Assert.Equal(
            EventTaskLink.Create("task-1", TaskPurpose.Prep),
            EventTaskLink.Create("task-1", TaskPurpose.Linked));
        Assert.NotEqual(
            EventTaskLink.Create("task-1", TaskPurpose.Prep),
            EventTaskLink.Create("task-2", TaskPurpose.Prep));
    }

    [Fact]
    public void A_task_link_without_a_task_id_is_a_caller_bug()
    {
        Assert.Throws<ArgumentException>(() => EventTaskLink.Create("", TaskPurpose.Prep));
    }
}
