using System.Globalization;

namespace Backlog.UI.Components.Progress;

/// <summary>
/// One sprint as the In progress tab's sprint view draws it, in plain values
/// (<c>.devbook/domain/progress/features.md#following-a-sprint</c>): the library carries
/// no domain, so the host turns its sprint progress into this and the board's callbacks
/// back into its own calls — the <see cref="SprintSeriesCardModel"/> precedent.
/// </summary>
/// <param name="SeriesName">The series the sprint belongs to.</param>
/// <param name="SprintName">The sprint's name, "Sprint 42".</param>
/// <param name="Dates">The sprint's window in words, "Mon 5 Oct – Sun 18 Oct".</param>
/// <param name="Repositories">The series' repositories, by alias, in order: the
/// repository filter's choices and the header's count.</param>
/// <param name="HasPrevious">Whether an earlier sprint of the series exists.</param>
/// <param name="HasNext">Whether a later sprint of the series exists.</param>
/// <param name="Goal">The sprint's goal in words; empty when none was set.</param>
/// <param name="GoalCounts">The counts over the goal tasks alone.</param>
/// <param name="GoalRules">The goal rules that picked the goal tasks, in words —
/// "+rekeningen, +verplaatsen-specs and tasks marked goal".</param>
/// <param name="GoalMet">Whether the share of done goal tasks reaches the series'
/// threshold.</param>
/// <param name="AllWork">The counts over every member.</param>
/// <param name="DayText">Where the sprint stands in its working days, "Day 7 of 10 · 3
/// working days left".</param>
/// <param name="DonePerDay">Members done by the end of each working day elapsed so far,
/// in order — cumulative, so it never falls.</param>
/// <param name="WorkingDays">Monday to Friday in the sprint's window.</param>
/// <param name="PaceReading">One sentence on how the sprint is going against the even
/// pace.</param>
/// <param name="GoalPlans">The plan tags an "in these plans" goal rule names, without
/// the sigil: their groups carry the "Goal plan" badge.</param>
/// <param name="Rows">Every member, as a row.</param>
public sealed record SprintBoardModel(
    string SeriesName,
    string SprintName,
    string Dates,
    IReadOnlyList<string> Repositories,
    bool HasPrevious,
    bool HasNext,
    string Goal,
    SprintBoardCounts GoalCounts,
    string GoalRules,
    bool GoalMet,
    SprintBoardCounts AllWork,
    string DayText,
    IReadOnlyList<int> DonePerDay,
    int WorkingDays,
    string PaceReading,
    IReadOnlyList<string> GoalPlans,
    IReadOnlyList<SprintBoardRow> Rows)
{
    /// <summary>Members outside the goal set.</summary>
    public int OutsideGoal => Math.Max(0, AllWork.Total - GoalCounts.Total);

    /// <summary>"2 repositories".</summary>
    public string RepositoryCount => Repositories.Count == 1 ? "1 repository" : $"{Repositories.Count.ToString(CultureInfo.InvariantCulture)} repositories";
}

/// <summary>How many members are in each state. <see cref="Total"/> is their sum;
/// <paramref name="AddedAfterStart"/> counts within it.</summary>
public sealed record SprintBoardCounts(int Done, int InProgress, int Blocked, int NotStarted, int AddedAfterStart = 0)
{
    public static SprintBoardCounts None { get; } = new(0, 0, 0, 0);

    public int Total => Done + InProgress + Blocked + NotStarted;

    /// <summary>The counts over <paramref name="rows"/>.</summary>
    public static SprintBoardCounts Of(IEnumerable<SprintBoardRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var list = rows.ToList();
        return new(
            list.Count(row => row.State is SprintRowState.Done),
            list.Count(row => row.State is SprintRowState.InProgress),
            list.Count(row => row.State is SprintRowState.Blocked),
            list.Count(row => row.State is SprintRowState.NotStarted),
            list.Count(row => row.AddedAfterStart));
    }
}

/// <summary>Where one member stands, for its status and the counts.</summary>
public enum SprintRowState
{
    NotStarted,
    InProgress,
    Blocked,
    Done
}

/// <summary>The state of a pull request a member links, which colours its chip:
/// merged green, open amber, draft grey. <see cref="Unknown"/> until the host has read
/// it.</summary>
public enum SprintPullRequestState
{
    Unknown,
    Open,
    Draft,
    Merged,
    Closed
}

/// <summary>One pull request a member links.</summary>
/// <param name="Repository">The repository it lives in, <c>owner/name</c>.</param>
/// <param name="Number">Its number, as text.</param>
public sealed record SprintBoardPullRequest(string Repository, string Number, SprintPullRequestState State)
{
    /// <summary>The host's key for it: <c>owner/name#number</c>, the key the pull
    /// requests list opens on.</summary>
    public string Key => $"{Repository}#{Number}";

    /// <summary>What its chip reads: "#4530", "#4707 open", "draft", "#12 closed".</summary>
    public string Label => State switch
    {
        SprintPullRequestState.Open => $"#{Number} open",
        SprintPullRequestState.Draft => "draft",
        SprintPullRequestState.Closed => $"#{Number} closed",
        _ => $"#{Number}"
    };
}

/// <summary>One member of the sprint, as its row draws it.</summary>
/// <param name="TaskId">The task's id, as text.</param>
/// <param name="Plan">The plan the task is filed under, without the sigil, or null.</param>
/// <param name="Repository">The repository it belongs to, by alias, or null.</param>
/// <param name="IsGoal">Whether it is in the goal set.</param>
/// <param name="AddedAfterStart">Whether it joined after the sprint's first day.</param>
/// <param name="Owners">The authors of its linked pull requests; empty when none is
/// known — there is no owner field.</param>
/// <param name="Note">The sprint's one-line note on it, or null.</param>
/// <param name="OpenQuestion">Whether the note is an open question.</param>
public sealed record SprintBoardRow(
    string TaskId,
    string Title,
    SprintRowState State,
    string? Plan,
    string? Repository,
    bool IsGoal,
    bool AddedAfterStart,
    IReadOnlyList<SprintBoardPullRequest> PullRequests,
    IReadOnlyList<string> Owners,
    string? Note,
    bool OpenQuestion)
{
    /// <summary>In progress with a pull request open for review — drawn "In review".</summary>
    public bool InReview => State is SprintRowState.InProgress
        && PullRequests.Any(pull => pull.State is SprintPullRequestState.Open);

    /// <summary>What the status column reads.</summary>
    public string StatusText => State switch
    {
        SprintRowState.Done => "Done",
        SprintRowState.Blocked => "Blocked",
        SprintRowState.InProgress => InReview ? "In review" : "In progress",
        _ => "Not started"
    };
}

/// <summary>Live work running against a task that is not in the sprint, or against no
/// task at all: what the folded "Outside the sprint" line counts.</summary>
/// <param name="Key">The host's key for it: a task id, a session id or a pull request
/// key.</param>
/// <param name="Title">What it is called: the task's title, or the session's or pull
/// request's own.</param>
/// <param name="Sessions">The live sessions it counts.</param>
/// <param name="PullRequests">The open pull requests it counts.</param>
/// <param name="TaskId">The task it runs against, or null for a session or pull request
/// no task links.</param>
public sealed record SprintBoardOutside(string Key, string Title, int Sessions, int PullRequests, string? TaskId = null);

/// <summary>A member that needs the person, and why: blocked, a pull request waiting
/// on review, or an open question.</summary>
public sealed record SprintNeedsYou(SprintBoardRow Row, SprintNeedsYouKind Kind, string Reason);

/// <summary>Why a member needs the person, in the order the strip lists them.</summary>
public enum SprintNeedsYouKind
{
    Blocked,
    WaitingOnReview,
    OpenQuestion
}

/// <summary>One group of rows under one plan, or every row when the board is not
/// grouped.</summary>
/// <param name="Plan">The plan tag without the sigil, or null for the rows filed under
/// no plan — or for the one group of an ungrouped board.</param>
/// <param name="IsGoalPlan">Whether an "in these plans" goal rule names the plan.</param>
public sealed record SprintBoardGroup(string? Plan, bool IsGoalPlan, IReadOnlyList<SprintBoardRow> Rows)
{
    public SprintBoardCounts Counts => SprintBoardCounts.Of(Rows);
}

/// <summary>
/// The rules the board draws by, apart from the markup, so a host and its tests read
/// the same answer: which rows the repository filter keeps, how they group by plan,
/// what needs the person, and what the outside line says.
/// </summary>
public static class SprintBoardRules
{
    /// <summary>The rows in <paramref name="repository"/>, or every row when it is
    /// null.</summary>
    public static IReadOnlyList<SprintBoardRow> Filter(IEnumerable<SprintBoardRow> rows, string? repository)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return string.IsNullOrWhiteSpace(repository)
            ? [.. rows]
            : [.. rows.Where(row => string.Equals(row.Repository, repository, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// One group per plan — goal plans first, then the rest by name, and the rows filed
    /// under no plan last — or, ungrouped, one group of every row. Within a group the
    /// rows run done first, then blocked, in progress and not started, each by title.
    /// </summary>
    public static IReadOnlyList<SprintBoardGroup> Group(
        IEnumerable<SprintBoardRow> rows,
        IReadOnlyCollection<string> goalPlans,
        bool byPlan)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(goalPlans);

        var list = rows.ToList();
        if (!byPlan) return list.Count == 0 ? [] : [new SprintBoardGroup(null, false, Order(list))];

        var goals = goalPlans.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
        [
            .. list
                .GroupBy(row => string.IsNullOrWhiteSpace(row.Plan) ? null : row.Plan.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => new SprintBoardGroup(group.Key, group.Key is not null && goals.Contains(group.Key), Order(group)))
                .OrderBy(group => group.Plan is null ? 2 : group.IsGoalPlan ? 0 : 1)
                .ThenBy(group => group.Plan, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static IReadOnlyList<SprintBoardRow> Order(IEnumerable<SprintBoardRow> rows) =>
        [.. rows
            .OrderBy(row => row.State switch
            {
                SprintRowState.Done => 0,
                SprintRowState.Blocked => 1,
                SprintRowState.InProgress => 2,
                _ => 3
            })
            .ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>
    /// The members that need the person, each once, for its first reason: blocked, then
    /// a pull request open and waiting on review, then an open question. A done member
    /// needs nobody unless it carries an open question.
    /// </summary>
    public static IReadOnlyList<SprintNeedsYou> NeedsYou(IEnumerable<SprintBoardRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var found = new List<SprintNeedsYou>();
        foreach (var row in rows)
        {
            var plan = string.IsNullOrWhiteSpace(row.Plan) ? string.Empty : $" · +{row.Plan}";
            if (row.State is SprintRowState.Blocked)
            {
                found.Add(new(row, SprintNeedsYouKind.Blocked, $"Blocked{plan}"));
            }
            else if (row.State is not SprintRowState.Done
                && row.PullRequests.FirstOrDefault(pull => pull.State is SprintPullRequestState.Open) is { } open)
            {
                found.Add(new(row, SprintNeedsYouKind.WaitingOnReview, $"#{open.Number} waits on review{plan}"));
            }
            else if (row.OpenQuestion)
            {
                var question = string.IsNullOrWhiteSpace(row.Note) ? "Open question" : $"Open question: {row.Note.Trim()}";
                found.Add(new(row, SprintNeedsYouKind.OpenQuestion, $"{question}{plan}"));
            }
        }

        return [.. found.OrderBy(item => item.Kind)];
    }

    /// <summary>"2 sessions and 1 pull request are running against work that is not in
    /// Sprint 42."</summary>
    public static string OutsideLine(IReadOnlyCollection<SprintBoardOutside> outside, string sprintName)
    {
        ArgumentNullException.ThrowIfNull(outside);

        var sessions = outside.Sum(item => item.Sessions);
        var pulls = outside.Sum(item => item.PullRequests);
        var parts = new List<string>(2);
        if (sessions > 0) parts.Add(Count(sessions, "session", "sessions"));
        if (pulls > 0) parts.Add(Count(pulls, "pull request", "pull requests"));

        if (parts.Count == 0) return $"Nothing is running against work that is not in {sprintName}.";

        var verb = sessions + pulls == 1 ? "is" : "are";
        return $"{string.Join(" and ", parts)} {verb} running against work that is not in {sprintName}.";
    }

    /// <summary>"1 session", "3 pull requests".</summary>
    public static string Count(int count, string one, string many) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? one : many)}";
}
