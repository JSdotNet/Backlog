using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.UI.InProgress;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Progress;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Closing a sprint from the sprint view (<c>.devbook/domain/progress/features.md#closing-a-sprint</c>):
/// "Close sprint…" on the open sprint opens the review over the last reading, the close is
/// one write through <see cref="ISprintSeriesPlanning"/> with an outcome per unfinished
/// item and the next goal, and the view then opens on the sprint it started while the
/// closed one stays readable a step back.
/// </summary>
public sealed class SprintCloseTests : IDisposable
{
    private static readonly DateOnly Start = new(2026, 10, 5);
    private static readonly Guid Brief = Guid.NewGuid();
    private static readonly Guid Ledger = Guid.NewGuid();
    private static readonly Guid Specs = Guid.NewGuid();
    private readonly BunitContext _context = new();

    public SprintCloseTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    private static readonly IReadOnlyList<(Guid Id, string Title, SprintWorkState State, string? Plan)> Tasks =
    [
        (Brief, "Opdrachtbrief", SprintWorkState.Done, "rekeningen"),
        (Ledger, "Grootboek", SprintWorkState.NotStarted, null),
        (Specs, "Specs naar het devbook", SprintWorkState.Blocked, "verplaatsen-specs")
    ];

    // ---- The mapping ----------------------------------------------------------------------

    [Fact]
    public void The_dialog_lists_the_unfinished_members_with_their_goal_plan_and_state()
    {
        var series = Series();
        var progress = FakeProgress.Read(series, series.OpenSprint!);
        var board = SprintBoardMapper.ToModel(progress, series);

        var model = SprintCloseMapper.ToModel(progress, series, board);

        Assert.Equal("Sprint 42", model.SprintName);
        Assert.Equal("Sprint 43", model.NextSprintName);
        Assert.Equal("Fincent · Mon 5 Oct – Sun 18 Oct · goal: Ship it", model.Subtitle);
        Assert.Equal((1, 3), (model.AllDone, model.AllTotal));
        Assert.Equal(1, model.AddedAfterStart);
        Assert.Equal([Specs.ToString(), Ledger.ToString()], model.Unfinished.Select(item => item.TaskId));
        Assert.Equal("Goal · +verplaatsen-specs · blocked 3 days", model.Unfinished[0].Detail);
        Assert.Equal("not started", model.Unfinished[1].Detail);
    }

    [Fact]
    public void The_choices_become_the_modules_outcomes_by_task_id()
    {
        var outcomes = SprintCloseMapper.Outcomes(new SprintCloseDecision(
            new Dictionary<string, SprintCloseChoice>
            {
                [Ledger.ToString()] = SprintCloseChoice.Carry,
                [Specs.ToString()] = SprintCloseChoice.Drop,
                [Brief.ToString()] = SprintCloseChoice.BackToBacklog,
                ["not-a-task"] = SprintCloseChoice.Drop
            },
            "Next"));

        Assert.Equal(3, outcomes.Count);
        Assert.Equal(CloseOutcome.Carry, outcomes[Ledger]);
        Assert.Equal(CloseOutcome.Drop, outcomes[Specs]);
        Assert.Equal(CloseOutcome.BackToBacklog, outcomes[Brief]);
    }

    // ---- The sprint view ------------------------------------------------------------------

    [Fact]
    public async Task Close_sprint_opens_the_review_of_the_open_sprint()
    {
        Compose(Series());
        var rendered = await OpenSprintViewAsync();

        await rendered.Find("[data-testid=in-progress-sprint-close]").ClickAsync(new MouseEventArgs());

        rendered.WaitForAssertion(() => rendered.Find("[data-testid=sprint-close-dialog]"));
        Assert.Equal("Goal not met", rendered.Find("[data-testid=sprint-close-goal-verdict]").TextContent.Trim());
        Assert.Equal("2 unfinished", rendered.Find("[data-testid=sprint-close-unfinished-heading]").TextContent.Trim());
        Assert.Equal(
            [Specs.ToString(), Ledger.ToString()],
            rendered.FindAll("[data-testid=sprint-close-item]").Select(item => item.GetAttribute("data-task-id")));
        Assert.Equal("Close and start Sprint 43", rendered.Find("[data-testid=sprint-close-confirm]").TextContent.Trim());
    }

    [Fact]
    public async Task Closing_writes_the_outcomes_and_the_next_goal_and_opens_the_next_sprint()
    {
        var progress = Compose(Series());
        var rendered = await OpenSprintViewAsync();

        await rendered.Find("[data-testid=in-progress-sprint-close]").ClickAsync(new MouseEventArgs());
        var choices = rendered.FindAll("[data-testid=sprint-close-item-choice] select");
        await choices[0].ChangeAsync(new ChangeEventArgs { Value = "Drop" });
        await rendered.Find("[data-testid=sprint-close-next-goal] input").InputAsync(new ChangeEventArgs { Value = "Ship the review" });
        await rendered.Find("[data-testid=sprint-close-confirm]").ClickAsync(new MouseEventArgs());

        Assert.Equal([$"Close {Ledger}=Carry {Specs}=Drop · Ship the review"], progress.Writes);

        // The view opens on the sprint the close started, holding the carried task alone.
        rendered.WaitForAssertion(() => Assert.Empty(rendered.FindAll("[data-testid=sprint-close-dialog]")));
        rendered.WaitForAssertion(() => Assert.Equal(["Grootboek"], rendered.FindAll("[data-testid=sprint-row-title]").Select(title => title.TextContent.Trim())));
        Assert.Contains("Ship the review", rendered.Find("[data-testid=sprint-board-goal]").TextContent, StringComparison.Ordinal);
        Assert.Single(rendered.FindAll("[data-testid=in-progress-sprint-close]"));
    }

    [Fact]
    public async Task The_closed_sprint_stays_readable_a_step_back_and_takes_no_writes()
    {
        Compose(Series());
        var rendered = await OpenSprintViewAsync();
        await rendered.Find("[data-testid=in-progress-sprint-close]").ClickAsync(new MouseEventArgs());
        await rendered.Find("[data-testid=sprint-close-confirm]").ClickAsync(new MouseEventArgs());
        // Both unfinished tasks were carried, so Sprint 43 holds them.
        rendered.WaitForAssertion(() => Assert.Equal(2, rendered.FindAll("[data-testid=sprint-row-title]").Count));

        await rendered.Find("[data-testid=sprint-board-previous]").ClickAsync(new MouseEventArgs());

        rendered.WaitForAssertion(() => Assert.Equal(3, rendered.FindAll("[data-testid=sprint-row-title]").Count));
        Assert.Empty(rendered.FindAll("[data-testid=in-progress-sprint-close]"));
        Assert.Empty(rendered.FindAll("[data-testid=in-progress-sprint-add]"));
        Assert.Empty(rendered.FindAll("[data-testid=sprint-row-menu]"));
    }

    [Fact]
    public async Task A_dropped_item_stays_in_the_closed_sprint_alone()
    {
        var progress = Compose(Series());
        var rendered = await OpenSprintViewAsync();

        await rendered.Find("[data-testid=in-progress-sprint-close]").ClickAsync(new MouseEventArgs());
        await rendered.FindAll("[data-testid=sprint-close-item-choice] select")[0].ChangeAsync(new ChangeEventArgs { Value = "Drop" });
        await rendered.FindAll("[data-testid=sprint-close-item-choice] select")[1].ChangeAsync(new ChangeEventArgs { Value = "BackToBacklog" });
        await rendered.Find("[data-testid=sprint-close-confirm]").ClickAsync(new MouseEventArgs());

        Assert.Equal([$"Close {Ledger}=BackToBacklog {Specs}=Drop · "], progress.Writes);
        rendered.WaitForAssertion(() => Assert.Empty(rendered.FindAll("[data-testid=sprint-row-title]")));

        await rendered.Find("[data-testid=sprint-board-previous]").ClickAsync(new MouseEventArgs());

        rendered.WaitForAssertion(() => Assert.Equal(
            ["Opdrachtbrief", "Specs naar het devbook", "Grootboek"],
            rendered.FindAll("[data-testid=sprint-row-title]").Select(title => title.TextContent.Trim())));
    }

    [Fact]
    public async Task A_refused_close_reads_the_sprint_again_and_lists_what_turned_unfinished()
    {
        var progress = Compose(Series());
        var rendered = await OpenSprintViewAsync();
        await rendered.Find("[data-testid=in-progress-sprint-close]").ClickAsync(new MouseEventArgs());
        Assert.Equal(2, rendered.FindAll("[data-testid=sprint-close-item]").Count);
        await rendered.FindAll("[data-testid=sprint-close-item-choice] select")[0].ChangeAsync(new ChangeEventArgs { Value = "Drop" });

        progress.Reopened.Add(Brief);
        progress.Refuse = "Opdrachtbrief has no outcome yet.";
        await rendered.Find("[data-testid=sprint-close-confirm]").ClickAsync(new MouseEventArgs());

        rendered.WaitForAssertion(() => Assert.Equal(3, rendered.FindAll("[data-testid=sprint-close-item]").Count));
        Assert.Contains("Opdrachtbrief has no outcome yet.", rendered.Find("[data-testid=sprint-close-error]").TextContent, StringComparison.Ordinal);

        // The choice already made is kept; the item that turned unfinished starts on carry.
        var choices = rendered.FindAll("[data-testid=sprint-close-item]")
            .ToDictionary(item => item.GetAttribute("data-task-id")!, item => item.QuerySelector("select")!.GetAttribute("value"));
        Assert.Equal("Drop", choices[Specs.ToString()]);
        Assert.Equal("Carry", choices[Brief.ToString()]);
    }

    [Fact]
    public async Task A_refused_close_stays_in_the_dialog_in_the_modules_words()
    {
        var progress = Compose(Series());
        var rendered = await OpenSprintViewAsync();
        progress.Refuse = "Grootboek has no outcome yet.";

        await rendered.Find("[data-testid=in-progress-sprint-close]").ClickAsync(new MouseEventArgs());
        await rendered.Find("[data-testid=sprint-close-confirm]").ClickAsync(new MouseEventArgs());

        rendered.WaitForAssertion(() => Assert.Contains("Grootboek has no outcome yet.", rendered.Find("[data-testid=sprint-close-error]").TextContent, StringComparison.Ordinal));
        Assert.Single(rendered.FindAll("[data-testid=sprint-close-dialog]"));
        Assert.Empty(progress.Writes);
    }

    // ---- Composition ----------------------------------------------------------------------

    private FakeProgress Compose(SprintSeriesDto series)
    {
        var progress = new FakeProgress(series);
        _context.Services.AddSingleton<IAppFeatureSettings>(new ProgressSwitch());
        _context.Services.AddSingleton<ISprintSeriesPlanning>(progress);
        _context.Services.AddSingleton<ISprintProgress>(progress);
        return progress;
    }

    private async Task<IRenderedComponent<InProgressSprints>> OpenSprintViewAsync()
    {
        var rendered = _context.Render<InProgressSprints>(parameters => parameters
            .Add(p => p.Repositories, ["fincent"])
            .Add(p => p.Now, (RenderFragment<RenderFragment?>)(viewSwitch => builder => builder.AddContent(0, viewSwitch))));
        rendered.WaitForAssertion(() => rendered.Find("[data-testid=in-progress-view-switch-sprint]"));
        await rendered.Find("[data-testid=in-progress-view-switch-sprint]").ClickAsync(new MouseEventArgs());
        rendered.WaitForAssertion(() => rendered.Find("[data-testid=in-progress-sprint-close]"));
        return rendered;
    }

    private static SprintSeriesDto Series() =>
        new(Guid.NewGuid(), "Fincent", ["fincent"], SprintLength.TwoWeeks, DayOfWeek.Monday, "Sprint {n}", false, false,
            [GoalRuleDto.InPlans("rekeningen", "verplaatsen-specs")], 100,
            [new SprintDto(42, "Sprint 42", Start, Start.AddDays(13), "Ship it", true, [],
                [.. Tasks.Select(task => new SprintItemDto(task.Id, true, task.Id == Ledger ? Start.AddDays(2) : Start, false, null, false, null))])]);

    public void Dispose() => _context.Dispose();

    private sealed class ProgressSwitch : IAppFeatureSettings
    {
        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public bool IsEnabled(string key) => key == ProgressFeatures.Progress;

        public string? SetEnabled(string key, bool enabled) => null;
    }

    /// <summary>
    /// The module's two ports over one series in memory. A sprint's members are its
    /// directly added items, in the states <see cref="Tasks"/> gives them; closing records
    /// every outcome on the closed sprint and starts the next with the carried tasks, as
    /// the module does.
    /// </summary>
    private sealed class FakeProgress(SprintSeriesDto series) : ISprintSeriesPlanning, ISprintProgress
    {
        private SprintSeriesDto _series = series;

        public List<string> Writes { get; } = [];

        public string? Refuse { get; set; }

        public Task<Result<IReadOnlyList<SprintSeriesDto>>> ListSeriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success<IReadOnlyList<SprintSeriesDto>>([_series]));

        public Task<Result<SprintProgressDto>> ReadAsync(Guid seriesId, int? number = null, CancellationToken cancellationToken = default)
        {
            var sprint = number is { } asked ? _series.Sprints.Single(s => s.Number == asked) : _series.OpenSprint!;
            return Task.FromResult(Result.Success(Read(_series, sprint, Reopened)));
        }

        /// <summary>Tasks read as not started whatever <see cref="Tasks"/> says: a task
        /// reopened since the dialog was drawn.</summary>
        public HashSet<Guid> Reopened { get; } = [];

        public static SprintProgressDto Read(SprintSeriesDto series, SprintDto sprint, IReadOnlySet<Guid>? reopened = null)
        {
            var today = Start.AddDays(9);
            var members = sprint.Items
                .Select(item => (Item: item, Task: Tasks.Single(task => task.Id == item.TaskId)))
                .Select(pair => reopened?.Contains(pair.Task.Id) == true ? (pair.Item, Task: pair.Task with { State = SprintWorkState.NotStarted }) : pair)
                .Select(pair => new SprintMemberDto(
                    pair.Task.Id, pair.Task.Title, SprintTaskStatus.Ready, pair.Task.State, pair.Task.Plan, "fincent",
                    pair.Task.State == SprintWorkState.Done ? Start.AddDays(1) : null, [], [],
                    pair.Item.IsGoal, pair.Item.AddedDirectly, pair.Item.AddedOn > sprint.Start, pair.Item.Note, pair.Item.OpenQuestion, pair.Item.Outcome,
                    BlockedSince: pair.Task.State == SprintWorkState.Blocked ? today.AddDays(-3) : null))
                .ToList();
            var goal = members.Where(member => member.PlanTag is not null).ToList();

            return new SprintProgressDto(
                series.Id, series.Name, sprint.Number, sprint.Name, sprint.Start, sprint.End, sprint.Goal, sprint.IsOpen,
                members, series.GoalRules, [.. goal.Select(member => member.TaskId)], Counts(goal), Counts(members),
                100, false, 10, 7, 0, today);
        }

        private static SprintCountsDto Counts(IReadOnlyList<SprintMemberDto> members) => new(
            members.Count(m => m.State == SprintWorkState.Done),
            members.Count(m => m.State == SprintWorkState.InProgress),
            members.Count(m => m.State == SprintWorkState.Blocked),
            members.Count(m => m.State == SprintWorkState.NotStarted),
            members.Count(m => m.AddedAfterStart));

        public Task<Result<SprintSeriesDto>> CloseSprintAsync(Guid seriesId, IReadOnlyDictionary<Guid, CloseOutcome> outcomes, string nextGoal, CancellationToken cancellationToken = default)
        {
            if (Refuse is { } words) return Task.FromResult(Result.Failure<SprintSeriesDto>(Error.Conflict("Progress.UnsettledItem", words)));

            var open = _series.OpenSprint!;
            Writes.Add($"Close {string.Join(' ', Tasks.Where(task => outcomes.ContainsKey(task.Id)).Select(task => $"{task.Id}={outcomes[task.Id]}"))} · {nextGoal}");

            var closed = open with
            {
                IsOpen = false,
                Items = [.. open.Items.Select(item => item with { Outcome = outcomes.TryGetValue(item.TaskId, out var outcome) ? outcome : null })]
            };
            var next = new SprintDto(open.Number + 1, $"Sprint {open.Number + 1}", open.End.AddDays(1), open.End.AddDays(14), nextGoal, true, [],
                [.. outcomes.Where(pair => pair.Value == CloseOutcome.Carry).Select(pair => new SprintItemDto(pair.Key, true, open.End.AddDays(1), false, null, false, null))]);

            _series = _series with { Sprints = [.. _series.Sprints.Where(s => s.Number != open.Number), closed, next] };
            return Task.FromResult(Result.Success(_series));
        }

        public Task<Result<SprintSeriesDto>> GetSeriesAsync(Guid seriesId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto?>> FindSeriesOfAsync(string repositoryAlias, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> CreateSeriesAsync(CreateSprintSeries request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> AddRepositoryAsync(Guid seriesId, string repositoryAlias, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> RemoveRepositoryAsync(Guid seriesId, string repositoryAlias, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> ChangeCadenceAsync(Guid seriesId, SprintLength length, DayOfWeek startWeekday, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> RenameAsync(Guid seriesId, string name, string namePattern, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> ShowInBriefsAsync(Guid seriesId, bool daily, bool weekly, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> SwitchAsync(Guid seriesId, bool on, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> SetGoalRulesAsync(Guid seriesId, IReadOnlyList<GoalRuleDto> rules, int goalShare, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> StartSprintAsync(Guid seriesId, DateOnly startOn, string goal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> SetSprintGoalAsync(Guid seriesId, int number, string goal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> AddTaskAsync(Guid seriesId, int number, Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> RemoveTaskAsync(Guid seriesId, int number, Guid taskId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> LinkPlanAsync(Guid seriesId, int number, string planTag, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> UnlinkPlanAsync(Guid seriesId, int number, string planTag, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> MarkGoalAsync(Guid seriesId, int number, Guid taskId, bool isGoal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> SetNoteAsync(Guid seriesId, int number, Guid taskId, string? note, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<SprintSeriesDto>> SetOpenQuestionAsync(Guid seriesId, int number, Guid taskId, bool openQuestion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result> DeleteSeriesAsync(Guid seriesId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
