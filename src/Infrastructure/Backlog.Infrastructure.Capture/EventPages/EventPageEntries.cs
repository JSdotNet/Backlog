using System.Globalization;
using System.Text.RegularExpressions;

using Backlog.Infrastructure.Capture.Feeds;
using Backlog.Modules.Capture.Ports;

namespace Backlog.Infrastructure.Capture.EventPages;

/// <summary>The page a person watches: its URL as the external id and the
/// facts write it, and its title, or null when it has none.</summary>
internal sealed record WatchedPage(string Url, string? Title)
{
    /// <summary>What an entry is called when the page is all there is to
    /// call it by.</summary>
    public string Name => Title ?? Url;
}

/// <summary>
/// Turns what the steps read into entries: one per upcoming edition, each
/// under an external id that stays the same across runs, with a body that
/// reads on its own and the edition's <see cref="CaptureEventFacts"/>.
/// <para>
/// The external id is the whole of "delivered once" (<c>CaptureIds</c> derives
/// the item's id from it), so it is built from what does not move: the
/// listing's own number when the edition came from Eventbrite, otherwise the
/// watched page, the year it starts and its place — the event's own URL after
/// them when an upcoming edition that starts earlier holds that id. Next year's
/// edition on the same page is a new item; a date that moves within the year is
/// not — the known limit the feature chapter records.
/// </para>
/// <para>
/// An edition whose end — or start, when it has no end — is before today is
/// skipped, because an organiser's page often still shows last year's, and the
/// read goes on to the next step.
/// </para>
/// </summary>
internal static partial class EventPageEntries
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The watched page's URL as an id: absolute, the host in lower
    /// case, no fragment, the path as written — a trailing slash kept, since
    /// <c>/events</c> and <c>/events/</c> can be two pages.</summary>
    public static string PageId(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        return url.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
    }

    /// <summary>
    /// Every structured-data event that has not passed, as its own entry, or
    /// null when every one has: a passed edition does not stop the read,
    /// because a page often still carries last edition's event, or links its
    /// listing, after its text names the next one.
    /// </summary>
    /// <param name="events">What the step found.</param>
    /// <param name="foundOn">The page the events were read from — the watched
    /// page, or the registration page one hop away.</param>
    /// <param name="link">The entry's link when an event names no fetchable
    /// URL of its own.</param>
    /// <param name="watched">The page the person watches.</param>
    /// <param name="today">The reader's today.</param>
    public static TargetReading? FromSchema(IReadOnlyList<SchemaEvent> events, Uri foundOn, string link, WatchedPage watched, DateOnly today)
    {
        var upcoming = events.Where(found => !HasPassed(found.Start, found.End, today)).ToList();
        var ids = EditionIds(upcoming, foundOn, watched);

        // One event carried twice, in two blocks or a graph, is one edition.
        var entries = upcoming
            .Select((found, index) => (Found: found, Id: ids[index]))
            .DistinctBy(pair => pair.Id, StringComparer.Ordinal)
            .Select(pair => Entry(
                pair.Id,
                pair.Found.Name ?? watched.Name,
                OwnUrl(pair.Found)?.AbsoluteUri ?? link,
                new CaptureEventFacts(pair.Found.Start, pair.Found.End, pair.Found.Place, watched.Url, watched.Title)))
            .ToList();

        return entries.Count == 0 ? null : new TargetReading(entries);
    }

    /// <summary>
    /// The first date in the page text that has not passed, as one entry —
    /// the first, because a page leads with the edition it is announcing and
    /// what follows is a programme or last year's recap — or null when every
    /// date has passed.
    /// </summary>
    public static TargetReading? FromText(IReadOnlyList<PageDateSpan> spans, WatchedPage watched, DateOnly today)
    {
        var upcoming = spans.FirstOrDefault(span => span.End >= today);

        if (upcoming is null) return null;

        var start = new CaptureEventDate(upcoming.Start);
        var end = upcoming.End == upcoming.Start ? null : new CaptureEventDate(upcoming.End);

        return new TargetReading(
        [
            Entry(
                EditionId(watched, upcoming.Start.Year, place: null),
                watched.Name,
                watched.Url,
                new CaptureEventFacts(start, end, Place: null, watched.Url, watched.Title))
        ]);
    }

    /// <summary>The last day of the latest edition a step read, or null when
    /// it read none.</summary>
    public static DateOnly? LastDayOf(IReadOnlyList<SchemaEvent> events) =>
        events.Count == 0 ? null : events.Max(found => (found.End ?? found.Start).Date);

    /// <inheritdoc cref="LastDayOf(IReadOnlyList{SchemaEvent})"/>
    public static DateOnly? LastDayOf(IReadOnlyList<PageDateSpan> spans) =>
        spans.Count == 0 ? null : spans.Max(span => span.End);

    /// <summary>The reading when every edition the steps read has passed: no
    /// entry, and the reason nothing arrived, naming the latest end — the
    /// edition the page still shows — and a registration page that could not
    /// be read, since the next edition may be behind it.</summary>
    public static TargetReading Passed(DateOnly ended, string? hopFailure = null) =>
        TargetReading.Remark(
            $"the edition it shows has passed (ended {Day(ended)})"
            + (hopFailure is null ? string.Empty : $", and {hopFailure}"));

    /// <summary>A page no step found a date on still arrives — with its link,
    /// so the person can look — under the page's own id, so it arrives once.
    /// When that page later shows a date it arrives again, as the edition.</summary>
    public static CapturedEntry Undated(WatchedPage watched) =>
        Entry(watched.Url, watched.Name, watched.Url, new CaptureEventFacts(Start: null, End: null, Place: null, watched.Url, watched.Title));

    /// <summary>Whether a URL is on Eventbrite — <c>eventbrite.</c> anywhere in
    /// its host, for every country's domain. The one rule for which link the
    /// second step hops to and which URL gives a listing id, so the two cannot
    /// come to disagree.</summary>
    internal static bool IsEventbrite(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        return url.Host.Contains("eventbrite.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The listing number in an Eventbrite URL — the digits that end
    /// its <c>/e/&lt;slug&gt;-&lt;digits&gt;</c> path — or null for a URL that
    /// is not an Eventbrite listing.</summary>
    internal static string? ListingIdOf(Uri? url)
    {
        if (url is null || !IsEventbrite(url)) return null;

        var match = EventbriteListing().Match(url.AbsolutePath);

        return match.Success ? match.Groups["id"].Value : null;
    }

    /// <summary>"When: 26 – 28 October 2026" — as short as the dates allow,
    /// with the time and its offset only when the source gave them.</summary>
    internal static string When(CaptureEventDate start, CaptureEventDate? end)
    {
        if (end is null || end == start || (end.Date == start.Date && end.Time is null)) return Point(start);

        if (start.Time is null && end.Time is null)
        {
            var s = start.Date;
            var e = end.Date;

            if (s.Year != e.Year) return $"{Day(s)} – {Day(e)}";
            if (s.Month != e.Month) return $"{s.ToString("d MMMM", Invariant)} – {Day(e)}";

            return $"{s.Day.ToString(Invariant)} – {Day(e)}";
        }

        if (end.Date == start.Date && start.Time is { } from && end.Time is { } to && start.Offset == end.Offset)
        {
            return $"{Day(start.Date)}, {Clock(from)} – {Clock(to)}{OffsetNote(start.Offset)}";
        }

        return $"{Point(start)} – {Point(end)}";
    }

    private static CapturedEntry Entry(string externalId, string title, string url, CaptureEventFacts facts)
    {
        var lines = new List<string>();

        if (facts.Start is { } start)
        {
            lines.Add($"When: {When(start, facts.End)}");
        }
        else
        {
            lines.Add("No date was found on the page.");
        }

        if (facts.Place is { } place) lines.Add($"Where: {place}");
        lines.Add($"Read from {Link(facts.PageTitle ?? facts.PageUrl!, facts.PageUrl!)}");

        return new CapturedEntry(
            externalId,
            title,
            url,
            string.Join("\n\n", lines),
            PublishedAt: null,
            new CaptureFacts(ContentKind: null, Tags: [], Person: null, List: null) { Event = facts });
    }

    /// <summary>
    /// Each upcoming event's external id, in the order given. The listing
    /// number when there is one; otherwise the page, the year and the place.
    /// Two upcoming events under that one id are told apart by their own URL:
    /// the one that starts first keeps the plain id, and a later one whose URL
    /// differs from it gets that URL appended.
    /// <para>
    /// The earliest keeps the plain id because it is the edition delivered
    /// under it before the URL counted: every upcoming edition got the plain
    /// id then, and the first of them was the one delivered. When it passes,
    /// the next takes over the plain id, which is already known, so nothing
    /// arrives twice: that next edition was delivered under its URL while the
    /// earlier one was still to come. Ranking by start rather than by place on
    /// the page keeps an id still when an organiser reorders the list.
    /// </para>
    /// </summary>
    private static string[] EditionIds(IReadOnlyList<SchemaEvent> events, Uri foundOn, WatchedPage watched)
    {
        var firstUrls = new Dictionary<string, string?>(StringComparer.Ordinal);
        var ids = new string[events.Count];

        // Stable, so editions that start alike keep the page's own order.
        var byStart = Enumerable.Range(0, events.Count).OrderBy(index => events[index].Start.Date).ThenBy(index => events[index].Start.Time ?? TimeOnly.MinValue);

        foreach (var index in byStart)
        {
            var found = events[index];
            var own = OwnUrl(found);

            if ((ListingIdOf(own) ?? ListingIdOf(foundOn)) is { } listing)
            {
                ids[index] = $"eventbrite:{listing}";
                continue;
            }

            var plain = EditionId(watched, found.Start.Date.Year, found.Place);
            var url = own is null ? null : PageId(own);

            if (!firstUrls.TryGetValue(plain, out var first))
            {
                firstUrls[plain] = url;
                ids[index] = plain;
            }
            else
            {
                ids[index] = url is null || string.Equals(url, first, StringComparison.Ordinal) ? plain : $"{plain}|{url}";
            }
        }

        return ids;
    }

    /// <summary>The event's own page, when it names one that can be fetched.</summary>
    private static Uri? OwnUrl(SchemaEvent found) =>
        found.Url is not null && Uri.TryCreate(found.Url, UriKind.Absolute, out var url) && SourceTargets.IsFetchable(url) ? url : null;

    /// <summary>The page, the year and the place — lower case and trimmed,
    /// empty when the page did not say — so a venue written twice two ways is
    /// one edition.</summary>
    private static string EditionId(WatchedPage watched, int year, string? place) =>
        $"{watched.Url}|{year.ToString(Invariant)}|{place?.Trim().ToLowerInvariant() ?? string.Empty}";

    private static bool HasPassed(CaptureEventDate start, CaptureEventDate? end, DateOnly today) =>
        (end ?? start).Date < today;

    private static string Point(CaptureEventDate date) =>
        Day(date.Date)
        + (date.Time is { } time ? $", {Clock(time)}" : string.Empty)
        + OffsetNote(date.Offset);

    private static string Day(DateOnly date) => date.ToString("d MMMM yyyy", Invariant);

    private static string Clock(TimeOnly time) => time.ToString("HH:mm", Invariant);

    private static string OffsetNote(TimeSpan? offset) =>
        offset is { } o
            ? $" ({(o < TimeSpan.Zero ? "-" : "+")}{o.Duration().ToString(@"hh\:mm", Invariant)})"
            : string.Empty;

    /// <summary>A Markdown link whose text cannot close it early and whose
    /// destination survives a parenthesis or a space.</summary>
    private static string Link(string text, string url)
    {
        var label = text.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
        var destination = url.IndexOfAny(['(', ')', ' ']) >= 0 ? $"<{url}>" : url;

        return $"[{label}]({destination})";
    }

    [GeneratedRegex(@"^/e/(?:[^/]*-)?(?<id>[0-9]+)/?$")]
    private static partial Regex EventbriteListing();
}
