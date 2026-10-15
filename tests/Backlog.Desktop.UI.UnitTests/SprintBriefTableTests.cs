using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;
using Backlog.UI.Components.Progress;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The weekly brief's Copy as table builds the sprint view's own table
/// (<see cref="SprintTableClipboard"/>) from the block's sprint as the dashboard's port
/// answers it: each member the row the board would draw for it, the goal plans leading.
/// </summary>
public class SprintBriefTableTests
{
    [Fact]
    public void Each_member_becomes_the_row_the_sprint_view_draws_for_it()
    {
        var pull = new SprintBriefPullRequest("innovadis-dev/Fincent", "4707", SprintBriefPullRequestState.Open, null);
        var member = SprintFixtures.Member("Klantrekening", goal: true, plan: "rekeningen", added: true, pulls: [pull]) with
        {
            IsInProgress = true,
            Repository = "Fincent",
            Owners = ["jsdotnet"],
            Note = "Waits on design",
            OpenQuestion = true
        };

        var row = SprintBriefTable.Row(member);

        Assert.Equal(member.TaskId.ToString(), row.TaskId);
        Assert.Equal("Klantrekening", row.Title);
        Assert.Equal(SprintRowState.InProgress, row.State);
        Assert.Equal("rekeningen", row.Plan);
        Assert.Equal("Fincent", row.Repository);
        Assert.True(row.IsGoal);
        Assert.True(row.AddedAfterStart);
        Assert.Equal(new SprintBoardPullRequest("innovadis-dev/Fincent", "4707", SprintPullRequestState.Open), Assert.Single(row.PullRequests));
        Assert.Equal(["jsdotnet"], row.Owners);
        Assert.Equal("Waits on design", row.Note);
        Assert.True(row.OpenQuestion);
        Assert.Equal("In review", row.StatusText);
    }

    [Theory]
    [InlineData(true, false, false, SprintRowState.Done)]
    [InlineData(false, true, false, SprintRowState.Blocked)]
    [InlineData(false, false, true, SprintRowState.InProgress)]
    [InlineData(false, false, false, SprintRowState.NotStarted)]
    public void The_state_is_the_progress_contexts_reading(bool done, bool blocked, bool inProgress, SprintRowState expected)
    {
        var member = SprintFixtures.Member("Task", blocked: blocked, doneOn: done ? new DateOnly(2026, 10, 6) : null) with { IsInProgress = inProgress };

        Assert.Equal(expected, SprintBriefTable.Row(member).State);
    }

    [Fact]
    public void The_table_is_the_sprint_views_table_over_every_member_with_the_goal_plans_first()
    {
        var sprint = SprintFixtures.Sprint42() with { GoalPlans = ["specs"] };

        var table = SprintBriefTable.Build(sprint);

        var expected = SprintTableClipboard.Build([.. sprint.Members.Select(SprintBriefTable.Row)], ["specs"]);
        Assert.Equal(expected, table);

        var lines = table.Text.Split("\r\n");
        Assert.Equal(string.Join('\t', SprintTableClipboard.Columns), lines[0]);
        Assert.Equal(1 + sprint.Members.Count, lines.Length);
        Assert.StartsWith("+specs\tRekeningen\tDone", lines[1], StringComparison.Ordinal);
    }
}
