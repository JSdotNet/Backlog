using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.UI.InProgress;
using Backlog.Modules.Progress.UI.Planning;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Progress;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Planning tasks and plans into a sprint (<c>.devbook/domain/progress/features.md#filling-a-sprint</c>
/// and <c>#planning-from-a-task</c>): "Add to sprint…" and the row menu on the sprint view,
/// and a task's Sprint section — each act a write through <see cref="ISprintSeriesPlanning"/>,
/// the module's port, and the view drawn from what the module answers. A closed sprint and
/// Progress off offer nothing.
/// </summary>
public sealed class SprintPlanningTests : IDisposable
{
    private static readonly DateOnly Start = new(2026, 10, 5);
    private static readonly Guid Brief = Guid.NewGuid();
    private static readonly Guid Ledger = Guid.NewGuid();
    private static readonly Guid Specs = Guid.NewGuid();
    private static readonly Guid Elsewhere = Guid.NewGuid();
    private readonly BunitContext _context = new();

    public SprintPlanningTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    private static IReadOnlyList<SprintCandidate> Candidates =>
    [
        new(Brief, "Opdrachtbrief", ["fincent"], ["+rekeningen"]),
        new(Ledger, "Grootboek", ["fincent"], []),
        new(Specs, "Specs naar het devbook", ["marketplace"], ["+verplaatsen-specs"]),
        new(Elsewhere, "Not in the series", ["tools"], ["+tools-plan"])
    ];

    // ---- The rules ----------------------------------------------------------------------

    [Fact]
    public void The_picker_offers_the_plans_and_tasks_of_the_series_repositories_and_marks_what_the_sprint_holds()
    {
        var series = Series(items: [Item(Ledger, direct: true)], plans: ["rekeningen"]);

        var choices = SprintPlanning.Choices(series, series.OpenSprint!, Candidates);

        Assert.Equal(["+rekeningen", "+verplaatsen-specs"], choices.Where(c => c.Kind == SprintAddKind.Plan).Select(c => c.Label));
        Assert.True(choices.Single(c => c.Id == "rekeningen").InSprint);
        Assert.DoesNotContain(choices, c => c.Label.Contains("tools", StringComparison.Ordinal) || c.Label == "Not in the series");
        Assert.True(choices.Single(c => c.Id == Ledger.ToString()).InSprint);
        Assert.True(choices.Single(c => c.Id == Brief.ToString()).InSprint);
        Assert.False(choices.Single(c => c.Id == Specs.ToString()).InSprint);
    }

    [Fact]
    public void A_rows_menu_takes_a_task_out_the_way_it_came_in()
    {
        var sprint = Series(items: [Item(Ledger, direct: true, goal: true)], plans: ["rekeningen"]).OpenSprint!;

        var direct = SprintPlanning.RowMenu(sprint, Member(Ledger, "Grootboek", null)).Select(item => item.Id).ToList();
        var planned = SprintPlanning.RowMenu(sprint, Member(Brief, "Opdrachtbrief", "rekeningen")).Select(item => item.Id).ToList();

        Assert.Equal([SprintPlanning.UnmarkGoal, SprintPlanning.EditNote, SprintPlanning.MarkQuestion, "remove-divider", SprintPlanning.RemoveTask], direct);
        Assert.Equal([SprintPlanning.MarkGoal, SprintPlanning.EditNote, SprintPlanning.MarkQuestion, "remove-divider", SprintPlanning.UnlinkPlan], planned);
    }

    // ---- The sprint view ----------------------------------------------------------------

    [Fact]
    public async Task Add_to_sprint_adds_a_task_through_the_module_and_the_sprint_is_read_again()
    {
        var progress = Compose(on: true, Series(items: [], plans: []));
        var rendered = await OpenSprintViewAsync();
        Assert.Empty(rendered.FindAll("[data-testid=sprint-row]"));

        await rendered.Find("[data-testid=in-progress-sprint-add]").ClickAsync(new MouseEventArgs());
        var option = rendered.FindAll("[data-testid=sprint-add-option]").Single(o => o.GetAttribute("data-option-id") == Ledger.ToString());
        await option.ClickAsync(new MouseEventArgs());

        Assert.Equal([$"AddTask 42 {Ledger}"], progress.Writes);
        rendered.WaitForAssertion(() => Assert.Single(rendered.FindAll("[data-testid=sprint-row]")));
        Assert.Contains("Added “Grootboek”.", rendered.Find("[data-testid=sprint-add-status]").TextContent);
        rendered.WaitForAssertion(() => Assert.True(rendered.FindAll("[data-testid=sprint-add-option]")
            .Single(o => o.GetAttribute("data-option-id") == Ledger.ToString()).HasAttribute("disabled")));
    }

    [Fact]
    public async Task Add_to_sprint_links_a_whole_plan()
    {
        var progress = Compose(on: true, Series(items: [], plans: []));
        var rendered = await OpenSprintViewAsync();

        await rendered.Find("[data-testid=in-progress-sprint-add]").ClickAsync(new MouseEventArgs());
        await rendered.FindAll("[data-testid=sprint-add-option]").Single(o => o.GetAttribute("data-option-id") == "rekeningen").ClickAsync(new MouseEventArgs());

        Assert.Equal(["LinkPlan 42 rekeningen"], progress.Writes);
        rendered.WaitForAssertion(() => Assert.Equal("Opdrachtbrief", rendered.Find("[data-testid=sprint-row-title]").TextContent.Trim()));

        // The plan's task now reads as held, so the open picker no longer offers it.
        Assert.True(rendered.FindAll("[data-testid=sprint-add-option]").Single(o => o.GetAttribute("data-option-id") == Brief.ToString()).HasAttribute("disabled"));
    }

    [Fact]
    public async Task The_row_menu_marks_goal_and_removes_through_the_module()
    {
        var progress = Compose(on: true, Series(items: [Item(Ledger, direct: true)], plans: []));
        var rendered = await OpenSprintViewAsync();

        await ChooseAsync(rendered, SprintPlanning.MarkGoal);
        Assert.Equal([$"MarkGoal 42 {Ledger} True"], progress.Writes);

        rendered.WaitForAssertion(() => Assert.Single(rendered.FindAll("[data-testid=sprint-row-goal]")));
        await ChooseAsync(rendered, SprintPlanning.RemoveTask);

        Assert.Equal($"RemoveTask 42 {Ledger}", progress.Writes[^1]);
        rendered.WaitForAssertion(() => Assert.Empty(rendered.FindAll("[data-testid=sprint-row]")));
    }

    [Fact]
    public async Task The_note_dialog_writes_the_note_and_the_open_question()
    {
        var progress = Compose(on: true, Series(items: [Item(Ledger, direct: true)], plans: []));
        var rendered = await OpenSprintViewAsync();

        await ChooseAsync(rendered, SprintPlanning.EditNote);
        await rendered.Find("[data-testid=sprint-note-text] input").InputAsync(new ChangeEventArgs { Value = "Ask Anne" });
        await rendered.Find("[data-testid=sprint-note-question] input").ChangeAsync(new ChangeEventArgs { Value = true });
        await rendered.Find("[data-testid=sprint-note-save]").ClickAsync(new MouseEventArgs());

        Assert.Equal([$"SetNote 42 {Ledger} Ask Anne", $"SetOpenQuestion 42 {Ledger} True"], progress.Writes);
        rendered.WaitForAssertion(() => Assert.Equal("Ask Anne", rendered.Find("[data-testid=sprint-row-question]").TextContent.Trim()));
        Assert.Empty(rendered.FindAll("[data-testid=sprint-note-dialog]"));
    }

    [Fact]
    public async Task A_refused_write_is_said_in_the_modules_words()
    {
        var progress = Compose(on: true, Series(items: [Item(Ledger, direct: true)], plans: []));
        var rendered = await OpenSprintViewAsync();
        progress.Refuse = "Sprint 42 is closed.";

        await ChooseAsync(rendered, SprintPlanning.MarkGoal);

        rendered.WaitForAssertion(() => Assert.Contains("Sprint 42 is closed.", rendered.Find("[data-testid=in-progress-sprint-write-error]").TextContent));
    }

    [Fact]
    public async Task A_closed_sprint_offers_no_add_and_no_row_menu()
    {
        var series = Series(items: [Item(Ledger, direct: true)], plans: []);
        var closed = series.OpenSprint! with { Number = 41, Name = "Sprint 41", Start = Start.AddDays(-14), End = Start.AddDays(-1), IsOpen = false };
        Compose(on: true, series with { Sprints = [closed, .. series.Sprints] });
        var rendered = await OpenSprintViewAsync();
        Assert.Single(rendered.FindAll("[data-testid=in-progress-sprint-add]"));

        await rendered.Find("[data-testid=sprint-board-previous]").ClickAsync(new MouseEventArgs());

        rendered.WaitForAssertion(() => Assert.Empty(rendered.FindAll("[data-testid=in-progress-sprint-add]")));
        Assert.Empty(rendered.FindAll("[data-testid=sprint-row-menu]"));
    }

    // ---- A task's Sprint section --------------------------------------------------------

    [Fact]
    public async Task A_tasks_sprint_section_adds_it_marks_it_and_takes_it_out_through_the_module()
    {
        var progress = Compose(on: true, Series(items: [], plans: []));
        var section = _context.Render<TaskSprintSection>(parameters => parameters.Add(p => p.Candidate, Candidates[1]));

        section.WaitForAssertion(() => Assert.Equal("Not in Sprint 42.", section.Find("[data-testid=sprint-task-line]").TextContent.Trim()));
        await section.Find("[data-testid=sprint-task-add]").ClickAsync(new MouseEventArgs());
        section.WaitForAssertion(() => Assert.Equal("In Sprint 42, added directly.", section.Find("[data-testid=sprint-task-line]").TextContent.Trim()));

        await section.Find("[data-testid=sprint-task-goal-toggle]").ClickAsync(new MouseEventArgs());
        section.WaitForAssertion(() => Assert.Single(section.FindAll("[data-testid=sprint-task-goal]")));

        await section.Find("[data-testid=sprint-task-remove]").ClickAsync(new MouseEventArgs());

        Assert.Equal([$"AddTask 42 {Ledger}", $"MarkGoal 42 {Ledger} True", $"RemoveTask 42 {Ledger}"], progress.Writes);
    }

    [Fact]
    public async Task A_tasks_sprint_section_links_its_plan_and_writes_its_note()
    {
        var progress = Compose(on: true, Series(items: [], plans: []));
        var section = _context.Render<TaskSprintSection>(parameters => parameters.Add(p => p.Candidate, Candidates[0]));

        section.WaitForAssertion(() => section.Find("[data-testid=sprint-task-link-plan]"));
        await section.Find("[data-testid=sprint-task-link-plan]").ClickAsync(new MouseEventArgs());
        section.WaitForAssertion(() => Assert.Equal("In Sprint 42 through +rekeningen.", section.Find("[data-testid=sprint-task-line]").TextContent.Trim()));

        await section.Find("[data-testid=sprint-task-edit-note]").ClickAsync(new MouseEventArgs());
        await section.Find("[data-testid=sprint-note-text] input").InputAsync(new ChangeEventArgs { Value = "Demo first" });
        await section.Find("[data-testid=sprint-note-save]").ClickAsync(new MouseEventArgs());

        Assert.Equal(["LinkPlan 42 rekeningen", $"SetNote 42 {Brief} Demo first"], progress.Writes);
        section.WaitForAssertion(() => Assert.Equal("Demo first", section.Find("[data-testid=sprint-task-note]").TextContent.Trim()));
    }

    [Fact]
    public void With_progress_off_or_the_task_in_no_series_the_section_draws_nothing()
    {
        Compose(on: false, Series(items: [], plans: []));
        var off = _context.Render<TaskSprintSection>(parameters => parameters.Add(p => p.Candidate, Candidates[1]));
        Assert.Empty(off.FindAll("[data-testid=sprint-task-panel]"));

        using var other = new BunitContext();
        other.JSInterop.Mode = JSRuntimeMode.Loose;
        var progress = new FakeProgress(Series(items: [], plans: []));
        other.Services.AddSingleton<IAppFeatureSettings>(new ProgressSwitch(true));
        other.Services.AddSingleton<ISprintSeriesPlanning>(progress);
        var elsewhere = other.Render<TaskSprintSection>(parameters => parameters.Add(p => p.Candidate, Candidates[3]));

        Assert.Empty(elsewhere.FindAll("[data-testid=sprint-task-panel]"));
    }

    // ---- Composition --------------------------------------------------------------------

    private FakeProgress Compose(bool on, SprintSeriesDto series)
    {
        var progress = new FakeProgress(series);
        _context.Services.AddSingleton<IAppFeatureSettings>(new ProgressSwitch(on));
        _context.Services.AddSingleton<ISprintSeriesPlanning>(progress);
        _context.Services.AddSingleton<ISprintProgress>(progress);
        return progress;
    }

    private async Task<IRenderedComponent<InProgressSprints>> OpenSprintViewAsync()
    {
        var rendered = _context.Render<InProgressSprints>(parameters => parameters
            .Add(p => p.Repositories, ["fincent"])
            .Add(p => p.Candidates, () => Candidates)
            .Add(p => p.Now, (RenderFragment<RenderFragment?>)(viewSwitch => builder => builder.AddContent(0, viewSwitch))));
        rendered.WaitForAssertion(() => rendered.Find("[data-testid=in-progress-view-switch-sprint]"));
        await rendered.Find("[data-testid=in-progress-view-switch-sprint]").ClickAsync(new MouseEventArgs());
        rendered.WaitForAssertion(() => rendered.Find("[data-testid=sprint-board-goal]"));
        return rendered;
    }

    private static async Task ChooseAsync(IRenderedComponent<InProgressSprints> rendered, string act)
    {
        rendered.WaitForAssertion(() => rendered.Find("[data-testid=sprint-row-menu]"));
        await rendered.Find("[data-testid=sprint-row-menu]").ClickAsync(new MouseEventArgs());
        await rendered.Find($"[data-testid=sprint-row-menu-list-item-{act}]").ClickAsync(new MouseEventArgs());
    }

    private static SprintSeriesDto Series(IReadOnlyList<SprintItemDto> items, IReadOnlyList<string> plans) =>
        new(Guid.NewGuid(), "Fincent", ["fincent", "marketplace"], SprintLength.TwoWeeks, DayOfWeek.Monday, "Sprint {n}", false, false,
            [GoalRuleDto.MarkedAsGoal()], 100,
            [new SprintDto(42, "Sprint 42", Start, Start.AddDays(13), "Ship it", true, [.. plans.Select(plan => new PlanLinkDto(plan, Start))], items)]);

    private static SprintItemDto Item(Guid taskId, bool direct = false, bool goal = false, string? note = null, bool question = false) =>
        new(taskId, direct, direct ? Start : null, goal, note, question, null);

    private static SprintMemberDto Member(Guid taskId, string title, string? plan) =>
        new(taskId, title, SprintTaskStatus.Ready, SprintWorkState.NotStarted, plan, "fincent", null, [], [], false, false, false, null, false, null);

    public void Dispose() => _context.Dispose();

    private sealed class ProgressSwitch(bool on) : IAppFeatureSettings
    {
        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public bool IsEnabled(string key) => key == ProgressFeatures.Progress && on;

        public string? SetEnabled(string key, bool enabled) => null;
    }

    /// <summary>
    /// The module's two ports over one series in memory: each write is recorded and
    /// applied the way the module records it — on the open sprint alone — and the
    /// progress read gives the members a sprint holds: those added directly and those
    /// filed under a linked plan in the series' repositories.
    /// </summary>
    private sealed class FakeProgress(SprintSeriesDto series) : ISprintSeriesPlanning, ISprintProgress
    {
        private SprintSeriesDto _series = series;

        public List<string> Writes { get; } = [];

        /// <summary>Set, every write is refused with these words.</summary>
        public string? Refuse { get; set; }

        public Task<Result<IReadOnlyList<SprintSeriesDto>>> ListSeriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success<IReadOnlyList<SprintSeriesDto>>([_series]));

        public Task<Result<SprintProgressDto>> ReadAsync(Guid seriesId, int? number = null, CancellationToken cancellationToken = default)
        {
            var sprint = number is { } asked ? _series.Sprints.Single(s => s.Number == asked) : _series.OpenSprint!;
            var members = Candidates
                .Where(task => task.Repositories.Any(alias => _series.Repositories.Contains(alias, StringComparer.OrdinalIgnoreCase)))
                .Where(task => sprint.Items.Any(item => item.TaskId == task.TaskId && item.AddedDirectly)
                    || task.PlanTags.Any(tag => SprintPlanning.IsLinked(sprint, tag)))
                .Select(task =>
                {
                    var item = sprint.Items.FirstOrDefault(candidate => candidate.TaskId == task.TaskId);
                    return new SprintMemberDto(task.TaskId, task.Title, SprintTaskStatus.Ready, SprintWorkState.NotStarted,
                        task.PlanTags.Select(SprintPlanning.Bare).FirstOrDefault(), task.Repositories[0], null, [], [],
                        item?.IsGoal ?? false, item?.AddedDirectly ?? false, false, item?.Note, item?.OpenQuestion ?? false, null);
                })
                .ToList();
            var counts = new SprintCountsDto(0, 0, 0, members.Count, 0);

            return Task.FromResult(Result.Success(new SprintProgressDto(
                _series.Id, _series.Name, sprint.Number, sprint.Name, sprint.Start, sprint.End, sprint.Goal, sprint.IsOpen,
                members, _series.GoalRules, [.. members.Where(m => m.IsGoal).Select(m => m.TaskId)], counts, counts, 100, false, 10, 3, 0)));
        }

        public Task<Result<SprintSeriesDto>> AddTaskAsync(Guid seriesId, int number, Guid taskId, CancellationToken cancellationToken = default) =>
            Write($"AddTask {number} {taskId}", number, sprint => sprint with { Items = Upsert(sprint, taskId, item => item with { AddedDirectly = true, AddedOn = Start }) });

        public Task<Result<SprintSeriesDto>> RemoveTaskAsync(Guid seriesId, int number, Guid taskId, CancellationToken cancellationToken = default) =>
            Write($"RemoveTask {number} {taskId}", number, sprint => sprint with { Items = [.. sprint.Items.Where(item => item.TaskId != taskId)] });

        public Task<Result<SprintSeriesDto>> LinkPlanAsync(Guid seriesId, int number, string planTag, CancellationToken cancellationToken = default) =>
            Write($"LinkPlan {number} {planTag}", number, sprint => sprint with { PlanLinks = [.. sprint.PlanLinks, new PlanLinkDto(SprintPlanning.Bare(planTag), Start)] });

        public Task<Result<SprintSeriesDto>> UnlinkPlanAsync(Guid seriesId, int number, string planTag, CancellationToken cancellationToken = default) =>
            Write($"UnlinkPlan {number} {planTag}", number, sprint => sprint with { PlanLinks = [.. sprint.PlanLinks.Where(link => !string.Equals(link.PlanTag, SprintPlanning.Bare(planTag), StringComparison.OrdinalIgnoreCase))] });

        public Task<Result<SprintSeriesDto>> MarkGoalAsync(Guid seriesId, int number, Guid taskId, bool isGoal, CancellationToken cancellationToken = default) =>
            Write($"MarkGoal {number} {taskId} {isGoal}", number, sprint => sprint with { Items = Upsert(sprint, taskId, item => item with { IsGoal = isGoal }) });

        public Task<Result<SprintSeriesDto>> SetNoteAsync(Guid seriesId, int number, Guid taskId, string? note, CancellationToken cancellationToken = default) =>
            Write($"SetNote {number} {taskId} {note}", number, sprint => sprint with { Items = Upsert(sprint, taskId, item => item with { Note = note }) });

        public Task<Result<SprintSeriesDto>> SetOpenQuestionAsync(Guid seriesId, int number, Guid taskId, bool openQuestion, CancellationToken cancellationToken = default) =>
            Write($"SetOpenQuestion {number} {taskId} {openQuestion}", number, sprint => sprint with { Items = Upsert(sprint, taskId, item => item with { OpenQuestion = openQuestion }) });

        private Task<Result<SprintSeriesDto>> Write(string what, int number, Func<SprintDto, SprintDto> change)
        {
            if (Refuse is { } words) return Task.FromResult(Result.Failure<SprintSeriesDto>(Error.Conflict("Progress.SprintClosed", words)));

            Writes.Add(what);
            _series = _series with { Sprints = [.. _series.Sprints.Select(sprint => sprint.Number == number ? change(sprint) : sprint)] };
            return Task.FromResult(Result.Success(_series));
        }

        private static IReadOnlyList<SprintItemDto> Upsert(SprintDto sprint, Guid taskId, Func<SprintItemDto, SprintItemDto> change)
        {
            var existing = sprint.Items.FirstOrDefault(item => item.TaskId == taskId) ?? new SprintItemDto(taskId, false, null, false, null, false, null);
            return [.. sprint.Items.Where(item => item.TaskId != taskId), change(existing)];
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
        public Task<Result<SprintSeriesDto>> CloseSprintAsync(Guid seriesId, IReadOnlyDictionary<Guid, CloseOutcome> outcomes, string nextGoal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
