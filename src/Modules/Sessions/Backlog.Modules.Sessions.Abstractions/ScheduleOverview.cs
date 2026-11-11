using System.Globalization;

namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>The Schedules page's Show filter (local ADR 0051 §6).</summary>
public enum ScheduleFilter
{
    /// <summary>Every schedule.</summary>
    All,

    /// <summary>The schedules that lead the page: Needs you and Held.</summary>
    NeedsYou,

    /// <summary>The schedules whose latest run waits to be reviewed.</summary>
    ReviewReady,

    /// <summary>The schedules whose latest run failed, or that missed a due time.</summary>
    FailedOrMissed,
}

/// <summary>The schedules of one repository, as the Schedules page groups its table.</summary>
/// <param name="Repository">The repository the schedules reported, or null for those that named none.</param>
public sealed record ScheduleGroup(string? Repository, IReadOnlyList<ScheduleReadingResult> Schedules);

/// <summary>
/// The pure rules the Schedules page reads its readings by: which schedules need the person,
/// which a Show filter keeps, how the table groups them, and what holds a held one.
/// </summary>
public static class ScheduleOverview
{
    /// <summary>
    /// Whether a schedule in <paramref name="state"/> leads the page: it reads Needs you, or
    /// Held, whose runs keep skipping themselves until the person answers the run that holds
    /// them (<c>.devbook/domain/sessions/features.md#schedules-and-their-runs</c>).
    /// </summary>
    public static bool NeedsYou(ScheduledRunState? state) =>
        state is ScheduledRunState.NeedsYou or ScheduledRunState.Held;

    /// <summary>Whether <paramref name="filter"/> keeps a schedule in <paramref name="state"/>.</summary>
    public static bool Matches(ScheduleFilter filter, ScheduledRunState? state) => filter switch
    {
        ScheduleFilter.All => true,
        ScheduleFilter.NeedsYou => NeedsYou(state),
        ScheduleFilter.ReviewReady => state is ScheduledRunState.ReviewReady,
        ScheduleFilter.FailedOrMissed => state is ScheduledRunState.Failed or ScheduledRunState.Missed,
        _ => false,
    };

    /// <summary>How many of <paramref name="schedules"/> <paramref name="filter"/> keeps.</summary>
    public static int Count(IEnumerable<ScheduleReadingResult> schedules, ScheduleFilter filter)
    {
        ArgumentNullException.ThrowIfNull(schedules);

        return schedules.Count(schedule => Matches(filter, schedule.State));
    }

    /// <summary>A schedule's name: its title, or its id where no report named one.</summary>
    public static string Name(Schedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return string.IsNullOrWhiteSpace(schedule.Title) ? schedule.Id : schedule.Title;
    }

    /// <summary>
    /// <paramref name="schedules"/> grouped by the repository each reported, repositories in
    /// name order regardless of case, the schedules that named none last; within a group,
    /// schedules in name order.
    /// </summary>
    public static IReadOnlyList<ScheduleGroup> Groups(IEnumerable<ScheduleReadingResult> schedules)
    {
        ArgumentNullException.ThrowIfNull(schedules);

        return [.. schedules
            .GroupBy(schedule => Repository(schedule.Schedule), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key is null ? 1 : 0)
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ScheduleGroup(
                group.Key,
                [.. group
                    .OrderBy(schedule => Name(schedule.Schedule), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(schedule => schedule.Schedule.Id, StringComparer.Ordinal)]))];
    }

    /// <summary>
    /// The run that holds a held schedule: the one its latest skipped run named, where that
    /// run is among the schedule's runs; where the skip named none, the newest run that did
    /// not skip itself and is not yet handled. Null for a schedule that does not read Held,
    /// or when no such run is known — never a run already archived, which holds nothing.
    /// </summary>
    public static ScheduledRunReading? Holder(ScheduleReadingResult reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (reading.State is not ScheduledRunState.Held || reading.Runs.Count == 0) return null;

        if (reading.Runs[0].Run.HeldBy is { } holder)
        {
            return reading.Runs.FirstOrDefault(run =>
                string.Equals(run.Run.SessionId, holder, StringComparison.Ordinal)
                && run.State is not ScheduledRunState.Handled);
        }

        return reading.Runs.FirstOrDefault(run => !run.Run.Skipped && run.State is not ScheduledRunState.Handled);
    }

    /// <summary>How many runs in a row, newest first, skipped themselves.</summary>
    public static int SkippedInARow(ScheduleReadingResult reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return reading.Runs.TakeWhile(run => run.Run.Skipped).Count();
    }

    /// <summary>When a run began: its start report, or its first report where no start arrived.</summary>
    public static DateTimeOffset Began(ScheduledRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return run.StartedAt ?? run.FirstReportedAt;
    }

    private static string? Repository(Schedule schedule) =>
        string.IsNullOrWhiteSpace(schedule.Repository) ? null : schedule.Repository.Trim();
}

/// <summary>
/// A schedule's cron in the words the Schedules page shows as its cadence:
/// <c>30 5 * * 1-5</c> reads <c>Mon–Fri 05:30</c>. A shape it has no words for reads as the
/// cron itself, which is still the truth.
/// </summary>
public static class ScheduleCadence
{
    /// <summary>The wording for a schedule that reported no cron.</summary>
    public const string Unknown = "No cadence reported";

    private static readonly string[] Days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    /// <summary>The cadence of <paramref name="cron"/>.</summary>
    public static string Describe(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron)) return Unknown;

        var text = cron.Trim();
        var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5) return text;

        var (minute, hour, dayOfMonth, month, dayOfWeek) = (fields[0], fields[1], fields[2], fields[3], fields[4]);
        if (month != "*") return text;

        if (Number(minute, 59) is { } atMinute)
        {
            if (Number(hour, 23) is { } atHour)
            {
                var time = $"{atHour:00}:{atMinute:00}";

                if (dayOfMonth == "*")
                {
                    return Weekdays(dayOfWeek) is { } days ? days.Length == 0 ? $"Daily {time}" : $"{days} {time}" : text;
                }

                return dayOfWeek == "*" && Number(dayOfMonth, 31) is { } day and > 0
                    ? $"Monthly on day {day} {time}"
                    : text;
            }

            if (dayOfMonth != "*" || dayOfWeek != "*") return text;
            if (hour == "*") return $"Hourly at :{atMinute:00}";

            return Step(hour, 23) is { } hours ? $"Every {hours} hours at :{atMinute:00}" : text;
        }

        return hour == "*" && dayOfMonth == "*" && dayOfWeek == "*" && Step(minute, 59) is { } minutes
            ? $"Every {minutes} min"
            : text;
    }

    /// <summary>The weekday field in words: an empty string for every day, null for a shape
    /// with no words.</summary>
    private static string? Weekdays(string field)
    {
        if (field == "*") return string.Empty;

        var days = new SortedSet<int>();

        foreach (var part in field.Split(','))
        {
            var range = part.Split('-');

            if (range.Length == 1 && Day(range[0]) is { } single)
            {
                days.Add(single % 7);
            }
            else if (range.Length == 2 && Day(range[0]) is { } from && Day(range[1]) is { } to && from <= to)
            {
                // The bounds are read before 7 folds into Sunday, so 0-7 and 1-7 are every
                // day, as the cron reader takes them, and never the range Sun–Sun.
                for (var day = from; day <= to; day++) days.Add(day % 7);

                if (to - from >= 2 && to - from < 6 && to < 7 && field.IndexOf(',', StringComparison.Ordinal) < 0)
                {
                    return $"{Days[from]}–{Days[to]}";
                }
            }
            else
            {
                return null;
            }
        }

        if (days.Count == 7) return string.Empty;

        return string.Join(", ", days.Select(day => Days[day]));
    }

    /// <summary>A weekday by number (0 to 7, 7 being Sunday as 0 is, folded by the caller)
    /// or by its three-letter name.</summary>
    private static int? Day(string text)
    {
        if (Number(text, 7) is { } number) return number;

        var index = Array.FindIndex(Days, day => string.Equals(day, text, StringComparison.OrdinalIgnoreCase));

        return index < 0 ? null : index;
    }

    private static int? Number(string text, int max) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value <= max ? value : null;

    private static int? Step(string text, int max) =>
        text.StartsWith("*/", StringComparison.Ordinal) && Number(text[2..], max) is { } step and > 0 ? step : null;
}
