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
/// One date that differs from the weekly pattern (local ADR 0019, §5): a day of leave or
/// a public holiday the pattern would work, or one Saturday it would not.
/// </summary>
/// <param name="Date">The local date.</param>
/// <param name="Worked">False for a blocked date, true for an unblocked one. An unblocked
/// date counts its weekday's stored start and end, which the pattern keeps for a day off
/// too.</param>
public sealed record DayOverride(DateOnly Date, bool Worked);

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
/// <para>
/// The seven days are the <em>pattern</em>; <see cref="Overrides"/> are the dates that
/// differ from it (local ADR 0019, §5). The <see cref="DayOfWeek"/> members read the
/// pattern alone — the dashboard outlines weekdays, not dates — and the
/// <see cref="DateOnly"/> members read a date, its override first. A week's length,
/// <see cref="PerWeek"/>, is the pattern's, so an override never changes a pace's unit.
/// </para>
/// <para>
/// A record compares its lists by reference, so two weeks built apart are never equal
/// by <c>==</c>. Compare them day by day and override by override, as the settings store
/// does.
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

    /// <summary>
    /// The dates that differ from the pattern, by date. <see cref="Toggled"/> keeps them
    /// sorted, one per date, and only ever holding a date its pattern reads otherwise.
    /// Read through <see cref="OverrideOn"/>: a list off disk is allowed to be in any
    /// order, and a date listed twice reads its first entry.
    /// </summary>
    public IReadOnlyList<DayOverride> Overrides { get; init; } = [];

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
    /// <see cref="Default"/>. The pattern's: no override changes it.</summary>
    public TimeSpan PerWeek => TimeSpan.FromTicks(Week.Sum(day => WorkedOn(day).Ticks));

    /// <summary>The override on <paramref name="date"/>, or <c>null</c> when the date
    /// reads its pattern.</summary>
    public DayOverride? OverrideOn(DateOnly date) =>
        Overrides.FirstOrDefault(entry => entry.Date == date);

    /// <summary>
    /// Whether <paramref name="date"/> is worked: its override says so, or — with none —
    /// its weekday's pattern does. Either way the weekday's end must be after its start;
    /// an unblocked date whose weekday stores an empty range is still not worked, as
    /// <see cref="IsWorked(DayOfWeek)"/> reads that range.
    /// </summary>
    public bool IsWorked(DateOnly date)
    {
        var working = On(date.DayOfWeek);
        return MarkedWorked(date) && working.End > working.Start;
    }

    /// <summary>How long <paramref name="date"/> is worked: its weekday's stored
    /// <c>End - Start</c> when it is worked — an unblocked date counts them even though
    /// the pattern marks that weekday off — and zero when it is not.</summary>
    public TimeSpan WorkedOn(DateOnly date)
    {
        if (!IsWorked(date)) return TimeSpan.Zero;

        var working = On(date.DayOfWeek);
        return working.End - working.Start;
    }

    /// <summary>
    /// This week with <paramref name="date"/> flipped: a date marked worked is blocked,
    /// one marked off is unblocked (local ADR 0019, §5). A flip that brings the date back
    /// to its pattern removes its override rather than storing one, so the set only ever
    /// holds dates that differ. The overrides stay sorted by date, one per date.
    /// <para>
    /// What flips is the mark, not the hours: a weekday storing an empty range reads as
    /// not worked whichever way it is marked, and a second press still returns the date
    /// to its pattern.
    /// </para>
    /// </summary>
    public WorkingHours Toggled(DateOnly date)
    {
        var worked = !MarkedWorked(date);
        var others = Overrides.Where(entry => entry.Date != date);
        var kept = worked == On(date.DayOfWeek).Working ? others : others.Append(new DayOverride(date, worked));

        return this with { Overrides = [.. kept.OrderBy(entry => entry.Date)] };
    }

    /// <summary>The longest range of days off <see cref="WithDaysOff"/> takes in one go: a
    /// year, leap day included. A range is a holiday or a leave, and a longer one is a
    /// slip of the year field that would write hundreds of overrides nobody meant.</summary>
    public const int MaxDaysOffRange = 366;

    /// <summary>
    /// This week with every date from <paramref name="from"/> through
    /// <paramref name="through"/> a day off, as the Days off dialog adds a holiday (local
    /// ADR 0019, §4): a date the pattern works is blocked, and a date the pattern leaves
    /// off holds no override — an unblocked one inside the range loses its own — so the
    /// set still only holds dates that differ from the pattern. One new week for the whole
    /// range, so a store writes it once and announces it once.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="through"/> is before
    /// <paramref name="from"/>, or the range is longer than <see cref="MaxDaysOffRange"/>
    /// days.</exception>
    public WorkingHours WithDaysOff(DateOnly from, DateOnly through)
    {
        if (through < from)
        {
            throw new ArgumentOutOfRangeException(nameof(through), through, "A range of days off ends on or after the day it starts.");
        }

        if (through.DayNumber - from.DayNumber + 1 > MaxDaysOffRange)
        {
            throw new ArgumentOutOfRangeException(nameof(through), through, $"A range of days off is at most {MaxDaysOffRange} days.");
        }

        var outside = Overrides.Where(entry => entry.Date < from || entry.Date > through);
        var blocked = Enumerable.Range(0, through.DayNumber - from.DayNumber + 1)
            .Select(from.AddDays)
            .Where(date => On(date.DayOfWeek).Working)
            .Select(date => new DayOverride(date, Worked: false));

        return this with { Overrides = [.. outside.Concat(blocked).OrderBy(entry => entry.Date)] };
    }

    /// <summary>
    /// This week with <paramref name="date"/> worked, as the Days off dialog adds a worked
    /// day (local ADR 0019, §4): unblocked when the pattern leaves its weekday off, and
    /// back to its pattern — no override — when the pattern works it. A date the pattern
    /// works and nobody blocked is this week unchanged.
    /// </summary>
    public WorkingHours WithWorkedDay(DateOnly date)
    {
        var others = Overrides.Where(entry => entry.Date != date);
        var kept = On(date.DayOfWeek).Working ? others : others.Append(new DayOverride(date, Worked: true));

        return this with { Overrides = [.. kept.OrderBy(entry => entry.Date)] };
    }

    /// <summary>This week with <paramref name="date"/> back on its pattern: its override,
    /// if it has one, removed — what removing an entry in the Days off dialog does.</summary>
    public WorkingHours WithoutOverride(DateOnly date) =>
        this with { Overrides = [.. Overrides.Where(entry => entry.Date != date).OrderBy(entry => entry.Date)] };

    /// <summary>
    /// This week, or <see cref="Default"/> when its pattern holds no working hours at all
    /// (local ADR 0019, §2). A week that works nothing cannot size a window or divide a
    /// pace; the settings screen refuses to make one a day at a time, so it takes a
    /// hand-edited file or every day switched off. The fallback keeps this week's
    /// overrides: they are dates the person set, and apply on top of whichever pattern is
    /// in effect.
    /// </summary>
    public WorkingHours Effective =>
        PerWeek > TimeSpan.Zero ? this
        : Overrides.Count == 0 ? Default
        : Default with { Overrides = Overrides };

    /// <summary>The first worked date on or after <paramref name="day"/>, in the
    /// <see cref="Effective"/> week — where work counted from <paramref name="day"/>
    /// starts. A blocked date is passed over and an unblocked one is a start.</summary>
    public DateOnly FirstWorkedDay(DateOnly day)
    {
        var effective = Effective;

        // The effective pattern works at least one weekday, and each blocked date can
        // push the search one day further, so this many steps always find one.
        var steps = 7L + effective.Overrides.Count;

        for (var offset = 0L; offset < steps && day.DayNumber + offset <= DateOnly.MaxValue.DayNumber; offset++)
        {
            var candidate = DateOnly.FromDayNumber((int)(day.DayNumber + offset));
            if (effective.IsWorked(candidate)) return candidate;
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
    /// worked date on or after the start, each worked date takes its whole hours — its
    /// override's or its pattern's — and the last is the date nothing is left. A date
    /// counts whole, so the start day is never skipped, and dates not worked add nothing.
    /// </summary>
    public DateOnly LastDayOf(DateOnly start, TimeSpan needed) => LastDayOf(start, (decimal)needed.Ticks);

    /// <summary>
    /// The walk itself, in ticks so a sum of days stays exact. Whole weeks are skipped
    /// where they can be — any seven days in a row with no override hold the whole
    /// week's hours — so an absurd amount costs no more than a small one, and is clamped
    /// to the calendar's last day. A skip never reaches past the next override, so a
    /// week holding one is walked date by date (local ADR 0019, §1).
    /// </summary>
    private DateOnly LastDayOf(DateOnly start, decimal neededTicks)
    {
        var effective = Effective;
        var perWeek = (decimal)effective.PerWeek.Ticks;
        var overridden = effective.Overrides.Select(entry => (long)entry.Date.DayNumber).Distinct().Order().ToArray();
        var next = 0;

        long day = FirstWorkedDay(start).DayNumber;
        var left = neededTicks;

        while (day <= DateOnly.MaxValue.DayNumber)
        {
            while (next < overridden.Length && overridden[next] < day) next++;

            if (left > perWeek)
            {
                // Leave at most one week to walk, and never nothing: the walk ends on a
                // worked day. Stop short of the next override, which the walk must count.
                var clear = next < overridden.Length ? (overridden[next] - day) / 7 : long.MaxValue;
                var weeks = Math.Min(Math.Ceiling(left / perWeek) - 1, clear);

                if (weeks > 0)
                {
                    if (day + weeks * 7 > DateOnly.MaxValue.DayNumber) return DateOnly.MaxValue;

                    day += (long)(weeks * 7);
                    left -= weeks * perWeek;
                }
            }

            var date = DateOnly.FromDayNumber((int)day);
            left -= effective.WorkedOn(date).Ticks;
            if (left <= 0) return date;

            day++;
        }

        return DateOnly.MaxValue;
    }

    /// <summary>Whether <paramref name="date"/> is marked worked — its override, or its
    /// weekday's flag — before its hours are looked at.</summary>
    private bool MarkedWorked(DateOnly date) =>
        OverrideOn(date)?.Worked ?? On(date.DayOfWeek).Working;

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

    /// <summary>
    /// Puts the weekly pattern back to <see cref="WorkingHours.Default"/>. The
    /// <see cref="WorkingHours.Overrides"/> are kept: reset is the settings screen's
    /// action on the pattern it shows, and the blocked and unblocked dates were set on
    /// the roadmap's axis, where the person can see and undo each one.
    /// </summary>
    string? ResetToDefault();

    /// <summary>
    /// Blocks <paramref name="date"/> when it is marked worked, or unblocks it when it is
    /// not (<see cref="WorkingHours.Toggled"/>; local ADR 0019, §5), stores the result and
    /// raises <see cref="Changed"/>. Returns null when it was stored, or a message saying
    /// why not.
    /// </summary>
    string? ToggleDate(DateOnly date);

    /// <summary>
    /// Makes every date from <paramref name="from"/> through <paramref name="through"/> a
    /// day off (<see cref="WorkingHours.WithDaysOff"/>; local ADR 0019, §4) — one store and
    /// one <see cref="Changed"/> for the whole range. Returns null when it was stored or
    /// changed nothing, or why it was refused: a range ending before it starts, or one
    /// longer than <see cref="WorkingHours.MaxDaysOffRange"/> days.
    /// </summary>
    string? BlockDays(DateOnly from, DateOnly through);

    /// <summary>
    /// Makes <paramref name="date"/> worked (<see cref="WorkingHours.WithWorkedDay"/>),
    /// stores it and raises <see cref="Changed"/>. Returns null when it was stored, or a
    /// note saying why nothing was: the pattern already works that date.
    /// </summary>
    string? AddWorkedDay(DateOnly date);

    /// <summary>
    /// Returns <paramref name="date"/> to its pattern (<see cref="WorkingHours.WithoutOverride"/>),
    /// stores it and raises <see cref="Changed"/> when it had an override. Returns null when
    /// it was stored or there was nothing to remove, or a message saying why not.
    /// </summary>
    string? RemoveDayOverride(DateOnly date);
}
