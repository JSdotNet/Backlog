namespace Backlog.SharedKernel;

/// <summary>
/// One day of the working week: whether it is worked at all, and between which hours.
/// </summary>
/// <param name="Day">Which day.</param>
/// <param name="Working">False for a day off. The times are still carried, so turning a
/// day back on restores the hours it had rather than the defaults — a reader who marked
/// Saturday off should not lose the hours they had set for it.</param>
/// <param name="Start">When the day starts, local.</param>
/// <param name="End">When it ends, local. Exclusive: a day ending at 17:30 is not being
/// worked at 17:30.</param>
public sealed record WorkingDay(DayOfWeek Day, bool Working, TimeOnly Start, TimeOnly End);

/// <summary>
/// The reader's own working week, as a personal preference rather than anything the data
/// knows.
/// <para>
/// Presentation only, and that is a deliberate limit. Nothing here changes a figure:
/// agent-active time is agent-active time whether it happened at eleven in the morning or
/// eleven at night, and a surface that quietly excluded out-of-hours work would be
/// answering a different question from the one its tile is named for. What this does is
/// let a grid say which hours were meant to be worked, so the reader can see the
/// difference themselves.
/// </para>
/// <para>
/// Seven independent days rather than one range and a set of working days, because the
/// two are not the same preference: somebody who starts at seven on Fridays cannot say so
/// with a single range, and the shape that cannot express it would have to be replaced
/// rather than extended.
/// </para>
/// </summary>
public sealed record WorkingHours
{
    /// <summary>Nine, because that is where a working day conventionally starts and a
    /// default nobody recognises is a default everybody has to change.</summary>
    public static TimeOnly DefaultStart { get; } = new(9, 0);

    public static TimeOnly DefaultEnd { get; } = new(17, 30);

    /// <summary>
    /// The week in reading order, Monday first.
    /// <para>
    /// Monday rather than <see cref="DayOfWeek"/>'s own Sunday-first order, which is a
    /// calendar convention rather than a working-week one and would put the weekend on
    /// both ends of the list.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DayOfWeek> Week { get; } =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    /// <summary>
    /// What is stored. Possibly incomplete and possibly in any order — it comes off disk
    /// and a hand-edited file is allowed to be missing a day — so read it through
    /// <see cref="On"/> rather than by index.
    /// </summary>
    public IReadOnlyList<WorkingDay> Days { get; init; } = [];

    /// <summary>Monday to Friday, nine to half five, and the weekend off.</summary>
    public static WorkingHours Default { get; } = new() { Days = [.. Week.Select(DefaultFor)] };

    /// <summary>
    /// The stored day, or the default for it when nothing is stored.
    /// <para>
    /// A missing day is filled in rather than treated as a day off. The two are different
    /// claims and only one of them was made: nothing stored means nobody said, and saying
    /// "not a working day" on somebody's behalf is the kind of quiet assumption this
    /// surface exists to avoid.
    /// </para>
    /// </summary>
    public WorkingDay On(DayOfWeek day) =>
        Days.FirstOrDefault(entry => entry.Day == day) ?? DefaultFor(day);

    /// <summary>
    /// Whether any part of the given local hour falls inside that day's working hours.
    /// <para>
    /// Any part, not most of it. A day ending at 17:30 leaves the 17:00 hour half worked,
    /// and there is no half-marked cell to draw it with — so the choice is to mark the
    /// hour or to disown it, and disowning it would say that work at ten past five
    /// happened out of hours. The caption says the hours are marked where they overlap.
    /// </para>
    /// <para>
    /// An end at or before the start is a range nobody can work, so nothing is marked. It
    /// is reachable only by editing the file by hand — the setter refuses it — and a grid
    /// that marked every hour because two times were the wrong way round would be worse
    /// than one that marked none.
    /// </para>
    /// </summary>
    public bool Covers(DayOfWeek day, int hour)
    {
        var working = On(day);

        if (!working.Working || working.End <= working.Start) return false;

        var beganByTheEndOfTheHour = working.Start.Hour <= hour;
        var ranPastTheStartOfIt = working.End.Hour > hour || (working.End.Hour == hour && working.End.Minute > 0);

        return beganByTheEndOfTheHour && ranPastTheStartOfIt;
    }

    private static WorkingDay DefaultFor(DayOfWeek day) =>
        new(day, day is not (DayOfWeek.Saturday or DayOfWeek.Sunday), DefaultStart, DefaultEnd);
}

/// <summary>
/// Where the reader's working week is kept.
/// <para>
/// In the kernel for the reason <see cref="IAppFeatureSettings"/> is: more than one
/// context asks the question and none of them owns the answer. The dashboard reads it to
/// shade a grid, the settings screen writes it, and neither may reach through the other.
/// What a working day <em>means</em> is the reader's business; this is only the question
/// and the answer.
/// </para>
/// </summary>
public interface IWorkingHoursSettings
{
    /// <summary>Raised after a change lands, so a surface showing the week can redraw
    /// without polling.</summary>
    event Action? Changed;

    WorkingHours Current { get; }

    /// <summary>Where the choices are kept, so the settings screen can say where.</summary>
    string SettingsPath { get; }

    /// <summary>
    /// Replaces one day. Returns null when it was stored, or why it was refused —
    /// a message rather than an exception, on the precedent every other settings store
    /// here sets: a rejected value belongs in the status line beside the field, not in a
    /// stack trace.
    /// </summary>
    string? SetDay(DayOfWeek day, bool working, TimeOnly start, TimeOnly end);

    /// <summary>Puts the whole week back to <see cref="WorkingHours.Default"/>.</summary>
    string? ResetToDefault();
}
