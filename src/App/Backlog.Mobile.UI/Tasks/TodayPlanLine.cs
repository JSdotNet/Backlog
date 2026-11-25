using Backlog.Modules.Tasks.Abstractions;

using MyDayRule = Backlog.Modules.Tasks.Abstractions.MyDay;

namespace Backlog.Mobile.UI.Tasks;

/// <summary>What a plan's one line on the Today tab says of its run.</summary>
public enum TodayPlanState
{
    /// <summary>Nothing to say: no entry yet, or none open that is ready, blocked or a draft.</summary>
    None,

    /// <summary>An open entry of the plan is in progress — the run, or a person, is on it.</summary>
    InProgress,

    /// <summary>An open entry is marked blocked and none is ready.</summary>
    Blocked,

    /// <summary>Open entries are ready to pick up.</summary>
    Ready,

    /// <summary>Every open entry is still a draft.</summary>
    Draft,

    /// <summary>Every entry is ticked off.</summary>
    Done
}

/// <summary>
/// One plan in today's My Day as the phone's Today tab draws it (local ADR 0062 §8;
/// <c>.devbook/domain/tasks/features.md#my-day</c>): its name, how many of its entries are
/// done, and its run state — one line, without its entries.
/// <para>
/// Pure: handed the plans on My Day, the tasks the phone holds and the day, it decides
/// nothing else. A plan is in today's My Day by exactly the rule a task is
/// (<see cref="MyDayRule.Includes"/>) under the same Clear My Day overnight switch, with
/// the plan finished on the day its last entry was ticked off.
/// </para>
/// </summary>
/// <param name="Name">The plan's name: its tag without the <c>+</c>.</param>
/// <param name="Done">Its entries ticked off.</param>
/// <param name="Total">All its entries, open and ticked.</param>
/// <param name="State">Its run state.</param>
/// <param name="ReadyCount">Its open entries that are ready and not marked blocked.</param>
public sealed record TodayPlanLine(string Name, int Done, int Total, TodayPlanState State, int ReadyCount)
{
    /// <summary>The run state in the line's words — <c>In progress</c>, <c>Blocked</c>,
    /// <c>3 ready</c>, <c>Draft</c>, <c>Done</c> — or empty.</summary>
    public string StateText => State switch
    {
        TodayPlanState.InProgress => "In progress",
        TodayPlanState.Blocked => "Blocked",
        TodayPlanState.Ready => $"{ReadyCount} ready",
        TodayPlanState.Draft => "Draft",
        TodayPlanState.Done => "Done",
        _ => string.Empty,
    };

    /// <summary>The line's accessible sentence: name, progress and state.</summary>
    public string Label => StateText.Length == 0
        ? $"{Name}, {Done} of {Total} done"
        : $"{Name}, {Done} of {Total} done, {StateText}";

    /// <summary>
    /// The plans in the My Day of <paramref name="today"/>, one line each, in the order
    /// <paramref name="plansOnMyDay"/> lists them — the roadmap's own item order.
    /// </summary>
    /// <param name="plansOnMyDay">Each plan put on My Day, by <see cref="PlanKey"/>, and the
    /// day it was picked for.</param>
    /// <param name="tasks">The tasks the phone holds; deleted ones are passed over.</param>
    /// <param name="clearsOvernight">Clear My Day overnight, as a desktop last set it.</param>
    public static IReadOnlyList<TodayPlanLine> For(
        IEnumerable<(string Plan, DateOnly On)> plansOnMyDay,
        IEnumerable<TaskViewRow> tasks,
        DateOnly today,
        bool clearsOvernight)
    {
        ArgumentNullException.ThrowIfNull(plansOnMyDay);
        ArgumentNullException.ThrowIfNull(tasks);

        var stamps = plansOnMyDay.ToList();
        if (stamps.Count == 0) return [];

        var live = tasks
            .Where(row => row.DeletedAt is null && !IsArchived(row))
            .ToList();
        var lines = new List<TodayPlanLine>();

        foreach (var (plan, on) in stamps)
        {
            var entries = live.Where(row => PlanOf(row) == plan).ToList();
            if (!MyDayRule.Includes(on, FinishedOn(entries), today, clearsOvernight)) continue;

            lines.Add(Line(plan, entries));
        }

        return lines;
    }

    /// <summary>
    /// A plan tag as Roadmap Planning keys its items, and the desktop with it: the bare tag
    /// lower-cased, every run of anything not a letter or digit one hyphen, the ends trimmed
    /// — so <c>+Lets_BBQ</c> and the item tagged <c>lets-bbq</c> are one plan.
    /// </summary>
    public static string PlanKey(string? tag)
    {
        var bare = (tag ?? string.Empty).Trim().TrimStart('+', '#').Trim().ToLowerInvariant();
        var key = new System.Text.StringBuilder(bare.Length);
        var hyphen = false;

        foreach (var character in bare)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (hyphen && key.Length > 0) key.Append('-');
                key.Append(character);
                hyphen = false;
            }
            else
            {
                hyphen = true;
            }
        }

        return key.ToString();
    }

    private static TodayPlanLine Line(string plan, IReadOnlyList<TaskViewRow> entries)
    {
        var open = entries.Where(row => !TodayPlan.IsTicked(row)).ToList();
        var ready = open.Count(row => StatusOf(row) is EntryStatus.Ready && row.Task.BlockedSince is null);
        var done = entries.Count - open.Count;

        var state = open.Any(row => StatusOf(row) is EntryStatus.InProgress) ? TodayPlanState.InProgress
            : ready == 0 && open.Any(row => row.Task.BlockedSince is not null) ? TodayPlanState.Blocked
            : ready > 0 ? TodayPlanState.Ready
            : open.Count > 0 && open.All(row => StatusOf(row) is EntryStatus.Draft) ? TodayPlanState.Draft
            : entries.Count > 0 && open.Count == 0 ? TodayPlanState.Done
            : TodayPlanState.None;

        return new TodayPlanLine(plan, done, entries.Count, state, ready);
    }

    /// <summary>The plan's key of the first plan tag a task carries, or null.</summary>
    private static string? PlanOf(TaskViewRow row) =>
        row.Task.Tags.FirstOrDefault(tag => tag.TrimStart().StartsWith('+')) is { } tag ? PlanKey(tag) : null;

    /// <summary>The day the plan was finished — its last entry ticked off, once every entry
    /// is — or null while any is open or it gathers none.</summary>
    private static DateOnly? FinishedOn(IReadOnlyList<TaskViewRow> entries)
    {
        DateOnly? last = null;

        foreach (var row in entries)
        {
            if (row.Task.CompletedOn is not { } finished) return null;
            if (last is null || finished > last) last = finished;
        }

        return last;
    }

    private static bool IsArchived(TaskViewRow row) =>
        string.Equals(row.Task.Status, "archived", StringComparison.OrdinalIgnoreCase);

    private static EntryStatus? StatusOf(TaskViewRow row)
    {
        try
        {
            return EnumMap.ParseStatus(row.Task.Status?.Trim() ?? string.Empty);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
