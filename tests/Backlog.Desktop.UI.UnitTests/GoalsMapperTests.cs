using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.UI.Goals;
using Backlog.UI.Components.Goals;
using Backlog.UI.Components.Progress;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The stand-up reading, the goals' attachments, their pull requests' status and the
/// shell's candidates, turned into the board the Goals view draws
/// (<c>.devbook/domain/progress/features.md#the-goals-view</c>): the library takes plain
/// values, so every Progress word is translated here and nowhere else.
/// </summary>
public sealed class GoalsMapperTests
{
    private static DateOnly Oct(int day) => new(2026, 10, day);

    private static readonly Guid PaidId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SearchId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Invoice = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Recurring = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Matching = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Rules = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid Gone = Guid.Parse("00000000-0000-0000-0000-000000000009");
    private static readonly Guid BankId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    private static readonly StandUpDto StandUp = new(
        new IterationWindowDto(IterationLength.Week, Oct(12), Oct(16)),
        new IterationWindowDto(IterationLength.Week, Oct(19), Oct(23)),
        [
            new StandUpGoalDto(PaidId, "Customers get paid faster", Oct(30), new GoalProgressDto(9, 15, 3, 1, 2, 3), GoalStanding.AtRisk, new DateOnly(2026, 11, 6)),
            new StandUpGoalDto(SearchId, "Devbook search ships", Oct(23), new GoalProgressDto(5, 6, 0, 0, 1, 0), GoalStanding.OnTrack, null)
        ],
        Finished:
        [
            new StandUpGroupDto(PaidId, "Customers get paid faster", [Item(GoalAttachmentKind.Task, "Invoice PDF export", Invoice)]),
            new StandUpGroupDto(null, null, [Item(GoalAttachmentKind.Task, "VAT rate per line item", Guid.NewGuid())])
        ],
        Planned:
        [
            new StandUpGroupDto(PaidId, "Customers get paid faster",
            [
                Item(GoalAttachmentKind.Task, "Customer search by IBAN", Guid.NewGuid()),
                new StandUpItemDto(GoalAttachmentKind.PullRequest, null, "Payment status webhook", "JSdotNet/Backlog", 1357, 3, null, null)
            ])
        ],
        PlannedEffort: 21,
        Capacity: 18,
        Impediments:
        [
            new StandUpImpedimentDto(BankId, "Bank sample files missing", ImpedimentState.Escalated, "Account manager", 3, null, PaidId, "Customers get paid faster", "QA12 phone note")
        ],
        ResolvedInLast: 1);

    private static StandUpItemDto Item(GoalAttachmentKind kind, string title, Guid taskId) =>
        new(kind, taskId, title, null, null, null, null, null);

    private static readonly IReadOnlyList<GoalSummaryDto> Summaries =
    [
        new(new GoalDto(PaidId, "Customers get paid faster", null, Oct(30),
            [
                Attached(GoalItemDto.ForPlan("bank-integration")),
                Attached(GoalItemDto.ForTask(Invoice)),
                Attached(GoalItemDto.ForTask(Recurring)),
                Attached(GoalItemDto.ForTask(Gone)),
                Attached(GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1352))
            ]), new GoalProgressDto(9, 15, 3, 1, 2, 3)),
        new(new GoalDto(SearchId, "Devbook search ships", null, Oct(23), []), new GoalProgressDto(5, 6, 0, 0, 1, 0))
    ];

    private static GoalAttachmentDto Attached(GoalItemDto item) => new(item, Oct(12));

    private static readonly GoalsCandidates Candidates = new(
        [
            new(Invoice, "Invoice PDF export", GoalTaskStatus.Done, [], [new PullRequestRefDto("JSdotNet/Backlog", "1341")], true),
            new(Recurring, "Recurring invoices", GoalTaskStatus.Doing, [], [], true),
            new(Matching, "Payment matching rules", GoalTaskStatus.Doing, ["+bank-integration"], [], true),
            new(Rules, "Bank import rules", GoalTaskStatus.Done, ["bank-integration"], [], true),
            new(Guid.NewGuid(), "VAT rate per line item", GoalTaskStatus.Doing, [], [], true)
        ],
        ["bank-integration", "devbook-search"],
        [new GoalsPullRequestCandidate("JSdotNet/Backlog", 1341, "Invoice PDF export", true, "https://github.com/JSdotNet/Backlog/pull/1341")]);

    private static readonly IReadOnlyList<GoalPullRequestDto> Pulls =
    [
        new("JSdotNet/Backlog", "1341", LinkedPullRequestState.Open, null, new GoalPullRequestChecksDto(14, 0, 0, []), new GoalPullRequestApprovalsDto(1, 2), ReviewRequested: true),
        new("JSdotNet/Backlog", "1352", LinkedPullRequestState.Merged, Oct(14), null, null),
        new("JSdotNet/Backlog", "1357", LinkedPullRequestState.Draft, null, new GoalPullRequestChecksDto(9, 0, 5, []), null)
    ];

    private static GoalsBoardModel Map() => GoalsMapper.Map(StandUp, Summaries, Pulls, Candidates);

    [Fact]
    public void The_board_reads_the_length_and_both_windows()
    {
        var board = Map();

        Assert.Equal(GoalsIteration.Week, board.Iteration);
        Assert.Equal(new GoalsWindow(Oct(12), Oct(16)), board.Last);
        Assert.Equal(new GoalsWindow(Oct(19), Oct(23)), board.Next);
    }

    [Fact]
    public void Each_goal_takes_its_counts_forecast_and_landing_in_the_stand_ups_order()
    {
        var goals = Map().Goals;

        Assert.Equal([PaidId.ToString(), SearchId.ToString()], goals.Select(goal => goal.Id));
        var paid = goals[0];
        Assert.Equal(new GoalCounts(9, 15, 3, 1, 2, 3), paid.Counts);
        Assert.True(paid.AtRisk);
        Assert.Equal(new DateOnly(2026, 11, 6), paid.LandsOn);
        Assert.Equal(Oct(30), paid.DueDate);
        Assert.False(goals[1].AtRisk);
    }

    /// <summary>A goal without a due date reaches the card with next's last day as its due
    /// date, marked as the iteration's end; a dated goal's is its own.</summary>
    [Fact]
    public void A_goal_without_a_due_date_reaches_the_card_due_at_the_iteration_end()
    {
        var reading = StandUp with
        {
            Goals =
            [
                StandUp.Goals[0] with { DueDate = Oct(23), DueIsIterationEnd = true },
                StandUp.Goals[1]
            ]
        };

        var goals = GoalsMapper.Map(reading, Summaries, Pulls, Candidates).Goals;

        Assert.Equal((Oct(23), true), (goals[0].DueDate, goals[0].DueIsIterationEnd));
        Assert.Equal((Oct(23), false), (goals[1].DueDate, goals[1].DueIsIterationEnd));
    }

    /// <summary>Open question 6's default: a plan's count is its rows done over its rows,
    /// as the shell's task list has them.</summary>
    [Fact]
    public void An_attached_plan_counts_the_rows_filed_under_it()
    {
        var plan = Assert.Single(Map().Goals[0].Plans);

        Assert.Equal(new GoalPlanModel("bank-integration", 1, 2, GoalSubject.ForPlan("bank-integration"), Doing: 1), plan);
    }

    /// <summary>A task in progress and marked <c>blocked:</c> reaches the mapper as Blocked,
    /// and a plan counts it as that alone, so a row is never both blocked and in progress. A
    /// mark on a task that has not started arrives apart, as
    /// <see cref="GoalsTaskCandidate.MarkedBlocked"/>; see <see cref="A_task_stands_on_its_goal_as_the_bar_reads_it"/>.</summary>
    [Fact]
    public void An_attached_plan_counts_its_blocked_rows_apart_from_those_in_progress()
    {
        var candidates = Candidates with
        {
            Tasks =
            [
                .. Candidates.Tasks,
                new(Guid.NewGuid(), "Bank sample files", GoalTaskStatus.Blocked, ["bank-integration"], [], true),
                new(Guid.NewGuid(), "Statement parser", GoalTaskStatus.ToDo, ["bank-integration"], [], true)
            ]
        };

        var plan = Assert.Single(GoalsMapper.Map(StandUp, Summaries, Pulls, candidates).Goals[0].Plans);

        Assert.Equal(new GoalPlanModel("bank-integration", 1, 4, GoalSubject.ForPlan("bank-integration"), Doing: 1, Blocked: 1), plan);
    }

    /// <summary>The goal's bar reads a task by <c>GoalItems</c>: done by status, then done
    /// when every linked pull request is answered merged, then blocked by its mark, then its
    /// status. The rows read it the same way, on a plan's counts and on an attached task. One
    /// row, attached and filed under the plan, so each case proves both.</summary>
    [Theory]
    [InlineData(GoalTaskStatus.ToDo, false, "", "", "", GoalTaskStatus.ToDo)]
    [InlineData(GoalTaskStatus.Doing, false, "", "", "", GoalTaskStatus.Doing)]
    [InlineData(GoalTaskStatus.Blocked, true, "", "", "", GoalTaskStatus.Blocked)]
    [InlineData(GoalTaskStatus.Done, false, "", "", "", GoalTaskStatus.Done)]
    [InlineData(GoalTaskStatus.ToDo, true, "", "", "", GoalTaskStatus.Blocked)] // marked, not started
    [InlineData(GoalTaskStatus.Done, true, "", "", "", GoalTaskStatus.Done)] // done outranks the mark
    [InlineData(GoalTaskStatus.Doing, false, "1,2", "1,2", "", GoalTaskStatus.Done)] // in progress, all merged
    [InlineData(GoalTaskStatus.ToDo, false, "1", "1", "", GoalTaskStatus.Done)] // not started, all merged
    [InlineData(GoalTaskStatus.ToDo, true, "1", "1", "", GoalTaskStatus.Done)] // merged outranks the mark
    [InlineData(GoalTaskStatus.Blocked, true, "1", "1", "", GoalTaskStatus.Done)] // merged outranks the mark
    [InlineData(GoalTaskStatus.Doing, false, "1,2", "1", "2", GoalTaskStatus.Doing)] // partly merged
    [InlineData(GoalTaskStatus.ToDo, true, "1,2", "1", "2", GoalTaskStatus.Blocked)] // partly merged, marked
    [InlineData(GoalTaskStatus.Doing, false, "1,2", "1", "", GoalTaskStatus.Doing)] // merged, one unanswered
    [InlineData(GoalTaskStatus.ToDo, false, "1", "", "", GoalTaskStatus.ToDo)] // none answered
    public void A_task_stands_on_its_goal_as_the_bar_reads_it(
        GoalTaskStatus status, bool marked, string linked, string merged, string open, GoalTaskStatus expected)
    {
        var row = new GoalsTaskCandidate(Guid.NewGuid(), "A task", status, ["bank-integration"], [.. Numbers(linked).Select(Linked)], true, marked);
        var goal = new GoalDto(PaidId, "Customers get paid faster", null, Oct(30), [Attached(GoalItemDto.ForPlan("bank-integration")), Attached(GoalItemDto.ForTask(row.Id))]);
        IReadOnlyList<GoalPullRequestDto> answers =
        [
            .. Numbers(merged).Select(number => Answer(number, LinkedPullRequestState.Merged)),
            .. Numbers(open).Select(number => Answer(number, LinkedPullRequestState.Open))
        ];

        var card = GoalsMapper.Map(StandUp, [new GoalSummaryDto(goal, new GoalProgressDto(0, 1, 0, 0, 1, 0))], answers, new GoalsCandidates([row], [], [])).Goals[0];

        var plan = Assert.Single(card.Plans);
        Assert.Equal(
            (expected == GoalTaskStatus.Done ? 1 : 0, 1, expected == GoalTaskStatus.Doing ? 1 : 0, expected == GoalTaskStatus.Blocked ? 1 : 0),
            (plan.Done, plan.Total, plan.Doing, plan.Blocked));
        Assert.Equal(expected, Assert.Single(card.Tasks).Status);
    }

    /// <summary>The bar tells pull requests apart by <c>GoalItems.Key</c>, regardless of case
    /// and of a leading <c>#</c>, on the link and on the answer; so do the rows.</summary>
    [Theory]
    [InlineData("JSdotNet/Backlog", "1400", "jsdotnet/backlog", "1400")]
    [InlineData("JSdotNet/Backlog", "#1400", "JSdotNet/Backlog", "1400")]
    [InlineData("JSdotNet/Backlog", "1400", "JSdotNet/Backlog", "#1400")]
    public void A_merged_answer_reaches_its_link_however_either_is_spelled(string linkRepository, string linkNumber, string answerRepository, string answerNumber)
    {
        var row = new GoalsTaskCandidate(Guid.NewGuid(), "A task", GoalTaskStatus.Doing, ["bank-integration"], [new PullRequestRefDto(linkRepository, linkNumber)], true);
        IReadOnlyList<GoalPullRequestDto> answers = [new(answerRepository, answerNumber, LinkedPullRequestState.Merged, Oct(14), null, null)];

        var plan = Assert.Single(GoalsMapper.Map(StandUp, Summaries, answers, new GoalsCandidates([row], [], [])).Goals[0].Plans);

        Assert.Equal((1, 0), (plan.Done, plan.Doing));
    }

    /// <summary>The fix's point: over one set of facts, a plan's row and the bar of a goal
    /// holding that plan and nothing else say the same thing, each read by its own code.</summary>
    [Fact]
    public void A_plans_row_counts_what_the_bar_of_a_goal_holding_it_counts()
    {
        (ProgressTaskStatus Status, bool Marked, string[] Linked)[] facts =
        [
            (ProgressTaskStatus.Done, false, []),
            (ProgressTaskStatus.Done, true, []),
            (ProgressTaskStatus.InProgress, false, []),
            (ProgressTaskStatus.InProgress, true, []),
            (ProgressTaskStatus.Ready, false, []),
            (ProgressTaskStatus.Draft, false, []),
            (ProgressTaskStatus.Ready, true, []),
            (ProgressTaskStatus.InProgress, false, ["1", "2"]),
            (ProgressTaskStatus.Ready, false, ["1"]),
            (ProgressTaskStatus.Ready, true, ["2"]),
            (ProgressTaskStatus.InProgress, true, ["1"]),
            (ProgressTaskStatus.InProgress, false, ["1", "3"]),
            (ProgressTaskStatus.Ready, true, ["1", "3"]),
            (ProgressTaskStatus.InProgress, false, ["1", "4"]),
            (ProgressTaskStatus.Ready, false, ["4"]),
            (ProgressTaskStatus.InProgress, false, ["#5"]),
            (ProgressTaskStatus.InProgress, false, ["#582"]),
            (ProgressTaskStatus.InProgress, false, ["1", "backlog:7"])
        ];
        var goal = new GoalDto(PaidId, "Customers get paid faster", null, Oct(30), [Attached(GoalItemDto.ForPlan("bank-integration"))]);
        var ids = facts.Select(_ => Guid.NewGuid()).ToList();

        IReadOnlyList<GoalsTaskCandidate> rows =
        [
            .. facts.Select((fact, index) => new GoalsTaskCandidate(
                ids[index],
                "A task",
                RowStatus(fact.Status, fact.Marked),
                ["bank-integration"],
                [.. fact.Linked.Select(Stored).Where(Drawable)],
                true,
                fact.Marked,
                [.. fact.Linked.Select(Stored)]))
        ];
        IReadOnlyList<ProgressTaskDto> tasks =
        [
            .. facts.Select((fact, index) => new ProgressTaskDto(ids[index], "A task", fact.Status, fact.Marked, ["bank-integration"], [], [], null, null, [.. fact.Linked.Select(Stored)]))
        ];
        // 1, 2, 5 and 582 merged, 3 open, 4 and backlog's 7 with no answer; 2 and 5 answered in
        // another spelling than they link.
        IReadOnlyList<GoalPullRequestDto> answers =
        [
            Answer("1", LinkedPullRequestState.Merged),
            new("jsdotnet/backlog", "2", LinkedPullRequestState.Merged, Oct(14), null, null),
            Answer("3", LinkedPullRequestState.Open),
            Answer("5", LinkedPullRequestState.Merged),
            Answer("582", LinkedPullRequestState.Merged)
        ];

        var plan = Assert.Single(GoalsMapper.Map(StandUp, [new GoalSummaryDto(goal, new GoalProgressDto(0, 0, 0, 0, 0, 0))], answers, new GoalsCandidates(rows, [], [])).Goals[0].Plans);
        var bar = GoalItems.Count(GoalItems.Items(goal, [goal], tasks, [.. answers.Select(LinkedPullRequestDto.Of)]));

        Assert.Equal((bar.Done, bar.Total, bar.Doing, bar.Blocked, bar.ToDo), (plan.Counts.Done, plan.Counts.Total, plan.Counts.Doing, plan.Counts.Blocked, plan.Counts.ToDo));
        // Pinned too, so two readers agreeing on the wrong thing still fails.
        Assert.Equal((8, 18, 4, 3, 3), (plan.Done, plan.Total, plan.Doing, plan.Blocked, plan.Counts.ToDo));
    }

    /// <summary>Stored as <c>#582</c>, the link is one the bar reads and the column cannot draw:
    /// the task stands done on its plan and as attached, the column gains nothing, and the
    /// one pull request is asked for once, though the task reaches the read twice.</summary>
    [Fact]
    public void A_merged_link_spelled_with_a_hash_makes_its_task_done_and_stays_out_of_the_column()
    {
        var row = new GoalsTaskCandidate(Guid.NewGuid(), "A task", GoalTaskStatus.Doing, ["bank-integration"], [], true, StoredPullRequests: [Linked("#582")]);
        var goal = new GoalDto(PaidId, "Customers get paid faster", null, Oct(30), [Attached(GoalItemDto.ForPlan("bank-integration")), Attached(GoalItemDto.ForTask(row.Id))]);
        IReadOnlyList<GoalSummaryDto> summaries = [new GoalSummaryDto(goal, new GoalProgressDto(0, 1, 0, 0, 1, 0))];
        var candidates = new GoalsCandidates([row], [], []);

        var card = GoalsMapper.Map(StandUp, summaries, [Answer("582", LinkedPullRequestState.Merged)], candidates).Goals[0];

        Assert.Equal((1, 0), (Assert.Single(card.Plans).Done, Assert.Single(card.Plans).Doing));
        Assert.Equal(GoalTaskStatus.Done, Assert.Single(card.Tasks).Status);
        Assert.Empty(card.PullRequests);
        Assert.Equal(["1357", "582"], GoalsMapper.PullRequestsToRead(StandUp, summaries, candidates).Select(pull => pull.Number));
    }

    /// <summary>A link under a repository that is not <c>owner/name</c> is never answered merged,
    /// so it holds its task open beside a merged one, as on the bar, though the row cannot draw it.</summary>
    [Fact]
    public void A_link_outside_any_owner_and_name_with_no_answer_keeps_its_task_not_done()
    {
        var row = new GoalsTaskCandidate(
            Guid.NewGuid(), "A task", GoalTaskStatus.Doing, ["bank-integration"], [Linked("1")], true,
            StoredPullRequests: [Linked("1"), new PullRequestRefDto("backlog", "7")]);
        var goal = new GoalDto(PaidId, "Customers get paid faster", null, Oct(30), [Attached(GoalItemDto.ForPlan("bank-integration")), Attached(GoalItemDto.ForTask(row.Id))]);

        var card = GoalsMapper.Map(
            StandUp,
            [new GoalSummaryDto(goal, new GoalProgressDto(0, 1, 0, 0, 1, 0))],
            [Answer("1", LinkedPullRequestState.Merged)],
            new GoalsCandidates([row], [], [])).Goals[0];

        Assert.Equal((0, 1), (Assert.Single(card.Plans).Done, Assert.Single(card.Plans).Doing));
        Assert.Equal(GoalTaskStatus.Doing, Assert.Single(card.Tasks).Status);
    }

    /// <summary>A fact's link as a task stores it: <c>repository:number</c>, else a number in
    /// JSdotNet/Backlog, spelled as given.</summary>
    private static PullRequestRefDto Stored(string link) =>
        link.Split(':') is [var repository, var number] ? new PullRequestRefDto(repository, number) : Linked(link);

    /// <summary>What <c>EntryLinks.PullRequests</c> keeps of a stored link.</summary>
    private static bool Drawable(PullRequestRefDto link) =>
        link.Repository.Contains('/', StringComparison.Ordinal) && int.TryParse(link.Number, out _);

    /// <summary>What <c>GoalsCandidateSource</c> gives a row's status: blocked only in progress.</summary>
    private static GoalTaskStatus RowStatus(ProgressTaskStatus status, bool marked) => status switch
    {
        ProgressTaskStatus.Done => GoalTaskStatus.Done,
        ProgressTaskStatus.InProgress => marked ? GoalTaskStatus.Blocked : GoalTaskStatus.Doing,
        _ => GoalTaskStatus.ToDo
    };

    private static IEnumerable<string> Numbers(string list) => list.Split(',', StringSplitOptions.RemoveEmptyEntries);

    private static PullRequestRefDto Linked(string number) => new("JSdotNet/Backlog", number);

    private static GoalPullRequestDto Answer(string number, LinkedPullRequestState state) =>
        new("JSdotNet/Backlog", number, state, state == LinkedPullRequestState.Merged ? Oct(14) : null, null, null);

    /// <summary>No goal lists work in progress: a marked task that has not started is not,
    /// and stays out however the mark reads on a goal; a marked one in progress still is.</summary>
    [Fact]
    public void No_goal_reads_a_tasks_status_and_not_its_mark()
    {
        var notStarted = new GoalsTaskCandidate(Guid.NewGuid(), "Marked, not started", GoalTaskStatus.ToDo, [], [], true, MarkedBlocked: true);
        var inProgress = new GoalsTaskCandidate(Guid.NewGuid(), "Marked, in progress", GoalTaskStatus.Blocked, [], [], true, MarkedBlocked: true);

        var row = Assert.Single(GoalsMapper.Map(StandUp, Summaries, Pulls, new GoalsCandidates([notStarted, inProgress], [], [])).NoGoal);

        Assert.Equal(("Marked, in progress", GoalTaskStatus.Blocked), (row.Title, row.Status));
    }

    /// <summary>A task whose pull requests all merged stands done on a goal, and its status is
    /// still open: the attach search goes on offering it, and No goal goes on listing it.</summary>
    [Fact]
    public void The_attach_search_and_no_goal_read_a_tasks_status_and_not_its_standing()
    {
        var merged = new GoalsTaskCandidate(Guid.NewGuid(), "All merged", GoalTaskStatus.Doing, [], [Linked("1352")], true);

        var board = GoalsMapper.Map(StandUp, Summaries, Pulls, new GoalsCandidates([merged], [], []));

        Assert.Contains(board.Candidates, item => item.Title == "All merged");
        Assert.Equal(GoalTaskStatus.Doing, Assert.Single(board.NoGoal, item => item.Kind == GoalSubjectKind.Task).Status);
    }

    [Fact]
    public void An_attached_task_is_listed_with_its_status_and_a_task_the_list_no_longer_has_is_left_out()
    {
        Assert.Equal(
            [
                new GoalTaskModel(Invoice.ToString(), "Invoice PDF export", GoalTaskStatus.Done, GoalSubject.ForTask(Invoice)),
                new GoalTaskModel(Recurring.ToString(), "Recurring invoices", GoalTaskStatus.Doing, GoalSubject.ForTask(Recurring))
            ],
            Map().Goals[0].Tasks);
    }

    /// <summary>A goal's own pull request detaches; one only a task brings has no subject,
    /// since detaching it would leave it on the goal through the task.</summary>
    [Fact]
    public void Only_a_goals_own_pull_request_carries_a_subject_to_detach_it_by()
    {
        var paid = Map().Goals[0];

        Assert.Equal([GoalSubject.ForPullRequest("JSdotNet/Backlog", 1352), null], paid.PullRequestSubjects);
        Assert.Equal(GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1352), GoalSubjects.ToItem(paid.PullRequestSubject(0)!));
    }

    [Fact]
    public void A_goal_carries_its_outcome_from_its_attachments_reading()
    {
        var summaries = Summaries.Select(summary => summary.Goal.Id == PaidId
            ? summary with { Goal = summary.Goal with { Outcome = "Invoices are paid within 14 days" } }
            : summary).ToList();

        var goals = GoalsMapper.Map(StandUp, summaries, Pulls, Candidates).Goals;

        Assert.Equal("Invoices are paid within 14 days", goals[0].Outcome);
        Assert.Null(goals[1].Outcome);
    }

    [Fact]
    public void A_goals_pull_requests_are_its_own_and_those_its_tasks_link_each_with_its_status()
    {
        var pulls = Map().Goals[0].PullRequests;

        Assert.Equal([1352, 1341], pulls.Select(pull => pull.Number));
        Assert.Equal("merged", pulls[0].Slug);

        var linked = pulls[1];
        Assert.Equal("open", linked.Slug);
        Assert.Equal(new PullRequestChecks(14, 0, 0), linked.Checks);
        Assert.Equal(new PullRequestApprovals(1, 2), linked.Approvals);
        Assert.Equal("Invoice PDF export", linked.Title);
        Assert.Equal("https://github.com/JSdotNet/Backlog/pull/1341", linked.Url);
        Assert.True(linked.ShowsYourReview);
        Assert.False(pulls[0].ShowsYourReview);
    }

    /// <summary>1352 is the goal's own and no candidate holds it, the way a pull request
    /// awaiting your review is not among the view's candidates: the read names it.</summary>
    [Fact]
    public void A_goals_pull_request_no_candidate_holds_takes_the_title_the_read_answered()
    {
        IReadOnlyList<GoalPullRequestDto> pulls =
        [
            new("JSdotNet/Backlog", "1352", LinkedPullRequestState.Open, null, null, null, Title: "Settle by bank transfer")
        ];

        var pull = GoalsMapper.Map(StandUp, Summaries, pulls, Candidates).Goals[0].PullRequests[0];

        Assert.Equal((1352, "Settle by bank transfer"), (pull.Number, pull.Title));
    }

    [Fact]
    public void A_candidates_title_stands_over_the_one_the_read_answered()
    {
        IReadOnlyList<GoalPullRequestDto> pulls =
        [
            new("JSdotNet/Backlog", "1341", LinkedPullRequestState.Open, null, null, null, Title: "Older words from the read")
        ];

        var pull = GoalsMapper.Map(StandUp, Summaries, pulls, Candidates).Goals[0].PullRequests[1];

        Assert.Equal((1341, "Invoice PDF export"), (pull.Number, pull.Title));
    }

    /// <summary>1357 is a planned stand-up row no candidate holds: the row keeps the title
    /// it came with, whatever the read answered.</summary>
    [Fact]
    public void A_rows_own_title_stands_over_the_one_the_read_answered()
    {
        IReadOnlyList<GoalPullRequestDto> pulls =
        [
            new("JSdotNet/Backlog", "1357", LinkedPullRequestState.Draft, null, null, null, Title: "Words from the read")
        ];

        var row = GoalsMapper.Map(StandUp, Summaries, pulls, Candidates).StandUp.Planned[0].Items[1];

        Assert.Equal((1357, "Payment status webhook"), (row.PullRequest!.Number, row.PullRequest.Title));
    }

    [Fact]
    public void A_goals_pull_request_nothing_read_and_no_candidate_holds_has_no_title()
    {
        var pull = GoalsMapper.Map(StandUp, Summaries, [], Candidates).Goals[0].PullRequests[0];

        Assert.Equal(1352, pull.Number);
        Assert.Null(pull.Title);
    }

    [Fact]
    public void A_pull_request_whose_status_was_not_read_draws_unknown()
    {
        var pulls = GoalsMapper.Map(StandUp, Summaries, [], Candidates).Goals[0].PullRequests;

        Assert.All(pulls, pull => Assert.Equal("unknown", pull.Slug));
    }

    [Fact]
    public void Finished_and_planned_keep_their_groups_and_a_planned_pull_request_carries_its_status()
    {
        var standUp = Map().StandUp;

        Assert.Equal(["Customers get paid faster", null], standUp.Finished.Select(group => group.GoalTitle));
        Assert.Equal("VAT rate per line item", standUp.Finished[1].Items[0].Title);

        var planned = standUp.Planned[0].Items;
        Assert.Null(planned[0].PullRequest);
        Assert.Equal(1357, planned[1].PullRequest!.Number);
        Assert.Equal("draft", planned[1].PullRequest!.Slug);
        Assert.Equal(21, standUp.PlannedEffort);
        Assert.Equal(18, standUp.Capacity);
        Assert.Equal(1, standUp.ResolvedInLast);
    }

    [Fact]
    public void An_impediment_carries_its_state_and_only_the_moves_it_may_make()
    {
        var impediment = Assert.Single(Map().StandUp.Impediments);

        Assert.Equal(BankId.ToString(), impediment.Id);
        Assert.Equal(StandUpImpedimentState.Escalated, impediment.State);
        Assert.Equal([StandUpImpedimentState.BeingHandled, StandUpImpedimentState.Resolved], impediment.Moves);
        Assert.Equal("Account manager", impediment.With);
        Assert.Equal(3, impediment.DaysOpen);
        Assert.Equal("Customers get paid faster", impediment.GoalTitle);
        Assert.Equal("QA12 phone note", impediment.Note);
    }

    [Fact]
    public void The_attach_search_matches_every_unfinished_plan_every_open_task_and_every_open_pull_request()
    {
        var candidates = Map().Candidates;

        Assert.Equal(
            [GoalSubject.ForPlan("bank-integration"), GoalSubject.ForPlan("devbook-search")],
            candidates.Where(item => item.Kind == GoalSubjectKind.Plan).Select(item => item.Subject));
        Assert.Equal(
            ["Recurring invoices", "Payment matching rules", "VAT rate per line item"],
            candidates.Where(item => item.Kind == GoalSubjectKind.Task).Select(item => item.Title));
        var pull = Assert.Single(candidates, item => item.Kind == GoalSubjectKind.PullRequest);
        Assert.Equal((GoalSubject.ForPullRequest("JSdotNet/Backlog", 1341), 1341), (pull.Subject, pull.Number));
    }

    /// <summary>A finished plan has nothing left to attach to a goal, whichever way its
    /// entries spell the tag.</summary>
    [Theory]
    [InlineData("search-rollout")]
    [InlineData("+search-rollout")]
    public void The_attach_search_leaves_out_a_plan_whose_entries_are_all_done(string tag)
    {
        var offered = PlansOffered(Entry(GoalTaskStatus.Done, tag), Entry(GoalTaskStatus.Done, tag));

        Assert.Empty(offered);
    }

    [Fact]
    public void The_attach_search_keeps_a_plan_with_an_open_entry()
    {
        var offered = PlansOffered(Entry(GoalTaskStatus.Done, "search-rollout"), Entry(GoalTaskStatus.Doing, "+search-rollout"));

        Assert.Equal([GoalSubject.ForPlan("search-rollout")], offered);
    }

    [Fact]
    public void The_attach_search_keeps_a_plan_with_no_entries()
    {
        var offered = PlansOffered(Entry(GoalTaskStatus.Done, "another-plan"));

        Assert.Equal([GoalSubject.ForPlan("search-rollout")], offered);
    }

    private static GoalsTaskCandidate Entry(GoalTaskStatus status, string planTag) =>
        new(Guid.NewGuid(), "An entry", status, [planTag], [], true);

    /// <summary>The plans the attach search offers when the shell lists one plan and these
    /// entries.</summary>
    private static List<GoalSubject> PlansOffered(params GoalsTaskCandidate[] entries)
    {
        var candidates = Candidates with { Tasks = entries, Plans = ["search-rollout"] };

        return [.. GoalsMapper.Map(StandUp, Summaries, Pulls, candidates).Candidates
            .Where(item => item.Kind == GoalSubjectKind.Plan)
            .Select(item => item.Subject)];
    }

    [Fact]
    public void The_repositories_a_bare_pull_request_number_is_read_in_are_the_shells()
    {
        Assert.Empty(Map().Repositories);

        var board = GoalsMapper.Map(StandUp, Summaries, Pulls, Candidates with { Repositories = ["JSdotNet/Backlog", "JSdotNet/Marketplace"] });

        Assert.Equal(["JSdotNet/Backlog", "JSdotNet/Marketplace"], board.Repositories);
    }

    [Fact]
    public void No_goal_is_what_no_goal_holds()
    {
        var noGoal = Map().NoGoal;

        Assert.Equal(["VAT rate per line item"], noGoal.Select(item => item.Title));
    }

    /// <summary>A No goal pull request row carries the status line the goals' own read, from
    /// the same reading, so it draws the way the open goal's Pull requests column does.</summary>
    [Fact]
    public void A_no_goal_pull_request_row_carries_its_read_status()
    {
        var candidates = Candidates with
        {
            PullRequests = [.. Candidates.PullRequests, new GoalsPullRequestCandidate("JSdotNet/Backlog", 1362, "Credit notes", true, "https://github.com/JSdotNet/Backlog/pull/1362")]
        };
        var pulls = (IReadOnlyList<GoalPullRequestDto>)[.. Pulls, new("JSdotNet/Backlog", "1362", LinkedPullRequestState.Open, null, new GoalPullRequestChecksDto(10, 1, 2, []), new GoalPullRequestApprovalsDto(0, 1), ReviewRequested: true)];

        var row = Assert.Single(GoalsMapper.Map(StandUp, Summaries, pulls, candidates).NoGoal, item => item.Kind == GoalSubjectKind.PullRequest);

        Assert.Equal(
            new PullRequestStatusModel(1362, "open", new PullRequestChecks(10, 1, 2), new PullRequestApprovals(0, 1), "Credit notes", "https://github.com/JSdotNet/Backlog/pull/1362", true),
            row.PullRequest);
        Assert.Null(row.Status);
    }

    [Fact]
    public void A_no_goal_pull_request_row_whose_status_was_not_read_draws_unknown_with_its_title_and_url()
    {
        var candidates = Candidates with
        {
            PullRequests = [.. Candidates.PullRequests, new GoalsPullRequestCandidate("JSdotNet/Backlog", 1362, "Credit notes", true)]
        };

        var row = Assert.Single(GoalsMapper.Map(StandUp, Summaries, Pulls, candidates).NoGoal, item => item.Kind == GoalSubjectKind.PullRequest);

        Assert.Equal(
            new PullRequestStatusModel(1362, "unknown", Title: "Credit notes", Url: "https://github.com/JSdotNet/Backlog/pull/1362"),
            row.PullRequest);
    }

    [Fact]
    public void A_no_goal_task_row_carries_its_status_through_the_board()
    {
        var row = Assert.Single(Map().NoGoal);

        Assert.Equal(GoalTaskStatus.Doing, row.Status);
        Assert.Null(row.PullRequest);
    }

    [Fact]
    public void Every_pull_request_the_board_shows_is_read_once()
    {
        var refs = GoalsMapper.PullRequestsToRead(StandUp, Summaries, Candidates);

        Assert.Equal(
            ["1352", "1341", "1357"],
            refs.Select(pull => pull.Number));
    }

    /// <summary>No goal's pull requests are read too, after the goals' and the stand-up's,
    /// each once; one a goal holds or one outside the scope is not a No goal row.</summary>
    [Fact]
    public void The_pull_requests_no_goal_lists_are_read_after_the_goals_and_the_stand_ups()
    {
        var candidates = Candidates with
        {
            PullRequests =
            [
                .. Candidates.PullRequests,
                new GoalsPullRequestCandidate("JSdotNet/Backlog", 1362, "Credit notes", true),
                new GoalsPullRequestCandidate("JSdotNet/Backlog", 1357, "Payment status webhook", true),
                new GoalsPullRequestCandidate("JSdotNet/Backlog", 1370, "Other repository work", false)
            ]
        };

        var refs = GoalsMapper.PullRequestsToRead(StandUp, Summaries, candidates);

        Assert.Equal(
            ["1352", "1341", "1357", "1362"],
            refs.Select(pull => pull.Number));
    }

    /// <summary>A plan's rows are counted by their pull requests, so those are read too:
    /// after the rest, which keep their order, and each once, however a link spells it.</summary>
    [Fact]
    public void The_pull_requests_a_plans_rows_link_are_read_last()
    {
        var candidates = Candidates with
        {
            Tasks =
            [
                .. Candidates.Tasks,
                new(Guid.NewGuid(), "Bank sample files", GoalTaskStatus.Doing, ["bank-integration"], [Linked("1400"), Linked("1341"), Linked("1401")], true),
                new(Guid.NewGuid(), "Statement parser", GoalTaskStatus.ToDo, ["+bank-integration"], [Linked("1401"), Linked("#1402"), new PullRequestRefDto("jsdotnet/backlog", "1400")], true),
                new(Guid.NewGuid(), "Filed elsewhere", GoalTaskStatus.ToDo, ["devbook-search"], [Linked("1499")], true)
            ]
        };

        var refs = GoalsMapper.PullRequestsToRead(StandUp, Summaries, candidates);

        Assert.Equal(["1352", "1341", "1357", "1400", "1401", "1402"], refs.Select(pull => pull.Number));
    }

    /// <summary>Read to count them, not to show them: the Pull requests column stays its
    /// own and those of its attached tasks.</summary>
    [Fact]
    public void The_pull_requests_a_plans_rows_link_do_not_join_the_cards_column()
    {
        var candidates = Candidates with
        {
            Tasks = [.. Candidates.Tasks, new(Guid.NewGuid(), "Bank sample files", GoalTaskStatus.Doing, ["bank-integration"], [Linked("1400")], true)]
        };
        IReadOnlyList<GoalPullRequestDto> pulls = [.. Pulls, Answer("1400", LinkedPullRequestState.Merged)];

        var card = GoalsMapper.Map(StandUp, Summaries, pulls, candidates).Goals[0];

        Assert.Equal([1352, 1341], card.PullRequests.Select(pull => pull.Number));
        Assert.Equal([GoalSubject.ForPullRequest("JSdotNet/Backlog", 1352), null], card.PullRequestSubjects);
    }

    [Theory]
    [InlineData(IterationLength.Day, GoalsIteration.Day)]
    [InlineData(IterationLength.Week, GoalsIteration.Week)]
    [InlineData(IterationLength.TwoWeeks, GoalsIteration.TwoWeeks)]
    [InlineData(IterationLength.Month, GoalsIteration.Month)]
    public void The_iteration_lengths_map_both_ways(IterationLength length, GoalsIteration iteration)
    {
        Assert.Equal(iteration, GoalsMapper.Iteration(length));
        Assert.Equal(length, GoalsMapper.Length(iteration));
    }

    [Theory]
    [InlineData(ImpedimentState.New, StandUpImpedimentState.New)]
    [InlineData(ImpedimentState.Reported, StandUpImpedimentState.Reported)]
    [InlineData(ImpedimentState.Escalated, StandUpImpedimentState.Escalated)]
    [InlineData(ImpedimentState.BeingHandled, StandUpImpedimentState.BeingHandled)]
    [InlineData(ImpedimentState.Resolved, StandUpImpedimentState.Resolved)]
    public void The_impediment_states_map_both_ways(ImpedimentState state, StandUpImpedimentState shown)
    {
        Assert.Equal(shown, GoalsMapper.State(state));
        Assert.Equal(state, GoalsMapper.State(shown));
    }
}
