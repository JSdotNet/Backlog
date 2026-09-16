namespace Backlog.Infrastructure.FileSystem;

/// <summary>How often the backlog is backed up.</summary>
public enum BackupCadence
{
    /// <summary>Never on its own. The button still works.</summary>
    Off,

    /// <summary>Once a day, at <see cref="BackupSchedule.At"/>.</summary>
    Daily,

    /// <summary>Once a week, on <see cref="BackupSchedule.Day"/> at
    /// <see cref="BackupSchedule.At"/>.</summary>
    Weekly
}

/// <summary>
/// When the backlog is backed up to its repository: a cadence, a local time of
/// day, and — for the weekly cadence — a day of the week. The day is carried
/// while the cadence is daily too, so switching to weekly and back does not
/// forget which day was chosen.
/// <para>
/// A schedule is a rule, not a timer: it answers "when is the next slot after
/// this moment?" and "when was the last slot at or before it?", and the worker
/// turns those into a timer and a catch-up. Both are asked in local time,
/// because "six in the evening" means the evening where the laptop is, and a
/// laptop that crossed a time zone since the last backup gets the next one at
/// six in the evening there.
/// </para>
/// </summary>
public sealed record BackupSchedule(BackupCadence Cadence, TimeOnly At, DayOfWeek Day)
{
    /// <summary>Nothing scheduled, with the time and day a person most often
    /// wants when they switch it on: the end of the working day, and the end of
    /// the working week.</summary>
    public static BackupSchedule Off { get; } = new(BackupCadence.Off, new TimeOnly(18, 0), DayOfWeek.Friday);

    /// <summary>Whether anything runs on its own.</summary>
    public bool IsScheduled => Cadence is not BackupCadence.Off;

    /// <summary>The first slot strictly after <paramref name="localNow"/>, or
    /// null while off. A slot that falls exactly on <paramref name="localNow"/>
    /// is the one that just fired, so the next is the one after it.</summary>
    public DateTimeOffset? NextSlotAfter(DateTimeOffset localNow)
    {
        if (!IsScheduled) return null;

        var candidate = SlotOn(localNow.Date, localNow.Offset);
        while (candidate <= localNow || !RunsOn(candidate.DayOfWeek))
        {
            candidate = SlotOn(candidate.Date.AddDays(1), localNow.Offset);
        }

        return candidate;
    }

    /// <summary>The most recent slot at or before <paramref name="localNow"/>,
    /// or null while off. What a catch-up compares the last run against: a slot
    /// that went by while the app was closed is a backup that is owed.</summary>
    public DateTimeOffset? LastSlotAtOrBefore(DateTimeOffset localNow)
    {
        if (!IsScheduled) return null;

        var candidate = SlotOn(localNow.Date, localNow.Offset);
        while (candidate > localNow || !RunsOn(candidate.DayOfWeek))
        {
            candidate = SlotOn(candidate.Date.AddDays(-1), localNow.Offset);
        }

        return candidate;
    }

    private bool RunsOn(DayOfWeek day) => Cadence is BackupCadence.Daily || day == Day;

    private DateTimeOffset SlotOn(DateTime date, TimeSpan offset) =>
        new(date.Year, date.Month, date.Day, At.Hour, At.Minute, 0, offset);
}
