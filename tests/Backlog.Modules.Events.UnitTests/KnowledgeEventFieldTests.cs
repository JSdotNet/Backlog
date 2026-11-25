using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The fields of <c>.devbook/domain/events/domain.md#knowledge-event</c> no rule guards — each
/// set and cleared — and the restore from storage, which rebuilds every field without checking
/// the rules again.
/// </summary>
public sealed class KnowledgeEventFieldTests
{
    [Fact]
    public void The_series_is_set_and_cleared()
    {
        var spotted = Spotted();
        var techorama = Guid.NewGuid();

        spotted.SetSeries(techorama);
        Assert.Equal(techorama, spotted.Series);

        spotted.SetSeries(null);
        Assert.Null(spotted.Series);
    }

    [Fact]
    public void The_place_is_set_and_cleared()
    {
        var spotted = Spotted();

        spotted.SetPlace(Place.Online);
        Assert.Equal(Place.Online, spotted.Place);

        spotted.SetPlace(null);
        Assert.Null(spotted.Place);
    }

    [Fact]
    public void The_link_is_trimmed_and_a_blank_one_clears_it()
    {
        var spotted = Spotted();

        spotted.SetLink("  https://techorama.be  ");
        Assert.Equal("https://techorama.be", spotted.Link);

        spotted.SetLink(" ");
        Assert.Null(spotted.Link);
    }

    [Fact]
    public void What_to_learn_is_trimmed_and_a_blank_one_clears_it()
    {
        var spotted = Spotted();

        spotted.SetWhatToLearn("  Aspire in production ");
        Assert.Equal("Aspire in production", spotted.WhatToLearn);

        spotted.SetWhatToLearn(null);
        Assert.Null(spotted.WhatToLearn);
    }

    [Fact]
    public void The_key_dates_are_replaced_and_cleared()
    {
        var spotted = Spotted();
        var earlyBird = Assert.IsType<Ok<KeyDate>>(KeyDate.Create(new DateOnly(2026, 9, 1), "Early bird ends").Value).Value;
        var registration = Assert.IsType<Ok<KeyDate>>(KeyDate.Create(new DateOnly(2026, 10, 1), "Registration closes").Value).Value;

        spotted.SetKeyDates([earlyBird, registration]);
        Assert.Equal([earlyBird, registration], spotted.KeyDates);

        spotted.SetKeyDates([]);
        Assert.Empty(spotted.KeyDates);
    }

    [Fact]
    public void The_ticket_is_set_and_cleared()
    {
        var spotted = Spotted();

        spotted.SetTicket(TicketState.Bought);
        Assert.Equal(TicketState.Bought, spotted.Ticket);

        spotted.SetTicket(null);
        Assert.Null(spotted.Ticket);
    }

    [Fact]
    public void The_cost_is_set_and_cleared()
    {
        var spotted = Spotted();
        var ticket = Assert.IsType<Ok<Cost>>(Cost.Create(895m, "EUR").Value).Value;

        spotted.SetCost(ticket);
        Assert.Equal(ticket, spotted.Cost);

        spotted.SetCost(null);
        Assert.Null(spotted.Cost);
    }

    [Fact]
    public void Restore_rebuilds_every_field_as_stored()
    {
        var id = Guid.NewGuid();
        var series = Guid.NewGuid();
        var start = Assert.IsType<Ok<EventDate>>(EventDate.Create(new DateOnly(2026, 10, 26), new TimeOnly(9, 0), TimeSpan.FromHours(1)).Value).Value;
        var end = Assert.IsType<Ok<EventDate>>(EventDate.Create(new DateOnly(2026, 10, 28), time: null, utcOffset: null).Value).Value;
        var place = Assert.IsType<Ok<Place>>(Place.AtVenue("Kinepolis Antwerp").Value).Value;
        var earlyBird = Assert.IsType<Ok<KeyDate>>(KeyDate.Create(new DateOnly(2026, 9, 1), "Early bird ends").Value).Value;
        var cost = Assert.IsType<Ok<Cost>>(Cost.Create(895m, "EUR").Value).Value;
        var note = NoteLink.Create(NoteLinkKind.Brain, "events/techorama-2026.md", "Techorama 2026");
        var task = EventTaskLink.Create("task-harvest", TaskPurpose.Harvest);

        var restored = KnowledgeEvent.Restore(
            id,
            "Techorama 2026",
            series,
            start,
            end,
            DateSource.EventPage,
            place,
            "https://techorama.be",
            KnowledgeEventState.Harvested,
            skipReason: null,
            "Aspire in production",
            [earlyBird],
            TicketState.Bought,
            cost,
            [note],
            [task],
            "Worth it");

        Assert.Equal(id, restored.Id);
        Assert.Equal("Techorama 2026", restored.Title);
        Assert.Equal(series, restored.Series);
        Assert.Equal(start, restored.Start);
        Assert.Equal(end, restored.End);
        Assert.Equal(DateSource.EventPage, restored.DateSource);
        Assert.Equal(place, restored.Place);
        Assert.Equal("https://techorama.be", restored.Link);
        Assert.Equal(KnowledgeEventState.Harvested, restored.State);
        Assert.Null(restored.SkipReason);
        Assert.Equal("Aspire in production", restored.WhatToLearn);
        Assert.Equal([earlyBird], restored.KeyDates);
        Assert.Equal(TicketState.Bought, restored.Ticket);
        Assert.Equal(cost, restored.Cost);
        Assert.Equal([note], restored.Notes);
        Assert.Equal([task], restored.Tasks);
        Assert.Equal("Worth it", restored.Verdict?.Text);
    }

    [Fact]
    public void Restore_trusts_what_was_stored_and_checks_no_rule_again()
    {
        var restored = KnowledgeEvent.Restore(
            Guid.NewGuid(),
            "Techorama 2026",
            series: null,
            start: null,
            end: null,
            dateSource: null,
            place: null,
            link: null,
            KnowledgeEventState.Skipped,
            "Clashes with the sprint review",
            whatToLearn: null,
            keyDates: [],
            ticket: null,
            cost: null,
            notes: [],
            tasks: [],
            verdict: "Stored before the rule");

        Assert.Equal(KnowledgeEventState.Skipped, restored.State);
        Assert.Equal("Clashes with the sprint review", restored.SkipReason?.Text);
        Assert.Equal("Stored before the rule", restored.Verdict?.Text);
    }

    // --- A changed address is not looked up yet (SetPlace) -------------------------------

    [Fact]
    public void A_new_address_is_not_looked_up_yet_whatever_lookup_it_came_with()
    {
        var spotted = Spotted();

        spotted.SetPlace(PlaceAt("Nieuwegein", AddressLookup.Placed, Nieuwegein));

        Assert.Equal(AddressIn("Nieuwegein"), spotted.Place!.Address);
        Assert.Equal(AddressLookup.NotLookedUpYet, spotted.Place.Lookup);
        Assert.Null(spotted.Place.Point);
    }

    [Fact]
    public void A_changed_address_drops_the_point_found_for_the_old_one()
    {
        var meetup = PlacedIn("Nieuwegein", Nieuwegein);

        meetup.SetPlace(PlaceAt("Antwerpen", AddressLookup.Placed, Antwerpen));

        Assert.Equal(AddressIn("Antwerpen"), meetup.Place!.Address);
        Assert.Equal(AddressLookup.NotLookedUpYet, meetup.Place.Lookup);
        Assert.Null(meetup.Place.Point);
    }

    [Fact]
    public void An_unchanged_address_keeps_its_lookup_and_point()
    {
        var meetup = PlacedIn("Nieuwegein", Nieuwegein);

        meetup.SetPlace(PlaceAt("Nieuwegein", AddressLookup.NotLookedUpYet, point: null, venue: "4Dotnet office"));

        Assert.Equal("4Dotnet office", meetup.Place!.Venue);
        Assert.Equal(AddressLookup.Placed, meetup.Place.Lookup);
        Assert.Equal(Nieuwegein, meetup.Place.Point);
    }

    [Fact]
    public void An_unchanged_address_keeps_a_not_found_answer()
    {
        var meetup = Spotted();
        meetup.SetPlace(PlaceAt("Nowhere", AddressLookup.NotLookedUpYet, point: null));
        Assert.IsType<Ok>(meetup.RecordLookup(AddressIn("Nowhere"), AddressLookup.NotFound, point: null).Value);

        meetup.SetPlace(PlaceAt("Nowhere", AddressLookup.NotLookedUpYet, point: null, venue: "Renamed"));

        Assert.Equal(AddressLookup.NotFound, meetup.Place!.Lookup);
    }

    [Fact]
    public void Clearing_the_address_drops_its_lookup_and_point()
    {
        var meetup = PlacedIn("Nieuwegein", Nieuwegein);

        meetup.SetPlace(Assert.IsType<Ok<Place>>(Place.AtVenue("4Dotnet").Value).Value);

        Assert.Null(meetup.Place!.Address);
        Assert.Null(meetup.Place.Lookup);
        Assert.Null(meetup.Place.Point);
    }

    [Fact]
    public void Going_online_drops_the_address_and_point()
    {
        var meetup = PlacedIn("Nieuwegein", Nieuwegein);

        meetup.SetPlace(Place.Online);

        Assert.Equal(Place.Online, meetup.Place);
    }

    // --- A lookup is recorded for the address it was asked for (RecordLookup) ------------

    [Fact]
    public void A_placed_answer_stores_the_point()
    {
        var meetup = PlacedIn("Nieuwegein", Nieuwegein);

        Assert.Equal(AddressLookup.Placed, meetup.Place!.Lookup);
        Assert.Equal(Nieuwegein, meetup.Place.Point);
        Assert.Equal("4Dotnet", meetup.Place.Venue);
    }

    [Theory]
    [InlineData(AddressLookup.NotFound)]
    [InlineData(AddressLookup.NotLookedUpYet)]
    public void An_answer_without_a_match_stores_no_point(AddressLookup lookup)
    {
        var meetup = Spotted();
        meetup.SetPlace(PlaceAt("Nieuwegein", AddressLookup.NotLookedUpYet, point: null));

        Assert.IsType<Ok>(meetup.RecordLookup(AddressIn("Nieuwegein"), lookup, point: null).Value);

        Assert.Equal(lookup, meetup.Place!.Lookup);
        Assert.Null(meetup.Place.Point);
    }

    [Fact]
    public void A_stale_answer_for_an_address_since_changed_is_ignored()
    {
        var meetup = Spotted();
        meetup.SetPlace(PlaceAt("Nieuwegein", AddressLookup.NotLookedUpYet, point: null));
        meetup.SetPlace(PlaceAt("Antwerpen", AddressLookup.NotLookedUpYet, point: null));

        Assert.IsType<Ok>(meetup.RecordLookup(AddressIn("Nieuwegein"), AddressLookup.Placed, Nieuwegein).Value);

        Assert.Equal(AddressIn("Antwerpen"), meetup.Place!.Address);
        Assert.Equal(AddressLookup.NotLookedUpYet, meetup.Place.Lookup);
        Assert.Null(meetup.Place.Point);
    }

    [Fact]
    public void An_answer_for_an_event_with_no_address_is_ignored()
    {
        var webinar = Spotted();
        webinar.SetPlace(Place.Online);

        Assert.IsType<Ok>(webinar.RecordLookup(AddressIn("Nieuwegein"), AddressLookup.Placed, Nieuwegein).Value);

        Assert.Equal(Place.Online, webinar.Place);
    }

    // Local ADR 0061 §5: Placed leaves only when the address is cleared or the place goes
    // online, so a late answer that did not place it — a pass that read the address before
    // another save placed it, then failed — leaves the point where it is.
    [Theory]
    [InlineData(AddressLookup.NotFound)]
    [InlineData(AddressLookup.NotLookedUpYet)]
    public void A_late_answer_that_did_not_place_leaves_a_placed_address_on_the_map(AddressLookup lookup)
    {
        var meetup = PlacedIn("Nieuwegein", Nieuwegein);

        Assert.IsType<Ok>(meetup.RecordLookup(AddressIn("Nieuwegein"), lookup, point: null).Value);

        Assert.Equal(AddressLookup.Placed, meetup.Place!.Lookup);
        Assert.Equal(Nieuwegein, meetup.Place.Point);
    }

    // Local ADR 0061 §5: Address not found leaves only by a match; a retry that gets no answer
    // back does not make it Not looked up yet.
    [Fact]
    public void A_retry_that_got_no_answer_leaves_an_address_not_found()
    {
        var meetup = Spotted();
        meetup.SetPlace(PlaceAt("Nowhere", AddressLookup.NotLookedUpYet, point: null));
        Assert.IsType<Ok>(meetup.RecordLookup(AddressIn("Nowhere"), AddressLookup.NotFound, point: null).Value);

        Assert.IsType<Ok>(meetup.RecordLookup(AddressIn("Nowhere"), AddressLookup.NotLookedUpYet, point: null).Value);

        Assert.Equal(AddressLookup.NotFound, meetup.Place!.Lookup);
    }

    [Fact]
    public void A_retry_that_finds_an_address_not_found_places_it()
    {
        var meetup = Spotted();
        meetup.SetPlace(PlaceAt("Nieuwegein", AddressLookup.NotLookedUpYet, point: null));
        Assert.IsType<Ok>(meetup.RecordLookup(AddressIn("Nieuwegein"), AddressLookup.NotFound, point: null).Value);

        Assert.IsType<Ok>(meetup.RecordLookup(AddressIn("Nieuwegein"), AddressLookup.Placed, Nieuwegein).Value);

        Assert.Equal(AddressLookup.Placed, meetup.Place!.Lookup);
        Assert.Equal(Nieuwegein, meetup.Place.Point);
    }

    [Fact]
    public void A_placed_answer_without_a_point_is_refused()
    {
        var meetup = Spotted();
        meetup.SetPlace(PlaceAt("Nieuwegein", AddressLookup.NotLookedUpYet, point: null));

        var result = meetup.RecordLookup(AddressIn("Nieuwegein"), AddressLookup.Placed, point: null);

        Assert.Equal("place.lookup_mismatch", Assert.IsType<Error>(result.Value).Code);
        Assert.Equal(AddressLookup.NotLookedUpYet, meetup.Place!.Lookup);
    }

    [Fact]
    public void A_point_on_an_answer_that_did_not_place_is_refused()
    {
        var meetup = Spotted();
        meetup.SetPlace(PlaceAt("Nieuwegein", AddressLookup.NotLookedUpYet, point: null));

        var result = meetup.RecordLookup(AddressIn("Nieuwegein"), AddressLookup.NotFound, Nieuwegein);

        Assert.Equal("place.lookup_mismatch", Assert.IsType<Error>(result.Value).Code);
        Assert.Null(meetup.Place!.Point);
    }

    private static readonly MapPoint Nieuwegein = Assert.IsType<Ok<MapPoint>>(MapPoint.Create(52.0302, 5.0880).Value).Value;

    private static readonly MapPoint Antwerpen = Assert.IsType<Ok<MapPoint>>(MapPoint.Create(51.2194, 4.4025).Value).Value;

    private static Address AddressIn(string town) =>
        Assert.IsType<Ok<Address>>(Address.Create(street: null, postcode: null, town, country: null).Value).Value;

    private static Place PlaceAt(string town, AddressLookup lookup, MapPoint? point, string venue = "4Dotnet") =>
        Assert.IsType<Ok<Place>>(Place.Create(venue, online: false, AddressIn(town), lookup, point).Value).Value;

    private static KnowledgeEvent PlacedIn(string town, MapPoint point)
    {
        var meetup = Spotted();
        meetup.SetPlace(PlaceAt(town, AddressLookup.NotLookedUpYet, point: null));
        Assert.IsType<Ok>(meetup.RecordLookup(AddressIn(town), AddressLookup.Placed, point).Value);
        return meetup;
    }

    private static KnowledgeEvent Spotted() => Assert.IsType<Ok<KnowledgeEvent>>(KnowledgeEvent.Create(Guid.NewGuid(), "Techorama").Value).Value;
}
