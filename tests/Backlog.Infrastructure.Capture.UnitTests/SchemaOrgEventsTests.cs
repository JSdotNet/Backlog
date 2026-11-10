using System.Text.Json;

using Backlog.Infrastructure.Capture.EventPages;
using Backlog.Modules.Capture.Ports;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// The first step's reading of a page's structured data: which types are
/// events, what a schema.org date keeps, where a location says the event is,
/// and that a broken block costs only itself.
/// </summary>
public sealed class SchemaOrgEventsTests
{
    [Theory]
    [InlineData("Event", true)]
    [InlineData("SocialEvent", true)]
    [InlineData("BusinessEvent", true)]
    [InlineData("EducationEvent", true)]
    [InlineData("schema:Event", true)]
    [InlineData("WebPage", false)]
    [InlineData("EventSeries", false)]
    [InlineData("event", false)]
    [InlineData(null, false)]
    public void An_event_is_event_or_one_of_its_subtypes(string? type, bool isEvent)
    {
        Assert.Equal(isEvent, SchemaOrgEvents.IsEventType(type));
    }

    /// <summary>A date keeps exactly what the source said — the time and
    /// offset only when it gave them.</summary>
    [Fact]
    public void A_date_keeps_its_time_and_offset_only_when_given()
    {
        var day = new DateOnly(2026, 11, 27);

        Assert.Equal(new CaptureEventDate(day, new TimeOnly(16, 30), TimeSpan.FromHours(1)), SchemaOrgEvents.ParseDate("2026-11-27T16:30:00+01:00"));
        Assert.Equal(new CaptureEventDate(day), SchemaOrgEvents.ParseDate("2026-11-27"));
        Assert.Equal(new CaptureEventDate(day, new TimeOnly(16, 30)), SchemaOrgEvents.ParseDate("2026-11-27T16:30"));
        Assert.Equal(new CaptureEventDate(day, new TimeOnly(15, 30), TimeSpan.Zero), SchemaOrgEvents.ParseDate("2026-11-27T15:30:00.000Z"));
        Assert.Equal(new CaptureEventDate(day, new TimeOnly(9, 0), TimeSpan.FromHours(-5)), SchemaOrgEvents.ParseDate("2026-11-27T09:00-0500"));
    }

    [Theory]
    [InlineData("TBA")]
    [InlineData("27 November 2026")]
    [InlineData("2026-02-30")]
    [InlineData("2026-13-01")]
    [InlineData("2026-11-27T25:00")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_no_date(string? text)
    {
        Assert.Null(SchemaOrgEvents.ParseDate(text));
    }

    [Theory]
    [InlineData("""{ "@type": "Place", "name": "4Dotnet", "address": { "@type": "PostalAddress", "addressLocality": "Nieuwegein" } }""", "Nieuwegein")]
    [InlineData("""{ "@type": "Place", "name": "Kinepolis Antwerpen", "address": { "@type": "PostalAddress", "streetAddress": "Groenendaallaan 394" } }""", "Kinepolis Antwerpen")]
    [InlineData("""{ "@type": "Place", "address": "Jaarbeursplein 6, Utrecht" }""", "Jaarbeursplein 6, Utrecht")]
    [InlineData("""{ "@type": "VirtualLocation", "url": "https://example.org/live" }""", "Online")]
    [InlineData("""  "Utrecht  Centraal " """, "Utrecht Centraal")]
    [InlineData("""[{ "@type": "VirtualLocation" }, { "@type": "Place", "name": "Hall 1" }]""", "Online")]
    [InlineData("""{ "@type": "Place" }""", null)]
    public void A_location_says_where_in_the_fewest_words(string json, string? place)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(place, SchemaOrgEvents.PlaceOf(document.RootElement));
    }

    [Fact]
    public void Events_are_found_in_graphs_arrays_and_lists_and_other_blocks_are_ignored()
    {
        const string html = """
            <script type="application/ld+json">{ "@type": "WebPage", "name": "Not an event" }</script>
            <script type="application/ld+json">[{ "@type": "Event", "name": "One", "startDate": "2026-01-01" }]</script>
            <script type="application/ld+json">{ "@graph": [{ "@type": "Organization" }, { "@type": "SocialEvent", "name": "Two", "startDate": "2026-02-01" }] }</script>
            <script type="application/ld+json">{ "@type": "ItemList", "itemListElement": [{ "@type": "ListItem", "item": { "@type": "Event", "name": "Three", "startDate": "2026-03-01" } }] }</script>
            <script type="text/javascript">var x = { "@type": "Event", "name": "Not data", "startDate": "2026-04-01" };</script>
            """;

        Assert.Equal(["One", "Two", "Three"], SchemaOrgEvents.In(html).Select(found => found.Name).ToArray());
    }

    [Fact]
    public void A_broken_block_and_an_event_with_no_start_are_skipped()
    {
        const string html = """
            <script type="application/ld+json">{ "@type": "Event", "name": </script>
            <script type="application/ld+json">{ "@type": "Event", "name": "No start" }</script>
            <script type="application/ld+json">{ "@type": "Event", "name": "Kept", "startDate": "2026-05-01", }</script>
            """;

        var found = Assert.Single(SchemaOrgEvents.In(html));
        Assert.Equal("Kept", found.Name);
    }

    /// <summary>An event's own location, offers and sub-events are its own;
    /// a sub-event is not a second edition.</summary>
    [Fact]
    public void What_an_event_holds_is_not_read_as_another_event()
    {
        const string html = """
            <script type="application/ld+json">{ "@type": "Event", "name": "Conference", "startDate": "2026-10-26", "endDate": "2026-10-28",
              "subEvent": [{ "@type": "Event", "name": "Workshop day", "startDate": "2026-10-26" }] }</script>
            """;

        var found = Assert.Single(SchemaOrgEvents.In(html));
        Assert.Equal("Conference", found.Name);
        Assert.Equal(new CaptureEventDate(new DateOnly(2026, 10, 28)), found.End);
    }
}
