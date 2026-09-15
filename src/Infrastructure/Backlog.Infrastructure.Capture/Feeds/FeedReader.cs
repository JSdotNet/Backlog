using System.Globalization;
using System.Net;
using System.Xml;
using System.Xml.Linq;

using Backlog.Modules.Capture.Ports;

namespace Backlog.Infrastructure.Capture.Feeds;

/// <summary>
/// Atom 1.0 and RSS 2.0 into <see cref="CapturedEntry"/>, and nothing else
/// about either: no feed-level metadata, no categories, no enclosures. It reads
/// the four things the run needs — an id, a title, a link, a date — and is
/// deliberately tolerant about the rest, because a feed in the wild carries
/// whatever extensions its publisher bolted on (YouTube's <c>yt:</c> and
/// <c>media:</c>, a podcast's <c>itunes:</c>) and a reader that choked on one
/// would be a reader of nothing.
/// <para>
/// Hand-rolled over <see cref="XDocument"/> rather than
/// <c>System.ServiceModel.Syndication</c>: that package is a dependency the
/// solution does not carry (inherited ADR 0002), and two element shapes do not
/// earn one.
/// </para>
/// </summary>
internal static class FeedReader
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";

    /// <summary>Whether a body is a feed, read off its first tag rather than
    /// the content type the server put on it — servers send feeds as
    /// <c>text/html</c> often enough that the header only gets a vote. A
    /// byte-order mark ahead of the tag is a character, not whitespace, and
    /// is stepped over on its own.</summary>
    public static bool LooksLikeFeed(string body)
    {
        var start = body.AsSpan().TrimStart('\uFEFF').TrimStart();

        return start.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            || start.StartsWith("<rss", StringComparison.OrdinalIgnoreCase)
            || start.StartsWith("<feed", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads every entry that has a title. Throws
    /// <see cref="FormatException"/> for a document that is not XML or not a
    /// feed, with a message a person can read on the run's line. Entry links
    /// are resolved against <paramref name="baseUrl"/> — where the document
    /// was served from, after redirects — unless the feed names its own base;
    /// a link that resolves against neither is no link.</summary>
    public static IReadOnlyList<CapturedEntry> Read(string xml, Uri? baseUrl = null)
    {
        using var text = new StringReader(xml);
        return Read(text, baseUrl);
    }

    /// <summary>The same over a stream, which lets the XML reader honour the
    /// encoding the document declares rather than the one the fetch guessed.</summary>
    public static IReadOnlyList<CapturedEntry> Read(Stream xml, Uri? baseUrl = null)
    {
        using var reader = XmlReader.Create(xml, Settings);
        return Read(reader, baseUrl);
    }

    private static IReadOnlyList<CapturedEntry> Read(TextReader xml, Uri? baseUrl)
    {
        using var reader = XmlReader.Create(xml, Settings);
        return Read(reader, baseUrl);
    }

    /// <summary>No DTD, no external resolution: a feed is untrusted input, and
    /// an entity expansion is not something a reader of titles needs.</summary>
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true
    };

    private static IReadOnlyList<CapturedEntry> Read(XmlReader reader, Uri? baseUrl)
    {
        XDocument document;

        try
        {
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new FormatException($"the feed is not well-formed XML ({ex.Message.TrimEnd('.')})", ex);
        }

        var root = document.Root
            ?? throw new FormatException("the feed is not well-formed XML (empty document)");

        if (root.Name == Atom + "feed")
        {
            return [.. root.Elements(Atom + "entry").Select(entry => AtomEntry(entry, baseUrl)).OfType<CapturedEntry>()];
        }

        if (string.Equals(root.Name.LocalName, "rss", StringComparison.OrdinalIgnoreCase))
        {
            // RSS 2.0 has no namespace, but a publisher that put one on the
            // root put it on every child too, so the children are looked up
            // in whatever the root is in.
            var rss = root.Name.Namespace;

            return [.. root.Elements(rss + "channel").Elements(rss + "item").Select(item => RssItem(item, rss, baseUrl)).OfType<CapturedEntry>()];
        }

        throw new FormatException($"not an Atom or RSS feed (the document is <{root.Name.LocalName}>)");
    }

    private static CapturedEntry? AtomEntry(XElement entry, Uri? baseUrl)
    {
        var title = OneLine(Unescaped(entry.Element(Atom + "title")?.Value));
        if (title is null) return null;

        // rel="alternate" is the entry's page; a link with no rel means the same
        // thing (RFC 4287 §4.2.7.2). Enclosures, self links and the rest are
        // something else.
        var alternate = entry.Elements(Atom + "link")
            .FirstOrDefault(candidate =>
            {
                var rel = (string?)candidate.Attribute("rel");
                return string.IsNullOrEmpty(rel) || string.Equals(rel, "alternate", StringComparison.OrdinalIgnoreCase);
            });

        var link = alternate is null ? null : Resolved(alternate.Attribute("href")?.Value, alternate, baseUrl);

        var id = Trimmed(entry.Element(Atom + "id")?.Value) ?? link;
        if (id is null) return null;

        var published = Date(entry.Element(Atom + "published")?.Value) ?? Date(entry.Element(Atom + "updated")?.Value);

        // The one body worth carrying: YouTube's description, which is plain
        // text. An Atom summary or content is as likely HTML as not, and an
        // Inbox row that renders it as Markdown would show the tags.
        var body = Trimmed(Unescaped(entry.Element(Media + "group")?.Element(Media + "description")?.Value));

        return new CapturedEntry(id, title, link, body, published);
    }

    private static CapturedEntry? RssItem(XElement item, XNamespace rss, Uri? baseUrl)
    {
        var title = OneLine(Unescaped(item.Element(rss + "title")?.Value));
        if (title is null) return null;

        var linkElement = item.Element(rss + "link");
        var link = linkElement is null ? null : Resolved(linkElement.Value, linkElement, baseUrl);
        var id = Trimmed(item.Element(rss + "guid")?.Value) ?? link;
        if (id is null) return null;

        return new CapturedEntry(id, title, link, null, Date(item.Element(rss + "pubDate")?.Value));
    }

    /// <summary>A link as the Inbox can open it: absolute, resolved against
    /// the nearest <c>xml:base</c> above it and then against where the feed
    /// was served from, and spelled the escaped way a browser takes it. A
    /// relative link with no base to stand on is null, not a string that
    /// only looks like a URL.</summary>
    private static string? Resolved(string? href, XElement at, Uri? baseUrl)
    {
        var text = Trimmed(href);
        if (text is null) return null;

        var @base = BaseOf(at, baseUrl);

        if (@base is null)
        {
            return Uri.TryCreate(text, UriKind.Absolute, out var absolute) ? absolute.AbsoluteUri : null;
        }

        return Uri.TryCreate(@base, text, out var resolved) ? resolved.AbsoluteUri : null;
    }

    /// <summary>The base an element's links are relative to: every
    /// <c>xml:base</c> from the root down to the element, each resolved
    /// against the one above it, starting from where the document came from.
    /// One that does not resolve is skipped rather than fatal — a publisher's
    /// typo should cost that link's base, not the feed.</summary>
    private static Uri? BaseOf(XElement element, Uri? documentBase)
    {
        var @base = documentBase;

        foreach (var ancestor in element.AncestorsAndSelf().Reverse())
        {
            var declared = Trimmed(ancestor.Attribute(XNamespace.Xml + "base")?.Value);
            if (declared is null) continue;

            if (@base is null
                    ? Uri.TryCreate(declared, UriKind.Absolute, out var next)
                    : Uri.TryCreate(@base, declared, out next))
            {
                @base = next;
            }
        }

        return @base;
    }

    /// <summary>The XML has undone one level of escaping already; a publisher
    /// that HTML-encoded the text before the XML did leaves
    /// <c>&amp;#8217;</c> in the value, and the Inbox should see the
    /// character.</summary>
    private static string? Unescaped(string? text) =>
        text is null ? null : WebUtility.HtmlDecode(text);

    /// <summary>RFC 3339 in Atom, RFC 822 in RSS; both are what
    /// <see cref="DateTimeOffset.TryParse(string, IFormatProvider, DateTimeStyles, out DateTimeOffset)"/>
    /// reads, and a date it cannot read is no date rather than a failed
    /// entry.</summary>
    private static DateTimeOffset? Date(string? text) =>
        DateTimeOffset.TryParse(text?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    private static string? Trimmed(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>A title on one line, the way the Inbox stores it anyway.</summary>
    private static string? OneLine(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
