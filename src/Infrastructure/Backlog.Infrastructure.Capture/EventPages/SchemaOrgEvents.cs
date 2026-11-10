using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using Backlog.Infrastructure.Capture.Feeds;
using Backlog.Modules.Capture.Ports;

namespace Backlog.Infrastructure.Capture.EventPages;

/// <summary>One schema.org event as a page's structured data states it, with
/// a start the reader could read — an event without one is not kept.</summary>
/// <param name="Name">Its <c>name</c>, whitespace collapsed, or null.</param>
/// <param name="Start">Its <c>startDate</c>.</param>
/// <param name="End">Its <c>endDate</c>, or null when absent or unreadable.</param>
/// <param name="Url">Its <c>url</c> as written, or null.</param>
/// <param name="Place">Where, read off its <c>location</c>, or null.</param>
internal sealed record SchemaEvent(string? Name, CaptureEventDate Start, CaptureEventDate? End, string? Url, string? Place);

/// <summary>
/// The first step of reading an event page: the schema.org events in its
/// <c>&lt;script type="application/ld+json"&gt;</c> blocks.
/// <para>
/// Structured data is the one place a page says its dates for a machine, so it
/// is believed over anything in the page text. It is still a page's markup and
/// as untrusted as the rest: a block that is not JSON is skipped rather than
/// thrown on, and an event whose start cannot be read is not an event the
/// reader can be told about. Every block is walked — objects, arrays,
/// <c>@graph</c>, an <c>ItemList</c>'s items — because where a site's
/// generator nests its events is the generator's choice.
/// </para>
/// </summary>
internal static partial class SchemaOrgEvents
{
    /// <summary>Lenient where JSON-LD written by hand tends to be: a trailing
    /// comma or a comment is not a reason to lose the event.</summary>
    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Every event in the page's structured data, in the order the
    /// page wrote them.</summary>
    public static IReadOnlyList<SchemaEvent> In(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        var found = new List<SchemaEvent>();

        foreach (Match script in ScriptBlock().Matches(html))
        {
            var attributes = HtmlAttributes.Of(script.Groups["open"].Value);

            if (!attributes.TryGetValue("type", out var type)
                || !string.Equals(type.Trim(), "application/ld+json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(script.Groups["body"].Value.Trim(), Lenient);
            }
            catch (JsonException)
            {
                // One broken block does not hide the next one's event.
                continue;
            }

            using (document)
            {
                Walk(document.RootElement, found);
            }
        }

        return found;
    }

    /// <summary>Whether a <c>@type</c> names an event: <c>Event</c> itself or
    /// any of its subtypes, which schema.org names <c>…Event</c> —
    /// <c>SocialEvent</c>, <c>BusinessEvent</c>, <c>EducationEvent</c>. A
    /// prefixed spelling (<c>schema:Event</c>) ends the same way.</summary>
    internal static bool IsEventType(string? type) =>
        type is not null && type.EndsWith("Event", StringComparison.Ordinal);

    /// <summary>
    /// A schema.org date, keeping exactly what it says: <c>2026-11-27</c> is a
    /// date; <c>2026-11-27T16:30</c> adds the time; <c>2026-11-27T16:30:00+01:00</c>
    /// adds the offset, and <c>Z</c> is an offset of zero. Null for anything
    /// else, so a page's free-text "TBA" is no date rather than a wrong one.
    /// </summary>
    internal static CaptureEventDate? ParseDate(string? text)
    {
        if (text is null) return null;

        var match = IsoDate().Match(text);
        if (!match.Success) return null;

        var year = Number(match, "year");
        var month = Number(match, "month");
        var day = Number(match, "day");

        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return null;

        var date = new DateOnly(year, month, day);

        if (!match.Groups["hour"].Success) return new CaptureEventDate(date);

        var hour = Number(match, "hour");
        var minute = Number(match, "minute");
        var second = match.Groups["second"].Success ? Number(match, "second") : 0;

        if (hour > 23 || minute > 59 || second > 59) return null;

        var time = new TimeOnly(hour, minute, second);

        return new CaptureEventDate(date, time, OffsetOf(match.Groups["zone"]));
    }

    /// <summary>
    /// Where an event is, in the fewest words that still say it: a town before
    /// a venue — a reader deciding whether to go asks the town first — so a
    /// <c>Place</c> gives its <c>address.addressLocality</c>, else its
    /// <c>name</c>, else an <c>address</c> written as a string. A
    /// <c>VirtualLocation</c> is <c>Online</c>; a location written as a plain
    /// string is kept as written; of several, the first that says something.
    /// </summary>
    internal static string? PlaceOf(JsonElement location)
    {
        switch (location.ValueKind)
        {
            case JsonValueKind.String:
                return Collapse(location.GetString());

            case JsonValueKind.Array:
                foreach (var candidate in location.EnumerateArray())
                {
                    if (PlaceOf(candidate) is { } place) return place;
                }

                return null;

            case JsonValueKind.Object:
                if (TypesOf(location).Any(type => type.EndsWith("VirtualLocation", StringComparison.Ordinal))) return "Online";

                var address = location.TryGetProperty("address", out var a) ? a : default;
                var locality = address.ValueKind == JsonValueKind.Object ? Text(address, "addressLocality") : null;

                return locality
                    ?? Text(location, "addressLocality")
                    ?? Text(location, "name")
                    ?? (address.ValueKind == JsonValueKind.String ? Collapse(address.GetString()) : null);

            default:
                return null;
        }
    }

    private static void Walk(JsonElement element, List<SchemaEvent> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Walk(item, found);
                break;

            case JsonValueKind.Object when TypesOf(element).Any(IsEventType):
                // Not walked further: what an event holds — its location, its
                // offers, a sub-event — is part of that event, not another.
                if (Read(element) is { } read) found.Add(read);
                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject()) Walk(property.Value, found);
                break;
        }
    }

    private static SchemaEvent? Read(JsonElement element)
    {
        if (ParseDate(Text(element, "startDate")) is not { } start) return null;

        return new SchemaEvent(
            Text(element, "name"),
            start,
            ParseDate(Text(element, "endDate")),
            Text(element, "url"),
            element.TryGetProperty("location", out var location) ? PlaceOf(location) : null);
    }

    /// <summary>The <c>@type</c>, written as one string or as several.</summary>
    private static IEnumerable<string> TypesOf(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var type)) yield break;

        if (type.ValueKind == JsonValueKind.String)
        {
            yield return type.GetString()!;
        }
        else if (type.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in type.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) yield return item.GetString()!;
            }
        }
    }

    /// <summary>A property that is a string — or the first string of an
    /// array of them — collapsed, or null when it is anything else or blank.</summary>
    private static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => Collapse(value.GetString()),
            JsonValueKind.Array => value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => Collapse(item.GetString()))
                .FirstOrDefault(text => text is not null),
            _ => null
        };
    }

    private static string? Collapse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        return Whitespace().Replace(text.Trim(), " ");
    }

    private static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);

    private static TimeSpan? OffsetOf(Group zone)
    {
        if (!zone.Success) return null;
        if (zone.Value is "Z" or "z") return TimeSpan.Zero;

        var digits = zone.Value[1..].Replace(":", string.Empty, StringComparison.Ordinal);
        var hours = int.Parse(digits.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture);
        var minutes = int.Parse(digits.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture);

        if (hours > 14 || minutes > 59) return null;

        var offset = new TimeSpan(hours, minutes, 0);

        return zone.Value[0] == '-' ? -offset : offset;
    }

    [GeneratedRegex(@"(?<open><script\b[^>]*>)(?<body>.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptBlock();

    /// <summary>ASCII digits only: <c>\d</c> would take any script's digits,
    /// which the invariant parse after it would then refuse.</summary>
    [GeneratedRegex(@"^\s*(?<year>[0-9]{4})-(?<month>[0-9]{2})-(?<day>[0-9]{2})(?:[T ](?<hour>[0-9]{2}):(?<minute>[0-9]{2})(?::(?<second>[0-9]{2})(?:\.[0-9]+)?)?(?<zone>[Zz]|[+-][0-9]{2}:?[0-9]{2})?)?\s*$")]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
