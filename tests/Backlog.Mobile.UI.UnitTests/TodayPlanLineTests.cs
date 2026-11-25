using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A plan on the phone's Today tab (local ADR 0062 §8): in today's My Day by exactly the
/// rule a task is, under the same Clear My Day overnight switch, and drawn as one line —
/// its name, done of total and run state, never its entries.
/// </summary>
public sealed class TodayPlanLineTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 25);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    [Fact]
    public void A_plan_put_on_today_is_one_line_with_its_name_and_done_of_total()
    {
        var lines = TodayPlanLine.For(
            [("lets-bbq", Today)],
            [Entry("+lets-bbq", "ready"), Entry("+lets-bbq", "done", completedOn: Today), Entry("+other", "ready")],
            Today,
            clearsOvernight: true);

        var line = Assert.Single(lines);
        Assert.Equal("lets-bbq", line.Name);
        Assert.Equal(1, line.Done);
        Assert.Equal(2, line.Total);
        Assert.Equal("lets-bbq, 1 of 2 done, 1 ready", line.Label);
    }

    [Theory]
    // Clear My Day overnight on: only today's stamp is in.
    [InlineData(-1, true, false, false)]
    [InlineData(0, true, false, true)]
    [InlineData(1, true, false, false)]
    // Off: an earlier pick carries over while the plan is unfinished.
    [InlineData(-1, false, false, true)]
    [InlineData(1, false, false, false)]
    // Off, finished yesterday: gone.
    [InlineData(-3, false, true, false)]
    public void A_plan_is_in_by_the_task_rule(int stampOffset, bool clearsOvernight, bool finishedYesterday, bool expectedIn)
    {
        var entry = finishedYesterday ? Entry("+lets-bbq", "done", completedOn: Yesterday) : Entry("+lets-bbq", "ready");

        var lines = TodayPlanLine.For([("lets-bbq", Today.AddDays(stampOffset))], [entry], Today, clearsOvernight);

        Assert.Equal(expectedIn, lines.Count == 1);
    }

    [Fact]
    public void A_carried_plan_finished_today_stays_ticked_until_the_day_ends()
    {
        var lines = TodayPlanLine.For(
            [("lets-bbq", Yesterday.AddDays(-1))],
            [Entry("+lets-bbq", "done", completedOn: Yesterday), Entry("+lets-bbq", "done", completedOn: Today)],
            Today,
            clearsOvernight: false);

        var line = Assert.Single(lines);
        Assert.Equal(TodayPlanState.Done, line.State);
        Assert.Equal("Done", line.StateText);
    }

    [Fact]
    public void The_plan_tag_is_matched_by_the_roadmaps_slug_and_a_plan_with_no_entry_reads_empty()
    {
        var lines = TodayPlanLine.For(
            [("lets-bbq", Today), ("fresh-plan", Today)],
            [Entry("+Lets_BBQ", "ready")],
            Today,
            clearsOvernight: true);

        Assert.Collection(
            lines,
            bbq => Assert.Equal((1, 0), (bbq.Total, bbq.Done)),
            fresh =>
            {
                Assert.Equal("fresh-plan", fresh.Name);
                Assert.Equal((0, 0), (fresh.Total, fresh.Done));
                Assert.Equal(string.Empty, fresh.StateText);
            });
    }

    [Fact]
    public void Deleted_and_archived_entries_are_not_counted()
    {
        var deleted = Entry("+lets-bbq", "ready") with { DeletedAt = Now };

        var line = Assert.Single(TodayPlanLine.For(
            [("lets-bbq", Today)],
            [deleted, Entry("+lets-bbq", "archived"), Entry("+lets-bbq", "draft")],
            Today,
            clearsOvernight: true));

        Assert.Equal(1, line.Total);
    }

    [Theory]
    [InlineData(new[] { "in_progress", "ready" }, TodayPlanState.InProgress, "In progress")]
    [InlineData(new[] { "ready", "ready", "draft" }, TodayPlanState.Ready, "2 ready")]
    [InlineData(new[] { "draft", "draft" }, TodayPlanState.Draft, "Draft")]
    public void The_run_state_reads_off_the_open_entries(string[] statuses, TodayPlanState state, string text)
    {
        var line = Assert.Single(TodayPlanLine.For(
            [("lets-bbq", Today)],
            [.. statuses.Select(status => Entry("+lets-bbq", status))],
            Today,
            clearsOvernight: true));

        Assert.Equal(state, line.State);
        Assert.Equal(text, line.StateText);
    }

    [Fact]
    public void A_plan_whose_open_entry_is_marked_blocked_with_none_ready_reads_blocked()
    {
        var blocked = Entry("+lets-bbq", "ready");
        blocked = blocked with { Task = blocked.Task with { BlockedSince = Yesterday } };

        var line = Assert.Single(TodayPlanLine.For([("lets-bbq", Today)], [blocked, Entry("+lets-bbq", "draft")], Today, clearsOvernight: true));

        Assert.Equal(TodayPlanState.Blocked, line.State);
        Assert.Equal(0, line.ReadyCount);
    }

    [Fact]
    public void Lines_keep_the_order_the_roadmap_lists_its_plans()
    {
        var lines = TodayPlanLine.For([("zeta", Today), ("alpha", Today)], [], Today, clearsOvernight: true);

        Assert.Equal(["zeta", "alpha"], lines.Select(line => line.Name));
    }

    [Theory]
    [InlineData("+Lets_BBQ", "lets-bbq")]
    [InlineData("plan-first-tasks", "plan-first-tasks")]
    [InlineData("  +A  b--c ", "a-b-c")]
    [InlineData("+", "")]
    public void PlanKey_is_the_roadmaps_slug(string tag, string key) =>
        Assert.Equal(key, TodayPlanLine.PlanKey(tag));

    private static TaskViewRow Entry(string plan, string status, DateOnly? completedOn = null)
    {
        var change = TestTasks.Task("Entry", Now, status: status, tags: [plan]);
        return new TaskViewRow(change.Id, change.UpdatedAt, change.DeletedAt, 1, change.Task with { CompletedOn = completedOn });
    }
}
