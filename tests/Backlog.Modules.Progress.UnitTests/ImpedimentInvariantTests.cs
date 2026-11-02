using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.DomainModels;

using static Backlog.Modules.Progress.UnitTests.MakeImpediment;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// The rules <c>.devbook/domain/progress/domain.invariants.md#impediment</c> gives an
/// impediment, one test or theory each, and every move of the state diagram in
/// <c>.devbook/domain/progress/domain.md#impediment-state</c> — allowed and refused.
/// </summary>
public sealed class ImpedimentInvariantTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_impediment_has_a_title(string? title)
    {
        Assert.Equal("progress.impediment_without_title", Impediment.Raise(Guid.NewGuid(), title, Wed14).Error.Code);

        var impediment = Raised();
        Assert.Equal("progress.impediment_without_title", impediment.Rename(title).Error.Code);
        Assert.Equal("Bank sample files missing", impediment.Title);
    }

    [Fact]
    public void An_impediment_starts_New_and_its_history_opens_with_that_step()
    {
        var impediment = Impediment.Raise(Guid.NewGuid(), "  Bank sample files missing ", Wed14).Value;

        Assert.Equal("Bank sample files missing", impediment.Title);
        Assert.Equal(ImpedimentState.New, impediment.State);
        Assert.True(impediment.IsOpen);
        Assert.Equal(Wed14, impediment.RaisedOn);
        Assert.Null(impediment.ResolvedOn);
        Assert.Equal([new ImpedimentStateChange(ImpedimentState.New, Wed14, null)], impediment.History);
    }

    /// <summary>Every allowed forward move, Escalated skipped or not.</summary>
    [Theory]
    [InlineData(new[] { ImpedimentState.Reported })]
    [InlineData(new[] { ImpedimentState.Reported, ImpedimentState.Escalated })]
    [InlineData(new[] { ImpedimentState.Reported, ImpedimentState.BeingHandled })]
    [InlineData(new[] { ImpedimentState.Reported, ImpedimentState.Escalated, ImpedimentState.BeingHandled })]
    public void An_impediment_moves_forward_along_its_states(ImpedimentState[] path)
    {
        var impediment = At(path);

        Assert.Equal(path[^1], impediment.State);
        Assert.Equal([ImpedimentState.New, .. path], impediment.History.Select(step => step.State));
    }

    /// <summary>Every move the diagram does not draw, from each open state.</summary>
    [Theory]
    [InlineData(new ImpedimentState[0], ImpedimentState.New)]
    [InlineData(new ImpedimentState[0], ImpedimentState.Escalated)]
    [InlineData(new ImpedimentState[0], ImpedimentState.BeingHandled)]
    [InlineData(new[] { ImpedimentState.Reported }, ImpedimentState.New)]
    [InlineData(new[] { ImpedimentState.Reported }, ImpedimentState.Reported)]
    [InlineData(new[] { ImpedimentState.Reported, ImpedimentState.Escalated }, ImpedimentState.New)]
    [InlineData(new[] { ImpedimentState.Reported, ImpedimentState.Escalated }, ImpedimentState.Reported)]
    [InlineData(new[] { ImpedimentState.Reported, ImpedimentState.Escalated }, ImpedimentState.Escalated)]
    [InlineData(new[] { ImpedimentState.Reported, ImpedimentState.BeingHandled }, ImpedimentState.New)]
    [InlineData(new[] { ImpedimentState.Reported, ImpedimentState.BeingHandled }, ImpedimentState.Reported)]
    [InlineData(new[] { ImpedimentState.Reported, ImpedimentState.BeingHandled }, ImpedimentState.Escalated)]
    [InlineData(new[] { ImpedimentState.Reported, ImpedimentState.BeingHandled }, ImpedimentState.BeingHandled)]
    public void Any_other_move_is_refused_and_changes_nothing(ImpedimentState[] path, ImpedimentState to)
    {
        var impediment = At(path);
        var before = impediment.History.ToList();

        var moved = impediment.MoveTo(to, Fri16.AddDays(3));

        Assert.Equal("progress.impediment_move_not_allowed", moved.Error.Code);
        Assert.Equal(before, impediment.History);
    }

    /// <summary>Requirements scenario "Moving back": Escalated to Reported is refused.</summary>
    [Fact]
    public void Moving_an_escalated_impediment_back_to_Reported_is_refused()
    {
        var impediment = At(ImpedimentState.Reported, ImpedimentState.Escalated);

        Assert.True(impediment.MoveTo(ImpedimentState.Reported, Fri16.AddDays(1)).IsFailure);
        Assert.Equal(ImpedimentState.Escalated, impediment.State);
    }

    [Theory]
    [InlineData(new ImpedimentState[0])]
    [InlineData(new[] { ImpedimentState.Reported })]
    [InlineData(new[] { ImpedimentState.Reported, ImpedimentState.Escalated })]
    [InlineData(new[] { ImpedimentState.Reported, ImpedimentState.Escalated, ImpedimentState.BeingHandled })]
    public void Any_open_impediment_may_be_resolved(ImpedimentState[] path)
    {
        var impediment = At(path);
        var on = Fri16.AddDays(3);

        Assert.True(impediment.MoveTo(ImpedimentState.Resolved, on).IsSuccess);
        Assert.Equal(ImpedimentState.Resolved, impediment.State);
        Assert.False(impediment.IsOpen);
        Assert.Equal(on, impediment.ResolvedOn);
        Assert.Equal([ImpedimentState.New, .. path, ImpedimentState.Resolved], impediment.History.Select(step => step.State));
    }

    [Theory]
    [InlineData(ImpedimentState.New)]
    [InlineData(ImpedimentState.Reported)]
    [InlineData(ImpedimentState.Escalated)]
    [InlineData(ImpedimentState.BeingHandled)]
    [InlineData(ImpedimentState.Resolved)]
    public void A_resolved_impediment_stays_resolved(ImpedimentState to)
    {
        var impediment = Raised();
        Assert.True(impediment.MoveTo(ImpedimentState.Resolved, Thu15).IsSuccess);

        Assert.Equal("progress.impediment_resolved", impediment.MoveTo(to, Fri16).Error.Code);
        Assert.Equal(2, impediment.History.Count);
    }

    /// <summary>Requirements scenario "Escalating on a Friday".</summary>
    [Fact]
    public void Every_move_records_its_day_and_who_it_is_with()
    {
        var impediment = Raised();

        Assert.True(impediment.MoveTo(ImpedimentState.Reported, Thu15, "bank support").IsSuccess);
        Assert.True(impediment.MoveTo(ImpedimentState.Escalated, Fri16, " account manager ").IsSuccess);

        Assert.Equal(
            [
                new ImpedimentStateChange(ImpedimentState.New, Wed14, null),
                new ImpedimentStateChange(ImpedimentState.Reported, Thu15, "bank support"),
                new ImpedimentStateChange(ImpedimentState.Escalated, Fri16, "account manager"),
            ],
            impediment.History);
        Assert.Equal("account manager", impediment.ReportedTo);
        Assert.Null(impediment.HandledBy);

        Assert.True(impediment.MoveTo(ImpedimentState.BeingHandled, Fri16, "bank integration team").IsSuccess);
        Assert.Equal("bank integration team", impediment.HandledBy);
        Assert.Equal("account manager", impediment.ReportedTo);
    }

    [Fact]
    public void A_move_without_who_keeps_whoever_it_was_with()
    {
        var impediment = Raised();
        Assert.True(impediment.MoveTo(ImpedimentState.Reported, Thu15, "bank support").IsSuccess);

        Assert.True(impediment.MoveTo(ImpedimentState.Escalated, Fri16, "  ").IsSuccess);

        Assert.Null(impediment.History[^1].With);
        Assert.Equal("bank support", impediment.ReportedTo);
    }

    [Fact]
    public void The_history_days_never_go_backwards()
    {
        var impediment = Raised(on: Thu15);

        var moved = impediment.MoveTo(ImpedimentState.Reported, Wed14);

        Assert.Equal("progress.impediment_move_before_last_step", moved.Error.Code);
        Assert.Single(impediment.History);
        Assert.True(impediment.MoveTo(ImpedimentState.Reported, Thu15).IsSuccess);
    }

    [Fact]
    public void An_item_is_held_back_once_by_kind_and_identity()
    {
        var impediment = Raised();
        var task = Guid.NewGuid();

        impediment.HoldBack(HeldTask(task));
        impediment.HoldBack(HeldPullRequest("Acme/Payments", 412));
        impediment.HoldBack(HeldPlan("+Goals-standup"));
        impediment.HoldBack(HeldTask(task));
        impediment.HoldBack(HeldPullRequest("acme/payments", 412));
        impediment.HoldBack(HeldPlan("goals-standup"));

        Assert.Equal([HeldTask(task), HeldPullRequest("acme/payments", 412), HeldPlan("goals-standup")], impediment.HoldsBack);
        Assert.True(impediment.Holds(HeldPullRequest("ACME/PAYMENTS", 412)));
        Assert.False(impediment.Holds(HeldPullRequest("acme/payments", 413)));
    }

    [Fact]
    public void A_task_a_pull_request_and_a_plan_never_equal_one_another()
    {
        Assert.NotEqual(HeldPullRequest("acme/payments", 7), HeldPlan("acme/payments"));
        Assert.NotEqual(HeldPlan("7"), HeldPullRequest("7", 7));
        Assert.NotEqual(HeldTask(Guid.Parse("00000000-0000-0000-0000-000000000007")), HeldPullRequest("acme/payments", 7));
    }

    [Theory]
    [InlineData(null, 412)]
    [InlineData("", 412)]
    [InlineData("   ", 412)]
    [InlineData("acme/payments", null)]
    [InlineData("acme/payments", 0)]
    [InlineData("acme/payments", -3)]
    public void A_held_back_pull_request_names_its_repository_and_a_positive_number(string? repository, int? number) =>
        Assert.Equal("progress.held_back_item_without_identity", HeldBackItem.ForPullRequest(repository, number).Error.Code);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+")]
    [InlineData(" + ")]
    public void A_held_back_plan_names_a_non_blank_tag(string? tag) =>
        Assert.Equal("progress.held_back_item_without_identity", HeldBackItem.ForPlan(tag).Error.Code);

    [Fact]
    public void A_held_back_task_names_its_id_and_a_plan_is_kept_without_its_sigil()
    {
        Assert.Equal("progress.held_back_item_without_identity", HeldBackItem.ForTask(Guid.Empty).Error.Code);

        var plan = HeldPlan(" +goals-standup ");
        Assert.Equal(HeldBackKind.Plan, plan.Kind);
        Assert.Equal("goals-standup", plan.PlanTag);

        var pull = HeldPullRequest(" acme/payments ", 412);
        Assert.Equal("acme/payments", pull.Repository);
        Assert.Equal(412, pull.Number);
    }

    [Fact]
    public void Releasing_an_item_it_does_not_hold_back_changes_nothing()
    {
        var impediment = At(ImpedimentState.Reported);
        var task = Guid.NewGuid();
        impediment.HoldBack(HeldTask(task));
        impediment.HoldBack(HeldPullRequest("acme/payments", 412));
        var history = impediment.History.ToList();

        Assert.False(impediment.Release(HeldTask(Guid.NewGuid())));
        Assert.False(impediment.Release(HeldPullRequest("acme/payments", 413)));
        Assert.False(impediment.Release(HeldPlan("goals-standup")));

        Assert.Equal([HeldTask(task), HeldPullRequest("acme/payments", 412)], impediment.HoldsBack);
        Assert.Equal(history, impediment.History);

        Assert.True(impediment.Release(HeldPullRequest("ACME/payments", 412)));
        Assert.Equal([HeldTask(task)], impediment.HoldsBack);
    }

    [Fact]
    public void Linking_and_unlinking_leave_the_state_and_history_alone()
    {
        var impediment = At(ImpedimentState.Reported, ImpedimentState.Escalated);
        var history = impediment.History.ToList();

        impediment.HoldBack(HeldPlan("goals-standup"));
        impediment.HoldBack(HeldPullRequest("acme/payments", 412));
        impediment.Release(HeldPlan("goals-standup"));

        Assert.Equal(ImpedimentState.Escalated, impediment.State);
        Assert.Equal(history, impediment.History);
    }

    [Fact]
    public void Check_again_and_note_are_set_and_cleared()
    {
        var impediment = Raised();

        impediment.SetCheckAgain(new DateOnly(2026, 10, 20));
        impediment.EditNote("  Asked for the sample twice.  ");
        Assert.Equal(new DateOnly(2026, 10, 20), impediment.CheckAgain);
        Assert.Equal("Asked for the sample twice.", impediment.Note);

        impediment.SetCheckAgain(null);
        impediment.EditNote("   ");
        Assert.Null(impediment.CheckAgain);
        Assert.Null(impediment.Note);
    }

    [Fact]
    public void Restoring_keeps_each_item_once_in_link_order_and_opens_an_empty_history_as_New()
    {
        var task = Guid.NewGuid();

        var restored = Impediment.Restore(
            Guid.NewGuid(),
            "Bank sample files missing",
            null,
            null,
            null,
            null,
            [],
            [HeldPlan("goals-standup"), HeldTask(task), HeldPullRequest("acme/payments", 412), HeldTask(task), HeldPlan("+GOALS-STANDUP")],
            null,
            Thu15);

        Assert.Equal([HeldPlan("goals-standup"), HeldTask(task), HeldPullRequest("acme/payments", 412)], restored.HoldsBack);
        Assert.Equal(ImpedimentState.New, restored.State);
        Assert.Equal(Thu15, restored.RaisedOn);
    }

    private static HeldBackItem HeldTask(Guid id) => HeldBackItem.ForTask(id).Value;

    private static HeldBackItem HeldPullRequest(string repository, int number) => HeldBackItem.ForPullRequest(repository, number).Value;

    private static HeldBackItem HeldPlan(string tag) => HeldBackItem.ForPlan(tag).Value;
}
