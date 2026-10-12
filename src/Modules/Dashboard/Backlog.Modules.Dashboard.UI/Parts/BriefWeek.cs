using System.Globalization;
using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.UI.Parts;

/// <summary>
/// The week a weekly brief covers: the last completed Monday-to-Sunday week, or one
/// before it.
/// </summary>
/// <remarks>
/// <para>
/// An ISO week on the UTC calendar, because every weekly series the tabs read is cut
/// that way and the brief reads those series rather than cutting its own. The week
/// today falls in is still running, so it is never the one reported.
/// </para>
/// <para>
/// The brief reads the twelve-week series, which reach back to the thirteenth week
/// counting this one. Eight bars for the reported week and one more for its comparison
/// fit <see cref="MaxWeeksBack"/> weeks back and no further, which is where the link to
/// the week before stops.
/// </para>
/// </remarks>
/// <param name="WeeksBack">How many weeks before the last completed one: zero for it.</param>
/// <param name="Monday">The week's first day.</param>
/// <param name="Number">The ISO week number.</param>
public sealed record BriefWeek(int WeeksBack, DateOnly Monday, int Number)
{
    /// <summary>How many bars a block's chart draws, the reported week the last.</summary>
    public const int Bars = 8;

    /// <summary>How far back the brief goes: thirteen weekly buckets hold the running
    /// week, the reported one, its seven before it and the one it is compared with.</summary>
    public const int MaxWeeksBack = 4;

    /// <summary>The week's last day.</summary>
    public DateOnly Sunday => Monday.AddDays(6);

    /// <summary>The week as the weekly series label it: <c>W41</c>.</summary>
    public string Label => "W" + Number.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// The reported week's value off a series that has no point for a week it has no
    /// reading for — a median over nothing — found by its label, or null when the week
    /// has none. <see cref="Read"/> reads by position and is only for series that keep
    /// every week on the axis.
    /// </summary>
    public decimal? ValueAt(IReadOnlyList<InsightPoint> series)
    {
        ArgumentNullException.ThrowIfNull(series);

        return series.FirstOrDefault(point => string.Equals(point.Label, Label, StringComparison.Ordinal))?.Value;
    }

    /// <summary>Whether a link to the week before this one has anywhere to go.</summary>
    public bool HasEarlier => WeeksBack < MaxWeeksBack;

    /// <summary>The ISO number of the week after this one, for the heading that closes
    /// the brief.</summary>
    public int NextNumber => ISOWeek.GetWeekOfYear(Sunday.AddDays(1).ToDateTime(TimeOnly.MinValue));

    /// <summary>The last completed week as of <paramref name="now"/>, or
    /// <paramref name="weeksBack"/> weeks before it, clamped to what the brief can
    /// draw.</summary>
    public static BriefWeek Of(DateTimeOffset now, int weeksBack)
    {
        var back = Math.Clamp(weeksBack, 0, MaxWeeksBack);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var monday = thisMonday.AddDays(-7 * (back + 1));

        return new BriefWeek(back, monday, ISOWeek.GetWeekOfYear(monday.ToDateTime(TimeOnly.MinValue)));
    }

    /// <summary>
    /// The reported week's reading off one weekly series whose last point is the week
    /// today falls in: its value, the week before's, and the bars up to it. Null when the
    /// series does not reach back that far.
    /// </summary>
    public BriefReading? Read(IReadOnlyList<InsightPoint> series)
    {
        ArgumentNullException.ThrowIfNull(series);

        var index = series.Count - 2 - WeeksBack;
        if (index < 0) return null;

        var first = Math.Max(0, index - Bars + 1);

        return new BriefReading(
            series[index].Value,
            index > 0 ? series[index - 1].Value : null,
            [.. series.Skip(first).Take(index - first + 1)]);
    }
}

/// <summary>One block's reading for the reported week.</summary>
/// <param name="Value">The reported week's figure.</param>
/// <param name="Previous">The week before's, or null when the series starts at the
/// reported week.</param>
/// <param name="Bars">Up to <see cref="BriefWeek.Bars"/> weeks, oldest first, the
/// reported week last.</param>
public sealed record BriefReading(decimal Value, decimal? Previous, IReadOnlyList<InsightPoint> Bars);
