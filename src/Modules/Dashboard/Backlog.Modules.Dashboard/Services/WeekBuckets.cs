using System.Globalization;
using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// One weekly bucket: what identifies it, and what a chart axis calls it.
/// </summary>
/// <remarks>
/// Two strings rather than one, because the short form is not unique. <c>W34</c> is
/// what an axis should say — a year on every label of a twelve-week chart is noise —
/// but week 34 comes round every year, so matching on it would count a merge from
/// last August into this one. The key carries the ISO year and the label does not.
/// </remarks>
internal sealed record WeekBucket(string Key, string Label);

/// <summary>
/// The weekly buckets every productivity series is drawn on, and how a timestamp
/// finds its bucket.
/// </summary>
/// <remarks>
/// <para>
/// ISO weeks. ISO rather than a plain seven-day count back from today because a
/// bucket has to mean the same thing on two consecutive days: a rolling window would
/// move every reading into a different bucket overnight and no two screenshots would
/// ever agree.
/// </para>
/// <para>
/// Every series in a set is built over the same bucket list even where a repository
/// reported nothing in a week, so the charts can be read across each other. A
/// missing week and a zero week look identical here on purpose — for a count of
/// merged pull requests they are the same fact.
/// </para>
/// </remarks>
internal static class WeekBuckets
{
    /// <summary>
    /// The bucket an instant belongs to.
    /// </summary>
    /// <remarks>
    /// The ISO year rather than the calendar year. They disagree by a few days at
    /// each turn — 1 January 2027 falls in ISO week 53 of 2026 — and using the
    /// calendar year would split one week across two keys.
    /// </remarks>
    internal static WeekBucket Of(DateTimeOffset instant)
    {
        var date = instant.UtcDateTime;
        var week = ISOWeek.GetWeekOfYear(date);
        var year = ISOWeek.GetYear(date);

        return new WeekBucket(
            year.ToString("0000", CultureInfo.InvariantCulture)
                + "-W" + week.ToString("00", CultureInfo.InvariantCulture),
            "W" + week.ToString("00", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The buckets covering a window, oldest first, one per ISO week. Anchored to
    /// each week's Monday so the last bucket is the week <paramref name="to"/> falls
    /// in rather than a partial trailing seven days.
    /// </summary>
    internal static IReadOnlyList<WeekBucket> Buckets(DateTimeOffset from, DateTimeOffset to)
    {
        var buckets = new List<WeekBucket>();
        var cursor = StartOfWeek(from);
        var last = StartOfWeek(to);

        // Bounded rather than trusted to terminate: a caller handing over a reversed
        // or absurd window should get an empty axis, not a hang.
        for (var guard = 0; cursor <= last && guard < 520; guard++)
        {
            buckets.Add(Of(cursor));
            cursor = cursor.AddDays(7);
        }

        return buckets;
    }

    /// <summary>
    /// Counts items into the given buckets, keyed by the instant
    /// <paramref name="instantOf"/> reads off each one. Anything falling outside the
    /// buckets is dropped rather than folded into the nearest — a window's edge is
    /// the window's edge, and matching on the key rather than the label is what makes
    /// that true a year later as well as a month.
    /// </summary>
    internal static IReadOnlyList<InsightPoint> Count<T>(
        IReadOnlyList<WeekBucket> buckets,
        IEnumerable<T> items,
        Func<T, DateTimeOffset> instantOf)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(instantOf);

        var counts = buckets.ToDictionary(bucket => bucket.Key, _ => 0, StringComparer.Ordinal);

        foreach (var item in items)
        {
            var key = Of(instantOf(item)).Key;
            if (counts.ContainsKey(key)) counts[key]++;
        }

        return [.. buckets.Select(bucket => new InsightPoint(bucket.Label, counts[bucket.Key]))];
    }

    /// <summary>
    /// Reduces items into the given buckets with an arbitrary aggregate — a rate, a
    /// mean, anything that is not a count. The selector receives every item that fell
    /// in the bucket, including none of them.
    /// </summary>
    internal static IReadOnlyList<InsightPoint> Reduce<T>(
        IReadOnlyList<WeekBucket> buckets,
        IEnumerable<T> items,
        Func<T, DateTimeOffset> instantOf,
        Func<IReadOnlyList<T>, decimal> aggregate)
    {
        ArgumentNullException.ThrowIfNull(buckets);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(instantOf);
        ArgumentNullException.ThrowIfNull(aggregate);

        var grouped = items
            .GroupBy(item => Of(instantOf(item)).Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<T>)[.. group], StringComparer.Ordinal);

        return
        [
            .. buckets.Select(bucket => new InsightPoint(
                bucket.Label,
                aggregate(grouped.TryGetValue(bucket.Key, out var inBucket) ? inBucket : [])))
        ];
    }

    /// <summary>The Monday of the ISO week an instant falls in, at midnight.</summary>
    private static DateTimeOffset StartOfWeek(DateTimeOffset instant)
    {
        var date = instant.UtcDateTime.Date;
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return new DateTimeOffset(date.AddDays(-offset), TimeSpan.Zero);
    }
}
