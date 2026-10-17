using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Services;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The rules a goal holds itself and its attachments to
/// (<c>.devbook/domain/progress/domain.invariants.md#goal</c>), and the one rule across goals
/// (<c>#goal-assignment</c>), one test or more per invariant.
/// </summary>
public sealed class GoalInvariantTests
{
    // --- Invariant: A goal has a title -------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_goal_without_a_title_is_refused(string? title)
    {
        Assert.Equal("progress.goal_without_title", Goal.Create(Guid.NewGuid(), title, null, MakeGoal.Due).Error.Code);

        var goal = MakeGoal.Named();
        Assert.Equal("progress.goal_without_title", goal.Rename(title).Error.Code);
        Assert.Equal("Customers get paid faster", goal.Title);
    }

    [Fact]
    public void A_title_is_kept_trimmed()
    {
        Assert.Equal("Customers get paid faster", Goal.Create(Guid.NewGuid(), "  Customers get paid faster ", null, MakeGoal.Due).Value.Title);
    }

    // --- Invariant: A goal has a due date ----------------------------------------

    [Fact]
    public void A_goal_without_a_due_date_is_refused()
    {
        Assert.Equal("progress.goal_without_due_date", Goal.Create(Guid.NewGuid(), "Customers get paid faster", null, null).Error.Code);

        var goal = MakeGoal.Named();
        Assert.Equal("progress.goal_without_due_date", goal.ChangeDueDate(null).Error.Code);
        Assert.Equal(MakeGoal.Due, goal.DueDate);
    }

    // --- Invariant: An outcome is one line ---------------------------------------

    [Theory]
    [InlineData("Invoices settle\nin a day")]
    [InlineData("Invoices settle\r\nin a day")]
    public void An_outcome_of_more_than_one_line_is_refused(string outcome)
    {
        Assert.Equal("progress.outcome_not_one_line", Goal.Create(Guid.NewGuid(), "T", outcome, MakeGoal.Due).Error.Code);
        Assert.Equal("progress.outcome_not_one_line", MakeGoal.Named().ChangeOutcome(outcome).Error.Code);
    }

    [Fact]
    public void A_blank_outcome_is_none()
    {
        var goal = Goal.Create(Guid.NewGuid(), "T", "Invoices settle in a day", MakeGoal.Due).Value;
        Assert.Equal("Invoices settle in a day", goal.Outcome);

        Assert.True(goal.ChangeOutcome("  ").IsSuccess);
        Assert.Null(goal.Outcome);
    }

    // --- Invariant: An item is attached to a goal once ---------------------------

    [Fact]
    public void Attaching_an_item_again_changes_nothing_and_keeps_its_first_day()
    {
        var task = Guid.NewGuid();
        var goal = MakeGoal.Named("G", MakeGoal.Task(task));

        goal.Attach(GoalAttachment.ForTask(task, MakeGoal.Today.AddDays(3)).Value);
        goal.Attach(GoalAttachment.ForPlan("BANK-integration", MakeGoal.Today).Value);
        goal.Attach(GoalAttachment.ForPlan("+bank-integration", MakeGoal.Today).Value);

        Assert.Equal(2, goal.Attachments.Count);
        Assert.Equal(MakeGoal.Today, goal.Attachments[0].AttachedOn);
    }

    [Fact]
    public void An_attachment_is_equal_by_kind_and_identity_alone()
    {
        Assert.Equal(
            GoalAttachment.ForPullRequest("JSdotNet/Backlog", 1338, MakeGoal.Today).Value,
            GoalAttachment.ForPullRequest("jsdotnet/backlog", 1338, MakeGoal.Today.AddDays(1)).Value);
        Assert.NotEqual(
            GoalAttachment.ForPullRequest("JSdotNet/Backlog", 1338, MakeGoal.Today).Value,
            GoalAttachment.ForPullRequest("JSdotNet/Backlog", 1340, MakeGoal.Today).Value);
    }

    [Fact]
    public void Detaching_answers_whether_it_was_held()
    {
        var task = Guid.NewGuid();
        var goal = MakeGoal.Named("G", MakeGoal.Task(task));

        Assert.True(goal.Detach(MakeGoal.Task(task)));
        Assert.False(goal.Detach(MakeGoal.Task(task)));
        Assert.Empty(goal.Attachments);
    }

    // --- Invariant: A pull request attachment names its repository ---------------

    [Theory]
    [InlineData(null, 1338)]
    [InlineData(" ", 1338)]
    [InlineData("JSdotNet/Backlog", 0)]
    [InlineData("JSdotNet/Backlog", -4)]
    [InlineData("JSdotNet/Backlog", null)]
    public void A_pull_request_without_a_repository_or_a_number_is_refused(string? repository, int? number)
    {
        Assert.Equal(
            "progress.pull_request_without_repository",
            GoalAttachment.ForPullRequest(repository, number, MakeGoal.Today).Error.Code);
    }

    [Fact]
    public void A_plan_needs_its_tag_and_a_task_its_id()
    {
        Assert.Equal("progress.plan_tag_required", GoalAttachment.ForPlan("+", MakeGoal.Today).Error.Code);
        Assert.Equal("progress.task_required", GoalAttachment.ForTask(Guid.Empty, MakeGoal.Today).Error.Code);
        Assert.Equal("bank-integration", GoalAttachment.ForPlan(" +bank-integration ", MakeGoal.Today).Value.Plan);
    }

    // --- Invariant: An item belongs to at most one goal (Goal Assignment) ---------

    /// <summary>Scenario "A task another goal holds": refused with the other goal's name,
    /// and the task stays where it was.</summary>
    [Fact]
    public void Attaching_an_item_another_goal_holds_is_refused_naming_that_goal()
    {
        var task = Guid.NewGuid();
        var search = MakeGoal.Named("Devbook search ships", MakeGoal.Task(task));
        var paid = MakeGoal.Named("Customers get paid faster");

        var assigned = GoalAssignment.Assign([search, paid], paid, MakeGoal.Task(task));

        Assert.Equal("progress.item_in_other_goal", assigned.Error.Code);
        Assert.Equal("Already in Devbook search ships", assigned.Error.Message);
        Assert.True(search.Holds(MakeGoal.Task(task)));
        Assert.Empty(paid.Attachments);
    }

    [Fact]
    public void A_plan_or_a_pull_request_belongs_to_one_goal_too()
    {
        var search = MakeGoal.Named("Devbook search ships", MakeGoal.Plan("+bank-integration"), MakeGoal.PullRequest("JSdotNet/Backlog", 1352));
        var paid = MakeGoal.Named("Customers get paid faster");

        Assert.True(GoalAssignment.Assign([search, paid], paid, MakeGoal.Plan("bank-integration")).IsFailure);
        Assert.True(GoalAssignment.Assign([search, paid], paid, MakeGoal.PullRequest("jsdotnet/backlog", 1352)).IsFailure);
    }

    /// <summary>Asked to move it, the item leaves the other goal in the same change, so it
    /// is never in two; the goal it left is answered so it is saved too.</summary>
    [Fact]
    public void Moving_an_item_takes_it_out_of_the_other_goal()
    {
        var task = Guid.NewGuid();
        var search = MakeGoal.Named("Devbook search ships", MakeGoal.Task(task));
        var paid = MakeGoal.Named("Customers get paid faster");

        var assigned = GoalAssignment.Assign([search, paid], paid, MakeGoal.Task(task), move: true);

        Assert.Same(search, assigned.Value);
        Assert.False(search.Holds(MakeGoal.Task(task)));
        Assert.True(paid.Holds(MakeGoal.Task(task)));
    }

    [Fact]
    public void Attaching_to_the_goal_that_holds_it_already_is_no_conflict()
    {
        var task = Guid.NewGuid();
        var paid = MakeGoal.Named("Customers get paid faster", MakeGoal.Task(task));

        var assigned = GoalAssignment.Assign([paid], paid, MakeGoal.Task(task));

        Assert.True(assigned.IsSuccess);
        Assert.Null(assigned.Value);
        Assert.Single(paid.Attachments);
    }

    /// <summary>A task held directly by one goal while another holds its plan is no
    /// conflict: the rule is over attachments, and the counting gives the entry to the goal
    /// that holds it directly (see <see cref="GoalCountingTests"/>).</summary>
    [Fact]
    public void A_task_another_goal_holds_through_its_plan_may_be_attached_directly()
    {
        var task = Guid.NewGuid();
        var paid = MakeGoal.Named("Customers get paid faster", MakeGoal.Plan("+bank-integration"));
        var search = MakeGoal.Named("Devbook search ships");

        Assert.True(GoalAssignment.Assign([paid, search], search, MakeGoal.Task(task)).IsSuccess);
    }
}
