using System.Globalization;

namespace Backlog.UI.Components.Progress;

/// <summary>
/// The fixed phrases the Goals view and its stand-up print
/// (<c>.devbook/domain/progress/features.md#the-goals-view</c> and
/// <c>#the-stand-up</c>). Pure, so the board, its parts and <see cref="StandUpClipboard"/>
/// read the same words. Dates are English and culture-independent: "30 Oct", "check Tue".
/// </summary>
public static class GoalsText
{
    private const string Separator = " · ";

    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <summary>"12–16 Oct → 19–23 Oct": the last window, then the next.</summary>
    public static string Window(GoalsWindow last, GoalsWindow next)
    {
        ArgumentNullException.ThrowIfNull(last);
        ArgumentNullException.ThrowIfNull(next);
        return $"{Range(last)} → {Range(next)}";
    }

    /// <summary>One window: "16 Oct" for a day, "12–16 Oct" inside a month, "28 Sep – 2 Oct"
    /// across two.</summary>
    public static string Range(GoalsWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.First == window.Last) return Day(window.First);
        if (window.First.Year == window.Last.Year && window.First.Month == window.Last.Month)
            return $"{window.First.Day.ToString(English)}–{Day(window.Last)}";

        return $"{Day(window.First)} – {Day(window.Last)}";
    }

    /// <summary>"9/15 · +3 · due 30 Oct · lands ~6 Nov": <c>+n</c> left out at nought, the
    /// landing left out with no forecast, and "due 23 Oct (end of iteration)" for a goal
    /// without a due date of its own.</summary>
    public static string GoalLine(GoalCardModel goal)
    {
        ArgumentNullException.ThrowIfNull(goal);

        var parts = new List<string> { $"{goal.Counts.Done}/{goal.Counts.Total}" };
        if (goal.Counts.DoneInWindow > 0) parts.Add($"+{goal.Counts.DoneInWindow}");
        parts.Add(Due(goal.DueDate, goal.DueIsIterationEnd));
        if (goal.LandsOn is { } landsOn) parts.Add($"lands ~{Day(landsOn)}");

        return string.Join(Separator, parts);
    }

    /// <summary>"due 30 Oct", or "due 23 Oct (end of iteration)" when the day is the
    /// iteration's end because the goal has no due date of its own.</summary>
    public static string Due(DateOnly day, bool isIterationEnd) =>
        isIterationEnd ? $"due {Day(day)} (end of iteration)" : $"due {Day(day)}";

    /// <summary>The forecast badge's words.</summary>
    public static string Forecast(bool atRisk) => atRisk ? "At risk" : "On track";

    /// <summary>"9 done, 3 in progress, 1 blocked, 2 not started": the stacked bar for a
    /// screen reader.</summary>
    public static string BarLabel(GoalCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return $"{counts.Done} done, {counts.Doing} in progress, {counts.Blocked} blocked, {counts.ToDo} not started";
    }

    /// <summary>One item's effort: "5 pts".</summary>
    public static string Points(int points) => $"{points.ToString(English)} pts";

    /// <summary>"21 / 18 pts", or "21 pts" without a pace.</summary>
    public static string PointsTag(int planned, int? capacity) =>
        capacity is { } pace ? $"{planned} / {pace} pts" : $"{planned} pts";

    /// <summary>"21 points planned, you usually finish 18 a week", the period word
    /// following the length; null without a pace.</summary>
    public static string? PointsTitle(int planned, int? capacity, GoalsIteration length) =>
        capacity is { } pace ? $"{planned} points planned, you usually finish {pace} {Period(length)}" : null;

    /// <summary>"Account manager · 3 days", or "· check Tue" when a check-again day is
    /// set. With nobody named, a New one reads "Not reported yet · 1 day" and a later state
    /// reads its age alone, as the phone does.</summary>
    public static string ImpedimentMeta(StandUpImpediment impediment)
    {
        ArgumentNullException.ThrowIfNull(impediment);

        var when = impediment.CheckAgain is { } day ? CheckAgain(day) : Days(impediment.DaysOpen);
        if (!string.IsNullOrWhiteSpace(impediment.With)) return impediment.With.Trim() + Separator + when;
        return impediment.State == StandUpImpedimentState.New ? "Not reported yet" + Separator + when : when;
    }

    /// <summary>The meta line with the goal it holds back after it.</summary>
    public static string ImpedimentLine(StandUpImpediment impediment)
    {
        ArgumentNullException.ThrowIfNull(impediment);

        var meta = ImpedimentMeta(impediment);
        return string.IsNullOrWhiteSpace(impediment.GoalTitle) ? meta : meta + Separator + impediment.GoalTitle;
    }

    /// <summary>"check Tue".</summary>
    public static string CheckAgain(DateOnly day) => "check " + day.ToString("ddd", English);

    /// <summary>"1 day", "3 days".</summary>
    public static string Days(int days) => days == 1 ? "1 day" : $"{days.ToString(English)} days";

    /// <summary>A state's display words: "Being handled".</summary>
    public static string State(StandUpImpedimentState state) => state switch
    {
        StandUpImpedimentState.New => "New",
        StandUpImpedimentState.Reported => "Reported",
        StandUpImpedimentState.Escalated => "Escalated",
        StandUpImpedimentState.BeingHandled => "Being handled",
        _ => "Resolved"
    };

    /// <summary>A switch label: "2 weeks".</summary>
    public static string Iteration(GoalsIteration length) => length switch
    {
        GoalsIteration.Day => "Day",
        GoalsIteration.Week => "Week",
        GoalsIteration.TwoWeeks => "2 weeks",
        _ => "Month"
    };

    private static string Period(GoalsIteration length) => length switch
    {
        GoalsIteration.Day => "a day",
        GoalsIteration.Week => "a week",
        GoalsIteration.TwoWeeks => "in two weeks",
        _ => "a month"
    };

    private static string Day(DateOnly day) => day.ToString("d MMM", English);
}
