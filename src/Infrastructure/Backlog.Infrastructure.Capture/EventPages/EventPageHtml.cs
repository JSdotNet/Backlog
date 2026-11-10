using System.Net;
using System.Text;
using System.Text.RegularExpressions;

using Backlog.Infrastructure.Capture.Feeds;

namespace Backlog.Infrastructure.Capture.EventPages;

/// <summary>
/// What the event page adapter reads off a page's markup besides its
/// structured data: the page's title, the registration link the second step
/// hops to, and the text the third step looks for a date in.
/// <para>
/// Read with patterns, not a parser, the way the website adapter reads a head:
/// a page is untrusted and often malformed, and what is wanted from it is
/// three small things a pattern finds as well as a DOM would.
/// </para>
/// </summary>
internal static partial class EventPageHtml
{
    /// <summary>The words a registration link's text says, in English or
    /// Dutch. "Tickets", not "ticket": a link to the ticket terms is not the
    /// way in.</summary>
    private static readonly string[] RegistrationWords = ["register", "registreer", "tickets"];

    /// <summary>The attributes a tag speaks with when its content is an image.</summary>
    private static readonly string[] SpokenAttributes = ["alt", "title"];

    /// <summary>The page's title as a person would name it: its
    /// <c>og:title</c> when it has one — what the site chose to be shared as,
    /// usually without the "| Home" a <c>&lt;title&gt;</c> carries — else its
    /// <c>&lt;title&gt;</c>, decoded and trimmed. Null when it has neither.</summary>
    public static string? TitleOf(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        foreach (Match tag in MetaTag().Matches(html))
        {
            var attributes = HtmlAttributes.Of(tag.Value);
            var key = attributes.GetValueOrDefault("property") ?? attributes.GetValueOrDefault("name");

            if (string.Equals(key, "og:title", StringComparison.OrdinalIgnoreCase)
                && Clean(attributes.GetValueOrDefault("content")) is { } shared)
            {
                return shared;
            }
        }

        return TitleElement().Match(html) is { Success: true } title ? Clean(StripTags(title.Groups["text"].Value)) : null;
    }

    /// <summary>
    /// The link the second step hops to: an anchor to an Eventbrite host
    /// (<see cref="EventPageEntries.IsEventbrite"/>) or else
    /// the first whose text says <i>register</i>, <i>registreer</i> or
    /// <i>tickets</i>. Eventbrite wins wherever it is on the page, because a
    /// listing there carries the structured data the hop is for, and a
    /// "Tickets" menu item on the organiser's own site usually does not.
    /// Resolved against the page and held to the fetcher's rule; a link back
    /// to the page itself — an in-page "#tickets" — is not a hop.
    /// <para>
    /// Only links the page shows count: comments, scripts and styles are read
    /// past the way <see cref="TextOf"/> reads past them. A page that keeps
    /// last year's listing commented out would otherwise hop to it, find its
    /// edition passed, and never read this year's date in its own text.
    /// </para>
    /// </summary>
    public static Uri? RegistrationLinkIn(string html, Uri page)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(page);

        Uri? worded = null;
        var pageId = EventPageEntries.PageId(page);

        foreach (Match anchor in Anchor().Matches(Hidden().Replace(html, " ")))
        {
            var attributes = HtmlAttributes.Of(anchor.Groups["open"].Value);

            if (!attributes.TryGetValue("href", out var href) || string.IsNullOrWhiteSpace(href)) continue;
            if (!Uri.TryCreate(page, WebUtility.HtmlDecode(href).Trim(), out var url) || !SourceTargets.IsFetchable(url)) continue;
            if (EventPageEntries.PageId(url) == pageId) continue;

            if (EventPageEntries.IsEventbrite(url)) return url;

            if (worded is null)
            {
                var text = WebUtility.HtmlDecode(StripTags(anchor.Groups["inner"].Value));

                if (RegistrationWords.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))) worded = url;
            }
        }

        return worded;
    }

    /// <summary>
    /// The text the third step looks for a date in: what the page shows —
    /// scripts, styles and comments gone, tags stripped, entities decoded,
    /// whitespace collapsed — with every <c>alt</c> and <c>title</c> attribute
    /// read where its tag stands. A page that draws its dates as an image says
    /// them only there; Techorama's do.
    /// </summary>
    public static string TextOf(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var visible = Hidden().Replace(html, " ");
        var text = AnyTag().Replace(visible, tag => SpokenBy(tag.Value));

        return Whitespace().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    /// <summary>A tag's own words, for the text it stands in: its <c>alt</c>
    /// and its <c>title</c>, spaced off from whatever is either side.</summary>
    private static string SpokenBy(string tag)
    {
        var attributes = HtmlAttributes.Of(tag);
        var spoken = new StringBuilder(" ");

        foreach (var name in SpokenAttributes)
        {
            if (attributes.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)) spoken.Append(value).Append(' ');
        }

        return spoken.ToString();
    }

    private static string StripTags(string html) => AnyTag().Replace(html, " ");

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var cleaned = Whitespace().Replace(WebUtility.HtmlDecode(text), " ").Trim();

        return cleaned.Length == 0 ? null : cleaned;
    }

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex(@"<title\b[^>]*>(?<text>.*?)</title\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleElement();

    [GeneratedRegex(@"(?<open><a\b[^>]*>)(?<inner>.*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Anchor();

    /// <summary>What a page has that nobody reads: comments, scripts and
    /// styles, with what is inside them.</summary>
    [GeneratedRegex(@"<!--.*?-->|<script\b.*?</script\s*>|<style\b.*?</style\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Hidden();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
