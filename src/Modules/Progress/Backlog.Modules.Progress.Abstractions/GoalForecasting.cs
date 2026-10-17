using Backlog.SharedKernel;

namespace Backlog.Modules.Progress.Abstractions;

/// <summary>When a goal is expected to land, and whether that is in time
/// (<c>.devbook/domain/progress/domain.md#goal-forecast</c>). Equality by value.</summary>
/// <param name="LandsOn">The last day of the iteration the goal is expected to land in;
/// null with no open item, or with no item done in the four iterations read.</param>
/// <param name="DoneRate">The items done per iteration, averaged over the four read.</param>
public sealed record GoalForecast(GoalStanding Standing, DateOnly? LandsOn, decimal DoneRate);

/// <summary>
/// Forecasts a goal from its done rate (<c>.devbook/domain/progress/domain.md#goal-forecast</c>):
/// the items done in each of the four iterations ending with last, averaged. At that rate
/// the open items need <c>n</c> more iterations, counting next as the first, and the goal
/// lands on the last day of the <c>n</c>-th. It is At risk when that day falls after the due
/// date, when it has open items and no landing date, or when an open impediment holds back
/// one of its items; landing on the due date is On track, and so is a goal with nothing open.
/// Pure, and in the published surface beside <see cref="Iterations"/>.
/// </summary>
public static class GoalForecasting
{
    /// <summary>How many iterations, ending with last, the done rate is read over.</summary>
    public const int HistoryLength = 4;

    /// <param name="open">The goal's open items: its total less those done.</param>
    /// <param name="doneOn">The day each done item was done, in any order; a day no window of
    /// the four iterations read contains is ignored.</param>
    /// <param name="next">The window the reading calls next.</param>
    /// <param name="heldBack">Whether an open impediment holds back one of the goal's items.</param>
    public static GoalForecast Forecast(
        int open,
        IEnumerable<DateOnly> doneOn,
        IterationWindow next,
        DateOnly dueDate,
        bool heldBack,
        WorkingHours week)
    {
        ArgumentNullException.ThrowIfNull(doneOn);
        ArgumentNullException.ThrowIfNull(week);

        var history = Iterations.History(Iterations.Previous(next, week), HistoryLength, week);
        // Inside a window, not merely inside the span: a Saturday between two Week windows
        // belongs to no iteration and adds nothing to the rate.
        var done = doneOn.Count(day => history.Any(window => window.Contains(day)));
        var rate = (decimal)done / HistoryLength;

        if (open <= 0) return new GoalForecast(GoalStanding.OnTrack, null, rate);
        if (done == 0) return new GoalForecast(GoalStanding.AtRisk, null, rate);

        // ceil(open / rate), in whole numbers so no rate is ever rounded.
        var needed = ((open * HistoryLength) + done - 1) / done;

        var landing = next;
        for (var i = 1; i < needed; i++) landing = Iterations.Following(landing, week);

        var standing = heldBack || landing.Last > dueDate ? GoalStanding.AtRisk : GoalStanding.OnTrack;
        return new GoalForecast(standing, landing.Last, rate);
    }
}
