using System.Globalization;
using System.Text.RegularExpressions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>The three groups the Plans view reads the selected plan in (local ADR 0062 §3).</summary>
public enum PlanSection
{
    /// <summary>In a session.</summary>
    Now,

    /// <summary>Ready to pick up, queued with the plan while it is queued.</summary>
    Next,

    /// <summary>Still waiting on another entry, or not yet ready itself.</summary>
    Waiting,

    /// <summary>Finished, or merged into the plan branch.</summary>
    Done
}

/// <summary>One entry of the selected plan, in its group.</summary>
/// <param name="Row">The entry.</param>
/// <param name="State">The state its plan's run gives it.</param>
/// <param name="Section">The group it reads under.</param>
/// <param name="Number">How a line names it: <c>#14</c> off a numbered title, else the title.</param>
public sealed record PlanSectionEntry(EntryRow Row, PlanEntryState State, PlanSection Section, string Number);

/// <summary>The selected plan, expanded: its heading and its Now, Next and Later groups.</summary>
/// <param name="Tag">The plan tag, <c>+</c> and all.</param>
/// <param name="Name">The plan's name, the tag without its sigil.</param>
/// <param name="Entries">Every entry of the plan with its group, in the store's order.</param>
/// <param name="PointsLeft">The effort of the entries not yet done.</param>
/// <param name="RunState">The plan's run state, as its rail row reads it.</param>
/// <param name="UnlockLine">Later's waiting line.</param>
/// <param name="DoneLine">Later's done line.</param>
public sealed record PlanSectionsView(
    string Tag,
    string Name,
    IReadOnlyList<PlanSectionEntry> Entries,
    int PointsLeft,
    PlanRunState RunState,
    string UnlockLine,
    string DoneLine)
{
    /// <summary>The entries in a session.</summary>
    public IReadOnlyList<PlanSectionEntry> Now => [.. Entries.Where(entry => entry.Section is PlanSection.Now)];

    /// <summary>The ready entries.</summary>
    public IReadOnlyList<PlanSectionEntry> Next => [.. Entries.Where(entry => entry.Section is PlanSection.Next)];

    /// <summary>The waiting entries, Later's first line.</summary>
    public IReadOnlyList<PlanSectionEntry> Waiting => [.. Entries.Where(entry => entry.Section is PlanSection.Waiting)];

    /// <summary>The done entries, Later's second line.</summary>
    public IReadOnlyList<PlanSectionEntry> Done => [.. Entries.Where(entry => entry.Section is PlanSection.Done)];

    /// <summary>All the plan's entries.</summary>
    public int Total => Entries.Count;

    /// <summary>The plan branch its work lands on.</summary>
    public string Branch => PlanEntryState.PlanBranchPrefix + Name;

    /// <summary>The heading's count: <c>8 of 24 · 71 pts left</c>.</summary>
    public string ProgressText =>
        $"{Done.Count} of {Total} · {PointsLeft.ToString(CultureInfo.InvariantCulture)} {(PointsLeft == 1 ? "pt" : "pts")} left";

    /// <summary>The heading's run state, or empty when it has none to say.</summary>
    public string RunStateText => RunState switch
    {
        PlanRunState.Running => "Plan session running · unattended",
        PlanRunState.Queued => "Queued",
        _ => string.Empty,
    };

    /// <summary>The bar's three segments, each 0 to 100 of the plan's entries.</summary>
    public (int Done, int Now, int Next) BarPercents => Total == 0
        ? (0, 0, 0)
        : (Percent(Done.Count), Percent(Now.Count), Percent(Next.Count));

    private int Percent(int count) => (int)Math.Round(count * 100.0 / Total, MidpointRounding.AwayFromZero);
}

/// <summary>One plan folded to a line under the expanded one.</summary>
/// <param name="Plan">The plan, as its rail row reads it.</param>
/// <param name="Note">The line's note: its queue place, its ready and waiting counts, or
/// that it is blocked.</param>
public sealed record FoldedPlan(PlanRailPlan Plan, string Note);

/// <summary>
/// The Plans view's main column (local ADR 0062 §3;
/// <c>.devbook/domain/tasks/requirements.md#requirement-the-selected-plan-reads-as-now-next-and-later</c>):
/// the selected plan read as Now, Next and Later, the other plans folded to one line
/// each. Pure, over the plan entry states the Tasks state already derives.
/// </summary>
public static partial class PlanSections
{
    /// <summary>How many entries Later's waiting line names as next to unlock.</summary>
    public const int UnlockNamed = 3;

    /// <summary>The group a plan entry state reads under. A ready <c>test</c> or
    /// <c>task</c> that waits on no open entry, and a held entry, are Next — their state
    /// says why the run leaves them; one not ready, or waiting on another open entry,
    /// is waiting like any other and keeps its Left for you label.</summary>
    public static PlanSection SectionOf(EntryRow row, PlanEntryState state)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(state);

        return state.State switch
        {
            PlanEntryQueueState.Done or PlanEntryQueueState.Landed => PlanSection.Done,
            PlanEntryQueueState.InPlanSession => PlanSection.Now,
            PlanEntryQueueState.Ready or PlanEntryQueueState.QueuedWithPlan or PlanEntryQueueState.HeldForYou => PlanSection.Next,
            PlanEntryQueueState.LeftForYou when row.PreviewStatus is EntryStatus.Ready && !state.WaitsOnOpen => PlanSection.Next,
            _ => PlanSection.Waiting,
        };
    }

    /// <summary>The selected plan, expanded.</summary>
    /// <param name="plan">The plan as its rail row reads it.</param>
    /// <param name="states">Its entries with their states, in the store's order.</param>
    /// <param name="dependsOn">What an entry waits on, as resolved task ids.</param>
    public static PlanSectionsView Build(
        PlanRailPlan plan,
        IReadOnlyList<(EntryRow Row, PlanEntryState State)> states,
        Func<EntryRow, IReadOnlyList<string>> dependsOn)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(dependsOn);

        var entries = states
            .Select(pair => new PlanSectionEntry(pair.Row, pair.State, SectionOf(pair.Row, pair.State), NumberOf(pair.Row.PreviewTitle)))
            .ToList();

        var pointsLeft = entries.Where(entry => entry.Section is not PlanSection.Done).Sum(entry => entry.Row.PreviewEffort ?? 0);

        return new PlanSectionsView(
            plan.Tag,
            plan.Name,
            entries,
            pointsLeft,
            plan.State,
            UnlockLine(entries, dependsOn),
            DoneLine(entries, PlanEntryState.PlanBranchPrefix + plan.Name));
    }

    /// <summary>
    /// Later's waiting line: <c>10 waiting — next to unlock: #20 when #14 and #17 land</c>.
    /// Next to unlock is a waiting entry every open entry it waits on is itself not
    /// waiting — in a session or ready — so it is one landing away; at most
    /// <see cref="UnlockNamed"/> are named, in the store's order. With none so placed
    /// the line is the count alone.
    /// </summary>
    public static string UnlockLine(IReadOnlyList<PlanSectionEntry> entries, Func<EntryRow, IReadOnlyList<string>> dependsOn)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(dependsOn);

        var waiting = entries.Where(entry => entry.Section is PlanSection.Waiting).ToList();
        if (waiting.Count == 0) return "Nothing waiting";

        var byId = new Dictionary<string, PlanSectionEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) byId.TryAdd(entry.Row.TaskId, entry);

        var unlocks = new List<string>();
        foreach (var entry in waiting)
        {
            if (unlocks.Count == UnlockNamed) break;

            var open = dependsOn(entry.Row)
                .Select(id => byId.GetValueOrDefault(id))
                .Where(blocker => blocker is not null && blocker.Section is not PlanSection.Done)
                .Select(blocker => blocker!)
                .ToList();

            if (open.Count == 0 || open.Any(blocker => blocker.Section is PlanSection.Waiting)) continue;

            unlocks.Add($"{entry.Number} when {JoinAnd(open.Select(blocker => blocker.Number))} {(open.Count == 1 ? "lands" : "land")}");
        }

        var count = $"{waiting.Count.ToString(CultureInfo.InvariantCulture)} waiting";

        return unlocks.Count == 0 ? count : $"{count} — next to unlock: {string.Join(", ", unlocks)}";
    }

    /// <summary>
    /// Later's done line: <c>8 done — 6 merged into plan/x · last: #13, PR #1841</c>, the
    /// last being the latest done entry, in the store's order, whose pull request merged
    /// into the plan branch.
    /// </summary>
    public static string DoneLine(IReadOnlyList<PlanSectionEntry> entries, string branch)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var done = entries.Where(entry => entry.Section is PlanSection.Done).ToList();
        if (done.Count == 0) return "Nothing done yet";

        var merged = done
            .Select(entry => (entry, pullRequest: MergedInto(entry.Row, branch)))
            .Where(pair => pair.pullRequest is not null)
            .ToList();

        var count = $"{done.Count.ToString(CultureInfo.InvariantCulture)} done";
        if (merged.Count == 0) return count;

        var (last, number) = merged[^1];

        return $"{count} — {merged.Count.ToString(CultureInfo.InvariantCulture)} merged into {branch} · last: {last.Number}, PR #{number!.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>The plans folded under the expanded one, in the rail's order, each with its note.</summary>
    /// <param name="plans">The rail's plans.</param>
    /// <param name="selectedTag">The expanded plan's tag, left out.</param>
    /// <param name="countsOf">How many of a plan's entries read under Next and how many
    /// wait, by tag.</param>
    public static IReadOnlyList<FoldedPlan> Folded(
        IReadOnlyList<PlanRailPlan> plans,
        string? selectedTag,
        Func<string, (int Ready, int Waiting)> countsOf)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(countsOf);

        return
        [
            .. plans
                .Where(plan => !string.Equals(plan.Tag, selectedTag, StringComparison.OrdinalIgnoreCase))
                .Select(plan =>
                {
                    var (ready, waiting) = countsOf(plan.Tag);

                    return new FoldedPlan(plan, FoldedNote(plan, ready, waiting));
                })
        ];
    }

    /// <summary>A folded plan's note: <c>Queued · 1st up next</c>, <c>Plan session
    /// running</c>, <c>Blocked</c>, or its ready and waiting counts — ready as Next
    /// reads it, so an entry waiting on another is counted waiting, not ready.</summary>
    public static string FoldedNote(PlanRailPlan plan, int ready, int waiting)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.State switch
        {
            PlanRunState.Running => "Plan session running",
            PlanRunState.Queued => plan.QueuePlace > 0 ? $"Queued · {PlanRail.Ordinal(plan.QueuePlace)} up next" : "Queued",
            PlanRunState.Blocked => waiting > 0 ? $"Blocked · {waiting.ToString(CultureInfo.InvariantCulture)} waiting" : "Blocked",
            PlanRunState.Draft => "Draft",
            _ => $"{ready.ToString(CultureInfo.InvariantCulture)} ready · {waiting.ToString(CultureInfo.InvariantCulture)} waiting",
        };
    }

    /// <summary>How a line names an entry: <c>#14</c> off a title numbered
    /// <c>14 - …</c> (the import grammar's step number), else the title itself.</summary>
    public static string NumberOf(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "an entry";

        var match = StepNumber().Match(title);

        return match.Success ? "#" + match.Groups[1].Value : title.Trim();
    }

    /// <summary>"#14", "#14 and #17", "#14, #15 and #17".</summary>
    public static string JoinAnd(IEnumerable<string> parts)
    {
        var list = parts.ToList();

        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
        };
    }

    /// <summary>The number of the pull request of <paramref name="row"/> merged into
    /// <paramref name="branch"/>, or null.</summary>
    private static int? MergedInto(EntryRow row, string branch)
    {
        foreach (var (link, pullRequest) in row.PullRequestStatuses)
        {
            if (pullRequest.State is LinkedPullRequestState.Merged
                && string.Equals(pullRequest.BaseRefName, branch, StringComparison.OrdinalIgnoreCase))
            {
                return link.Number;
            }
        }

        return null;
    }

    [GeneratedRegex(@"^\s*(\d+)\s+-\s+")]
    private static partial Regex StepNumber();
}
