using System.Net;

using Backlog.Infrastructure.Capture.EventPages;
using Backlog.Infrastructure.Capture.Feeds;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Ports;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// An event page read by its fixed rules, against the pages it watches first
/// as they were saved on 2026-11-10: dotNET Friday, which names its date in
/// prose and links to an Eventbrite listing that carries it as structured
/// data; and Techorama's two sites, which carry their dates only in an
/// image's <c>alt</c> and <c>title</c>. The wire is scripted; the markup is
/// the sites' own.
/// </summary>
public sealed class EventPageAdapterTests
{
    private const string DotnetFriday = "https://dotnetfriday.nl/";
    private const string Listing = "https://www.eventbrite.nl/e/registratie-dotnetfriday-meetup-27-november-2026-2003121143262";
    private const string RegisterNow = Listing + "?aff=eduard";
    private const string TechoramaNl = "https://techorama.nl/";
    private const string TechoramaBe = "https://techorama.be/";

    private static readonly DateOnly SavedOn = new(2026, 11, 10);

    [Fact]
    public void The_adapter_answers_for_event_pages()
    {
        Assert.Equal(CaptureSourceKind.EventPage, Adapter(new StubHttpMessageHandler(), SavedOn).Kind);
    }

    /// <summary>The page says "the 27th of November" with no year, and links
    /// to the listing as "Register Now": one hop, and the listing's start —
    /// with its time and offset — is the edition's.</summary>
    [Fact]
    public async Task Dotnet_friday_is_read_one_hop_away_on_its_eventbrite_listing()
    {
        var wire = new StubHttpMessageHandler()
            .Html(DotnetFriday, Fixture("dotnetfriday.nl.html"))
            .Html(RegisterNow, Fixture("eventbrite-dotnetfriday-2026-11-27.html"));

        var findings = await Run(wire, SavedOn, DotnetFriday);

        Assert.Empty(findings.Notes);
        Assert.Equal([DotnetFriday, RegisterNow], wire.Requested);

        var entry = Assert.Single(findings.Entries);
        Assert.Equal("eventbrite:2003121143262", entry.ExternalId);
        Assert.Equal("DotnetFriday Meetup - 27 november 2026", entry.Title);
        Assert.Equal(Listing, entry.Url);
        Assert.Null(entry.PublishedAt);
        Assert.Equal(DotnetFriday, entry.Target);

        var facts = EventOf(entry);
        Assert.Equal(new CaptureEventDate(new DateOnly(2026, 11, 27), new TimeOnly(16, 30), TimeSpan.FromHours(1)), facts.Start);
        Assert.Equal(new CaptureEventDate(new DateOnly(2026, 11, 27)), facts.End);
        Assert.Equal("Nieuwegein", facts.Place);
        Assert.Equal(DotnetFriday, facts.PageUrl);
        Assert.Equal("DotnetFriday", facts.PageTitle);

        Assert.Equal(
            "When: 27 November 2026, 16:30 (+01:00)\n\nWhere: Nieuwegein\n\nRead from [DotnetFriday](https://dotnetfriday.nl/)",
            entry.BodyMd);
    }

    /// <summary>Watched directly, the listing's structured data is the first
    /// step: no hop, and the same edition under the same id.</summary>
    [Fact]
    public async Task An_eventbrite_listing_watched_directly_is_read_from_its_own_structured_data()
    {
        var wire = new StubHttpMessageHandler().Html(Listing, Fixture("eventbrite-dotnetfriday-2026-11-27.html"));

        var findings = await Run(wire, SavedOn, Listing);

        Assert.Equal([Listing], wire.Requested);

        var entry = Assert.Single(findings.Entries);
        Assert.Equal("eventbrite:2003121143262", entry.ExternalId);
        Assert.Equal(new CaptureEventDate(new DateOnly(2026, 11, 27), new TimeOnly(16, 30), TimeSpan.FromHours(1)), EventOf(entry).Start);
        Assert.Equal("Nieuwegein", EventOf(entry).Place);
        Assert.Equal(Listing, EventOf(entry).PageUrl);
    }

    /// <summary>No structured data, a "Tickets" link that leads nowhere it can
    /// read, and the dates in an image's alt text: the text step reads them,
    /// as a run of days with no time, and the failed hop costs no note.</summary>
    [Fact]
    public async Task Techorama_nl_is_read_from_the_dates_in_its_image_text()
    {
        var wire = new StubHttpMessageHandler().Html(TechoramaNl, Fixture("techorama.nl.html"));

        var findings = await Run(wire, new DateOnly(2026, 10, 1), TechoramaNl);

        Assert.Empty(findings.Notes);
        Assert.Contains("https://techorama.nl/tickets", wire.Requested);

        var entry = Assert.Single(findings.Entries);
        Assert.Equal("https://techorama.nl/|2026|", entry.ExternalId);
        Assert.Equal("Techorama | Developer Conference", entry.Title);
        Assert.Equal(TechoramaNl, entry.Url);

        var facts = EventOf(entry);
        Assert.Equal(new CaptureEventDate(new DateOnly(2026, 10, 26)), facts.Start);
        Assert.Equal(new CaptureEventDate(new DateOnly(2026, 10, 28)), facts.End);
        Assert.Null(facts.Place);
        Assert.StartsWith("When: 26 – 28 October 2026\n\n", entry.BodyMd, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Techorama_nl_after_its_edition_gives_no_entry_and_says_why()
    {
        var wire = new StubHttpMessageHandler().Html(TechoramaNl, Fixture("techorama.nl.html"));

        var findings = await Run(wire, SavedOn, TechoramaNl);

        Assert.Empty(findings.Entries);
        Assert.Equal(
            "https://techorama.nl/: the edition it shows has passed (ended 28 October 2026)",
            Assert.Single(findings.Notes));
    }

    /// <summary>The page whose only edition is past: techorama.be still shows
    /// May 2026 in November, and its "May 12-13" without a year is no date.</summary>
    [Fact]
    public async Task Techorama_be_still_showing_last_springs_edition_gives_no_entry()
    {
        var wire = new StubHttpMessageHandler().Html(TechoramaBe, Fixture("techorama.be.html"));

        var findings = await Run(wire, SavedOn, TechoramaBe);

        Assert.Empty(findings.Entries);
        Assert.Equal(
            "https://techorama.be/: the edition it shows has passed (ended 13 May 2026)",
            Assert.Single(findings.Notes));
    }

    [Fact]
    public async Task A_dutch_page_is_read_like_an_english_one()
    {
        var wire = new StubHttpMessageHandler().Html(
            "https://conferentie.example.nl/",
            Page("Conferentie", "<p>Noteer alvast: 11 - 13 mei 2027 in de Jaarbeurs.</p>"));

        var findings = await Run(wire, SavedOn, "https://conferentie.example.nl/");

        var facts = EventOf(Assert.Single(findings.Entries));
        Assert.Equal(new CaptureEventDate(new DateOnly(2027, 5, 11)), facts.Start);
        Assert.Equal(new CaptureEventDate(new DateOnly(2027, 5, 13)), facts.End);
    }

    [Fact]
    public async Task A_run_across_two_months_is_one_edition()
    {
        var wire = new StubHttpMessageHandler().Html(
            "https://conferentie.example.nl/",
            Page("Conferentie", "<p>30 sep - 2 okt 2026</p>"));

        var findings = await Run(wire, new DateOnly(2026, 9, 1), "https://conferentie.example.nl/");

        var entry = Assert.Single(findings.Entries);
        Assert.Equal(new CaptureEventDate(new DateOnly(2026, 9, 30)), EventOf(entry).Start);
        Assert.Equal(new CaptureEventDate(new DateOnly(2026, 10, 2)), EventOf(entry).End);
        Assert.StartsWith("When: 30 September – 2 October 2026", entry.BodyMd, StringComparison.Ordinal);
    }

    /// <summary>Nothing to read a date from: the page still arrives, with its
    /// link and no dates, under the page's own id.</summary>
    [Fact]
    public async Task A_page_with_no_date_arrives_with_its_link_only()
    {
        var wire = new StubHttpMessageHandler().Html(
            "https://meetup.example.org/events/",
            Page("Our meetups", "<p>Watch this space.</p>"));

        var findings = await Run(wire, SavedOn, "https://meetup.example.org/events/");

        Assert.Empty(findings.Notes);

        var entry = Assert.Single(findings.Entries);
        Assert.Equal("https://meetup.example.org/events/", entry.ExternalId);
        Assert.Equal("Our meetups", entry.Title);
        Assert.Equal("https://meetup.example.org/events/", entry.Url);

        var facts = EventOf(entry);
        Assert.Null(facts.Start);
        Assert.Null(facts.End);
        Assert.Null(facts.Place);
        Assert.Equal("https://meetup.example.org/events/", facts.PageUrl);
        Assert.Equal(
            "No date was found on the page.\n\nRead from [Our meetups](https://meetup.example.org/events/)",
            entry.BodyMd);
    }

    /// <summary>The same page read twice gives the same id — that is all that
    /// keeps a second run from delivering the edition again — and next year's
    /// edition on it gives another.</summary>
    [Fact]
    public async Task An_edition_keeps_its_id_across_runs_and_next_years_gets_its_own()
    {
        const string url = "https://conference.example.org/";
        var thisYear = new StubHttpMessageHandler().Html(url, Page("Conference", "<p>26 - 28 OCT 2026</p>"));
        var nextYear = new StubHttpMessageHandler().Html(url, Page("Conference", "<p>25 - 27 OCT 2027</p>"));
        var today = new DateOnly(2026, 10, 1);

        var first = Assert.Single((await Run(thisYear, today, url)).Entries).ExternalId;
        var again = Assert.Single((await Run(thisYear, today, url)).Entries).ExternalId;
        var later = Assert.Single((await Run(nextYear, today, url)).Entries).ExternalId;

        Assert.Equal(first, again);
        Assert.Equal("https://conference.example.org/|2026|", first);
        Assert.Equal("https://conference.example.org/|2027|", later);
    }

    /// <summary>A target typed without its scheme, or with its host in
    /// capitals, is the same page and so the same edition.</summary>
    [Fact]
    public async Task The_page_part_of_the_id_is_the_typed_target_as_a_url()
    {
        var wire = new StubHttpMessageHandler().Html(TechoramaNl, Fixture("techorama.nl.html"));

        var findings = await Run(wire, new DateOnly(2026, 10, 1), "Techorama.NL");

        Assert.Equal("https://techorama.nl/|2026|", Assert.Single(findings.Entries).ExternalId);
        Assert.Equal("Techorama.NL", findings.Entries[0].Target);
    }

    [Fact]
    public async Task A_broken_structured_data_block_is_skipped_not_thrown_on()
    {
        const string url = "https://summit.example.org/";
        var wire = new StubHttpMessageHandler().Html(url, Page(
            "Summit",
            """
            <script type="application/ld+json">{ "@type": "Event", "startDate": </script>
            <script type="application/ld+json">{ "@type": "BusinessEvent", "name": "Summit 2027", "startDate": "2027-03-04", "location": { "@type": "VirtualLocation", "url": "https://summit.example.org/live" } }</script>
            """));

        var findings = await Run(wire, SavedOn, url);

        var entry = Assert.Single(findings.Entries);
        Assert.Equal("Summit 2027", entry.Title);
        Assert.Equal("https://summit.example.org/|2027|online", entry.ExternalId);
        Assert.Equal("Online", EventOf(entry).Place);
        Assert.Equal([url], wire.Requested);
    }

    /// <summary>Structured data with editions past and to come: the past ones
    /// are skipped, each one to come is its own entry.</summary>
    [Fact]
    public async Task Every_upcoming_structured_event_is_its_own_entry_and_past_ones_are_skipped()
    {
        const string url = "https://meetup.example.org/";
        var wire = new StubHttpMessageHandler().Html(url, Page(
            "Meetup",
            """
            <script type="application/ld+json">{ "@graph": [
              { "@type": "SocialEvent", "name": "Spring", "startDate": "2026-04-03", "location": "Utrecht" },
              { "@type": "SocialEvent", "name": "Winter", "startDate": "2026-12-04T17:00", "location": "Utrecht" },
              { "@type": ["Thing", "EducationEvent"], "name": "Next spring", "startDate": "2027-04-02", "location": "Amsterdam" }
            ] }</script>
            """));

        var findings = await Run(wire, SavedOn, url);

        Assert.Equal(["Winter", "Next spring"], findings.Entries.Select(entry => entry.Title).ToArray());
        Assert.Equal(new CaptureEventDate(new DateOnly(2026, 12, 4), new TimeOnly(17, 0)), EventOf(findings.Entries[0]).Start);
        Assert.Equal("https://meetup.example.org/|2026|utrecht", findings.Entries[0].ExternalId);
        Assert.Equal("https://meetup.example.org/|2027|amsterdam", findings.Entries[1].ExternalId);
    }

    /// <summary>The day after dotNET Friday's listed edition: the page still
    /// links last edition's listing, and the "Join us" line names the next
    /// date. The listing has passed, so the read goes on to the page text and
    /// finds it. The saved page with one line changed, the way its organisers
    /// announce the next edition.</summary>
    [Fact]
    public async Task Dotnet_friday_still_linking_its_passed_listing_is_read_on_to_the_next_date_in_its_text()
    {
        var page = Fixture("dotnetfriday.nl.html").Replace(
            "<h3>Join us the 27th of November</h3>",
            "<h3>Join us on February 12, 2027</h3>",
            StringComparison.Ordinal);
        Assert.Contains("February 12, 2027", page, StringComparison.Ordinal);

        var wire = new StubHttpMessageHandler()
            .Html(DotnetFriday, page)
            .Html(RegisterNow, Fixture("eventbrite-dotnetfriday-2026-11-27.html"));

        var findings = await Run(wire, new DateOnly(2026, 11, 28), DotnetFriday);

        Assert.Equal([DotnetFriday, RegisterNow], wire.Requested);
        Assert.Empty(findings.Notes);

        var entry = Assert.Single(findings.Entries);
        Assert.Equal("https://dotnetfriday.nl/|2027|", entry.ExternalId);
        Assert.Equal(DotnetFriday, entry.Url);
        Assert.Equal(new CaptureEventDate(new DateOnly(2027, 2, 12)), EventOf(entry).Start);
    }

    /// <summary>The saved page the day after its edition, unchanged: the
    /// listing has passed and the text names no year, so nothing arrives and
    /// the note names the listing's date — the latest any step read.</summary>
    [Fact]
    public async Task Dotnet_friday_after_its_listed_edition_with_no_new_date_says_it_has_passed()
    {
        var wire = new StubHttpMessageHandler()
            .Html(DotnetFriday, Fixture("dotnetfriday.nl.html"))
            .Html(RegisterNow, Fixture("eventbrite-dotnetfriday-2026-11-27.html"));

        var findings = await Run(wire, new DateOnly(2026, 11, 28), DotnetFriday);

        Assert.Empty(findings.Entries);
        Assert.Equal(
            "https://dotnetfriday.nl/: the edition it shows has passed (ended 27 November 2026)",
            Assert.Single(findings.Notes));
    }

    /// <summary>Last edition's structured data left on the page, and a link to
    /// this edition's listing: the passed event does not stop the read, the
    /// hop finds the listing.</summary>
    [Fact]
    public async Task A_passed_structured_event_on_the_page_is_read_past_to_its_registration_link()
    {
        const string url = "https://meetup.example.org/";
        var wire = new StubHttpMessageHandler()
            .Html(url, Page(
                "Meetup",
                $$"""
                <script type="application/ld+json">{ "@type": "Event", "name": "Last year", "startDate": "2025-11-28", "location": "Nieuwegein" }</script>
                <p><a href="{{Listing}}">Tickets</a></p>
                """))
            .Html(Listing, Fixture("eventbrite-dotnetfriday-2026-11-27.html"));

        var findings = await Run(wire, SavedOn, url);

        Assert.Equal([url, Listing], wire.Requested);
        Assert.Empty(findings.Notes);
        Assert.Equal("eventbrite:2003121143262", Assert.Single(findings.Entries).ExternalId);
    }

    /// <summary>Two editions in one year and place, each with a page of its
    /// own: the first keeps the id it was always delivered under, the second
    /// is told apart by its url.</summary>
    [Fact]
    public async Task Two_editions_in_the_same_year_and_place_are_told_apart_by_their_own_url()
    {
        const string url = "https://meetup.example.org/";
        var wire = new StubHttpMessageHandler().Html(url, Page(
            "Meetup",
            """
            <script type="application/ld+json">[
              { "@type": "Event", "name": "Spring", "startDate": "2027-04-02", "location": "Utrecht", "url": "https://meetup.example.org/spring" },
              { "@type": "Event", "name": "Autumn", "startDate": "2027-10-01", "location": "Utrecht", "url": "https://meetup.example.org/autumn" }
            ]</script>
            """));

        var findings = await Run(wire, SavedOn, url);

        Assert.Equal(
            ["https://meetup.example.org/|2027|utrecht", "https://meetup.example.org/|2027|utrecht|https://meetup.example.org/autumn"],
            findings.Entries.Select(entry => entry.ExternalId).ToArray());
    }

    /// <summary>The earlier edition has passed and still shows: the one to
    /// come keeps the plain id it was always delivered under, so a page
    /// watched only since the earlier passed delivers nothing twice.</summary>
    [Fact]
    public async Task A_passed_edition_still_on_the_page_does_not_move_the_next_ones_id()
    {
        const string url = "https://meetup.example.org/";
        var wire = new StubHttpMessageHandler().Html(url, Page(
            "Meetup",
            """
            <script type="application/ld+json">[
              { "@type": "Event", "name": "Spring", "startDate": "2026-04-03", "location": "Utrecht", "url": "https://meetup.example.org/spring" },
              { "@type": "Event", "name": "Winter", "startDate": "2026-12-04", "location": "Utrecht", "url": "https://meetup.example.org/winter" }
            ]</script>
            """));

        var findings = await Run(wire, SavedOn, url);

        var entry = Assert.Single(findings.Entries);
        Assert.Equal("Winter", entry.Title);
        Assert.Equal("https://meetup.example.org/|2026|utrecht", entry.ExternalId);
    }

    /// <summary>The page lists its editions out of order: the one that starts
    /// first keeps the plain id, wherever it stands.</summary>
    [Fact]
    public async Task The_edition_that_starts_first_keeps_the_plain_id_wherever_the_page_lists_it()
    {
        const string url = "https://meetup.example.org/";
        var wire = new StubHttpMessageHandler().Html(url, Page(
            "Meetup",
            """
            <script type="application/ld+json">[
              { "@type": "Event", "name": "Autumn", "startDate": "2027-10-01", "location": "Utrecht", "url": "https://meetup.example.org/autumn" },
              { "@type": "Event", "name": "Spring", "startDate": "2027-04-02", "location": "Utrecht", "url": "https://meetup.example.org/spring" }
            ]</script>
            """));

        var findings = await Run(wire, SavedOn, url);

        Assert.Equal(
            ["Autumn|https://meetup.example.org/|2027|utrecht|https://meetup.example.org/autumn", "Spring|https://meetup.example.org/|2027|utrecht"],
            findings.Entries.Select(entry => $"{entry.Title}|{entry.ExternalId}").ToArray());
    }

    /// <summary>The first edition names no page of its own, the second does:
    /// the second is still told apart.</summary>
    [Fact]
    public async Task A_later_edition_with_a_url_is_told_apart_from_a_first_one_without()
    {
        const string url = "https://meetup.example.org/";
        var wire = new StubHttpMessageHandler().Html(url, Page(
            "Meetup",
            """
            <script type="application/ld+json">[
              { "@type": "Event", "name": "Spring", "startDate": "2027-04-02", "location": "Utrecht" },
              { "@type": "Event", "name": "Autumn", "startDate": "2027-10-01", "location": "Utrecht", "url": "https://meetup.example.org/autumn" }
            ]</script>
            """));

        var findings = await Run(wire, SavedOn, url);

        Assert.Equal(
            ["https://meetup.example.org/|2027|utrecht", "https://meetup.example.org/|2027|utrecht|https://meetup.example.org/autumn"],
            findings.Entries.Select(entry => entry.ExternalId).ToArray());
    }

    /// <summary>The page's own event has passed and its listing cannot be
    /// read: the note says both, since the next edition may be behind it.</summary>
    [Fact]
    public async Task A_failed_hop_after_a_passed_event_is_named_beside_it()
    {
        const string url = "https://meetup.example.org/";
        var wire = new StubHttpMessageHandler()
            .Html(url, Page(
                "Meetup",
                """
                <script type="application/ld+json">{ "@type": "Event", "name": "Last year", "startDate": "2025-11-28", "location": "Nieuwegein" }</script>
                <p><a href="/register">Registreer nu</a></p>
                """))
            .Status("https://meetup.example.org/register", HttpStatusCode.InternalServerError);

        var findings = await Run(wire, SavedOn, url);

        Assert.Empty(findings.Entries);
        Assert.Equal(
            "https://meetup.example.org/: the edition it shows has passed (ended 28 November 2025), and its registration page https://meetup.example.org/register could not be read: the server answered 500 (InternalServerError)",
            Assert.Single(findings.Notes));
    }

    /// <summary>One event carried twice — in a page block and again in its
    /// graph — is one edition, not two told apart.</summary>
    [Fact]
    public async Task An_event_carried_twice_is_one_edition()
    {
        const string url = "https://meetup.example.org/";
        const string winter = """{ "@type": "Event", "name": "Winter", "startDate": "2026-12-04", "location": "Utrecht", "url": "https://meetup.example.org/winter" }""";
        var wire = new StubHttpMessageHandler().Html(url, Page(
            "Meetup",
            $"""
            <script type="application/ld+json">{winter}</script>
            <script type="application/ld+json">{winter}</script>
            """));

        var findings = await Run(wire, SavedOn, url);

        Assert.Equal("https://meetup.example.org/|2026|utrecht", Assert.Single(findings.Entries).ExternalId);
    }

    /// <summary>A registration link that cannot be read is not the target's
    /// failure: the text step still runs, and the hop is only mentioned when
    /// the page gave nothing else either.</summary>
    [Fact]
    public async Task A_failed_hop_is_mentioned_only_when_nothing_else_was_found()
    {
        const string url = "https://meetup.example.org/";
        var wire = new StubHttpMessageHandler()
            .Html(url, Page("Meetup", """<p><a href="/register">Registreer nu</a></p>"""))
            .Status("https://meetup.example.org/register", HttpStatusCode.InternalServerError);

        var findings = await Run(wire, SavedOn, url);

        Assert.Equal(url, Assert.Single(findings.Entries).ExternalId);
        Assert.Equal(
            "https://meetup.example.org/: its registration page https://meetup.example.org/register could not be read: the server answered 500 (InternalServerError)",
            Assert.Single(findings.Notes));
    }

    /// <summary>Last year's listing left in a comment is not hopped to: the
    /// page's own text, with this year's date and a time before it, is read.</summary>
    [Fact]
    public async Task A_commented_out_listing_is_not_hopped_to_and_the_text_date_is_read()
    {
        const string url = "https://meetup.example.org/";
        var wire = new StubHttpMessageHandler().Html(url, Page(
            "Meetup",
            """
            <!-- <a href="https://www.eventbrite.nl/e/meetup-2025-123">Tickets</a> -->
            <p>Inloop 18:30 - 27 november 2026</p>
            """));

        var findings = await Run(wire, SavedOn, url);

        Assert.Equal([url], wire.Requested);
        Assert.Empty(findings.Notes);

        var entry = Assert.Single(findings.Entries);
        Assert.Equal(new CaptureEventDate(new DateOnly(2026, 11, 27)), EventOf(entry).Start);
        Assert.Null(EventOf(entry).End);
    }

    [Fact]
    public async Task One_failing_target_does_not_stop_the_next()
    {
        var wire = new StubHttpMessageHandler()
            .Status("https://down.example.org/", HttpStatusCode.ServiceUnavailable)
            .Html(TechoramaNl, Fixture("techorama.nl.html"));

        var findings = await Run(wire, new DateOnly(2026, 10, 1), "https://down.example.org/", TechoramaNl);

        Assert.Equal("https://techorama.nl/|2026|", Assert.Single(findings.Entries).ExternalId);
        Assert.Equal(
            "https://down.example.org/: the server answered 503 (ServiceUnavailable)",
            Assert.Single(findings.Notes));
    }

    private static CaptureEventFacts EventOf(CapturedEntry entry)
    {
        Assert.NotNull(entry.Facts);
        Assert.Null(entry.Facts.ContentKind);
        Assert.Empty(entry.Facts.Tags);
        Assert.NotNull(entry.Facts.Event);

        return entry.Facts.Event;
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "EventPages", name));

    private static string Page(string title, string body) =>
        $"<!DOCTYPE html><html><head><title>{title}</title></head><body>{body}</body></html>";

    private static Task<CaptureSourceFindings> Run(StubHttpMessageHandler wire, DateOnly today, params string[] targets) =>
        Adapter(wire, today).RunAsync(new MonitoredSource(CaptureSourceKind.EventPage, Enabled: true, targets), TestContext.Current.CancellationToken);

    private static EventPageAdapter Adapter(StubHttpMessageHandler wire, DateOnly today) =>
        new(new FeedFetcher(new StubHttpClientFactory(wire)), new FixedDay(today));

    /// <summary>A clock stopped at noon on one day, in UTC as the local zone,
    /// so "today" is the day the test names on any machine.</summary>
    private sealed class FixedDay(DateOnly today) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }
}
