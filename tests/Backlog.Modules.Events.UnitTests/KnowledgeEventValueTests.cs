using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;

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

        Assert.Equal("event_date.offset_missing", result.Error.Code);
    }

    [Fact]
    public void A_date_alone_is_an_event_date()
    {
        var date = EventDate.Create(MeetupDay, time: null, utcOffset: null).Value;

        Assert.Equal(MeetupDay, date.Date);
        Assert.Null(date.Time);
        Assert.Null(date.UtcOffset);
    }

    [Fact]
    public void A_date_with_a_time_and_its_offset_is_an_event_date()
    {
        var date = EventDate.Create(MeetupDay, new TimeOnly(16, 30), CentralEuropean).Value;

        Assert.Equal(new TimeOnly(16, 30), date.Time);
        Assert.Equal(TimeSpan.FromHours(1), date.UtcOffset);
    }

    [Fact]
    public void An_offset_without_a_time_is_kept()
    {
        var date = EventDate.Create(MeetupDay, time: null, CentralEuropean).Value;

        Assert.Null(date.Time);
        Assert.Equal(TimeSpan.FromHours(1), date.UtcOffset);
    }

    [Fact]
    public void Two_event_dates_with_the_same_parts_are_equal()
    {
        Assert.Equal(
            EventDate.Create(MeetupDay, new TimeOnly(16, 30), CentralEuropean).Value,
            EventDate.Create(MeetupDay, new TimeOnly(16, 30), CentralEuropean).Value);
        Assert.NotEqual(
            EventDate.Create(MeetupDay, new TimeOnly(16, 30), CentralEuropean).Value,
            EventDate.Create(MeetupDay, new TimeOnly(17, 0), CentralEuropean).Value);
    }

    // --- A place is a venue or online ---------------------------------------------------

    [Fact]
    public void A_place_that_is_both_a_venue_and_online_is_refused()
    {
        Assert.Equal("place.invalid", Place.Create("Kinepolis Antwerp", online: true).Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_place_that_is_neither_a_venue_nor_online_is_refused(string? venue)
    {
        Assert.Equal("place.invalid", Place.Create(venue, online: false).Error.Code);
    }

    [Fact]
    public void A_blank_venue_is_refused()
    {
        Assert.Equal("place.invalid", Place.AtVenue(" ").Error.Code);
    }

    [Fact]
    public void A_venue_is_a_place_with_its_name_trimmed()
    {
        var place = Place.AtVenue("  Kinepolis Antwerp ").Value;

        Assert.Equal("Kinepolis Antwerp", place.Venue);
        Assert.False(place.IsOnline);
        Assert.Equal(place, Place.Create("Kinepolis Antwerp", online: false).Value);
    }

    [Fact]
    public void Online_is_a_place_with_no_venue()
    {
        Assert.True(Place.Online.IsOnline);
        Assert.Null(Place.Online.Venue);
        Assert.Equal(Place.Online, Place.Create(venue: null, online: true).Value);
    }

    // --- A key date has a label ---------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_key_date_without_a_label_is_refused(string label)
    {
        Assert.Equal("key_date.label_missing", KeyDate.Create(new DateOnly(2026, 9, 1), label).Error.Code);
    }

    [Fact]
    public void A_key_date_keeps_its_date_and_its_label_trimmed()
    {
        var earlyBird = KeyDate.Create(new DateOnly(2026, 9, 1), " Early bird ends ").Value;

        Assert.Equal(new DateOnly(2026, 9, 1), earlyBird.Date);
        Assert.Equal("Early bird ends", earlyBird.Label);
        Assert.Equal(earlyBird, KeyDate.Create(new DateOnly(2026, 9, 1), "Early bird ends").Value);
    }

    // --- A cost is not negative and names its currency ----------------------------------

    [Fact]
    public void A_negative_cost_is_refused()
    {
        Assert.Equal("cost.invalid", Cost.Create(-0.01m, "EUR").Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_cost_without_a_currency_is_refused(string currency)
    {
        Assert.Equal("cost.invalid", Cost.Create(150m, currency).Error.Code);
    }

    [Fact]
    public void A_free_event_costs_zero()
    {
        Assert.Equal(0m, Cost.Create(0m, "EUR").Value.Amount);
    }

    [Fact]
    public void A_cost_writes_its_currency_trimmed_and_upper_cased()
    {
        var ticket = Cost.Create(895m, " eur ").Value;

        Assert.Equal("EUR", ticket.Currency);
        Assert.Equal(ticket, Cost.Create(895m, "EUR").Value);
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
