using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;

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
        var earlyBird = KeyDate.Create(new DateOnly(2026, 9, 1), "Early bird ends").Value;
        var registration = KeyDate.Create(new DateOnly(2026, 10, 1), "Registration closes").Value;

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
        var ticket = Cost.Create(895m, "EUR").Value;

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
        var start = EventDate.Create(new DateOnly(2026, 10, 26), new TimeOnly(9, 0), TimeSpan.FromHours(1)).Value;
        var end = EventDate.Create(new DateOnly(2026, 10, 28), time: null, utcOffset: null).Value;
        var place = Place.AtVenue("Kinepolis Antwerp").Value;
        var earlyBird = KeyDate.Create(new DateOnly(2026, 9, 1), "Early bird ends").Value;
        var cost = Cost.Create(895m, "EUR").Value;
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

    private static KnowledgeEvent Spotted() => KnowledgeEvent.Create(Guid.NewGuid(), "Techorama").Value;
}
