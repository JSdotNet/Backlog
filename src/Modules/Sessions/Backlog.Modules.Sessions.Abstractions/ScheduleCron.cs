using System.Globalization;

namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// A schedule's five-field cron — minute, hour, day of month, month, day of week — read the
/// way the Claude desktop app's scheduled tasks write it, evaluated in this machine's time
/// zone. What <see cref="ScheduleReading"/> asks for a schedule's due time, to tell Missed
/// (local ADR 0051 §2).
/// <para>
/// Each field takes <c>*</c>, a number, a range <c>a-b</c>, a step <c>*/n</c> or
/// <c>a-b/n</c>, and comma lists of those; months and weekdays take their three-letter
/// English names too, weekday 7 is Sunday as 0 is, and a weekday range may run past
/// Saturday, as <c>FRI-SUN</c>. When both day fields are restricted
/// a day matches either, as cron has always read them.
/// </para>
/// <para>
/// A due time that falls in a daylight-saving gap does not occur, and an ambiguous one is
/// its first occurrence — the two answers a person setting a 02:30 schedule would expect
/// least to be surprised by.
/// </para>
/// </summary>
public sealed class ScheduleCron
{
    private static readonly string[] MonthNames = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
    private static readonly string[] DayNames = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    /// <summary>How far ahead a due time is looked for: long enough for a 29 February.</summary>
    private const int SearchDays = 366 * 8;

    private readonly bool[] _minutes;
    private readonly bool[] _hours;
    private readonly bool[] _days;
    private readonly bool[] _months;
    private readonly bool[] _weekdays;
    private readonly bool _anyDay;
    private readonly bool _anyWeekday;

    private ScheduleCron(string text, bool[] minutes, bool[] hours, bool[] days, bool[] months, bool[] weekdays, bool anyDay, bool anyWeekday)
    {
        Text = text;
        _minutes = minutes;
        _hours = hours;
        _days = days;
        _months = months;
        _weekdays = weekdays;
        _anyDay = anyDay;
        _anyWeekday = anyWeekday;
    }

    /// <summary>The expression as it was read.</summary>
    public string Text { get; }

    /// <summary>The cron <paramref name="text"/> names, or null where it is not five fields
    /// this reader understands — a schedule whose cron is null then never reads Missed.</summary>
    public static ScheduleCron? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (fields.Length != 5) return null;

        var minutes = Field(fields[0], 0, 59, null);
        var hours = Field(fields[1], 0, 23, null);
        var days = Field(fields[2], 1, 31, null);
        var months = Field(fields[3], 1, 12, MonthNames);
        var weekdays = Field(fields[4], 0, 7, DayNames, wraps: true);

        if (minutes is null || hours is null || days is null || months is null || weekdays is null) return null;

        // 7 is Sunday as well as 0.
        if (weekdays[7]) weekdays[0] = true;

        return new ScheduleCron(
            text.Trim(), minutes, hours, days, months, weekdays,
            anyDay: fields[2].StartsWith('*'),
            anyWeekday: fields[4].StartsWith('*'));
    }

    /// <summary>The first due time strictly after <paramref name="after"/>, in
    /// <paramref name="zone"/>, or null where none falls in the next eight years.</summary>
    public DateTimeOffset? NextAfter(DateTimeOffset after, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = TimeZoneInfo.ConvertTime(after, zone);
        var startDay = local.Date;

        for (var offset = 0; offset < SearchDays; offset++)
        {
            var day = startDay.AddDays(offset);

            if (!DayMatches(day)) continue;

            for (var hour = 0; hour < 24; hour++)
            {
                if (!_hours[hour]) continue;

                for (var minute = 0; minute < 60; minute++)
                {
                    if (!_minutes[minute]) continue;

                    var wall = day.AddHours(hour).AddMinutes(minute);

                    if (zone.IsInvalidTime(wall)) continue;

                    var instant = new DateTimeOffset(wall, FirstOffset(zone, wall));

                    if (instant > after) return instant;
                }
            }
        }

        return null;
    }

    private bool DayMatches(DateTime day)
    {
        if (!_months[day.Month]) return false;

        var dayOfMonth = _days[day.Day];
        var dayOfWeek = _weekdays[(int)day.DayOfWeek];

        // Both restricted: either matches. One restricted: that one decides.
        if (!_anyDay && !_anyWeekday) return dayOfMonth || dayOfWeek;

        return dayOfMonth && dayOfWeek;
    }

    private static TimeSpan FirstOffset(TimeZoneInfo zone, DateTime wall)
    {
        if (!zone.IsAmbiguousTime(wall)) return zone.GetUtcOffset(wall);

        // The earlier instant of an ambiguous wall time carries the larger offset.
        return zone.GetAmbiguousTimeOffsets(wall).Max();
    }

    /// <param name="wraps">Whether a range may run past Saturday onto Sunday: a weekday's
    /// <c>FRI-SUN</c> is Friday, Saturday and Sunday.</param>
    private static bool[]? Field(string text, int min, int max, string[]? names, bool wraps = false)
    {
        var allowed = new bool[max + 1];

        foreach (var part in text.Split(','))
        {
            if (part.Length == 0) return null;

            var range = part;
            var step = 1;
            var slash = part.IndexOf('/', StringComparison.Ordinal);

            if (slash >= 0)
            {
                if (!int.TryParse(part[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1) return null;

                range = part[..slash];
            }

            int low, high;

            if (range == "*")
            {
                (low, high) = (min, max);
            }
            else
            {
                var dash = range.IndexOf('-', StringComparison.Ordinal);

                if (dash >= 0)
                {
                    if (Value(range[..dash], names, min) is not { } a || Value(range[(dash + 1)..], names, min) is not { } b) return null;

                    (low, high) = (a, b);
                }
                else
                {
                    if (Value(range, names, min) is not { } single) return null;

                    // "5/15" means from 5 to the end, every 15.
                    (low, high) = (single, slash >= 0 ? max : single);
                }
            }

            if (low < min || high > max) return null;

            if (low > high)
            {
                // Only a weekday range wraps, and only onto Sunday as 0: SAT-SUN is 6, 0.
                if (!wraps || step != 1) return null;

                for (var value = low; value <= 6; value++) allowed[value] = true;
                for (var value = 0; value <= high; value++) allowed[value] = true;

                continue;
            }

            for (var value = low; value <= high; value += step) allowed[value] = true;
        }

        return allowed;
    }

    private static int? Value(string text, string[]? names, int min)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return number;

        if (names is null) return null;

        var index = Array.FindIndex(names, name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? null : index + min;
    }
}
