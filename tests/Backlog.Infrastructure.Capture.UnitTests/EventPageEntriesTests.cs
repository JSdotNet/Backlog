using Backlog.Infrastructure.Capture.EventPages;
using Backlog.Modules.Capture.Ports;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// How an edition is written up: the "When" line as short as its dates allow,
/// the listing number an Eventbrite URL carries, and the page id the other
/// editions are known by.
/// </summary>
public sealed class EventPageEntriesTests
{
    private static readonly DateOnly Day = new(2026, 11, 27);

    [Fact]
    public void When_says_only_what_the_source_gave()
    {
        var hour = TimeSpan.FromHours(1);

        Assert.Equal("27 November 2026", EventPageEntries.When(new(Day), null));
        Assert.Equal("27 November 2026, 16:30", EventPageEntries.When(new(Day, new TimeOnly(16, 30)), null));
        Assert.Equal("27 November 2026, 16:30 (+01:00)", EventPageEntries.When(new(Day, new TimeOnly(16, 30), hour), new(Day)));
        Assert.Equal("27 November 2026, 09:00 (-05:30)", EventPageEntries.When(new(Day, new TimeOnly(9, 0), new TimeSpan(-5, -30, 0)), null));
        Assert.Equal(
            "27 November 2026, 16:30 – 20:00 (+01:00)",
            EventPageEntries.When(new(Day, new TimeOnly(16, 30), hour), new(Day, new TimeOnly(20, 0), hour)));
        Assert.Equal(
            "27 November 2026, 16:30 (+01:00) – 28 November 2026",
            EventPageEntries.When(new(Day, new TimeOnly(16, 30), hour), new(Day.AddDays(1))));
    }

    [Fact]
    public void A_run_of_days_is_written_as_short_as_its_months_and_years_allow()
    {
        Assert.Equal("26 – 28 October 2026", EventPageEntries.When(new(new DateOnly(2026, 10, 26)), new(new DateOnly(2026, 10, 28))));
        Assert.Equal("30 September – 2 October 2026", EventPageEntries.When(new(new DateOnly(2026, 9, 30)), new(new DateOnly(2026, 10, 2))));
        Assert.Equal("30 December 2026 – 2 January 2027", EventPageEntries.When(new(new DateOnly(2026, 12, 30)), new(new DateOnly(2027, 1, 2))));
    }

    [Theory]
    [InlineData("https://www.eventbrite.nl/e/registratie-dotnetfriday-meetup-27-november-2026-2003121143262?aff=eduard", "2003121143262")]
    [InlineData("https://www.eventbrite.com/e/1234567/", "1234567")]
    [InlineData("https://www.eventbrite.co.uk/o/organiser-17154920697", null)]
    [InlineData("https://example.org/e/meetup-2003121143262", null)]
    public void An_eventbrite_listing_is_known_by_the_number_its_path_ends_in(string url, string? id)
    {
        Assert.Equal(id, EventPageEntries.ListingIdOf(new Uri(url)));
    }

    [Theory]
    [InlineData("https://www.eventbrite.nl/e/x-1", true)]
    [InlineData("https://EVENTBRITE.com/", true)]
    [InlineData("https://www.eventbrite.co.uk/o/organiser-1", true)]
    [InlineData("https://eventbrite-fan.example.org/", false)]
    [InlineData("https://dotnetfriday.nl/", false)]
    public void Eventbrite_is_any_host_with_eventbrite_dot_in_it(string url, bool isEventbrite)
    {
        Assert.Equal(isEventbrite, EventPageEntries.IsEventbrite(new Uri(url)));
    }

    [Theory]
    [InlineData("https://Techorama.NL", "https://techorama.nl/")]
    [InlineData("https://example.org/events/#next", "https://example.org/events/")]
    [InlineData("https://example.org/events?year=2026", "https://example.org/events?year=2026")]
    [InlineData("https://example.org:443/events", "https://example.org/events")]
    public void The_page_id_is_the_url_without_its_fragment_and_its_host_in_lower_case(string url, string id)
    {
        Assert.Equal(id, EventPageEntries.PageId(new Uri(url)));
    }

    /// <summary>A page title that would close the link early is escaped.</summary>
    [Fact]
    public void An_undated_entry_reads_on_its_own()
    {
        var entry = EventPageEntries.Undated(new WatchedPage("https://example.org/(events)", "Events [archive]"));

        Assert.Equal(
            "No date was found on the page.\n\nRead from [Events \\[archive\\]](<https://example.org/(events)>)",
            entry.BodyMd);
        Assert.Equal(new CaptureEventFacts(null, null, null, "https://example.org/(events)", "Events [archive]"), entry.Facts!.Event);
    }
}
