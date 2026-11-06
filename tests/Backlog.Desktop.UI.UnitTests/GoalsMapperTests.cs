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

        Assert.Equal(new GoalPlanModel("bank-integration", 1, 2, GoalSubject.ForPlan("bank-integration")), plan);
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
