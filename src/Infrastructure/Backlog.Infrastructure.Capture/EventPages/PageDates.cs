using System.Globalization;
using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Capture.EventPages;

/// <summary>An edition's days as a page's text wrote them: one day, or a run
/// of days. No time — the text never says one the reader can trust.</summary>
internal sealed record PageDateSpan(DateOnly Start, DateOnly End);

/// <summary>
/// The third step of reading an event page: dates in its text, when neither
/// the page nor its registration link carried structured data.
/// <para>
/// Three shapes, months in English or Dutch, in full or abbreviated, in any
/// case — the pages watched first are Dutch and Belgian, and their dates are
/// written both ways: <c>27 November 2026</c>, one day; <c>26 - 28 OCT 2026</c>,
/// a run inside one month; <c>30 sep - 2 okt 2026</c>, a run across two. Each
/// shape is read month first too, the way English pages write it —
/// <c>November 27, 2026</c>, <c>Oct 26-28, 2026</c>, <c>Oct 26 - Nov 2, 2026</c>
/// — and a day may carry its English ordinal, <c>27th November 2026</c> or
/// <c>November 27th, 2026</c>. The dash may be a hyphen, an en dash or an em
/// dash, with or without spaces. A year is required: "May 12-13" on a page
/// that has carried the same text for three years says nothing about which.
/// </para>
/// <para>
/// A day is never read out of the tail of another number — the minutes of
/// "18:30", the cents of a price, the second half of "1.27" or "11/27" — so
/// "Inloop 18:30 - 27 november 2026" is the 27th, not a run from the 30th.
/// </para>
/// </summary>
internal static partial class PageDates
{
    /// <summary>Every date the text holds, in the order it holds them. A match
    /// that is not a real day — 31 June — is read past. A run that ends before
    /// it starts is read as its end day: the run was a misreading, the day
    /// after the dash is still a date the page wrote.</summary>
    public static IReadOnlyList<PageDateSpan> In(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var spans = new List<PageDateSpan>();

        foreach (Match match in DatePattern().Matches(text))
        {
            if (SpanOf(match) is { } span) spans.Add(span);
        }

        return spans;
    }

    /// <summary>A month's number from its name in either language. The first
    /// three letters tell all twelve apart in both, <c>maart</c> included.</summary>
    internal static int? MonthOf(string name) =>
        name.Length < 3
            ? null
            : name[..3].ToLowerInvariant() switch
            {
                "jan" => 1,
                "feb" => 2,
                "mar" or "maa" => 3,
                "apr" => 4,
                "may" or "mei" => 5,
                "jun" => 6,
                "jul" => 7,
                "aug" => 8,
                "sep" => 9,
                "oct" or "okt" => 10,
                "nov" => 11,
                "dec" => 12,
                _ => null
            };

    /// <summary>
    /// Both orders share the group names: day first always has the end month
    /// and may have a start month, month first always has the start month and
    /// may have an end month. Whichever is missing is the other.
    /// </summary>
    private static PageDateSpan? SpanOf(Match match)
    {
        var startName = match.Groups["startMonth"];
        var endName = match.Groups["endMonth"];
        var startMonth = MonthOf(startName.Success ? startName.Value : endName.Value);
        var endMonth = MonthOf(endName.Success ? endName.Value : startName.Value);

        if (startMonth is null || endMonth is null) return null;

        var year = Number(match.Groups["year"]);
        var startDay = Number(match.Groups["startDay"]);

        if (!match.Groups["endDay"].Success)
        {
            return DateOf(year, startMonth.Value, startDay) is { } day ? new PageDateSpan(day, day) : null;
        }

        // "30 dec - 2 jan 2027" starts the year before the one it names.
        var startYear = startMonth > endMonth ? year - 1 : year;
        var start = DateOf(startYear, startMonth.Value, startDay);
        var end = DateOf(year, endMonth.Value, Number(match.Groups["endDay"]));

        if (start is { } s && end is { } e && s < e) return new PageDateSpan(s, e);

        return end is { } only ? new PageDateSpan(only, only) : null;
    }

    private static int Number(Group group) =>
        int.Parse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);

    private static DateOnly? DateOf(int year, int month, int day) =>
        year >= 1 && day >= 1 && day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;

    /// <summary>
    /// Day first, or month first. Day first: a day, an optional start month
    /// and a dash with an end day, an optional "of", the month, a year. Month
    /// first: the month, a day, an optional dash with an optional end month and
    /// an end day, a comma or a space, a year. A start month is only read with
    /// a dash after it, so "30 sep okt 2026" is no date. Longest month names
    /// first, and none followed by a letter, so <c>mar</c> is not read out of
    /// "market" nor <c>jun</c> out of "juni"; a month-first month is not
    /// preceded by a letter either. A day is one or two ASCII digits, with no
    /// digit after it, and not preceded by a letter, a digit, or the
    /// punctuation that joins one number to another — <c>:</c>, <c>.</c>,
    /// <c>/</c> — or by a currency sign. ASCII digits only, for the invariant
    /// parse after it.
    /// </summary>
    [GeneratedRegex(
        DayFirst + "|" + MonthFirst,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    private const string DayFirst =
        @"(?<![\p{L}0-9:./\p{Sc}])(?<startDay>" + Day + ")" + Ordinal + @"\s*"
        + @"(?:(?:(?<startMonth>" + Months + @")\.?\s*)?" + Dash + @"\s*(?<endDay>" + Day + ")" + Ordinal + @"\s*)?"
        + @"(?:of\s+)?(?<endMonth>" + Months + @")\.?,?\s*" + Year;

    private const string MonthFirst =
        @"(?<![\p{L}0-9])(?<startMonth>" + Months + @")\.?\s*(?<startDay>" + Day + ")" + Ordinal
        + @"(?:\s*" + Dash + @"\s*(?:(?<endMonth>" + Months + @")\.?\s*)?(?<endDay>" + Day + ")" + Ordinal + ")?"
        + @"(?:\s*,\s*|\s+)" + Year;

    private const string Day = "[0-9]{1,2}(?![0-9])";

    private const string Ordinal = "(?:st|nd|rd|th)?";

    private const string Dash = @"[-–—]";

    private const string Year = "(?<year>[0-9]{4})(?![0-9])";

    private const string Months =
        "(?:januari|january|jan|februari|february|feb|maart|march|mar|april|apr|mei|may|juni|june|jun|juli|july|jul|augustus|august|aug|september|sept|sep|oktober|october|okt|oct|november|nov|december|dec)(?!\\p{L})";
}
