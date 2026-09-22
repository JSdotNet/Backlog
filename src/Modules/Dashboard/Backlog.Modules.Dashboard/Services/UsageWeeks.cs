using System.Globalization;
using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// How the sessions part cuts a week: seven days from one instant to the next, anchored
/// on the moment the assistant's weekly allowance resets rather than on a calendar
/// Monday.
/// </summary>
/// <remarks>
/// <para>
/// A second week beside <see cref="WeekBuckets"/>' ISO one, and deliberately not a
/// replacement for it. The productivity parts count pull requests into calendar weeks
/// because that is the week a team talks in; the sessions part counts agent-hours into
/// the week an allowance is spent in, because that is the week the figures are bounded
/// by. Two questions, two cuts, one bucket record between them so the charts draw both
/// the same way.
/// </para>
/// <para>
/// The anchor is any reset instant: weeks are the seven-day periods that instant falls on
/// the edge of, so a reset recorded in August anchors this week as well as that one. The
/// label is the week's first day on the local clock — <c>24 Aug</c> — because the
/// person reads the reset off their own clock and that is the day they will look for.
/// The fallback, when no reset is known, is Monday midnight on the local clock, labelled
/// by ISO week number so the columns say what they are.
/// </para>
/// </remarks>
internal sealed class UsageWeeks
{
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    private readonly DateTimeOffset _anchor;
    private readonly TimeZoneInfo _zone;

    private UsageWeeks(DateTimeOffset anchor, TimeZoneInfo zone, WeekSource source)
    {
        _anchor = anchor;
        _zone = zone;
        Source = source;
    }

    /// <summary>Where the anchor came from — the sentence the part says beside the
    /// columns.</summary>
    public WeekSource Source { get; }

    /// <summary>The reset as a person would say it, on the local clock — "Monday
    /// 14:00" — or null for the calendar fallback.</summary>
    public string? ResetDescription
    {
        get
        {
            if (Source == WeekSource.Calendar) return null;

            var local = TimeZoneInfo.ConvertTime(_anchor, _zone);

            return $"{local.DayOfWeek} {local:HH:mm}";
        }
    }

    /// <summary>Weeks anchored on a known reset instant.</summary>
    public static UsageWeeks AnchoredOn(DateTimeOffset reset, TimeZoneInfo zone, WeekSource source) =>
        new(reset, zone, source);

    /// <summary>
    /// The fallback: weeks from Monday midnight on the local clock. Anchored on the
    /// Monday of the week <paramref name="now"/> falls in, and the anchor is only an
    /// anchor — any Monday midnight would cut the same weeks.
    /// </summary>
    public static UsageWeeks Calendar(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var monday = date.AddDays(-(((int)date.DayOfWeek + 6) % 7)).ToDateTime(TimeOnly.MinValue);

        return new UsageWeeks(new DateTimeOffset(monday, zone.GetUtcOffset(monday)), zone, WeekSource.Calendar);
    }

    /// <summary>The bucket an instant belongs to.</summary>
    public WeekBucket Of(DateTimeOffset instant)
    {
        var weeks = (long)Math.Floor((instant - _anchor) / Week);

        return BucketAt(_anchor + weeks * Week);
    }

    /// <summary>The key of the bucket an instant belongs to — what the counting
    /// helpers take.</summary>
    public string KeyOf(DateTimeOffset instant) => Of(instant).Key;

    /// <summary>The buckets covering a window, oldest first. Bounded on
    /// <see cref="WeekBuckets.Buckets"/>' rule: an absurd window gets an empty axis.</summary>
    public IReadOnlyList<WeekBucket> Buckets(DateTimeOffset from, DateTimeOffset to)
    {
        var buckets = new List<WeekBucket>();
        var cursor = Of(from).Start;
        var last = Of(to).Start;

        for (var guard = 0; cursor <= last && guard < 520; guard++)
        {
            buckets.Add(BucketAt(cursor));
            cursor += Week;
        }

        return buckets;
    }

    private WeekBucket BucketAt(DateTimeOffset start)
    {
        var local = TimeZoneInfo.ConvertTime(start, _zone);

        var label = Source == WeekSource.Calendar
            ? "W" + ISOWeek.GetWeekOfYear(local.DateTime).ToString("00", CultureInfo.InvariantCulture)
            : local.ToString("d MMM", CultureInfo.InvariantCulture);

        return new WeekBucket(start.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), label, start);
    }
}
