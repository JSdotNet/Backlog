using System.Net;

using Backlog.Infrastructure.Capture.Feeds;
using Backlog.Infrastructure.Capture.Website;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Ports;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// A page target is read as a feed when it is one, and otherwise as the HTML
/// page that links to its feed. A page that links to none is a note; so is a
/// page that could not be fetched — and neither stops the next target.
/// </summary>
public sealed class WebsiteFeedAdapterTests
{
    private const string Rss = """
        <?xml version="1.0"?>
        <rss version="2.0"><channel>
          <item><title>Headline</title><link>https://example.org/news/1</link><guid>news-1</guid></item>
        </channel></rss>
        """;

    [Fact]
    public async Task A_target_that_is_a_feed_is_read_directly()
    {
        var wire = new StubHttpMessageHandler().Xml("https://example.org/feed.xml", Rss);

        var findings = await Run(wire, "https://example.org/feed.xml");

        var entry = Assert.Single(findings.Entries);
        Assert.Equal("news-1", entry.ExternalId);
        Assert.Equal("https://example.org/news/1", entry.Url);
        Assert.Empty(findings.Notes);
        Assert.Equal(["https://example.org/feed.xml"], wire.Requested);
    }

    /// <summary>Some servers send a feed as text/html or text/plain. The body
    /// says what it is; the header only gets a vote.</summary>
    [Fact]
    public async Task A_feed_served_under_the_wrong_content_type_is_still_a_feed()
    {
        var wire = new StubHttpMessageHandler().Html("https://example.org/feed", Rss);

        var findings = await Run(wire, "https://example.org/feed");

        Assert.Single(findings.Entries);
    }

    /// <summary>A byte-order mark is not a tag, and a server that sends one
    /// ahead of a feed it calls text/plain has still sent a feed.</summary>
    [Fact]
    public async Task A_feed_behind_a_byte_order_mark_and_the_wrong_content_type_is_still_a_feed()
    {
        const string atom = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry><id>a</id><title>Post</title><link href="https://example.org/a"/></entry>
            </feed>
            """;
        var wire = new StubHttpMessageHandler().Map(
            "https://example.org/feed",
            () => StubHttpMessageHandler.Body(HttpStatusCode.OK, "\uFEFF" + atom, "text/plain"));

        var findings = await Run(wire, "https://example.org/feed");

        Assert.Single(findings.Entries);
        Assert.Equal(["https://example.org/feed"], wire.Requested);
    }

    /// <summary>Entry links are resolved against where the feed was finally
    /// served from, not where it was asked for: a feed reached through a
    /// redirect writes its links relative to the place it lives.</summary>
    [Fact]
    public async Task Relative_entry_links_are_resolved_against_the_feeds_post_redirect_url()
    {
        const string rss = """
            <rss version="2.0"><channel>
              <item><title>Post</title><link>/posts/x/</link><guid>x</guid></item>
            </channel></rss>
            """;
        var wire = new StubHttpMessageHandler().Map("https://example.org/feed", () =>
        {
            var response = StubHttpMessageHandler.Body(HttpStatusCode.OK, rss, "application/xml");
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://blog.example.org/feed/");
            return response;
        });

        var findings = await Run(wire, "https://example.org/feed");

        Assert.Equal("https://blog.example.org/posts/x/", Assert.Single(findings.Entries).Url);
    }

    /// <summary>A feed a page advertises is fetched on the reader's machine,
    /// so it is held to the same rule as an address they typed: http or https
    /// to a real host, and nothing else. A page that advertises a
    /// <c>file:</c> or <c>ftp:</c> feed is read on to the next candidate.</summary>
    [Fact]
    public async Task An_advertised_feed_that_is_not_a_web_address_is_skipped_and_discovery_continues()
    {
        var wire = new StubHttpMessageHandler()
            .Html("https://example.org/", """
                <html><head>
                  <link rel="alternate" type="application/rss+xml" href="file:///etc/feed.xml">
                  <link rel="alternate" type="application/rss+xml" href="ftp://example.org/feed.xml">
                  <link rel="alternate" type="application/rss+xml" href="http://localhost/feed.xml">
                  <link rel="alternate" type="application/rss+xml" href="/feed.xml">
                </head></html>
                """)
            .Xml("https://example.org/feed.xml", Rss);

        var findings = await Run(wire, "https://example.org/");

        Assert.Single(findings.Entries);
        Assert.Equal(["https://example.org/", "https://example.org/feed.xml"], wire.Requested);
    }

    [Fact]
    public async Task An_anchor_to_a_feed_that_is_not_a_web_address_is_skipped()
    {
        var wire = new StubHttpMessageHandler()
            .Html("https://example.org/", """<html><body><a href="ftp://example.org/feed/">RSS</a></body></html>""")
            .Xml("https://example.org/feed/", Rss);

        var findings = await Run(wire, "https://example.org/");

        Assert.Single(findings.Entries);
        Assert.DoesNotContain(wire.Requested, url => url.StartsWith("ftp:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_page_that_links_to_its_feed_is_read_through_that_link()
    {
        var wire = new StubHttpMessageHandler()
            .Html("https://example.org/blog/", """
                <!DOCTYPE html>
                <html><head>
                  <link rel="stylesheet" href="/site.css">
                  <link rel="alternate" type="application/rss+xml" title="Posts" href="/blog/feed.xml">
                  <link rel="alternate" type="application/atom+xml" href="/blog/atom.xml">
                </head><body>Hello</body></html>
                """)
            .Xml("https://example.org/blog/feed.xml", Rss);

        var findings = await Run(wire, "https://example.org/blog/");

        Assert.Single(findings.Entries);
        Assert.Empty(findings.Notes);
        Assert.Equal(["https://example.org/blog/", "https://example.org/blog/feed.xml"], wire.Requested);
    }

    [Fact]
    public async Task A_target_without_a_scheme_is_read_over_https()
    {
        var wire = new StubHttpMessageHandler().Xml("https://example.org/feed.xml", Rss);

        var findings = await Run(wire, "example.org/feed.xml");

        Assert.Single(findings.Entries);
    }

    [Fact]
    public async Task The_feed_link_may_be_absolute_and_in_any_attribute_order()
    {
        var wire = new StubHttpMessageHandler()
            .Html("https://example.org/", """<html><head><link href="https://feeds.example.net/x" type='application/atom+xml' rel="alternate"/></head></html>""")
            .Xml("https://feeds.example.net/x", Rss);

        var findings = await Run(wire, "https://example.org/");

        Assert.Single(findings.Entries);
    }

    /// <summary>A page that advertises nothing in its head but links to its
    /// feed in the body — WordPress's footer "RSS" link — is read through that
    /// anchor, with a relative href resolved against the page.</summary>
    [Fact]
    public async Task A_page_that_only_links_to_its_feed_in_the_body_is_read_through_that_anchor()
    {
        var wire = new StubHttpMessageHandler()
            .Html("https://example.org/blog/", """
                <html><head><title>Blog</title></head><body>
                  <a href="/about/">About</a>
                  <a href="https://twitter.com/example">Twitter</a>
                  <a class="rss" href="feed/">RSS</a>
                </body></html>
                """)
            .Xml("https://example.org/blog/feed/", Rss);

        var findings = await Run(wire, "https://example.org/blog/");

        Assert.Single(findings.Entries);
        Assert.Empty(findings.Notes);
        Assert.Equal(["https://example.org/blog/", "https://example.org/blog/feed/"], wire.Requested);
    }

    /// <summary>An anchor to a feed on another host is somebody else's feed,
    /// not this page's, and does not count.</summary>
    [Fact]
    public async Task An_anchor_to_a_feed_on_another_host_is_not_this_pages_feed()
    {
        var wire = new StubHttpMessageHandler()
            .Html("https://example.org/", """<html><body><a href="https://elsewhere.example.net/feed/">Their RSS</a></body></html>""")
            .Xml("https://elsewhere.example.net/feed/", Rss);

        var findings = await Run(wire, "https://example.org/");

        Assert.Empty(findings.Entries);
        Assert.DoesNotContain("https://elsewhere.example.net/feed/", wire.Requested);
    }

    /// <summary>A page that advertises nothing and links to nothing is asked
    /// at the well-known paths, the target's own path first — a section blog
    /// under <c>/dotnet/</c> keeps its feed under <c>/dotnet/feed/</c>.</summary>
    [Fact]
    public async Task A_page_with_neither_link_nor_anchor_is_probed_at_the_well_known_paths()
    {
        var wire = new StubHttpMessageHandler()
            .Html("https://devblogs.example.com/dotnet/", "<html><head><title>.NET Blog</title></head><body>Posts</body></html>")
            .Xml("https://devblogs.example.com/dotnet/feed/", Rss);

        var findings = await Run(wire, "https://devblogs.example.com/dotnet/");

        Assert.Single(findings.Entries);
        Assert.Empty(findings.Notes);
        Assert.Equal(["https://devblogs.example.com/dotnet/", "https://devblogs.example.com/dotnet/feed/"], wire.Requested);
    }

    [Fact]
    public async Task The_targets_own_path_is_probed_before_the_site_root()
    {
        var wire = new StubHttpMessageHandler()
            .Html("https://example.org/blog/", "<html><body>Posts</body></html>")
            .Xml("https://example.org/blog/feed/", Rss)
            .Xml("https://example.org/feed/", Rss.Replace("news-1", "root-1", StringComparison.Ordinal));

        var findings = await Run(wire, "https://example.org/blog/");

        Assert.Equal("news-1", Assert.Single(findings.Entries).ExternalId);
        Assert.DoesNotContain("https://example.org/feed/", wire.Requested);
    }

    /// <summary>A probe that answers with a page — the 200 a CMS puts on its
    /// own "not found" — is skipped as quietly as a 404 is.</summary>
    [Fact]
    public async Task A_probe_that_answers_with_a_page_or_a_404_is_skipped_and_the_next_one_tried()
    {
        var wire = new StubHttpMessageHandler()
            .Html("https://example.org/blog/", "<html><body>Posts</body></html>")
            .Html("https://example.org/blog/feed/", "<html><body>Not found</body></html>")
            .Status("https://example.org/feed/", HttpStatusCode.NotFound)
            .Xml("https://example.org/blog/feed", Rss);

        var findings = await Run(wire, "https://example.org/blog/");

        Assert.Single(findings.Entries);
        Assert.Empty(findings.Notes);
    }

    [Fact]
    public async Task When_every_probe_fails_the_target_gets_exactly_one_note()
    {
        var wire = new StubHttpMessageHandler().Html("https://example.org/", "<html><head><title>Plain</title></head></html>");

        var findings = await Run(wire, "https://example.org/");

        Assert.Empty(findings.Entries);
        Assert.Equal("https://example.org/: no feed found", Assert.Single(findings.Notes));
    }

    /// <summary>Six probes and no more: each well-known name at the target's
    /// path and then at the root, in the order the names are listed, and the
    /// page's own fetch on top. A dead page costs a bounded number of
    /// requests, not fourteen.</summary>
    [Fact]
    public async Task Probing_stops_after_six_requests()
    {
        var wire = new StubHttpMessageHandler().Html("https://example.org/blog/", "<html><body>Posts</body></html>");

        var findings = await Run(wire, "https://example.org/blog/");

        Assert.Single(findings.Notes);
        Assert.Equal(
            [
                "https://example.org/blog/",
                "https://example.org/blog/feed/",
                "https://example.org/feed/",
                "https://example.org/blog/feed",
                "https://example.org/feed",
                "https://example.org/blog/rss.xml",
                "https://example.org/rss.xml",
            ],
            wire.Requested);
    }

    /// <summary>At the root the two bases are the same place, so each name is
    /// probed once and the cap reaches further down the list.</summary>
    [Fact]
    public async Task At_the_site_root_each_well_known_path_is_probed_once()
    {
        var wire = new StubHttpMessageHandler().Html("https://example.org/", "<html><body>Home</body></html>");

        await Run(wire, "https://example.org/");

        Assert.Equal(
            [
                "https://example.org/",
                "https://example.org/feed/",
                "https://example.org/feed",
                "https://example.org/rss.xml",
                "https://example.org/atom.xml",
                "https://example.org/index.xml",
                "https://example.org/feed.xml",
            ],
            wire.Requested);
    }

    [Fact]
    public async Task A_server_error_is_a_note_and_the_next_target_still_runs()
    {
        var wire = new StubHttpMessageHandler()
            .Status("https://down.example.org/", HttpStatusCode.InternalServerError)
            .Xml("https://example.org/feed.xml", Rss);

        var findings = await Run(wire, "https://down.example.org/", "https://example.org/feed.xml");

        Assert.Single(findings.Entries);
        var note = Assert.Single(findings.Notes);
        Assert.StartsWith("https://down.example.org/: ", note, StringComparison.Ordinal);
        Assert.Contains("500", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_target_that_is_not_a_url_is_a_note()
    {
        var findings = await Run(new StubHttpMessageHandler(), "not a url at all");

        Assert.Empty(findings.Entries);
        Assert.StartsWith("not a url at all: ", Assert.Single(findings.Notes), StringComparison.Ordinal);
    }

    [Fact]
    public void The_adapter_answers_for_website()
    {
        Assert.Equal(CaptureSourceKind.Website, Adapter(new StubHttpMessageHandler()).Kind);
    }

    private static Task<CaptureSourceFindings> Run(StubHttpMessageHandler wire, params string[] targets) =>
        Adapter(wire).RunAsync(new MonitoredSource(CaptureSourceKind.Website, Enabled: true, targets), TestContext.Current.CancellationToken);

    private static WebsiteFeedAdapter Adapter(StubHttpMessageHandler wire) =>
        new(new FeedFetcher(new StubHttpClientFactory(wire)));
}
