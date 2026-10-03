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
/// The person's own working week, as a personal preference rather than anything the data
/// knows.
/// <para>
/// Two readers, two uses. The dashboard reads it for presentation only: agent-active time
/// is agent-active time whether it happened at eleven in the morning or eleven at night,
/// so its grids outline the hours meant to be worked and change no figure. The roadmap
/// reads it as a count of hours (local ADR 0019): an effort window counts the working
/// hours forward from its start, skipping the days not worked, and a measured pace is
/// counted over the same hours. One week per person, which the roadmap's pace document
/// carries between devices.
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

    /// <summary>
    /// Whether <paramref name="day"/> is worked: the week marks it worked and its end is
    /// after its start. A day ending at or before it starts is not worked, as
    /// <see cref="Covers"/> already reads it.
    /// </summary>
    public bool IsWorked(DayOfWeek day)
    {
        var working = On(day);
        return working.Working && working.End > working.Start;
    }

    /// <summary>How long <paramref name="day"/> is worked; zero for a day not worked.
    /// A span rather than a number of hours, so a sum of days stays exact: a day of
    /// 8h20m is no finite decimal of hours.</summary>
    public TimeSpan WorkedOn(DayOfWeek day) =>
        IsWorked(day) ? On(day).End - On(day).Start : TimeSpan.Zero;

    /// <summary>How long the whole week is worked — 42.5 hours on
    /// <see cref="Default"/>.</summary>
    public TimeSpan PerWeek => TimeSpan.FromTicks(Week.Sum(day => WorkedOn(day).Ticks));

    /// <summary>
    /// This week, or <see cref="Default"/> when it holds no working hours at all
    /// (local ADR 0019, §2). A week that works nothing cannot size a window or divide a
    /// pace; the settings screen refuses to make one a day at a time, so it takes a
    /// hand-edited file or every day switched off.
    /// </summary>
    public WorkingHours Effective => PerWeek > TimeSpan.Zero ? this : Default;

    /// <summary>The first worked day on or after <paramref name="day"/>, in the
    /// <see cref="Effective"/> week — where work counted from <paramref name="day"/>
    /// starts.</summary>
    public DateOnly FirstWorkedDay(DateOnly day)
    {
        var effective = Effective;

        // The effective week works at least one day, so seven steps always find it.
        for (var offset = 0; offset < 7 && day.DayNumber + offset <= DateOnly.MaxValue.DayNumber; offset++)
        {
            var candidate = day.AddDays(offset);
            if (effective.IsWorked(candidate.DayOfWeek)) return candidate;
        }

        return day;
    }

    /// <summary>
    /// The day <paramref name="amount"/> at <paramref name="perWeek"/> a working week
    /// runs out, counted from <paramref name="start"/> (local ADR 0019): the
    /// <see cref="Effective"/> week's hours × the amount ÷ the rate, multiplied before
    /// dividing so an amount equal to the rate is exactly one week. See
    /// <see cref="LastDayOf(DateOnly, TimeSpan)"/> for how the hours are counted.
    /// </summary>
    /// <param name="perWeek">How much a working week gets through; always positive.</param>
    public DateOnly LastDayOf(DateOnly start, decimal amount, decimal perWeek)
    {
        if (perWeek <= 0) throw new ArgumentOutOfRangeException(nameof(perWeek), perWeek, "A rate is always positive.");

        return LastDayOf(start, amount * Effective.PerWeek.Ticks / perWeek);
    }

    /// <summary>
    /// The day <paramref name="needed"/> working time runs out, counted from
    /// <paramref name="start"/> through the <see cref="Effective"/> week: from the first
    /// worked day on or after the start, each worked day takes its whole hours, and the
    /// last is the day nothing is left. A day counts whole, so the start day is never
    /// skipped, and days not worked add nothing.
    /// </summary>
    public DateOnly LastDayOf(DateOnly start, TimeSpan needed) => LastDayOf(start, (decimal)needed.Ticks);

    /// <summary>
    /// The walk itself, in ticks so a sum of days stays exact. Whole weeks are skipped
    /// first — any seven days in a row hold the whole week's hours — so an absurd amount
    /// costs no more than a small one, and is clamped to the calendar's last day.
    /// </summary>
    private DateOnly LastDayOf(DateOnly start, decimal neededTicks)
    {
        var effective = Effective;
        var perWeek = (decimal)effective.PerWeek.Ticks;

        long day = FirstWorkedDay(start).DayNumber;
        var left = neededTicks;

        if (left > perWeek)
        {
            // Leave at most one week to walk, and never nothing: the walk ends on a
            // worked day.
            var weeks = Math.Ceiling(left / perWeek) - 1;
            if (day + weeks * 7 > DateOnly.MaxValue.DayNumber) return DateOnly.MaxValue;

            day += (long)(weeks * 7);
            left -= weeks * perWeek;
        }

        while (day <= DateOnly.MaxValue.DayNumber)
        {
            var date = DateOnly.FromDayNumber((int)day);
            left -= effective.WorkedOn(date.DayOfWeek).Ticks;
            if (left <= 0) return date;

            day++;
        }

        return DateOnly.MaxValue;
    }

    private static WorkingDay DefaultFor(DayOfWeek day) =>
        new(day, day is not (DayOfWeek.Saturday or DayOfWeek.Sunday), DefaultStart, DefaultEnd);
}

/// <summary>
/// Where the reader's working week is kept.
/// <para>
/// In the kernel for the reason <see cref="IAppFeatureSettings"/> is: more than one
/// context asks the question and none of them owns the answer. The dashboard reads it to
/// shade a grid, the roadmap counts its hours, the settings screen writes it, and none may
/// reach through the other. What a working day <em>means</em> is the reader's business;
/// this is only the question and the answer.
/// </para>
/// <para>
/// This is the device's copy. A host that carries the roadmap's pace between devices
/// writes the week into the pace document beside it and replaces this copy when a pace
/// document arrives carrying one (local ADR 0019, §3).
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
