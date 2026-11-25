using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The pane's list by date (<c>.devbook/domain/events/requirements.md#requirement-the-pane-lists-events-by-date-upcoming-then-past</c>)
/// and its map numbers (<c>#requirement-upcoming-events-on-the-map-are-numbered-in-date-order</c>,
/// <c>domain.md#map-number</c>), on the fixture's clock: Tuesday 10 November 2026. Upcoming holds
/// what is not over yet, soonest first, then the undated; Past the rest, most recent first; an
/// empty section is not answered. Upcoming events with a stored map point that are not Skipped
/// are numbered 1..n in list order.
/// </summary>
public sealed class EventsPaneListTests : EventsPaneFixture
{
    public EventsPaneListTests()
    {
        Geocoder.Places("Utrecht", 52.0907, 5.1214);
        Geocoder.Places("Antwerpen", 51.2194, 4.4025);
        Geocoder.Places("Oslo", 59.9139, 10.7522);
        Geocoder.Places("London", 51.5072, -0.1276);
        Geocoder.FindsNothingIn("Nergenshuizen");
    }

    // --- Sections by date -------------------------------------------------------------------

    [Fact]
    public async Task Upcoming_lists_the_soonest_first_whatever_the_state_and_no_past_section_is_answered()
    {
        var oslo = await Dated("NDC Oslo", new DateOnly(2027, 6, 7));
        Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.MoveToAsync(oslo, KnowledgeEventState.Going, Token)).Value);
        await Dated("Techorama Belgium", new DateOnly(2027, 5, 18));
        await Dated("dotNET Friday", new DateOnly(2026, 11, 27));

        var section = Assert.Single(await Pane.ListAsync(Token));

        Assert.Equal(EventListSection.Upcoming, section.Section);
        Assert.Equal(["dotNET Friday", "Techorama Belgium", "NDC Oslo"], Titles(section));
    }

    [Fact]
    public async Task The_undated_close_upcoming_and_past_lists_the_most_recent_first()
    {
        await Dated("Techorama 2025", new DateOnly(2025, 5, 20));
        await Add("Azure Meetup");
        await Dated("dotNET Friday", new DateOnly(2026, 9, 25));
        await Dated("NDC Oslo", new DateOnly(2027, 6, 7));

        var sections = await Pane.ListAsync(Token);

        Assert.Equal([EventListSection.Upcoming, EventListSection.Past], sections.Select(section => section.Section));
        Assert.Equal(["NDC Oslo", "Azure Meetup"], Titles(sections[0]));
        Assert.Equal(["dotNET Friday", "Techorama 2025"], Titles(sections[1]));
    }

    [Fact]
    public async Task With_only_past_events_no_upcoming_section_is_answered()
    {
        await Dated("Techorama 2025", new DateOnly(2025, 5, 20));

        var section = Assert.Single(await Pane.ListAsync(Token));

        Assert.Equal(EventListSection.Past, section.Section);
    }

    [Fact]
    public async Task An_event_still_running_today_is_upcoming_and_one_that_ended_yesterday_is_past()
    {
        var running = await Add("Running since yesterday");
        Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.SetDatesAsync(running, new EventDateDto(new DateOnly(2026, 11, 9)), new EventDateDto(new DateOnly(2026, 11, 11)), Token)).Value);
        var ended = await Add("Ended yesterday");
        Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.SetDatesAsync(ended, new EventDateDto(new DateOnly(2026, 11, 8)), new EventDateDto(new DateOnly(2026, 11, 9)), Token)).Value);
        await Dated("Today", new DateOnly(2026, 11, 10));

        var sections = await Pane.ListAsync(Token);

        Assert.Equal(["Running since yesterday", "Today"], Titles(sections[0]));
        Assert.Equal(["Ended yesterday"], Titles(sections[1]));
    }

    /// <summary>Past is decided by the last day, so it is ordered by it too: a training that
    /// ended later is the more recent event, though a one-day meetup started after it.</summary>
    [Fact]
    public async Task Past_lists_the_event_that_ended_last_first()
    {
        var training = await Add("Training");
        Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.SetDatesAsync(training, new EventDateDto(new DateOnly(2026, 11, 1)), new EventDateDto(new DateOnly(2026, 11, 9)), Token)).Value);
        await Dated("Meetup", new DateOnly(2026, 11, 5));
        await Dated("Workshop", new DateOnly(2026, 11, 9));

        var section = Assert.Single(await Pane.ListAsync(Token));

        Assert.Equal(EventListSection.Past, section.Section);
        Assert.Equal(["Workshop", "Training", "Meetup"], Titles(section));
    }

    [Fact]
    public async Task A_skipped_event_keeps_its_date_section()
    {
        var london = await Dated("NDC London", new DateOnly(2027, 1, 25));
        Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.SkipAsync(london, "Clashes with the release", Token)).Value);

        var section = Assert.Single(await Pane.ListAsync(Token));

        Assert.Equal(EventListSection.Upcoming, section.Section);
        Assert.Equal(KnowledgeEventState.Skipped, Assert.Single(section.Events).State);
    }

    // --- Map numbers ------------------------------------------------------------------------

    [Fact]
    public async Task Upcoming_events_on_the_map_are_numbered_in_list_order()
    {
        await InTown("NDC Oslo", new DateOnly(2027, 6, 7), "Oslo");
        await InTown("dotNET Friday", new DateOnly(2026, 11, 27), "Utrecht");
        await InTown("Techorama Belgium", new DateOnly(2027, 5, 18), "Antwerpen");

        var section = Assert.Single(await Pane.ListAsync(Token));

        Assert.Equal(
            [("dotNET Friday", (int?)1), ("Techorama Belgium", 2), ("NDC Oslo", 3)],
            section.Events.Select(e => (e.Title, e.MapNumber)));
    }

    [Fact]
    public async Task An_event_with_no_map_point_takes_no_number_and_the_next_one_on_the_map_follows_on()
    {
        await InTown("dotNET Friday", new DateOnly(2026, 11, 27), "Utrecht");
        var build = await Dated("Build", new DateOnly(2026, 12, 1));
        Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.SetPlaceAsync(build, new PlaceDto(null, IsOnline: true), Token)).Value);
        await LookUpsSettledAsync();
        await InTown("Kinepolis", new DateOnly(2026, 12, 2), "Nergenshuizen");
        await InTown("Not looked up yet", new DateOnly(2026, 12, 3), "Somewhere the geocoder never answers");
        await Dated("No venue yet", new DateOnly(2026, 12, 4));
        await InTown("NDC Oslo", new DateOnly(2027, 6, 7), "Oslo");
        await InTown("Undated", null, "Antwerpen");

        var section = Assert.Single(await Pane.ListAsync(Token));

        Assert.Equal(
            [
                ("dotNET Friday", (int?)1), ("Build", null), ("Kinepolis", null), ("Not looked up yet", null),
                ("No venue yet", null), ("NDC Oslo", 2), ("Undated", 3)
            ],
            section.Events.Select(e => (e.Title, e.MapNumber)));
    }

    [Fact]
    public async Task Past_and_skipped_events_on_the_map_are_unnumbered()
    {
        await InTown("Techorama 2025", new DateOnly(2025, 5, 20), "Antwerpen");
        var london = await InTown("NDC London", new DateOnly(2027, 1, 25), "London");
        Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.SkipAsync(london, "Clashes with the release", Token)).Value);
        await InTown("NDC Oslo", new DateOnly(2027, 6, 7), "Oslo");

        var sections = await Pane.ListAsync(Token);

        Assert.Equal([("NDC London", (int?)null), ("NDC Oslo", 1)], sections[0].Events.Select(e => (e.Title, e.MapNumber)));
        var past = Assert.Single(sections[1].Events);
        Assert.Null(past.MapNumber);
        Assert.NotNull(past.Place?.Point);
    }

    /// <summary>Scope Q7: an upcoming event already marked Attended is numbered by its date.</summary>
    [Fact]
    public async Task An_upcoming_event_marked_attended_early_keeps_its_number()
    {
        var friday = await InTown("dotNET Friday", new DateOnly(2026, 11, 27), "Utrecht");
        Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.MoveToAsync(friday, KnowledgeEventState.Attended, Token)).Value);

        var listed = Assert.Single(Assert.Single(await Pane.ListAsync(Token)).Events);

        Assert.Equal((KnowledgeEventState.Attended, (int?)1), (listed.State, listed.MapNumber));
    }

    private async Task<Guid> InTown(string title, DateOnly? start, string town)
    {
        var id = start is { } day ? await Dated(title, day) : await Add(title);
        Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.SetPlaceAsync(id, new PlaceDto(title, false, new AddressDto(null, null, town, null)), Token)).Value);
        await LookUpsSettledAsync();
        return id;
    }

    private static IEnumerable<string> Titles(KnowledgeEventSectionDto section) => section.Events.Select(e => e.Title);
}
