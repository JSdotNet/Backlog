using System.Text.RegularExpressions;

using Backlog.Infrastructure.Capture.Feeds;
using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Ports;

namespace Backlog.Infrastructure.Capture.Website;

/// <summary>
/// Answers <see cref="ICaptureSourceAdapter"/> for a web page through the feed
/// behind it. A target that is a feed is read as one; a target that is a page
/// is read for the feed it advertises in its head — the
/// <c>&lt;link rel="alternate" type="application/rss+xml"&gt;</c> autodiscovery
/// every blog engine emits — and that feed is read instead.
/// <para>
/// Not every page advertises. When the head names no feed, the page is read
/// again for an anchor to one — an <c>&lt;a href&gt;</c> on the same host whose
/// path ends the way a feed's does, the footer "RSS" link WordPress and its
/// kin emit — and when there is none either, the well-known names are probed
/// in turn: each of <c>feed/</c>, <c>feed</c>, <c>rss.xml</c>,
/// <c>atom.xml</c>, <c>index.xml</c>, <c>feed.xml</c>, <c>rss</c> at the
/// target's own path and then at the site root, at most
/// <see cref="MaxProbes"/> requests in all, stopping at the first that parses
/// as a feed. A probe that 404s or answers with a page is skipped without a
/// word; only when every step has failed is the target "no feed found".
/// </para>
/// <para>
/// "A change on a page" (the domain's words for this source) is answered here
/// as "a new entry in the page's feed", which is what a page that publishes
/// anything means by it. A page with no feed is noted rather than diffed:
/// snapshotting HTML and reporting a change would report every rotated
/// banner, and is a different adapter for a later day.
/// </para>
/// </summary>
internal sealed partial class WebsiteFeedAdapter(FeedFetcher fetcher) : ICaptureSourceAdapter
{
    /// <summary>How many well-known paths a page that advertises nothing is
    /// asked at before it is given up on. Six is the target's path and the
    /// root for the three commonest names; a dead page costs that and no more.</summary>
    internal const int MaxProbes = 6;

    /// <summary>The names a feed lives under when nobody says where, commonest
    /// first: WordPress's <c>feed/</c>, then the static generators' files.</summary>
    private static readonly string[] WellKnownPaths = ["feed/", "feed", "rss.xml", "atom.xml", "index.xml", "feed.xml", "rss"];

    /// <summary>How a feed's path ends when it is linked rather than
    /// advertised. The same names as the probes, as suffixes, plus the
    /// trailing-slash spelling WordPress links with.</summary>
    private static readonly string[] FeedPathSuffixes = ["/feed", "/feed/", "/rss", "/rss.xml", "/atom.xml", "/index.xml", "/feed.xml"];

    public CaptureSourceKind Kind => CaptureSourceKind.Website;

    public Task<CaptureSourceFindings> RunAsync(MonitoredSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        return SourceTargets.ReadAllAsync(source, ReadTargetAsync, cancellationToken);
    }

    private async Task<TargetReading> ReadTargetAsync(string target, CancellationToken cancellationToken)
    {
        var document = await fetcher.GetAsync(SourceTargets.ParseUrl(target), cancellationToken).ConfigureAwait(false);

        if (document.IsXml || FeedReader.LooksLikeFeed(document.Text))
        {
            return Read(document);
        }

        var feed = FeedLinkIn(document.Text, document.Url) ?? FeedAnchorIn(document.Text, document.Url);

        if (feed is not null)
        {
            return Read(await fetcher.GetAsync(feed, cancellationToken).ConfigureAwait(false));
        }

        return await ProbeWellKnownPathsAsync(document.Url, cancellationToken).ConfigureAwait(false)
            ?? TargetReading.Remark("no feed found");
    }

    private static TargetReading Read(FetchedDocument document)
    {
        using var xml = document.OpenRead();
        return new TargetReading(FeedReader.Read(xml, document.Url));
    }

    /// <summary>The well-known names in turn, until one parses. A probe is a
    /// guess, so what it gets back — a 404, a 200 wearing an HTML "not found",
    /// a document that is not a feed — is skipped rather than reported; the
    /// reader's own cancellation and a timeout are not, since neither is an
    /// answer.</summary>
    private async Task<TargetReading?> ProbeWellKnownPathsAsync(Uri page, CancellationToken cancellationToken)
    {
        foreach (var candidate in ProbeUrlsFor(page).Take(MaxProbes))
        {
            FetchedDocument document;

            try
            {
                document = await fetcher.GetAsync(candidate, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                continue;
            }

            if (!document.IsXml && !FeedReader.LooksLikeFeed(document.Text)) continue;

            try
            {
                return Read(document);
            }
            catch (FormatException)
            {
                // Looked like XML and was not a feed — a sitemap, say.
            }
        }

        return null;
    }

    /// <summary>Each well-known name at the page's own path and then at the
    /// site root, name by name, so the commonest name is tried at both places
    /// before a rarer one is tried at either. At the root the two are the
    /// same place, and a URL is not asked twice.</summary>
    internal static IEnumerable<Uri> ProbeUrlsFor(Uri page)
    {
        var seen = new HashSet<Uri>();

        foreach (var path in WellKnownPaths)
        {
            foreach (var candidate in new[] { new Uri(page, path), new Uri(page, "/" + path) })
            {
                if (SourceTargets.IsFetchable(candidate) && seen.Add(candidate)) yield return candidate;
            }
        }
    }

    /// <summary>The first feed a page advertises, resolved against the page's
    /// own URL because the href is usually relative. Attribute order is
    /// whatever the template wrote, so the tag is read as a bag of attributes
    /// rather than matched as a whole. A page's markup is as untrusted as a
    /// feed's, so an advertised address is held to the same rule as a typed
    /// one — a <c>file:</c> or <c>ftp:</c> href is read past, not fetched.</summary>
    internal static Uri? FeedLinkIn(string html, Uri page)
    {
        foreach (Match tag in LinkTag().Matches(html))
        {
            var attributes = AttributesOf(tag.Value);

            if (!attributes.TryGetValue("rel", out var rel) || !IsAlternate(rel)) continue;
            if (!attributes.TryGetValue("type", out var type) || !IsFeedType(type)) continue;
            if (!attributes.TryGetValue("href", out var href) || string.IsNullOrWhiteSpace(href)) continue;

            if (Uri.TryCreate(page, href.Trim(), out var url) && SourceTargets.IsFetchable(url)) return url;
        }

        return null;
    }

    /// <summary>The first anchor to a feed on the page's own host, judged by
    /// where its path ends. Another host's feed is somebody else's, and a
    /// blogroll is full of those; an address that is not a web address at
    /// all is skipped the way <see cref="FeedLinkIn"/> skips one.</summary>
    internal static Uri? FeedAnchorIn(string html, Uri page)
    {
        foreach (Match tag in AnchorTag().Matches(html))
        {
            var attributes = AttributesOf(tag.Value);

            if (!attributes.TryGetValue("href", out var href) || string.IsNullOrWhiteSpace(href)) continue;
            if (!Uri.TryCreate(page, href.Trim(), out var url) || !SourceTargets.IsFetchable(url)) continue;
            if (!string.Equals(url.Host, page.Host, StringComparison.OrdinalIgnoreCase)) continue;

            if (FeedPathSuffixes.Any(suffix => url.AbsolutePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            {
                return url;
            }
        }

        return null;
    }

    /// <summary>First occurrence wins, and a repeated attribute — untrusted
    /// markup does that — is not an exception.</summary>
    private static Dictionary<string, string> AttributesOf(string tag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match attribute in Attribute().Matches(tag))
        {
            attributes.TryAdd(attribute.Groups["name"].Value, attribute.Groups["value"].Value);
        }

        return attributes;
    }

    /// <summary>rel is a space-separated list; "alternate" anywhere in it counts.</summary>
    private static bool IsAlternate(string rel) =>
        rel.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => string.Equals(token, "alternate", StringComparison.OrdinalIgnoreCase));

    private static bool IsFeedType(string type) =>
        type.Contains("application/rss+xml", StringComparison.OrdinalIgnoreCase)
        || type.Contains("application/atom+xml", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"<a\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex AnchorTag();

    /// <summary>One attribute, quoted either way or not at all. The unquoted
    /// form stops at whitespace or the tag's end.</summary>
    [GeneratedRegex(@"(?<name>[\w:-]+)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>/]+))")]
    private static partial Regex Attribute();
}
