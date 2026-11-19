using Backlog.Infrastructure.Capture.EventPages;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// What the event page adapter reads off markup besides structured data: the
/// page's title, the registration link to hop to, and the text — image text
/// included — a date is looked for in.
/// </summary>
public sealed class EventPageHtmlTests
{
    private static readonly Uri Page = new("https://meetup.example.org/");

    [Fact]
    public void The_title_is_the_shared_one_before_the_tab_one()
    {
        Assert.Equal(
            "Meetup & More",
            EventPageHtml.TitleOf("""<head><title>Home | Meetup</title><meta content="Meetup &amp; More" property="og:title"></head>"""));
        Assert.Equal("Home | Meetup", EventPageHtml.TitleOf("<head><title>\n  Home |   Meetup\n</title></head>"));
        Assert.Null(EventPageHtml.TitleOf("<p>No head at all</p>"));
    }

    /// <summary>Eventbrite wins wherever it is on the page; otherwise the
    /// first link whose text says register, registreer or tickets.</summary>
    [Fact]
    public void An_eventbrite_link_wins_over_a_worded_one()
    {
        const string html = """
            <a href="/tickets">Tickets</a>
            <a href="https://www.eventbrite.be/e/meetup-123?aff=x&amp;ref=y">Sign up</a>
            """;

        Assert.Equal(
            new Uri("https://www.eventbrite.be/e/meetup-123?aff=x&ref=y"),
            EventPageHtml.RegistrationLinkIn(html, Page));
    }

    /// <summary>Last year's listing kept in a comment, or a link a script
    /// writes, is not one the page shows, so it is not hopped to — the hop
    /// would find a passed edition and the page's own date would go unread.</summary>
    [Fact]
    public void A_link_the_page_does_not_show_is_not_hopped_to()
    {
        const string html = """
            <!-- <a href="https://www.eventbrite.nl/e/old-2025-123">Tickets</a> -->
            <script>document.write('<a href="https://www.eventbrite.nl/e/script-2025-456">Tickets</a>');</script>
            <style>/* <a href="https://www.eventbrite.nl/e/style-2025-789">x</a> */</style>
            <a href="/register">Register</a>
            """;

        Assert.Equal(new Uri("https://meetup.example.org/register"), EventPageHtml.RegistrationLinkIn(html, Page));
        Assert.Null(EventPageHtml.RegistrationLinkIn("<!-- <a href=\"https://www.eventbrite.nl/e/old-2025-123\">Tickets</a> -->", Page));
    }

    [Theory]
    [InlineData("""<a href="/join">Register now</a>""", "https://meetup.example.org/join")]
    [InlineData("""<a href="/inschrijven"><span>Registreer</span></a>""", "https://meetup.example.org/inschrijven")]
    [InlineData("""<a class=btn href="https://tickets.example.org/conf">Buy TICKETS</a>""", "https://tickets.example.org/conf")]
    [InlineData("""<a href="/info/ticket-terms">ticket terms and conditions</a>""", null)]
    [InlineData("""<a href="#tickets">Tickets</a>""", null)]
    [InlineData("""<a href="mailto:hi@example.org">Register by mail</a>""", null)]
    [InlineData("""<a href="/about">About</a>""", null)]
    public void A_worded_link_is_one_whose_text_says_so(string html, string? expected)
    {
        Assert.Equal(expected, EventPageHtml.RegistrationLinkIn(html, Page)?.AbsoluteUri);
    }

    /// <summary>Techorama draws its dates as an image and says them only in
    /// its alt and title — so those are read, where the tag stands.</summary>
    [Fact]
    public void The_text_is_what_the_page_shows_plus_what_its_images_say()
    {
        const string html = """
            <html><head><style>p { color: red }</style><script>var date = "1 Jan 2020";</script></head>
            <body><!-- 2 Feb 2020 --><p>Before</p><img src="d.svg" alt="26 - 28 OCT 2026" title="26 - 28 OCT 2026"><p>After&nbsp;&amp;   more</p></body></html>
            """;

        Assert.Equal("Before 26 - 28 OCT 2026 26 - 28 OCT 2026 After & more", EventPageHtml.TextOf(html));
    }
}
