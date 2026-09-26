using Backlog.Modules.Tasks.Abstractions;
using Backlog.UI.Components.Badges;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// How much work is open in the rows the repository scope leaves in view: the
/// three numbers the filter bar's summary button says.
/// <para>
/// Its own record rather than read off a whole <see cref="OpenWorkReport"/>
/// because the button is drawn on every render of the pane and the report is only
/// wanted while it is open — and the report is the half that resolves
/// dependencies.
/// </para>
/// <para>
/// Open is "not ticked off", the same test the tag chips' open counts use. Points
/// are summed over the open rows that carry an estimate — zero is one — and the
/// rows with none are counted beside the total rather than silently as zero, which
/// is the rule <c>.devbook/domain/roadmap/features.md</c> sets for every effort total.
/// </para>
/// </summary>
public sealed record OpenWorkTotals(int Open, int Points, int Unestimated)
{
    public static OpenWorkTotals Of(IReadOnlyList<EntryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var open = 0;
        var points = 0;
        var unestimated = 0;

        foreach (var row in rows)
        {
            if (row.IsPreviewCompleted) continue;

            open++;

            if (row.PreviewEffort is { } effort) points += effort;
            else unestimated++;
        }

        return new OpenWorkTotals(open, points, unestimated);
    }

    /// <summary>The button's face, in shorthand — "pts" and "unest." — so it costs
    /// the filter bar as little width as it can; <see cref="Description"/> spells
    /// it out for the tooltip and the accessible name.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{Open} open", Points == 1 ? "1 pt" : $"{Points} pts" };

            if (Unestimated > 0) parts.Add($"{Unestimated} unest.");

            return string.Join(" · ", parts);
        }
    }

    /// <summary>The Waiting line in Needs attention: "3 tasks · 5 points · 1
    /// unestimated", the summary's shape with a noun that does not repeat the
    /// heading above it.</summary>
    public string WaitingSummary
    {
        get
        {
            var parts = new List<string>
            {
                Open == 1 ? "1 task" : $"{Open} tasks",
                OpenWorkReport.PointsText(Points)
            };

            if (Unestimated > 0) parts.Add($"{Unestimated} unestimated");

            return string.Join(" · ", parts);
        }
    }

    /// <summary>The same three facts as a sentence, for the tooltip and the
    /// accessible name: "pts" and a bare number beside "open" are shorthand a
    /// screen reader would read out without the meaning.</summary>
    public string Description
    {
        get
        {
            var tasks = Open == 1 ? "1 open task" : $"{Open} open tasks";
            var sentence = $"{tasks} in scope, {OpenWorkReport.PointsText(Points)} estimated";

            if (Unestimated > 0)
            {
                sentence += Unestimated == 1 ? ", 1 not estimated" : $", {Unestimated} not estimated";
            }

            return sentence + ". Open the report.";
        }
    }
}

/// <summary>
/// What the open work in scope adds up to, for the Tasks pane's "Open work" report.
/// <para>
/// A pure record built from what the pane hands it: the scoped rows, the reader's
/// local date, and three questions the pane's state already answers — whether a
/// row is ready, which repositories it resolves to, and what it waits on. No clock
/// and no services, for the reason the pane reads the date and the state does not:
/// "overdue" and "the last seven days" are arithmetic against one date, read in one
/// place.
/// </para>
/// <para>
/// Waiting work is totalled rather than listed: how much of the open work is
/// held up, and what it weighs, is the report's question; which task waits on
/// what is the pane's, where each row says so on its "Waiting for" line.
/// </para>
/// </summary>
public sealed record OpenWorkReport(
    OpenWorkTotals Totals,
    int DoneLastSevenDays,
    IReadOnlyList<OpenWorkPlan> Plans,
    IReadOnlyList<OpenWorkCount> ByStatus,
    IReadOnlyList<OpenWorkCount> ByPriority,
    IReadOnlyList<OpenWorkCount> ByType,
    IReadOnlyList<OpenWorkCount> ByRepository,
    IReadOnlyList<OpenWorkTask> Overdue,
    IReadOnlyList<OpenWorkTask> DueSoon,
    OpenWorkTotals Waiting)
{
    /// <summary>The label a row with no resolvable repository is counted under —
    /// the same words the No repo scope chip uses.</summary>
    public const string NoRepository = "No repo";

    /// <summary>How many days "recently done" and "due soon" each span, today
    /// included.</summary>
    private const int WindowDays = 7;

    public bool IsEmpty => Totals.Open == 0;

    /// <param name="rows">The rows the repository scope leaves in view, before any
    /// status, tag, My Day or Not waiting filter — the population the chip counts
    /// use.</param>
    /// <param name="today">The reader's local date.</param>
    /// <param name="isReady">Whether an open row is waiting on nothing.</param>
    /// <param name="repositoryLabels">The repositories a row resolves to, by the
    /// name a reader knows them by; empty for none.</param>
    public static OpenWorkReport Build(
        IReadOnlyList<EntryRow> rows,
        DateOnly today,
        Func<EntryRow, bool> isReady,
        Func<EntryRow, IReadOnlyList<string>> repositoryLabels)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(isReady);
        ArgumentNullException.ThrowIfNull(repositoryLabels);

        var open = rows.Where(row => !row.IsPreviewCompleted).ToList();
        var recentFrom = today.AddDays(-(WindowDays - 1));
        var soonUntil = today.AddDays(WindowDays - 1);

        var doneRecently = rows.Count(row =>
            row.PreviewCompletedOn is { } completed && completed >= recentFrom && completed <= today);

        return new OpenWorkReport(
            OpenWorkTotals.Of(rows),
            doneRecently,
            PlansOf(rows, open),
            CountBy(open, row => row.PreviewStatus, StatusLabel),
            CountBy(open, row => row.PreviewPriority, PriorityLabel, descending: true),
            CountBy(open, row => row.PreviewType, TypeLabel),
            RepositoriesOf(open, repositoryLabels),
            [.. open
                .Where(row => row.PreviewDueOn is { } due && due < today)
                .OrderBy(row => row.PreviewDueOn)
                .Select(row => Named(row))],
            [.. open
                .Where(row => row.PreviewDueOn is { } due && due >= today && due <= soonUntil)
                .OrderBy(row => row.PreviewDueOn)
                .Select(row => Named(row))],
            OpenWorkTotals.Of([.. open.Where(row => !isReady(row))]));
    }

    /// <summary>"1 point", "N points" — the shelf's wording.</summary>
    public static string PointsText(int points) => points == 1 ? "1 point" : $"{points} points";

    private static OpenWorkTask Named(EntryRow row) =>
        new(
            row.TaskId,
            string.IsNullOrWhiteSpace(row.PreviewTitle) ? "Untitled" : row.PreviewTitle,
            row.PreviewDueOn);

    /// <summary>One row per plan any scoped row wears, open or finished, plus No
    /// plan for the open rows wearing none. A row with two plan tags is in both:
    /// it is work each of them holds. Plans with open work first, then No plan,
    /// then the plans that are finished — still listed, because "done" is the
    /// answer to how that plan is going.</summary>
    private static List<OpenWorkPlan> PlansOf(IReadOnlyList<EntryRow> rows, IReadOnlyList<EntryRow> open)
    {
        var plans = rows
            .SelectMany(row => row.PreviewTags.Where(TagText.IsPlan).Select(tag => (Tag: tag.Trim(), Row: row)))
            .GroupBy(pair => pair.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var members = group.Select(pair => pair.Row).Distinct().ToList();
                var openMembers = members.Where(row => !row.IsPreviewCompleted).ToList();

                return new OpenWorkPlan(
                    group.First().Tag,
                    openMembers.Count,
                    openMembers.Sum(row => row.PreviewEffort ?? 0),
                    openMembers.Count(row => row.PreviewEffort is null),
                    members.Where(row => row.IsPreviewCompleted).Sum(row => row.PreviewEffort ?? 0),
                    members.Sum(row => row.PreviewEffort ?? 0));
            })
            .OrderBy(plan => plan.OpenCount == 0)
            .ThenBy(plan => plan.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unplanned = open.Where(row => !row.PreviewTags.Any(TagText.IsPlan)).ToList();

        if (unplanned.Count > 0)
        {
            var noPlan = new OpenWorkPlan(
                null,
                unplanned.Count,
                unplanned.Sum(row => row.PreviewEffort ?? 0),
                unplanned.Count(row => row.PreviewEffort is null),
                0,
                0);

            var firstFinished = plans.FindIndex(plan => plan.OpenCount == 0);
            plans.Insert(firstFinished < 0 ? plans.Count : firstFinished, noPlan);
        }

        return plans;
    }

    /// <summary>Counts in the enum's own order (or its reverse, for priority, so
    /// the most urgent reads first), leaving out what nothing is.</summary>
    private static List<OpenWorkCount> CountBy<TKey>(
        IReadOnlyList<EntryRow> open,
        Func<EntryRow, TKey> key,
        Func<TKey, string> label,
        bool descending = false)
        where TKey : struct, Enum
    {
        var values = Enum.GetValues<TKey>();
        if (descending) Array.Reverse(values);

        return
        [
            .. values
                .Select(value => new OpenWorkCount(label(value), open.Count(row => key(row).Equals(value))))
                .Where(count => count.Count > 0)
        ];
    }

    /// <summary>Largest first, and No repo last whatever its size: it is the
    /// absence of a repository rather than one more of them.</summary>
    private static List<OpenWorkCount> RepositoriesOf(
        IReadOnlyList<EntryRow> open,
        Func<EntryRow, IReadOnlyList<string>> repositoryLabels)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var none = 0;

        foreach (var row in open)
        {
            var labels = repositoryLabels(row)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (labels.Count == 0)
            {
                none++;
                continue;
            }

            foreach (var label in labels)
            {
                counts[label] = counts.GetValueOrDefault(label) + 1;
            }
        }

        var result = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new OpenWorkCount(pair.Key, pair.Value))
            .ToList();

        if (none > 0) result.Add(new OpenWorkCount(NoRepository, none));

        return result;
    }

    private static string StatusLabel(EntryStatus status) => status switch
    {
        EntryStatus.Draft => "Draft",
        EntryStatus.Ready => "Ready",
        EntryStatus.InProgress => "In progress",
        EntryStatus.Done => "Done",
        EntryStatus.Archived => "Archived",
        _ => status.ToString()
    };

    private static string PriorityLabel(Priority priority) => priority.ToString();

    private static string TypeLabel(EntryType type) => type.ToString();
}

/// <summary>One plan's line in the report. <paramref name="Tag"/> is null for the
/// No plan line, which counts open work only and so has no progress to report.</summary>
/// <param name="DonePoints">Points on the plan's finished rows.</param>
/// <param name="TotalPoints">Points on all its rows, open and finished.</param>
public sealed record OpenWorkPlan(
    string? Tag,
    int OpenCount,
    int OpenPoints,
    int UnestimatedCount,
    int DonePoints,
    int TotalPoints)
{
    public bool IsNoPlan => Tag is null;

    public string Label => Tag is null ? "No plan" : TagText.Display(Tag);

    /// <summary>"D of T points done", or null for No plan.</summary>
    public string? Progress => IsNoPlan
        ? null
        : $"{DonePoints} of {OpenWorkReport.PointsText(TotalPoints)} done";

    /// <summary>Done points as a whole percentage of the total, rounded down so a
    /// plan reads 100 only when it is finished; null for No plan and for a plan
    /// with no points to divide by. The row's background fills to it.</summary>
    public int? DonePercent => IsNoPlan || TotalPoints <= 0
        ? null
        : Math.Clamp(DonePoints * 100 / TotalPoints, 0, 100);
}

/// <summary>How many open rows fall under one label of a breakdown.</summary>
public sealed record OpenWorkCount(string Label, int Count);

/// <summary>A task named in "Needs attention": one that is overdue or due
/// soon.</summary>
public sealed record OpenWorkTask(string TaskId, string Title, DateOnly? DueOn);
