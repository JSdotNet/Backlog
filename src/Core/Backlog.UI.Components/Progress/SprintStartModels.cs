namespace Backlog.UI.Components.Progress;

/// <summary>What starting a series' first sprint asks for: the day it starts on and its
/// goal in words, trimmed — empty for none.</summary>
public sealed record SprintStart(DateOnly On, string Goal);

/// <summary>
/// The rules the Start dialog offers a day by
/// (<c>.devbook/domain/progress/features.md#starting-the-first-sprint</c>). The module
/// holds the same rule — a sprint starts on its series' start weekday — and refuses a
/// day that breaks it; the dialog checks first only so the reason sits beside the field.
/// </summary>
public static class SprintStartRules
{
    /// <summary>The day a first sprint is offered to start on: today when today is the
    /// series' start weekday, else the next one.</summary>
    public static DateOnly DefaultStart(DateOnly today, DayOfWeek startWeekday) =>
        today.AddDays(((int)startWeekday - (int)today.DayOfWeek + 7) % 7);

    /// <summary>Whether a sprint of the series may start on <paramref name="day"/>.</summary>
    public static bool IsStartDay(DateOnly day, DayOfWeek startWeekday) => day.DayOfWeek == startWeekday;
}
