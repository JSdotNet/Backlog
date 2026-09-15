using System.Net;

using Backlog.Infrastructure.Capture.Feeds;
using Backlog.Infrastructure.Capture.YouTube;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Capture.UnitTests;

/// <summary>
/// A channel target in any of the spellings a person pastes — a feed URL, a
/// <c>/channel/UC…</c> URL, a bare id, a handle — ends up at the channel's Atom
/// feed, and what that feed holds comes back as entries. One target's trouble
/// is a note; the others still run.
/// </summary>
public sealed class YouTubeChannelAdapterTests
{
    private const string ChannelId = "UCvtT19MZW8dq5Wwfu6B0oxw";
    private const string FeedUrl = $"https://www.youtube.com/feeds/videos.xml?channel_id={ChannelId}";

    private const string Feed = $"""
        <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <id>yt:video:abc123DEF45</id>
            <yt:videoId>abc123DEF45</yt:videoId>
            <title>A video</title>
            <link rel="alternate" href="https://www.youtube.com/watch?v=abc123DEF45"/>
            <published>2026-09-10T15:00:00+00:00</published>
          </entry>
        </feed>
        """;

    [Fact]
    public async Task A_feed_url_is_used_as_it_is()
    {
        var wire = new StubHttpMessageHandler().Xml(FeedUrl, Feed);

        var findings = await Run(wire, FeedUrl);

        Assert.Equal([FeedUrl], wire.Requested);
        Assert.Equal("yt:video:abc123DEF45", Assert.Single(findings.Entries).ExternalId);
        Assert.Empty(findings.Notes);
    }

    [Theory]
    [InlineData($"https://www.youtube.com/channel/{ChannelId}")]
    [InlineData($"https://www.youtube.com/channel/{ChannelId}/videos")]
    [InlineData($"youtube.com/channel/{ChannelId}")]
    [InlineData(ChannelId)]
    public async Task A_channel_url_or_a_bare_id_goes_straight_to_the_feed(string target)
    {
        var wire = new StubHttpMessageHandler().Xml(FeedUrl, Feed);

        var findings = await Run(wire, target);

        Assert.Equal([FeedUrl], wire.Requested);
        Assert.Single(findings.Entries);
        Assert.Empty(findings.Notes);
    }

    [Theory]
    [InlineData("https://www.youtube.com/@dotnet", "https://www.youtube.com/@dotnet")]
    [InlineData("@dotnet", "https://www.youtube.com/@dotnet")]
    [InlineData("https://www.youtube.com/user/dotnet", "https://www.youtube.com/user/dotnet")]
    [InlineData("https://www.youtube.com/c/dotnet", "https://www.youtube.com/c/dotnet")]
    public async Task A_handle_or_a_named_page_is_fetched_and_its_channel_id_read_out(string target, string page)
    {
        var wire = new StubHttpMessageHandler()
            .Html(page, $$$$"""<html><head><title>dotnet</title></head><body><script>var ytInitialData = {"metadata":{"channelMetadataRenderer":{"externalId":"{{{{ChannelId}}}}","channelId":"{{{{ChannelId}}}}"}}};</script></body></html>""")
            .Xml(FeedUrl, Feed);

        var findings = await Run(wire, target);

        Assert.Equal([page, FeedUrl], wire.Requested);
        Assert.Single(findings.Entries);
        Assert.Empty(findings.Notes);
    }

    /// <summary>Every spelling the page is known to carry the id in, each on
    /// its own, since the page after a consent redirect may carry only one.</summary>
    [Theory]
    [InlineData($"""<link rel="canonical" href="https://www.youtube.com/channel/{ChannelId}">""")]
    [InlineData($"""<link rel="alternate" type="application/rss+xml" title="RSS" href="https://www.youtube.com/feeds/videos.xml?channel_id={ChannelId}">""")]
    [InlineData($$$$"""<script>var ytInitialData = {"metadata":{"channelMetadataRenderer":{"channelId":"{{{{ChannelId}}}}"}}};</script>""")]
    [InlineData($$$$"""<script>var ytInitialData = {"metadata":{"channelMetadataRenderer":{"externalId":"{{{{ChannelId}}}}"}}};</script>""")]
    [InlineData($"""<meta itemprop="identifier" content="{ChannelId}">""")]
    [InlineData($"""<meta itemprop="channelId" content="{ChannelId}">""")]
    public async Task The_channel_id_is_read_from_whichever_spelling_the_page_carries(string head)
    {
        var wire = new StubHttpMessageHandler()
            .Html("https://www.youtube.com/@dotnet", $"<html><head>{head}</head><body></body></html>")
            .Xml(FeedUrl, Feed);

        var findings = await Run(wire, "@dotnet");

        Assert.Single(findings.Entries);
        Assert.Equal(["https://www.youtube.com/@dotnet", FeedUrl], wire.Requested);
    }

    /// <summary>Without the consent cookie YouTube answers a handle page with
    /// a redirect to its consent wall and a landing page that names no
    /// channel. The cookie goes on the page request and nowhere else — the
    /// feed does not want it, and no other host should see it.</summary>
    [Fact]
    public async Task The_page_request_carries_the_consent_cookie_and_the_feed_request_does_not()
    {
        var wire = new StubHttpMessageHandler()
            .Html("https://www.youtube.com/@dotnet", $"""<html><head><meta itemprop="identifier" content="{ChannelId}"></head></html>""")
            .Xml(FeedUrl, Feed);

        await Run(wire, "@dotnet");

        Assert.Equal("SOCS=CAI; CONSENT=YES+cb", wire.HeaderSentTo("https://www.youtube.com/@dotnet", "Cookie"));
        Assert.Null(wire.HeaderSentTo(FeedUrl, "Cookie"));
    }

    [Fact]
    public async Task A_page_with_no_channel_id_is_a_note_for_that_target()
    {
        var wire = new StubHttpMessageHandler()
            .Html("https://www.youtube.com/@nobody", "<html><head><title>Not a channel</title></head></html>");

        var findings = await Run(wire, "@nobody");

        Assert.Empty(findings.Entries);
        Assert.Equal("@nobody: could not find a channel id", Assert.Single(findings.Notes));
    }

    [Fact]
    public async Task One_targets_failure_is_a_note_and_the_next_target_still_runs()
    {
        var wire = new StubHttpMessageHandler()
            .Status("https://www.youtube.com/@gone", HttpStatusCode.InternalServerError)
            .Xml(FeedUrl, Feed);

        var findings = await Run(wire, "@gone", ChannelId);

        Assert.Single(findings.Entries);
        var note = Assert.Single(findings.Notes);
        Assert.StartsWith("@gone: ", note, StringComparison.Ordinal);
        Assert.Contains("500", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_feed_that_is_not_xml_is_a_note_for_that_target()
    {
        var wire = new StubHttpMessageHandler().Html(FeedUrl, "<html>nope</html>");

        var findings = await Run(wire, FeedUrl);

        Assert.Empty(findings.Entries);
        Assert.StartsWith($"{FeedUrl}: ", Assert.Single(findings.Notes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_propagates_rather_than_becoming_a_note()
    {
        using var cancellation = new CancellationTokenSource();
        var wire = new StubHttpMessageHandler().Map(FeedUrl, () =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(wire, cancellation.Token, FeedUrl));
    }

    [Fact]
    public void The_adapter_answers_for_youtube()
    {
        Assert.Equal(CaptureSourceKind.YouTube, Adapter(new StubHttpMessageHandler()).Kind);
    }

    private static Task<Backlog.Modules.Capture.Ports.CaptureSourceFindings> Run(StubHttpMessageHandler wire, params string[] targets) =>
        Run(wire, TestContext.Current.CancellationToken, targets);

    private static Task<Backlog.Modules.Capture.Ports.CaptureSourceFindings> Run(StubHttpMessageHandler wire, CancellationToken cancellationToken, params string[] targets) =>
        Adapter(wire).RunAsync(new MonitoredSource(CaptureSourceKind.YouTube, Enabled: true, targets), cancellationToken);

    private static YouTubeChannelAdapter Adapter(StubHttpMessageHandler wire) =>
        new(new FeedFetcher(new StubHttpClientFactory(wire)));
}
