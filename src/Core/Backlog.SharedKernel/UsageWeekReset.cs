namespace Backlog.SharedKernel;

/// <summary>
/// When the assistant's weekly usage allowance resets: a day of the week and a time on
/// this machine's clock, recurring every seven days.
/// </summary>
/// <remarks>
/// <para>
/// Presentation only, as <see cref="WorkingHours"/> is: nothing here changes a figure.
/// It decides where the dashboard cuts a week — the columns of its weekly charts and
/// the seven days of its hour grids run from one reset to the next rather than from a
/// calendar Monday — so a week on screen is the week the allowance is spent in.
/// </para>
/// <para>
/// Local rather than UTC, because the reset is a fact the person reads off the
/// assistant's own usage screen in their own clock ("Resets Mon 2:00 PM"), and a
/// setting that asked them to convert it would be asking them to do the one
/// arithmetic that is most often done wrong. The dashboard converts on the way in.
/// </para>
/// </remarks>
/// <param name="Day">The day of the week the allowance resets on.</param>
/// <param name="Time">The local time of day it resets at.</param>
public sealed record UsageWeekReset(DayOfWeek Day, TimeOnly Time)
{
    /// <summary>
    /// The most recent reset at or before <paramref name="now"/>, on the clock the
    /// zone describes — the instant the week that contains <paramref name="now"/>
    /// began. Resolved through the zone's offset for that local moment, so a reset
    /// that falls inside a daylight-saving gap or overlap still names one instant
    /// rather than throwing.
    /// </summary>
    public DateTimeOffset MostRecentBefore(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = TimeZoneInfo.ConvertTime(now, zone);
        var today = DateOnly.FromDateTime(local.DateTime);

        // Walk back to the reset's weekday, then take that day's reset time; if that
        // is still ahead of now — it is the reset's weekday but earlier in the day —
        // the week began seven days before.
        var daysBack = ((int)today.DayOfWeek - (int)Day + 7) % 7;
        var candidate = today.AddDays(-daysBack).ToDateTime(Time);
        var instant = new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));

        return instant <= now ? instant : instant.AddDays(-7);
    }
}

/// <summary>
/// The person's own answer to when their weekly allowance resets, kept beside the
/// app's other per-user choices. Null when they have not said, in which case the
/// dashboard falls back to what the assistant's own records let it detect, and past
/// that to calendar weeks.
/// </summary>
public interface IUsageResetSettings
{
    /// <summary>Raised after a change has taken effect, whether or not it could be
    /// saved for next time.</summary>
    event Action? Changed;

    /// <summary>What is in force, or null when nothing has been configured.</summary>
    UsageWeekReset? Current { get; }

    /// <summary>Where the choice is kept, so the settings screen can say.</summary>
    string SettingsPath { get; }

    /// <summary>Sets the reset. Returns a message when the change took effect but
    /// could not be saved, and null otherwise.</summary>
    string? Set(DayOfWeek day, TimeOnly time);

    /// <summary>Forgets the configured reset, so detection takes over again.</summary>
    string? Clear();
}
