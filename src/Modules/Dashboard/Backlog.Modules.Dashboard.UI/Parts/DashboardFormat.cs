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

        // Zero is a reading, and it is the one reading the round-up below must not
        // reach. That rule is about a session that ran — something happened, and a
        // figure of "0m" would deny it — but an hour of a grid nobody worked and a
        // waiting column for an assistant that cannot record one are both genuinely
        // nothing, and rounding those up to a minute would invent the only activity
        // they have. An em dash is still reserved for having no figure at all.
        if (value == TimeSpan.Zero) return "0m";

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

    /// <summary>
    /// A row heading for the activity grid: the weekday and the day of the month, as
    /// <c>Wed 02</c>.
    /// <para>
    /// Both halves, because neither alone is enough here. The weekday is what the
    /// question is about — "when do I get agents running" is answered in Tuesdays, not in
    /// dates — while the date is what stops seven rows of a fortnight-old screenshot
    /// reading as this week. The grid is only ever seven rows, so a month never repeats
    /// inside it and naming one would be noise.
    /// </para>
    /// <para>
    /// Invariant, for the reason <see cref="Moment"/> and <see cref="Duration"/> are: a
    /// heading that moved with the machine's language would make a screenshot
    /// untranslatable back to the data behind it, and every assertion that spells a day
    /// out would fail on one machine and nowhere else.
    /// </para>
    /// </summary>
    public static string Day(DateOnly day) => day.ToString("ddd dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// A column heading for the activity grid: the hour, zero-padded, as <c>07</c>.
    /// <para>
    /// Padded because the grid matches its cells on this string, so <c>7</c> and
    /// <c>07</c> would be two different columns; and because twenty-four headings of even
    /// width are what let the columns stay narrow enough to fit.
    /// </para>
    /// </summary>
    public static string Hour(int hour) => hour.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// The hour a column heading names, or null when the string is not one this class
    /// wrote.
    /// <para>
    /// The counterpart to <see cref="Hour"/> and deliberately beside it. A grid that
    /// matches its cells on a label has to read that label back to say anything about the
    /// hour behind it, and a second place that knew the format would be a second place to
    /// change when the format does.
    /// </para>
    /// </summary>
    public static int? HourOf(string? label) =>
        int.TryParse(label, NumberStyles.None, CultureInfo.InvariantCulture, out var hour) && hour is >= 0 and <= 23
            ? hour
            : null;
}
