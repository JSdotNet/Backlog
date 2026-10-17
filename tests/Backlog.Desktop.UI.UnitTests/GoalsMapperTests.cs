using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.UI.Goals;
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
            new StandUpImpedimentDto(BankId, "Bank sample files missing", ImpedimentState.Escalated, "Account manager", 3, null, PaidId, "Customers get paid faster")
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
        new("JSdotNet/Backlog", "1341", LinkedPullRequestState.Open, null, new GoalPullRequestChecksDto(14, 0, 0, []), new GoalPullRequestApprovalsDto(1, 2)),
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

    /// <summary>Open question 6's default: a plan's count is its rows done over its rows,
    /// as the shell's task list has them.</summary>
    [Fact]
    public void An_attached_plan_counts_the_rows_filed_under_it()
    {
        var plan = Assert.Single(Map().Goals[0].Plans);

        Assert.Equal(new GoalPlanModel("bank-integration", 1, 2), plan);
    }

    [Fact]
    public void An_attached_task_is_listed_with_its_status_and_a_task_the_list_no_longer_has_is_left_out()
    {
        Assert.Equal(
            [
                new GoalTaskModel(Invoice.ToString(), "Invoice PDF export", GoalTaskStatus.Done),
                new GoalTaskModel(Recurring.ToString(), "Recurring invoices", GoalTaskStatus.Doing)
            ],
            Map().Goals[0].Tasks);
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
    }

    [Fact]
    public void The_attach_search_matches_every_plan_every_open_task_and_every_open_pull_request()
    {
        var candidates = Map().Candidates;

        Assert.Equal(
            ["plan:bank-integration", "plan:devbook-search"],
            candidates.Where(item => item.Kind == GoalsItemKind.Plan).Select(item => item.Key));
        Assert.Equal(
            ["Recurring invoices", "Payment matching rules", "VAT rate per line item"],
            candidates.Where(item => item.Kind == GoalsItemKind.Task).Select(item => item.Title));
        var pull = Assert.Single(candidates, item => item.Kind == GoalsItemKind.PullRequest);
        Assert.Equal(("pr:JSdotNet/Backlog#1341", 1341), (pull.Key, pull.Number));
    }

    [Fact]
    public void No_goal_is_what_no_goal_holds()
    {
        var noGoal = Map().NoGoal;

        Assert.Equal(["VAT rate per line item"], noGoal.Select(item => item.Title));
    }

    [Fact]
    public void Every_pull_request_the_board_shows_is_read_once()
    {
        var refs = GoalsMapper.PullRequestsToRead(StandUp, Summaries, Candidates);

        Assert.Equal(
            ["1352", "1341", "1357"],
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
