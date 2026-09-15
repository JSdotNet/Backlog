using Backlog.Infrastructure.Capture.Feeds;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// Atom 1.0 and RSS 2.0 into entries, read off small inline documents. The
/// fixtures are shaped like what the wire actually carries — a YouTube channel
/// feed with its <c>yt:</c> and <c>media:</c> namespaces, an RSS item with a
/// guid — because the reader's whole job is to be unbothered by what it does
/// not need.
/// </summary>
public sealed class FeedReaderTests
{
    private const string Atom = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Example blog</title>
          <entry>
            <id>tag:example.org,2026:post-1</id>
            <title>First post</title>
            <link rel="alternate" href="https://example.org/post/1"/>
            <link rel="enclosure" href="https://example.org/post/1.mp3"/>
            <published>2026-09-01T10:00:00Z</published>
            <updated>2026-09-02T10:00:00Z</updated>
          </entry>
          <entry>
            <id>tag:example.org,2026:post-2</id>
            <title>Second post</title>
            <link href="https://example.org/post/2"/>
            <updated>2026-09-03T10:00:00+02:00</updated>
          </entry>
        </feed>
        """;

    private const string Rss = """
        <?xml version="1.0"?>
        <rss version="2.0">
          <channel>
            <title>Example news</title>
            <item>
              <title>Headline</title>
              <link>https://example.org/news/1</link>
              <guid isPermaLink="false">news-1</guid>
              <pubDate>Mon, 07 Sep 2026 09:30:00 GMT</pubDate>
              <description>Some HTML.</description>
            </item>
            <item>
              <title>No guid, link stands in</title>
              <link>https://example.org/news/2</link>
            </item>
          </channel>
        </rss>
        """;

    private const string YouTube = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns:media="http://search.yahoo.com/mrss/" xmlns="http://www.w3.org/2005/Atom">
          <link rel="self" href="http://www.youtube.com/feeds/videos.xml?channel_id=UCvtT19MZW8dq5Wwfu6B0oxw"/>
          <id>yt:channel:vtT19MZW8dq5Wwfu6B0oxw</id>
          <yt:channelId>vtT19MZW8dq5Wwfu6B0oxw</yt:channelId>
          <title>dotnet</title>
          <entry>
            <id>yt:video:abc123DEF45</id>
            <yt:videoId>abc123DEF45</yt:videoId>
            <yt:channelId>UCvtT19MZW8dq5Wwfu6B0oxw</yt:channelId>
            <title>What's new in Aspire 13</title>
            <link rel="alternate" href="https://www.youtube.com/watch?v=abc123DEF45"/>
            <author><name>dotnet</name></author>
            <published>2026-09-10T15:00:00+00:00</published>
            <updated>2026-09-11T08:00:00+00:00</updated>
            <media:group>
              <media:title>What's new in Aspire 13</media:title>
              <media:content url="https://www.youtube.com/v/abc123DEF45?version=3" type="application/x-shockwave-flash" width="640" height="390"/>
              <media:thumbnail url="https://i2.ytimg.com/vi/abc123DEF45/hqdefault.jpg" width="480" height="360"/>
              <media:description>A tour of the release.
        Second paragraph.</media:description>
            </media:group>
          </entry>
        </feed>
        """;

    [Fact]
    public void Atom_entries_carry_id_title_alternate_link_and_published_date()
    {
        var entries = FeedReader.Read(Atom);

        Assert.Equal(2, entries.Count);

        var first = entries[0];
        Assert.Equal("tag:example.org,2026:post-1", first.ExternalId);
        Assert.Equal("First post", first.Title);
        Assert.Equal("https://example.org/post/1", first.Url);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), first.PublishedAt);
        Assert.Null(first.BodyMd);

        // No rel is alternate; no published falls back to updated.
        var second = entries[1];
        Assert.Equal("https://example.org/post/2", second.Url);
        Assert.Equal(new DateTimeOffset(2026, 9, 3, 10, 0, 0, TimeSpan.FromHours(2)), second.PublishedAt);
    }

    [Fact]
    public void Rss_items_carry_guid_title_link_and_pub_date()
    {
        var entries = FeedReader.Read(Rss);

        Assert.Equal(2, entries.Count);

        var first = entries[0];
        Assert.Equal("news-1", first.ExternalId);
        Assert.Equal("Headline", first.Title);
        Assert.Equal("https://example.org/news/1", first.Url);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 9, 30, 0, TimeSpan.Zero), first.PublishedAt);

        var second = entries[1];
        Assert.Equal("https://example.org/news/2", second.ExternalId);
        Assert.Null(second.PublishedAt);
    }

    [Fact]
    public void A_youtube_channel_feed_reads_as_atom_with_the_watch_link_and_the_description()
    {
        var entry = Assert.Single(FeedReader.Read(YouTube));

        Assert.Equal("yt:video:abc123DEF45", entry.ExternalId);
        Assert.Equal("What's new in Aspire 13", entry.Title);
        Assert.Equal("https://www.youtube.com/watch?v=abc123DEF45", entry.Url);
        Assert.Equal(new DateTimeOffset(2026, 9, 10, 15, 0, 0, TimeSpan.Zero), entry.PublishedAt);
        Assert.Equal("A tour of the release.\nSecond paragraph.", entry.BodyMd);
    }

    [Fact]
    public void An_entry_without_a_title_is_skipped()
    {
        const string feed = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry><id>a</id><link href="https://example.org/a"/></entry>
              <entry><id>b</id><title>   </title></entry>
              <entry><id>c</id><title>Kept</title></entry>
            </feed>
            """;

        Assert.Equal("c", Assert.Single(FeedReader.Read(feed)).ExternalId);
    }

    [Fact]
    public void An_atom_entry_without_an_id_falls_back_to_its_link()
    {
        const string feed = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry><title>No id</title><link href="https://example.org/a"/></entry>
            </feed>
            """;

        Assert.Equal("https://example.org/a", Assert.Single(FeedReader.Read(feed)).ExternalId);
    }

    [Fact]
    public void A_title_is_read_on_one_line()
    {
        const string feed = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry><id>a</id><title>
                 Two
                 lines  </title></entry>
            </feed>
            """;

        Assert.Equal("Two lines", Assert.Single(FeedReader.Read(feed)).Title);
    }

    [Fact]
    public void Malformed_xml_is_a_format_error_that_says_so()
    {
        var error = Assert.Throws<FormatException>(() => FeedReader.Read("<feed><entry>"));

        Assert.Contains("well-formed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_document_that_is_not_a_feed_is_a_format_error()
    {
        var error = Assert.Throws<FormatException>(() => FeedReader.Read("<html><body>Hello</body></html>"));

        Assert.Contains("not an Atom or RSS feed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A feed's links are as often relative as not, and a relative
    /// link is meaningless on an Inbox row: it is resolved against where the
    /// feed was fetched from — after redirects — the way a browser would.</summary>
    [Fact]
    public void A_relative_rss_link_is_resolved_against_the_feeds_own_url()
    {
        const string feed = """
            <rss version="2.0"><channel>
              <item><title>Post</title><link>/posts/x/</link><guid>x</guid></item>
            </channel></rss>
            """;

        var entry = Assert.Single(FeedReader.Read(feed, new Uri("https://h/blog/feed/")));

        Assert.Equal("https://h/posts/x/", entry.Url);
    }

    [Fact]
    public void A_relative_atom_href_is_resolved_against_the_feeds_own_url()
    {
        const string feed = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry><id>a</id><title>Post</title><link href="../posts/a"/></entry>
            </feed>
            """;

        var entry = Assert.Single(FeedReader.Read(feed, new Uri("https://h/blog/feed/")));

        Assert.Equal("https://h/blog/posts/a", entry.Url);
    }

    /// <summary>An <c>xml:base</c> on the feed or the entry is what the
    /// publisher says the links are relative to, and it outranks where the
    /// document was fetched from.</summary>
    [Fact]
    public void An_xml_base_on_the_feed_or_the_entry_is_honoured()
    {
        const string feed = """
            <feed xmlns="http://www.w3.org/2005/Atom" xml:base="https://h/2026/">
              <entry><id>a</id><title>At the feed's base</title><link href="a/"/></entry>
              <entry xml:base="old/" ><id>b</id><title>At the entry's base</title><link href="b/"/></entry>
            </feed>
            """;

        var entries = FeedReader.Read(feed, new Uri("https://h/blog/feed/"));

        Assert.Equal("https://h/2026/a/", entries[0].Url);
        Assert.Equal("https://h/2026/old/b/", entries[1].Url);
    }

    /// <summary>A relative link with nothing to resolve it against is no
    /// link, not a string the Inbox would try to open.</summary>
    [Fact]
    public void A_relative_link_with_no_base_is_no_link()
    {
        const string feed = """
            <rss version="2.0"><channel>
              <item><title>Post</title><link>/posts/x/</link><guid>x</guid></item>
            </channel></rss>
            """;

        Assert.Null(Assert.Single(FeedReader.Read(feed)).Url);
    }

    /// <summary>RSS has no namespace, but some publishers put one on anyway —
    /// Userland's own is the one seen in the wild — and the reader follows
    /// whatever the root declared rather than insisting on none.</summary>
    [Fact]
    public void Rss_in_a_default_namespace_is_read_all_the_same()
    {
        const string feed = """
            <?xml version="1.0"?>
            <rss version="2.0" xmlns="http://backend.userland.com/rss2">
              <channel>
                <title>Namespaced</title>
                <item>
                  <title>Headline</title>
                  <link>https://example.org/news/1</link>
                  <guid>news-1</guid>
                  <pubDate>Mon, 07 Sep 2026 09:30:00 GMT</pubDate>
                </item>
              </channel>
            </rss>
            """;

        var entry = Assert.Single(FeedReader.Read(feed));

        Assert.Equal("news-1", entry.ExternalId);
        Assert.Equal("Headline", entry.Title);
        Assert.Equal("https://example.org/news/1", entry.Url);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 9, 30, 0, TimeSpan.Zero), entry.PublishedAt);
    }

    /// <summary>A title in a feed is XML-escaped once by the XML and often
    /// HTML-escaped once more by the publisher, so what comes out of the XML
    /// still reads <c>&amp;#8217;</c>. The Inbox shows the character.</summary>
    [Fact]
    public void Html_entities_left_in_a_title_or_description_are_decoded()
    {
        const string feed = """
            <feed xmlns="http://www.w3.org/2005/Atom" xmlns:media="http://search.yahoo.com/mrss/">
              <entry>
                <id>a</id>
                <title>What&amp;#8217;s new &amp;amp; what&amp;#39;s not</title>
                <media:group><media:description>Tom &amp;amp; Jerry &amp;#8212; a tour</media:description></media:group>
              </entry>
            </feed>
            """;

        var entry = Assert.Single(FeedReader.Read(feed));

        Assert.Equal("What’s new & what's not", entry.Title);
        Assert.Equal("Tom & Jerry — a tour", entry.BodyMd);
    }

    [Fact]
    public void Html_entities_left_in_an_rss_title_are_decoded()
    {
        const string feed = """
            <rss version="2.0"><channel>
              <item><title>Rock &amp;amp; roll</title><guid>a</guid></item>
            </channel></rss>
            """;

        Assert.Equal("Rock & roll", Assert.Single(FeedReader.Read(feed)).Title);
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><rss/>", true)]
    [InlineData("\n  <rss version=\"2.0\">", true)]
    [InlineData("<feed xmlns=\"http://www.w3.org/2005/Atom\">", true)]
    [InlineData("\uFEFF<?xml version=\"1.0\"?><feed xmlns=\"http://www.w3.org/2005/Atom\">", true)]
    [InlineData("\uFEFF\n<rss version=\"2.0\">", true)]
    [InlineData("<!DOCTYPE html><html>", false)]
    [InlineData("", false)]
    public void Looks_like_a_feed_reads_the_first_tag(string body, bool expected)
    {
        Assert.Equal(expected, FeedReader.LooksLikeFeed(body));
    }
}
