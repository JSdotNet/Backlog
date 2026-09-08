using System.Globalization;

namespace Backlog.Modules.Dashboard.UI.Parts;

/// <summary>
/// Formatting this surface does that the metrics library does not.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in <c>MetricFormat</c> for the reason that class gives about
/// itself: it formats measures — a count, a share, an amount of money — and a duration
/// a person reads is a rounding policy rather than a format. "Nineteen and a bit hours"
/// is one thing to a reviewer waiting on a pull request and another to a library, and
/// the choice of where to stop is this dashboard's.
/// </para>
/// <para>
/// It was the throughput part's private method until the sessions part needed the same
/// answer. Two copies of a rounding policy is how two figures on one screen come to
/// disagree about what a day is.
/// </para>
/// </remarks>
public static class DashboardFormat
{
    /// <summary>
    /// A duration a person reads rather than a decimal number of hours. Under an hour
    /// it is minutes, under a day hours, above that days — nobody needs to know a
    /// review took 19.4 hours. An em dash for a duration there is none of, which is a
    /// different statement from a zero.
    /// </summary>
    public static string Duration(TimeSpan? span)
    {
        if (span is not { } value) return "—";

        return value.TotalHours switch
        {
            // Anything at all rounds up to a minute rather than down to nothing: a
            // session that ran is not a session that took no time.
            < 1 => $"{Math.Max(1, (int)value.TotalMinutes)}m",
            < 24 => $"{(int)value.TotalHours}h",

            // Invariant, for the reason MetricFormat states about every figure it
            // writes: on a machine with a comma decimal separator this would otherwise
            // read "1,5d", and every assertion that spells a duration out would fail
            // there and nowhere else.
            _ => value.TotalDays.ToString("0.#", CultureInfo.InvariantCulture) + "d"
        };
    }

    /// <summary>
    /// An instant a person reads: the day, the month and the time, in UTC.
    /// <para>
    /// Invariant and UTC rather than local and cultured, for the same reason every
    /// figure on this surface is: the timestamps come off files written by two agents
    /// and are already carried as instants, and rendering them through whatever
    /// locale the machine has would make one assertion pass on a desk and fail in CI.
    /// No year, because a dashboard shows at most a quarter and the year would be the
    /// same on every row.
    /// </para>
    /// An em dash where there is no instant, which is a different statement from a zero.
    /// </summary>
    public static string Moment(DateTimeOffset? moment) =>
        moment is { } value
            ? value.UtcDateTime.ToString("dd MMM HH:mm", CultureInfo.InvariantCulture)
            : "—";

    /// <summary>
    /// A day, for naming a stretch of history in prose. No time of day, unlike
    /// <see cref="Moment"/>: the block a target came from is four weeks long, and a
    /// minute-precise edge on it would be a precision nobody asked for.
    /// <para>
    /// The year is here where <see cref="Moment"/> leaves it out, and for the same
    /// reason it leaves it out — that one labels something inside the window on
    /// screen, this one names a block that may be five months behind it.
    /// </para>
    /// </summary>
    public static string Day(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// A derived target as a whole number of items. Full marks is arithmetic over a
    /// rate and lands on fractions — 380.0, or 356.25 — and a card that told a reader
    /// to merge a quarter of a pull request would be inviting the wrong argument.
    /// Rounded rather than truncated, and invariant, for the reason above.
    /// </summary>
    public static string Whole(decimal value) =>
        Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
}
