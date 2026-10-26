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
/// One weekly reset the person named — one per assistant account, since each account's
/// allowance resets on its own clock.
/// </summary>
/// <param name="Id">A short generated token that never changes, so renaming a reset
/// does not move the dashboard's choice off it.</param>
/// <param name="Name">What the person calls it — "Work", "Personal".</param>
/// <param name="Reset">When it resets.</param>
public sealed record NamedUsageReset(string Id, string Name, UsageWeekReset Reset)
{
    /// <summary>The longest name a reset can have: enough for an account's label,
    /// short enough for the dashboard's picker.</summary>
    public const int MaxNameLength = 40;
}

/// <summary>
/// The person's own answers to when their weekly allowances reset, kept beside the
/// app's other per-user choices, and which of them cuts the dashboard's weeks. Empty
/// when they have not said, in which case the dashboard falls back to what the
/// assistant's own records let it detect, and past that to calendar weeks.
/// </summary>
/// <remarks>
/// Every change returns null when it took effect and was saved, and a message when it
/// was refused (the state stays) or took effect but could not be saved for next time.
/// </remarks>
public interface IUsageResetSettings
{
    /// <summary>Raised after a change has taken effect, whether or not it could be
    /// saved for next time.</summary>
    event Action? Changed;

    /// <summary>The named resets, in the order the person added them.</summary>
    IReadOnlyList<NamedUsageReset> Resets { get; }

    /// <summary>The reset that cuts the dashboard's weeks: the stored choice when it
    /// names an entry, else the first entry, else null.</summary>
    NamedUsageReset? Selected { get; }

    /// <summary>What is in force, or null when nothing has been configured.</summary>
    UsageWeekReset? Current => Selected?.Reset;

    /// <summary>Where the choice is kept, so the settings screen can say.</summary>
    string SettingsPath { get; }

    /// <summary>Adds a reset at the end of the list. Returns its id, or null when the
    /// name was refused, and the message the change produced.</summary>
    (string? Id, string? Error) Add(string name, DayOfWeek day, TimeOnly time);

    /// <summary>Renames or re-times the reset with this id.</summary>
    string? Update(string id, string name, DayOfWeek day, TimeOnly time);

    /// <summary>Removes the reset with this id. Removing the chosen one hands the cut
    /// to the first remaining reset; removing the last lets detection take over.</summary>
    string? Remove(string id);

    /// <summary>Chooses the reset that cuts the dashboard's weeks.</summary>
    string? Select(string id);
}
