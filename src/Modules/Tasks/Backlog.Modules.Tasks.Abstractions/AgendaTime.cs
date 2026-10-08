using System.Globalization;

namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// Where a task sits in its My Day: a <see cref="Start"/>, a local wall-clock time
/// of day, and a <see cref="DurationMinutes"/> in whole minutes. 10:45 for 45
/// minutes is one block that runs from 10:45 to 11:30
/// (<c>.devbook/domain/tasks/domain.md#agenda-time</c>).
/// <para>
/// It has no date of its own and no zone: the date is the task's My Day date, and
/// the time is read on the reader's clock, the same as a reminder. That is why the
/// aggregate holds one only while the task has an <c>in_my_day_on</c>.
/// </para>
/// <para>
/// In Abstractions rather than beside the aggregate for the reason
/// <see cref="Recurrence"/> is: the parser produces one, the DTO publishes one and
/// the aggregate holds one.
/// </para>
/// </summary>
public sealed record AgendaTime
{
    /// <summary>What a start written without a duration means.</summary>
    public const int DefaultDurationMinutes = 30;

    private const string StartFormat = "HH:mm";

    /// <param name="start">The local time of day the block starts at. Seconds are
    /// dropped: the token has minute resolution, and a value that compared unequal
    /// to its own round trip would not be a value object.</param>
    /// <param name="durationMinutes">A positive whole number of minutes.</param>
    public AgendaTime(TimeOnly start, int durationMinutes = DefaultDurationMinutes)
    {
        if (durationMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMinutes), durationMinutes, "An agenda duration is a positive number of minutes.");
        }

        Start = new TimeOnly(start.Hour, start.Minute);
        DurationMinutes = durationMinutes;
    }

    /// <summary>The local time of day the block starts at, to the minute.</summary>
    public TimeOnly Start { get; }

    /// <summary>How long the block runs, in whole minutes.</summary>
    public int DurationMinutes { get; }

    /// <summary>When the block ends, on the same clock. Wraps past midnight the way
    /// <see cref="TimeOnly"/> does; nothing in My Day reads beyond the day.</summary>
    public TimeOnly End => Start.AddMinutes(DurationMinutes);

    /// <summary>The <c>at:</c> value: 24-hour <c>HH:mm</c>, invariant, so the line
    /// round-trips between machines whatever their culture.</summary>
    public string StartToken => Start.ToString(StartFormat, CultureInfo.InvariantCulture);

    /// <summary>Reads an <c>at:</c> value: exactly <c>HH:mm</c>, <c>00:00</c> to
    /// <c>23:59</c>.</summary>
    public static bool TryParseStart(string? value, out TimeOnly start) =>
        TimeOnly.TryParseExact(value, StartFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out start);

    /// <summary>Reads a <c>for:</c> value: a positive whole number followed by
    /// <c>m</c>, such as <c>45m</c>.</summary>
    public static bool TryParseDuration(string? value, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrEmpty(value) || value.Length < 2 || value[^1] is not ('m' or 'M')) return false;

        return int.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
            && minutes > 0;
    }

    /// <summary>The <c>for:</c> value, such as <c>45m</c>.</summary>
    public static string DurationToken(int minutes) => minutes.ToString(CultureInfo.InvariantCulture) + "m";

    /// <summary>
    /// The agenda time two plain wire values describe, or null when they describe
    /// none. Tolerant the way every other wire field is: a start that does not
    /// read is no agenda time, and a duration that is missing or not positive
    /// means the default, because the start is what places the task.
    /// </summary>
    public static AgendaTime? FromWire(string? start, int? durationMinutes) =>
        TryParseStart(start, out var parsed)
            ? new AgendaTime(parsed, durationMinutes is > 0 ? durationMinutes.Value : DefaultDurationMinutes)
            : null;
}
