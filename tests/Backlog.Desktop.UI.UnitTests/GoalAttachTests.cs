using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.UI.Goals;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Goals;
using Backlog.UI.Components.Roadmap;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// "Attach to goal" with the goal port behind it
/// (<c>.devbook/domain/progress/features.md#attaching-work-to-a-goal</c>):
/// <see cref="GoalAttach"/> reads which goal holds the work and which goals are open, and
/// writes every attach through <see cref="IGoals"/> — moving work another goal holds
/// rather than refusing it, and only after the picker said so. With goals off it draws
/// nothing. <see cref="GoalControlRegistration.AddGoalControls"/> cascades it to every
/// screen, and the roadmap item editor draws it for its stored plan.
/// </summary>
public sealed class GoalAttachTests : IDisposable
{
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly GoalSubject Work = GoalSubject.ForTask(TaskId);
    private static readonly DateOnly Today = new(2026, 10, 17);

    private readonly BunitContext _context = new();

    public GoalAttachTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    // ---- What is drawn ---------------------------------------------------------------

    [Fact]
    public void With_no_goal_port_nothing_is_drawn()
    {
        using var bare = new BunitContext();
        bare.JSInterop.Mode = JSRuntimeMode.Loose;
        var unported = bare.Render<GoalAttach>(parameters => parameters.Add(p => p.Subject, Work));
        Assert.Empty(unported.Nodes);
    }

    /// <summary>Goals are always on: a host that composed a goal port and no feature
    /// settings at all draws the control.</summary>
    [Fact]
    public void With_a_goal_port_and_no_feature_settings_the_control_is_drawn()
    {
        Compose(new FakeGoals(Goal("Launch")));

        var control = Render();

        control.WaitForAssertion(() => control.Find("[data-testid=goal-control-open]"));
    }

    [Fact]
    public void The_control_reads_the_goal_that_holds_the_work_or_offers_to_attach_one()
    {
        var launch = Goal("Launch the desktop app", holds: GoalItemDto.ForTask(TaskId));
        Compose(new FakeGoals(launch, Goal("Docs")));

        var held = Render();
        held.WaitForAssertion(() => Assert.Equal("Launch the desktop app", held.Find("[data-testid=goal-control-open]").TextContent.Trim()));

        var free = _context.Render<GoalAttach>(parameters => parameters.Add(p => p.Subject, GoalSubject.ForPlan("release-q4")));
        free.WaitForAssertion(() => Assert.Equal("Attach to goal", free.Find("[data-testid=goal-control-open]").TextContent.Trim()));
    }

    // ---- Attaching -------------------------------------------------------------------

    [Fact]
    public async Task Attaching_work_no_goal_holds_attaches_it_without_a_move()
    {
        var docs = Goal("Docs");
        var goals = Compose(new FakeGoals(Goal("Launch"), docs));
        var control = await OpenPickerAsync();

        await ChooseAsync(control, docs.Goal.Id);
        Assert.Empty(control.FindAll("[data-testid=goal-picker-move-note]"));
        await control.Find("[data-testid=goal-picker-attach]").ClickAsync(new MouseEventArgs());

        Assert.Equal([$"Attach {docs.Goal.Id} Task:{TaskId} move:False"], goals.Writes);
        control.WaitForAssertion(() =>
        {
            Assert.Empty(control.FindAll("[data-testid=goal-picker]"));
            Assert.Equal("Docs", control.Find("[data-testid=goal-control-open]").TextContent.Trim());
        });
    }

    [Fact]
    public async Task Attaching_work_another_goal_holds_says_it_will_move_and_moves_it()
    {
        var launch = Goal("Launch the desktop app", holds: GoalItemDto.ForTask(TaskId));
        var docs = Goal("Docs");
        var goals = Compose(new FakeGoals(launch, docs));
        var control = await OpenPickerAsync();

        Assert.NotNull(control.Find($"[data-testid=goal-picker-option][data-goal-id='{launch.Goal.Id}'] [data-testid=goal-picker-attached]"));
        await ChooseAsync(control, docs.Goal.Id);
        Assert.Equal(
            "Attaching moves this task from “Launch the desktop app”.",
            control.Find("[data-testid=goal-picker-move-note]").TextContent.Trim());

        await control.Find("[data-testid=goal-picker-attach]").ClickAsync(new MouseEventArgs());

        Assert.Equal([$"Attach {docs.Goal.Id} Task:{TaskId} move:True"], goals.Writes);
        control.WaitForAssertion(() => Assert.Equal("Docs", control.Find("[data-testid=goal-control-open]").TextContent.Trim()));
    }

    [Fact]
    public async Task A_new_goal_is_set_and_the_work_attached_to_it()
    {
        var goals = Compose(new FakeGoals(Goal("Launch")));
        var control = await OpenPickerAsync();

        await control.Find("[data-testid=goal-picker-new]").ClickAsync(new MouseEventArgs());
        await control.Find("[data-testid=goal-picker-new-title] input").InputAsync(new ChangeEventArgs { Value = "Ship the beta" });
        await control.Find("[data-testid=goal-picker-new-due] input").InputAsync(new ChangeEventArgs { Value = "2026-12-18" });
        await control.Find("[data-testid=goal-picker-attach]").ClickAsync(new MouseEventArgs());

        var created = goals.Goals.Single(goal => goal.Goal.Title == "Ship the beta").Goal;
        Assert.Equal(new DateOnly(2026, 12, 18), created.DueDate);
        Assert.Equal(["Create Ship the beta 2026-12-18", $"Attach {created.Id} Task:{TaskId} move:False"], goals.Writes);
        control.WaitForAssertion(() => Assert.Equal("Ship the beta", control.Find("[data-testid=goal-control-open]").TextContent.Trim()));
    }

    [Fact]
    public async Task A_refused_attach_stays_in_the_picker_in_the_modules_words()
    {
        var docs = Goal("Docs");
        var goals = Compose(new FakeGoals(docs) { Refuse = "The task could not be read." });
        var control = await OpenPickerAsync();

        await ChooseAsync(control, docs.Goal.Id);
        await control.Find("[data-testid=goal-picker-attach]").ClickAsync(new MouseEventArgs());

        control.WaitForAssertion(() => Assert.Equal("The task could not be read.", control.Find("[data-testid=goal-picker-error]").TextContent.Trim()));
        Assert.Single(control.FindAll("[data-testid=goal-picker]"));
        Assert.Single(goals.Writes);
    }

    [Fact]
    public async Task Pressing_attach_again_after_a_new_goals_attach_was_refused_attaches_to_that_goal_rather_than_setting_another()
    {
        var goals = Compose(new FakeGoals(Goal("Launch")) { Refuse = "The task could not be read." });
        var control = await OpenPickerAsync();

        await control.Find("[data-testid=goal-picker-new]").ClickAsync(new MouseEventArgs());
        await control.Find("[data-testid=goal-picker-new-title] input").InputAsync(new ChangeEventArgs { Value = "Ship the beta" });
        await control.Find("[data-testid=goal-picker-new-due] input").InputAsync(new ChangeEventArgs { Value = "2026-12-18" });
        await control.Find("[data-testid=goal-picker-attach]").ClickAsync(new MouseEventArgs());
        control.WaitForAssertion(() => Assert.Equal("The task could not be read.", control.Find("[data-testid=goal-picker-error]").TextContent.Trim()));

        goals.Refuse = null;
        await control.Find("[data-testid=goal-picker-attach]").ClickAsync(new MouseEventArgs());

        var created = Assert.Single(goals.Goals, goal => goal.Goal.Title == "Ship the beta").Goal;
        Assert.Equal(
            ["Create Ship the beta 2026-12-18", $"Attach {created.Id} Task:{TaskId} move:False", $"Attach {created.Id} Task:{TaskId} move:False"],
            goals.Writes);
        control.WaitForAssertion(() => Assert.Equal("Ship the beta", control.Find("[data-testid=goal-control-open]").TextContent.Trim()));
    }

    // ---- The rules -------------------------------------------------------------------

    [Fact]
    public void The_picker_offers_the_goals_not_yet_reached_by_due_date_and_the_current_one_whatever_its_state()
    {
        var late = Summary("Late", due: Today.AddDays(30), done: 1, total: 4);
        var soon = Summary("Soon", due: Today.AddDays(3), done: 0, total: 0);
        var reached = Summary("Reached", due: Today.AddDays(1), done: 2, total: 2);
        var reachedCurrent = Summary("Reached and current", due: Today.AddDays(2), done: 5, total: 5);

        var offered = GoalSubjects.OpenGoals([late, soon, reached, reachedCurrent], reachedCurrent.Goal.Id);

        Assert.Equal(["Reached and current", "Soon", "Late"], offered.Select(goal => goal.Title));
        Assert.Equal(new GoalOption(late.Goal.Id, "Late", 1, 4, Today.AddDays(30)), offered[2]);
    }

    /// <summary>The picker reads no iteration, so a goal without a due date has no day to
    /// sort by: it follows the dated goals, by title, and says it is due at the iteration's end.</summary>
    [Fact]
    public void The_picker_offers_goals_without_a_due_date_after_the_dated_ones_by_title()
    {
        var undatedB = Summary("Search ships", due: null, done: 0, total: 2);
        var late = Summary("Late", due: Today.AddDays(30), done: 1, total: 4);
        var undatedA = Summary("Paid faster", due: null, done: 3, total: 8);

        var offered = GoalSubjects.OpenGoals([undatedB, late, undatedA], null);

        Assert.Equal(["Late", "Paid faster", "Search ships"], offered.Select(goal => goal.Title));
        Assert.Null(offered[1].DueDate);
        Assert.Equal("3/8 · due end of iteration", offered[1].Meta);
        Assert.Equal("1/4 · due 16 Nov", offered[0].Meta);
    }

    [Fact]
    public void A_subject_is_the_goal_ports_item_of_the_same_identity()
    {
        var task = Guid.NewGuid();

        Assert.Equal(GoalItemDto.ForTask(task), GoalSubjects.ToItem(GoalSubject.ForTask(task)));
        Assert.Equal(GoalItemDto.ForPlan("release-q4"), GoalSubjects.ToItem(GoalSubject.ForPlan("release-q4")));
        Assert.Equal(GoalItemDto.ForPullRequest("JSdotNet/Backlog", 655), GoalSubjects.ToItem(GoalSubject.ForPullRequest("JSdotNet/Backlog", 655)));
    }

    // ---- Composition and placements --------------------------------------------------

    [Fact]
    public void AddGoalControls_cascades_the_control_to_every_screen()
    {
        Compose(new FakeGoals(Goal("Launch")));
        _context.Services.AddGoalControls();

        var shelf = _context.Render<RoadmapPlanShelf>(parameters => parameters
            .Add(p => p.Plans, [new RoadmapShelfPlan("release-q4", ["backlog"], 6, 21, 2)]));

        shelf.WaitForAssertion(() =>
            Assert.Equal("Attach to goal", shelf.Find("[data-testid=roadmap-shelf-goal-release-q4] [data-testid=goal-control-open]").TextContent.Trim()));
    }

    /// <summary>Goals composed: every place that wraps the control draws it — the shelf
    /// row, the work card, the item editor — with no feature switch to turn on first.</summary>
    [Fact]
    public void The_cascaded_control_is_drawn_in_every_place_that_wraps_it()
    {
        _context.Services.AddSingleton<IGoals>(new FakeGoals(Goal("Launch")));
        _context.Services.AddGoalControls();

        var shelf = _context.Render<RoadmapPlanShelf>(parameters => parameters
            .Add(p => p.Plans, [new RoadmapShelfPlan("release-q4", ["backlog"], 6, 21, 2)]));
        var links = _context.Render<Backlog.UI.Components.Integrations.WorkLinks>(parameters => parameters
            .Add(p => p.PullRequests, [new Backlog.UI.Components.Integrations.WorkPullRequest("JSdotNet/Backlog#655", 655, "Goal buttons", Backlog.UI.Components.Integrations.IntegrationArtifactState.Open)]));
        var editor = _context.Render<RoadmapItemEditor>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Item, Item("sync")));

        shelf.WaitForAssertion(() => shelf.Find("[data-testid=roadmap-shelf-goal-release-q4] [data-testid=goal-control-open]"));
        links.WaitForAssertion(() => links.Find("[data-testid=work-links-goal] [data-testid=goal-control-open]"));
        editor.WaitForAssertion(() => editor.Find("[data-testid=roadmap-editor-goal] [data-testid=goal-control-open]"));
    }

    [Fact]
    public void Without_a_goal_port_the_cascaded_control_is_not_offered()
    {
        _context.Services.AddGoalControls();

        var shelf = _context.Render<RoadmapPlanShelf>(parameters => parameters
            .Add(p => p.Plans, [new RoadmapShelfPlan("release-q4", ["backlog"], 6, 21, 2)]));

        Assert.Empty(shelf.FindAll(".roadmap-shelf__goal"));
    }

    [Fact]
    public void The_roadmap_item_editor_draws_the_goal_for_its_stored_plan_and_none_for_new_work()
    {
        var drawn = new List<GoalSubject>();
        var slot = new GoalControlSlot(subject => builder =>
        {
            drawn.Add(subject);
            builder.AddContent(0, "goal");
        });

        var editor = _context.Render<RoadmapItemEditor>(parameters => parameters
            .AddCascadingValue(slot)
            .Add(p => p.Open, true)
            .Add(p => p.Item, Item("sync")));

        Assert.Single(editor.FindAll("[data-testid=roadmap-editor-goal]"));
        Assert.Equal([GoalSubject.ForPlan("sync")], drawn.Distinct());

        var fresh = _context.Render<RoadmapItemEditor>(parameters => parameters
            .AddCascadingValue(slot)
            .Add(p => p.Open, true)
            .Add(p => p.Item, (RoadmapItemDto?)null));
        Assert.Empty(fresh.FindAll("[data-testid=roadmap-editor-goal]"));

        var unslotted = _context.Render<RoadmapItemEditor>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Item, Item("sync")));
        Assert.Empty(unslotted.FindAll("[data-testid=roadmap-editor-goal]"));
    }

    // ---- Helpers ---------------------------------------------------------------------

    private FakeGoals Compose(FakeGoals goals)
    {
        _context.Services.AddSingleton<IGoals>(goals);
        return goals;
    }

    private IRenderedComponent<GoalAttach> Render() =>
        _context.Render<GoalAttach>(parameters => parameters.Add(p => p.Subject, Work));

    private async Task<IRenderedComponent<GoalAttach>> OpenPickerAsync()
    {
        var control = Render();
        control.WaitForAssertion(() => control.Find("[data-testid=goal-control-open]"));
        await control.Find("[data-testid=goal-control-open]").ClickAsync(new MouseEventArgs());
        control.WaitForAssertion(() => control.Find("[data-testid=goal-picker]"));
        return control;
    }

    private static async Task ChooseAsync(IRenderedComponent<GoalAttach> control, Guid goalId) =>
        await control.Find($"[data-testid=goal-picker-option][data-goal-id='{goalId}']").ClickAsync(new MouseEventArgs());

    private static GoalSummaryDto Goal(string title, GoalItemDto? holds = null) =>
        new(new GoalDto(Guid.NewGuid(), title, null, Today.AddDays(14), holds is null ? [] : [new GoalAttachmentDto(holds, Today)]),
            new GoalProgressDto(0, holds is null ? 0 : 1, 0, 0, holds is null ? 0 : 1, 0));

    private static GoalSummaryDto Summary(string title, DateOnly? due, int done, int total) =>
        new(new GoalDto(Guid.NewGuid(), title, null, due, []), new GoalProgressDto(done, total, 0, 0, total - done, 0));

    private static RoadmapItemDto Item(string tag) =>
        new(Guid.NewGuid(), "Ship the thing", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), PlanningPriority.Medium, [],
            Lane: null, TaskId: null, DependsOn: [], Tag: tag);

    public void Dispose() => _context.Dispose();

    /// <summary>
    /// The goal port over goals in memory: each write is recorded and applied the way the
    /// module applies it — work another goal holds is refused unless the attach asks for
    /// the move, and then leaves that goal in the same change.
    /// </summary>
    private sealed class FakeGoals(params GoalSummaryDto[] goals) : IGoals
    {
        public event Action? Changed { add { } remove { } }

        public List<GoalSummaryDto> Goals { get; } = [.. goals];

        public List<string> Writes { get; } = [];

        /// <summary>Set, every attach is refused with these words.</summary>
        public string? Refuse { get; set; }

        public Task<Result<IReadOnlyList<GoalSummaryDto>>> ListAsync(DateOnly? windowStart = null, DateOnly? windowEnd = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success<IReadOnlyList<GoalSummaryDto>>([.. Goals]));

        public Task<Result<GoalDto>> GetAsync(Guid goalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(Goals.Single(goal => goal.Goal.Id == goalId).Goal));

        public Task<Result<GoalDto?>> FindGoalOfAsync(GoalItemDto item, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(Holder(item)?.Goal));

        public Task<Result<GoalDto>> CreateAsync(SetGoalRequest request, CancellationToken cancellationToken = default)
        {
            Writes.Add($"Create {request.Title} {request.DueDate:yyyy-MM-dd}");
            var goal = new GoalDto(Guid.NewGuid(), request.Title, request.Outcome, request.DueDate, []);
            Goals.Add(new GoalSummaryDto(goal, new GoalProgressDto(0, 0, 0, 0, 0, 0)));
            return Task.FromResult(Result.Success(goal));
        }

        public Task<Result<GoalDto>> EditAsync(Guid goalId, SetGoalRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> DeleteAsync(Guid goalId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<GoalDto>> AttachAsync(Guid goalId, GoalItemDto item, bool moveFromOtherGoal = false, CancellationToken cancellationToken = default)
        {
            Writes.Add($"Attach {goalId} {item.Kind}:{item.TaskId?.ToString() ?? item.PlanTag ?? $"{item.Repository}#{item.Number}"} move:{moveFromOtherGoal}");
            if (Refuse is { } refusal) return Task.FromResult(Result.Failure<GoalDto>(Error.Validation("progress.task_required", refusal)));

            if (Holder(item) is { } holder && holder.Goal.Id != goalId)
            {
                if (!moveFromOtherGoal) return Task.FromResult(Result.Failure<GoalDto>(ProgressErrors.ItemInOtherGoal(holder.Goal.Title)));
                Replace(holder.Goal with { Attachments = [.. holder.Goal.Attachments.Where(attachment => attachment.Item != item)] });
            }

            var target = Goals.Single(goal => goal.Goal.Id == goalId).Goal;
            var attached = target with { Attachments = [.. target.Attachments, new GoalAttachmentDto(item, Today)] };
            Replace(attached);
            return Task.FromResult(Result.Success(attached));
        }

        public Task<Result<GoalDto>> DetachAsync(Guid goalId, GoalItemDto item, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private GoalSummaryDto? Holder(GoalItemDto item) =>
            Goals.FirstOrDefault(goal => goal.Goal.Attachments.Any(attachment => attachment.Item == item));

        private void Replace(GoalDto goal)
        {
            var index = Goals.FindIndex(summary => summary.Goal.Id == goal.Id);
            Goals[index] = Goals[index] with { Goal = goal };
        }
    }
}
