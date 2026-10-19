using Backlog.UI.Components.Goals;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Where the library draws a host's goal control: the task panel's Goal slot between its
/// detail rows and its body, beside "Plan it" on each plan shelf row, and beside each work
/// card's pull request whose key names one — each for its own work. With no
/// <see cref="GoalControlSlot"/> cascaded, none of them draws anything more than it did.
/// </summary>
public sealed class GoalSlotPlacementTests : IDisposable
{
    private readonly BunitContext _context = new();
    private readonly List<GoalSubject> _drawn = [];

    public GoalSlotPlacementTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    /// <summary>A slot that draws a marker naming the work it was drawn for.</summary>
    private GoalControlSlot Slot => new(subject => builder =>
    {
        _drawn.Add(subject);
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "data-testid", "goal-slot");
        builder.AddAttribute(2, "data-subject", subject.Noun);
        builder.CloseElement();
    });

    // ---- The task panel --------------------------------------------------------------

    [Fact]
    public void The_task_panel_draws_its_goal_between_the_details_and_the_body()
    {
        var panel = _context.Render<TaskPanel>(parameters => parameters
            .Add(p => p.Title, "Wire the pane into the shell")
            .Add(p => p.Details, "<p>the rows</p>")
            .Add(p => p.Goal, "<span data-testid=\"goal-slot\">Goal</span>")
            .Add(p => p.Body, "<p>the text</p>")
            .Add(p => p.TestId, "panel"));

        var goal = panel.Find("[data-testid=panel-goal]");
        Assert.Contains("task-panel__goal", goal.ClassList);
        Assert.NotNull(goal.QuerySelector("[data-testid=goal-slot]"));

        var parts = panel.Find("aside").Children.Select(child => child.GetAttribute("data-testid")).ToList();
        Assert.True(parts.IndexOf("panel-details") < parts.IndexOf("panel-goal"));
        Assert.True(parts.IndexOf("panel-goal") < parts.IndexOf("panel-body"));
    }

    [Fact]
    public void The_task_panel_without_a_goal_draws_no_goal_line()
    {
        var panel = _context.Render<TaskPanel>(parameters => parameters
            .Add(p => p.Title, "Wire the pane into the shell")
            .Add(p => p.TestId, "panel"));

        Assert.Empty(panel.FindAll("[data-testid=panel-goal]"));
        Assert.Empty(panel.FindAll(".task-panel__goal"));
    }

    // ---- The plan shelf --------------------------------------------------------------

    [Fact]
    public void Each_shelf_row_draws_the_goal_for_its_plan_beside_plan_it()
    {
        var shelf = _context.Render<RoadmapPlanShelf>(parameters => parameters
            .AddCascadingValue(Slot)
            .Add(p => p.Plans, [new("release-q4", ["backlog"], 6, 21, 2), new("one", [], 1, 1, 0)]));

        Assert.Equal([GoalSubject.ForPlan("release-q4"), GoalSubject.ForPlan("one")], _drawn.Distinct());

        var head = shelf.Find("[data-testid=roadmap-shelf-row-release-q4] .roadmap-shelf__row-head");
        Assert.Equal("plan +release-q4", head.QuerySelector("[data-testid=roadmap-shelf-goal-release-q4] [data-testid=goal-slot]")!.GetAttribute("data-subject"));
        Assert.NotNull(head.QuerySelector("[data-testid=roadmap-shelf-plan-release-q4]"));
    }

    [Fact]
    public void A_shelf_without_a_slot_draws_no_goal()
    {
        var shelf = _context.Render<RoadmapPlanShelf>(parameters => parameters
            .Add(p => p.Plans, [new("release-q4", ["backlog"], 6, 21, 2)]));

        Assert.Empty(shelf.FindAll(".roadmap-shelf__goal"));
        Assert.Empty(shelf.FindAll("[data-testid=goal-slot]"));
    }

    // ---- Work cards ------------------------------------------------------------------

    [Fact]
    public void Each_pull_request_card_draws_the_goal_for_the_pull_request_its_key_names()
    {
        var links = _context.Render<WorkLinks>(parameters => parameters
            .AddCascadingValue(Slot)
            .Add(p => p.PullRequests,
            [
                new WorkPullRequest("JSdotNet/Backlog#655", 655, "Goal buttons", IntegrationArtifactState.Open),
                new WorkPullRequest("local-key", 7, "Keyed some other way", IntegrationArtifactState.Open)
            ]));

        Assert.Equal([GoalSubject.ForPullRequest("JSdotNet/Backlog", 655)], _drawn.Distinct());

        var items = links.FindAll("[data-testid=work-links-item]");
        var item = Assert.Single(items);
        Assert.NotNull(item.QuerySelector("[data-testid=work-links-goal] [data-testid=goal-slot]"));
        Assert.Empty(links.FindAll("[data-testid=work-links-unlink]"));
    }

    [Fact]
    public void A_card_draws_its_goal_beside_its_unlink()
    {
        var links = _context.Render<WorkLinks>(parameters => parameters
            .AddCascadingValue(Slot)
            .Add(p => p.PullRequests, [new WorkPullRequest("JSdotNet/Backlog#655", 655, "Goal buttons", IntegrationArtifactState.Open)])
            .Add(p => p.OnUnlinkPullRequest, (WorkPullRequest _) => { }));

        var parts = links.Find("[data-testid=work-links-item]").Children.Select(child => child.GetAttribute("data-testid") ?? child.ClassName).ToList();
        Assert.True(parts.IndexOf("work-links-goal") < parts.IndexOf("work-links-unlink"));
    }

    [Fact]
    public void Work_cards_without_a_slot_are_what_they_were()
    {
        var links = _context.Render<WorkLinks>(parameters => parameters
            .Add(p => p.PullRequests, [new WorkPullRequest("JSdotNet/Backlog#655", 655, "Goal buttons", IntegrationArtifactState.Open)]));

        Assert.Empty(links.FindAll("[data-testid=work-links-item]"));
        Assert.Empty(links.FindAll("[data-testid=work-links-goal]"));
    }

    // ---- A slot cascaded but not offered ---------------------------------------------

    /// <summary>A host that composes goals with the switch off: the slot is there, and
    /// says it is not offered. Turn it on and it raises its watch.</summary>
    private sealed class Switchable
    {
        private Action? _changed;

        public bool On { get; private set; }

        public GoalControlSlot Slot(List<GoalSubject> drawn) => new(
            subject => builder =>
            {
                drawn.Add(subject);
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "data-testid", "goal-slot");
                builder.CloseElement();
            },
            isOffered: () => On,
            watch: changed =>
            {
                _changed += changed;
                return new Unwatch(() => _changed -= changed);
            });

        public void Turn(bool on)
        {
            On = on;
            _changed?.Invoke();
        }

        private sealed class Unwatch(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }

    [Fact]
    public void A_shelf_whose_slot_is_not_offered_draws_no_wrapper_and_draws_it_once_turned_on()
    {
        var goals = new Switchable();
        var shelf = _context.Render<RoadmapPlanShelf>(parameters => parameters
            .AddCascadingValue(goals.Slot(_drawn))
            .Add(p => p.Plans, [new("release-q4", ["backlog"], 6, 21, 2)]));

        Assert.Empty(shelf.FindAll(".roadmap-shelf__goal"));
        Assert.Empty(_drawn);

        goals.Turn(true);

        shelf.WaitForAssertion(() => Assert.NotNull(shelf.Find("[data-testid=roadmap-shelf-goal-release-q4] [data-testid=goal-slot]")));
    }

    [Fact]
    public void Work_cards_whose_slot_is_not_offered_are_what_they_were_until_it_is()
    {
        var goals = new Switchable();
        var links = _context.Render<WorkLinks>(parameters => parameters
            .AddCascadingValue(goals.Slot(_drawn))
            .Add(p => p.PullRequests, [new WorkPullRequest("JSdotNet/Backlog#655", 655, "Goal buttons", IntegrationArtifactState.Open)]));

        Assert.Empty(links.FindAll("[data-testid=work-links-item]"));
        Assert.Empty(links.FindAll("[data-testid=work-links-goal]"));
        Assert.Empty(_drawn);

        goals.Turn(true);

        links.WaitForAssertion(() => Assert.NotNull(links.Find("[data-testid=work-links-goal] [data-testid=goal-slot]")));
    }

    [Fact]
    public void A_disposed_placement_lets_go_of_the_slots_watch()
    {
        var goals = new Switchable();
        var shelf = _context.Render<RoadmapPlanShelf>(parameters => parameters
            .AddCascadingValue(goals.Slot(_drawn))
            .Add(p => p.Plans, [new("release-q4", ["backlog"], 6, 21, 2)]));

        shelf.Instance.Dispose();
        goals.Turn(true);

        Assert.Empty(_drawn);
    }

    public void Dispose() => _context.Dispose();
}
