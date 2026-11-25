using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>One plan on today's My Day, as <b>Plans today</b> lists it (local ADR 0062 §8).</summary>
/// <param name="Plan">The plan, as its rail row reads it: name, progress and run state.</param>
/// <param name="ForYou">Only its entries the person holds: left for you, held for you, or
/// awaiting validation.</param>
public sealed record PlanToday(PlanRailPlan Plan, IReadOnlyList<(EntryRow Row, PlanEntryState State)> ForYou)
{
    /// <summary>The run state as the line words it: the rail's words, a running plan said to
    /// run unattended, and <c>Not queued</c> when it has none.</summary>
    public string StateText => Plan.State switch
    {
        PlanRunState.Running => "Running · unattended",
        _ when Plan.StateText.Length > 0 => Plan.StateText,
        _ => "Not queued",
    };

    /// <summary>Why an entry is the person's, as its line says it.</summary>
    public static string ReasonFor(EntryRow row, PlanEntryState state)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(state);

        return state.State switch
        {
            PlanEntryQueueState.LeftForYou when row.PreviewType is EntryType.Test => "Test, left for you",
            PlanEntryQueueState.LeftForYou => "Task, yours to do",
            PlanEntryQueueState.HeldForYou => "Held for you",
            _ => "Awaiting validation",
        };
    }
}

/// <summary>
/// A whole plan on My Day in the Tasks pane (local ADR 0062 §8;
/// <c>.devbook/domain/tasks/features.md#my-day</c>): the sun on the plan's heading and its
/// waves view, the rail's My Day count over plans and tasks, and <b>Plans today</b> above the
/// tasks while the list shows today's My Day. My Day shows no suggestions.
/// </summary>
public partial class TasksPane
{
    /// <summary>The plan whose sun press is being stored, so a second press on that sun is
    /// not taken meanwhile; the other plans' suns stay free.</summary>
    private string? _planSunBusy;

    private bool IsPlanSunBusy(string plan) =>
        _planSunBusy is { } busy && string.Equals(PlanQueueOffer.Bare(busy), PlanQueueOffer.Bare(plan), StringComparison.OrdinalIgnoreCase);

    /// <summary>The plans in today's My Day, with what each shows. Each plan is counted the
    /// way its rail row counts it, over every entry it gathers — not over what the My Day
    /// scope leaves in view, which holds only the entries picked for today. A plan whose
    /// item its sun created before any entry carried its tag reads as empty.</summary>
    private IReadOnlyList<PlanToday> PlansToday()
    {
        if (State.MyDayOn is not { } day) return [];

        var tags = State.PlansInMyDay(day);
        if (tags.Count == 0) return [];

        var options = tags
            .Select(tag =>
            {
                var entries = State.PlanEntryStatesOf(tag);
                var done = entries.Count(pair => pair.Row.IsPreviewCompleted);
                return new TagFilterOption(tag, tag, entries.Count - done, done);
            })
            .ToList();
        var plans = PlanRail.Plans(options, State.Rows, _planSessions.Open, SessionQueueRows());

        return [.. plans.Select(plan => new PlanToday(plan, State.PlanEntriesForYou(plan.Tag, PlanRunOf(plan.Tag))))];
    }

    /// <summary>Whether Plans today is drawn: the list, narrowed to today's My Day, with a
    /// plan in it.</summary>
    private bool PlansTodayShown =>
        Layout == TasksLayout.List && State.MyDayOn is { } day && State.PlansInMyDay(day).Count > 0;

    /// <summary>How many plans in today's My Day are still open, for the rail's and the
    /// chip's count: open, as the tasks beside them are counted, so a plan finished today
    /// stays listed but is no longer something to do.</summary>
    private int PlansInMyDayCount => State.PlansInMyDay(Today).Count(State.IsPlanOpen);

    private async Task OnPlanSunPressedAsync(string plan)
    {
        if (IsPlanSunBusy(plan)) return;

        _planSunBusy = plan;
        try
        {
            await State.TogglePlanMyDayAsync(plan, Today);
        }
        finally
        {
            _planSunBusy = null;
        }
    }

    /// <summary>The sun's tooltip: what pressing it does to the plan. Its accessible name
    /// stays <c>+plan in My Day</c>, with the pressed state saying whether it is.</summary>
    private string PlanSunTitle(string plan) =>
        State.IsPlanInMyDay(plan, Today)
            ? $"Take {plan} off My Day"
            : $"Put {plan} on My Day";
}
