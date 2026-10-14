using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.UI.InProgress;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Progress;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The In progress tab's sprint view (<c>.devbook/domain/progress/features.md#following-a-sprint</c>):
/// the Now | Sprint n switch only while Progress is on and the scope holds a repository of
/// a series switched on; the board's groups, Needs you, chips and outside line; the words
/// and done line the module's counts are drawn with; and the pull request reader that
/// gives the chips their state and the rows their owner.
/// </summary>
public sealed class SprintViewTests : IDisposable
{
    private static readonly DateOnly Start = new(2026, 10, 5);
    private readonly BunitContext _context = new();

    // ---- The switch and the two views ------------------------------------------------

    [Fact]
    public void With_progress_off_the_now_view_is_drawn_with_no_switch()
    {
        Compose(on: false, Series("Fincent", ["fincent"]));

        var rendered = RenderTab(["fincent"]);

        Assert.Equal("now:no-switch", rendered.Find("[data-testid=now]").TextContent);
        Assert.Empty(rendered.FindAll("[data-testid=in-progress-view-switch]"));
    }

    [Fact]
    public void A_repository_in_no_series_keeps_the_now_view_unchanged()
    {
        Compose(on: true, Series("Fincent", ["fincent"]));

        var rendered = RenderTab(["tools"]);

        Assert.Equal("now:no-switch", rendered.Find("[data-testid=now]").TextContent);
    }

    [Fact]
    public void A_switched_off_series_reads_as_no_series()
    {
        Compose(on: true, Series("Fincent", ["fincent"]) with { IsOn = false });

        var rendered = RenderTab(["fincent"]);

        Assert.Equal("now:no-switch", rendered.Find("[data-testid=now]").TextContent);
    }

    [Fact]
    public void A_scope_holding_a_series_repository_shows_now_with_the_switch_naming_the_open_sprint()
    {
        Compose(on: true, Series("Fincent", ["fincent", "marketplace"]));

        var rendered = RenderTab(["tools", "marketplace"]);

        rendered.WaitForAssertion(() => Assert.Equal("now:switch", rendered.Find("[data-testid=now]").TextContent));
        Assert.Equal("Sprint 42", rendered.Find("[data-testid=in-progress-view-switch-sprint]").TextContent.Trim());
        Assert.Equal("true", rendered.Find("[data-testid=in-progress-view-switch-now]").GetAttribute("aria-checked"));
    }

    [Fact]
    public async Task Choosing_the_sprint_draws_the_sprint_view_and_choosing_now_brings_the_now_view_back()
    {
        var reads = new List<bool>();
        Compose(on: true, Series("Fincent", ["fincent", "marketplace"]));

        var rendered = RenderTab(["fincent"], onRead: reads.Add);
        rendered.WaitForAssertion(() => rendered.Find("[data-testid=in-progress-view-switch-sprint]"));

        await rendered.Find("[data-testid=in-progress-view-switch-sprint]").ClickAsync(new());

        rendered.WaitForAssertion(() => Assert.Single(rendered.FindAll("[data-testid=sprint-board]")));
        Assert.Empty(rendered.FindAll("[data-testid=now]"));
        Assert.Equal("Fincent · 2 repositories · Mon 5 Oct – Sun 18 Oct", rendered.Find("[data-testid=sprint-board-series]").TextContent);
        Assert.Equal(3, rendered.FindAll("[data-testid=sprint-row]").Count);
        Assert.Equal([false], reads);

        await rendered.Find("[data-testid=in-progress-view-switch-now]").ClickAsync(new());

        rendered.WaitForAssertion(() => Assert.Equal("now:switch", rendered.Find("[data-testid=now]").TextContent));
    }

    /// <summary>Copy as table (<c>.devbook/domain/progress/features.md#copying-a-sprint-as-a-table</c>)
    /// hands the clipboard the whole sprint as HTML and as plain text together.</summary>
    [Fact]
    public async Task Copy_as_table_puts_the_sprint_on_the_clipboard_as_html_and_text()
    {
        var copy = _context.JSInterop.Setup<bool>("backlogClipboard.copyRich", _ => true);
        copy.SetResult(true);
        Compose(on: true, Series("Fincent", ["fincent"]));

        var rendered = RenderTab(["fincent"]);
        rendered.WaitForAssertion(() => rendered.Find("[data-testid=in-progress-view-switch-sprint]"));
        await rendered.Find("[data-testid=in-progress-view-switch-sprint]").ClickAsync(new());
        rendered.WaitForAssertion(() => Assert.Equal(3, rendered.FindAll("[data-testid=sprint-row]").Count));

        var button = rendered.Find("[data-testid=in-progress-sprint-copy-table-button]");
        Assert.Equal("Copy as table", button.TextContent.Trim());
        Assert.False(button.HasAttribute("disabled"));

        await button.ClickAsync(new());

        var invocation = Assert.Single(copy.Invocations);
        var html = Assert.IsType<string>(invocation.Arguments[0]);
        var text = Assert.IsType<string>(invocation.Arguments[1]);
        Assert.StartsWith("<table style=", html, StringComparison.Ordinal);
        Assert.Equal(4, text.Split("\r\n").Length);
        rendered.WaitForAssertion(() => Assert.Equal("Copied", rendered.Find("[data-testid=in-progress-sprint-copy-table-status]").TextContent));
    }

    [Fact]
    public async Task The_outside_line_counts_live_work_on_tasks_the_sprint_does_not_hold()
    {
        Compose(on: true, Series("Fincent", ["fincent"]));
        IReadOnlySet<Guid>? asked = null;

        var rendered = _context.Render<InProgressSprints>(parameters => parameters
            .Add(p => p.Repositories, ["fincent"])
            .Add(p => p.Outside, members =>
            {
                asked = members;
                return [new SprintBoardOutside("t", "Other task", 2, 0, "t"), new SprintBoardOutside("p", "#9 Loose", 0, 1)];
            })
            .Add(p => p.Now, (RenderFragment<RenderFragment?>)(viewSwitch => builder => builder.AddContent(0, viewSwitch))));
        rendered.WaitForAssertion(() => rendered.Find("[data-testid=in-progress-view-switch-sprint]"));
        await rendered.Find("[data-testid=in-progress-view-switch-sprint]").ClickAsync(new());

        rendered.WaitForAssertion(() => Assert.Equal(
            "2 sessions and 1 pull request are running against work that is not in Sprint 42.",
            rendered.Find("[data-testid=sprint-board-outside-line]").TextContent));
        Assert.NotNull(asked);
        Assert.Equal(3, asked!.Count);
        Assert.True(rendered.Find("[data-testid=sprint-board-outside-list]").HasAttribute("hidden"));

        await rendered.Find("[data-testid=sprint-board-outside-toggle]").ClickAsync(new());

        Assert.False(rendered.Find("[data-testid=sprint-board-outside-list]").HasAttribute("hidden"));
        Assert.Equal(2, rendered.FindAll("[data-testid=sprint-board-outside-item]").Count);
    }

    [Fact]
    public async Task Previous_reads_the_earlier_sprint_and_next_is_refused_at_the_last()
    {
        var series = Series("Fincent", ["fincent"]);
        series = series with { Sprints = [Sprint(41, Start.AddDays(-14), isOpen: false), .. series.Sprints] };
        var progress = Compose(on: true, series);

        var rendered = RenderTab(["fincent"]);
        rendered.WaitForAssertion(() => rendered.Find("[data-testid=in-progress-view-switch-sprint]"));
        await rendered.Find("[data-testid=in-progress-view-switch-sprint]").ClickAsync(new());
        rendered.WaitForAssertion(() => rendered.Find("[data-testid=sprint-board]"));

        Assert.True(rendered.Find("[data-testid=sprint-board-next]").HasAttribute("disabled"));
        await rendered.Find("[data-testid=sprint-board-previous]").ClickAsync(new());

        rendered.WaitForAssertion(() => Assert.Equal(41, progress.LastNumber));
        rendered.WaitForAssertion(() => Assert.False(rendered.Find("[data-testid=sprint-board-next]").HasAttribute("disabled")));
    }

    [Fact]
    public void Narrowing_the_scope_from_every_repository_to_the_brain_alone_withdraws_the_switch()
    {
        Compose(on: true, Series("Fincent", ["fincent"]));

        var rendered = RenderTab([]);
        rendered.WaitForAssertion(() => Assert.Equal("now:switch", rendered.Find("[data-testid=now]").TextContent));

        rendered.Render(parameters => parameters.Add(p => p.Repositories, [""]));

        rendered.WaitForAssertion(() => Assert.Equal("now:no-switch", rendered.Find("[data-testid=now]").TextContent));
    }

    [Fact]
    public async Task A_sprint_that_cannot_be_read_says_why_and_keeps_the_switch_and_refresh()
    {
        var progress = Compose(on: true, Series("Fincent", ["fincent"]));
        progress.Fails = true;

        var rendered = RenderTab(["fincent"]);
        rendered.WaitForAssertion(() => rendered.Find("[data-testid=in-progress-view-switch-sprint]"));
        await rendered.Find("[data-testid=in-progress-view-switch-sprint]").ClickAsync(new());

        rendered.WaitForAssertion(() => Assert.Contains("Progress is off", rendered.Find("[data-testid=sprint-board-error]").TextContent));
        Assert.Single(rendered.FindAll("[data-testid=in-progress-view-switch]"));

        progress.Fails = false;
        await rendered.Find("[data-testid=in-progress-sprint-refresh]").ClickAsync(new());

        rendered.WaitForAssertion(() => Assert.Equal(3, rendered.FindAll("[data-testid=sprint-row]").Count));
    }

    // ---- The board ---------------------------------------------------------------------

    [Fact]
    public void Pull_request_chips_are_coloured_by_state_and_the_owner_is_their_author()
    {
        var rendered = _context.Render<SprintBoard>(parameters => parameters.Add(p => p.Model, Board()));

        var chips = rendered.FindAll("[data-testid=sprint-row-pull]");
        Assert.Equal(["merged", "open", "draft"], chips.Select(chip => chip.GetAttribute("data-state")));
        Assert.Contains("sprint-pr--merged", chips[0].ClassList);
        Assert.Equal("#4707 open", chips[1].TextContent);
        Assert.Equal("draft", chips[2].TextContent);

        var owners = rendered.FindAll("[data-testid=sprint-row-owner]").Select(owner => owner.TextContent).ToList();
        Assert.Contains("jsdotnet", owners);
        Assert.Contains(string.Empty, owners);
    }

    [Fact]
    public void A_goal_plan_group_carries_the_badge_and_a_goal_task_elsewhere_carries_goal()
    {
        var rendered = _context.Render<SprintBoard>(parameters => parameters.Add(p => p.Model, Board()));

        var groups = rendered.FindAll("[data-testid=sprint-board-group-section]");
        Assert.Equal(["rekeningen", "specs", ""], groups.Select(group => group.GetAttribute("data-plan")));
        Assert.Single(groups[0].QuerySelectorAll("[data-testid=sprint-board-goal-plan]"));
        Assert.Empty(groups[0].QuerySelectorAll("[data-testid=sprint-row-goal]"));
        Assert.Single(groups[1].QuerySelectorAll("[data-testid=sprint-row-goal]"));
        Assert.Single(rendered.FindAll("[data-testid=sprint-row-added]"));
    }

    [Fact]
    public void Needs_you_lists_the_blocked_the_waiting_on_review_and_the_open_question()
    {
        var rendered = _context.Render<SprintBoard>(parameters => parameters.Add(p => p.Model, Board()));

        var needs = rendered.FindAll("[data-testid=sprint-board-need]");
        Assert.Equal(["blocked", "review", "question"], needs.Select(need => need.GetAttribute("data-kind")));
        Assert.Contains("#4707 waits on review · +rekeningen", needs[1].TextContent);
        Assert.Contains("Open question: Import?", needs[2].TextContent);
    }

    [Fact]
    public void Choosing_a_repository_narrows_the_rows_and_no_grouping_draws_one_group()
    {
        var rendered = _context.Render<SprintBoard>(parameters => parameters.Add(p => p.Model, Board()));

        rendered.Find("[data-testid=sprint-board-repository] select").Change("marketplace");
        Assert.Single(rendered.FindAll("[data-testid=sprint-row]"));

        rendered.Find("[data-testid=sprint-board-repository] select").Change("*");
        rendered.Find("[data-testid=sprint-board-group] select").Change("none");
        var group = Assert.Single(rendered.FindAll("[data-testid=sprint-board-group-section]"));
        Assert.Equal("All sprint work", group.GetAttribute("aria-label"));
        Assert.Equal(5, rendered.FindAll("[data-testid=sprint-row]").Count);
    }

    // ---- The words and the done line -------------------------------------------------------

    [Fact]
    public void The_done_line_counts_members_done_by_the_end_of_each_working_day()
    {
        var progress = Progress(
            workingDaysElapsed: 4,
            Member("a", SprintWorkState.Done, doneOn: Start.AddDays(-3)),
            Member("b", SprintWorkState.Done, doneOn: Start.AddDays(1)),
            Member("c", SprintWorkState.Done, doneOn: Start.AddDays(3)),
            Member("d", SprintWorkState.InProgress));

        Assert.Equal([1, 2, 2, 3], SprintBoardMapper.DonePerDay(progress));
    }

    [Fact]
    public void The_pace_reading_names_the_open_goal_tasks_the_blocked_ones_and_the_gap_to_the_even_pace()
    {
        var blocked = Member("Openspec", SprintWorkState.Blocked);
        var progress = Progress(7, blocked, Member("b", SprintWorkState.Done, doneOn: Start)) with
        {
            GoalTaskIds = [blocked.TaskId],
            Goal = new SprintCountsDto(12, 2, 1, 2, 0),
            AllWork = new SprintCountsDto(13, 7, 1, 8, 2)
        };

        Assert.Equal(
            "Goal: 5 open with 3 working days left; Openspec is blocked. All work is 7 behind the even pace.",
            SprintBoardMapper.PaceReading(progress));
        Assert.Equal("Day 7 of 10 · 3 working days left", SprintBoardMapper.DayText(progress));
    }

    [Fact]
    public void The_goal_rules_read_as_the_plans_and_rules_that_counted_them()
    {
        Assert.Equal(
            "+rekeningen, +verplaatsen-specs and tasks marked goal",
            SprintBoardMapper.GoalRules([GoalRuleDto.InPlans("rekeningen", "+verplaatsen-specs"), GoalRuleDto.MarkedAsGoal()], 100));
        Assert.Equal("every task in the sprint · met at 80%", SprintBoardMapper.GoalRules([GoalRuleDto.Everything()], 80));
    }

    // ---- The pull request reader -------------------------------------------------------------

    [Fact]
    public async Task The_reader_answers_state_merge_day_and_author_from_github_and_reads_a_merged_one_once()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-sprint-pulls", Guid.NewGuid().ToString("n"));
        try
        {
            var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
            var (parsed, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
            settings.SetRepositories(parsed);
            var client = new TasksPaneHost.FakeGitHubClient();
            client.PullRequestsByNumber.Add(new GitHubLinkedPullRequest(4, "u", "t", "JSdotNet/Backlog", GitHubItemState.Merged, "jsdotnet", new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero)));
            client.PullRequestsByNumber.Add(new GitHubLinkedPullRequest(5, "u", "t", "JSdotNet/Backlog", GitHubItemState.Open, "octo", null));
            var reader = new GitHubSprintPullRequests(() => new GitHubIntegration(settings, client, new ConnectedProbe()));

            var first = await reader.ReadAsync([new("JSdotNet/Backlog", "4"), new("JSdotNet/Backlog", "5"), new("JSdotNet/Backlog", "6")], TestContext.Current.CancellationToken);

            Assert.Equal([LinkedPullRequestState.Merged, LinkedPullRequestState.Open, LinkedPullRequestState.Unknown], first.Select(pull => pull.State));
            Assert.Equal(["jsdotnet", "octo", null], first.Select(pull => pull.Author));
            Assert.Equal(new DateOnly(2026, 10, 7), first[0].MergedOn);

            client.ReadByNumber.Clear();
            await reader.ReadAsync([new("JSdotNet/Backlog", "4"), new("JSdotNet/Backlog", "6")], TestContext.Current.CancellationToken);
            Assert.Empty(client.ReadByNumber);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Without_github_every_pull_request_reads_unknown()
    {
        var reader = new GitHubSprintPullRequests(() => null);

        var answer = await reader.ReadAsync([new("JSdotNet/Backlog", "4")], TestContext.Current.CancellationToken);

        Assert.Equal(LinkedPullRequestState.Unknown, Assert.Single(answer).State);
    }

    // ---- Composition -------------------------------------------------------------------------

    private FakeProgress Compose(bool on, params SprintSeriesDto[] series)
    {
        var progress = new FakeProgress(series);
        _context.Services.AddSingleton<IAppFeatureSettings>(new ProgressSwitch(on));
        _context.Services.AddSingleton<ISprintSeriesPlanning>(progress);
        _context.Services.AddSingleton<ISprintProgress>(progress);
        return progress;
    }

    private IRenderedComponent<InProgressSprints> RenderTab(IReadOnlyList<string> scope, Action<bool>? onRead = null) =>
        _context.Render<InProgressSprints>(parameters => parameters
            .Add(p => p.Repositories, scope)
            .Add(p => p.OnRead, (bool refresh) => onRead?.Invoke(refresh))
            .Add(p => p.Now, (RenderFragment<RenderFragment?>)(viewSwitch => builder =>
            {
                builder.OpenElement(0, "p");
                builder.AddAttribute(1, "data-testid", "now");
                builder.AddContent(2, viewSwitch is null ? "now:no-switch" : "now:switch");
                builder.CloseElement();
                builder.AddContent(3, viewSwitch);
            })));

    private static SprintSeriesDto Series(string name, IReadOnlyList<string> repositories) =>
        new(Guid.NewGuid(), name, repositories, SprintLength.TwoWeeks, DayOfWeek.Monday, "Sprint {n}", false, false,
            [GoalRuleDto.InPlans("rekeningen")], 100, [Sprint(42, Start, isOpen: true)]);

    private static SprintDto Sprint(int number, DateOnly start, bool isOpen) =>
        new(number, $"Sprint {number}", start, start.AddDays(13), "Ship it", isOpen, [], []);

    private static SprintMemberDto Member(
        string title,
        SprintWorkState state,
        DateOnly? doneOn = null,
        string? plan = "rekeningen",
        string? repository = "fincent") =>
        new(Guid.NewGuid(), title, SprintTaskStatus.InProgress, state, plan, repository, doneOn, [], [], false, false, false, null, false, null);

    private static SprintProgressDto Progress(int workingDaysElapsed, params SprintMemberDto[] members) =>
        new(Guid.NewGuid(), "Fincent", 42, "Sprint 42", Start, Start.AddDays(13), "Ship it", true, members,
            [GoalRuleDto.InPlans("rekeningen")], [], new SprintCountsDto(0, 0, 0, 0, 0), new SprintCountsDto(0, 0, 0, 0, 0),
            100, false, 10, workingDaysElapsed, 0);

    private static SprintBoardModel Board()
    {
        static SprintBoardPullRequest Pull(string number, SprintPullRequestState state) => new("JSdotNet/Backlog", number, state);

        SprintBoardRow[] rows =
        [
            new("1", "Categorieën", SprintRowState.Done, "rekeningen", "fincent", true, false, [Pull("4685", SprintPullRequestState.Merged)], ["jsdotnet"], "Import?", true),
            new("2", "Klantrekening", SprintRowState.InProgress, "rekeningen", "fincent", true, true, [Pull("4707", SprintPullRequestState.Open)], ["jsdotnet"], null, false),
            new("3", "Openspec", SprintRowState.Blocked, "rekeningen", "fincent", true, false, [], [], null, false),
            new("4", "Rekeningen", SprintRowState.InProgress, "specs", "marketplace", true, false, [Pull("12", SprintPullRequestState.Draft)], ["jsdotnet"], null, false),
            new("5", "Grootboek", SprintRowState.NotStarted, null, "fincent", false, false, [], [], null, false)
        ];

        return new SprintBoardModel(
            "Fincent", "Sprint 42", "Mon 5 Oct – Sun 18 Oct", ["fincent", "marketplace"], true, false, "Ship it",
            new SprintBoardCounts(1, 2, 1, 0), "+rekeningen", false, SprintBoardCounts.Of(rows),
            "Day 7 of 10 · 3 working days left", [0, 1], 10, "Goal: 3 open.", ["rekeningen"], rows);
    }

    public void Dispose() => _context.Dispose();

    private sealed class ConnectedProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(true, "Connected."));

        public void Invalidate()
        {
        }
    }

    private sealed class ProgressSwitch(bool on) : IAppFeatureSettings
    {
        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public bool IsEnabled(string key) => key == ProgressFeatures.Progress && on;

        public string? SetEnabled(string key, bool enabled) => null;
    }

    /// <summary>The two ports the sprint view reads, over fixed series: every sprint reads
    /// the same three members. Nothing else is asked of them here.</summary>
    private sealed class FakeProgress(IReadOnlyList<SprintSeriesDto> series) : ISprintSeriesPlanning, ISprintProgress
    {
        public int? LastNumber { get; private set; }

        public bool Fails { get; set; }

        public Task<Result<IReadOnlyList<SprintSeriesDto>>> ListSeriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(series));

        public Task<Result<SprintProgressDto>> ReadAsync(Guid seriesId, int? number = null, CancellationToken cancellationToken = default)
        {
            if (Fails) return Task.FromResult(Result.Failure<SprintProgressDto>(Error.Conflict("Progress.Off", "Progress is off.")));

            var found = series.Single(candidate => candidate.Id == seriesId);
            var sprint = number is { } asked ? found.Sprints.Single(candidate => candidate.Number == asked) : found.OpenSprint!;
            LastNumber = sprint.Number;

            var members = new[]
            {
                Member("Opdrachtbrief", SprintWorkState.Done, doneOn: sprint.Start),
                Member("Klantrekening", SprintWorkState.InProgress),
                Member("Openspec", SprintWorkState.Blocked, repository: "marketplace")
            };

            return Task.FromResult(Result.Success(new SprintProgressDto(
                found.Id, found.Name, sprint.Number, sprint.Name, sprint.Start, sprint.End, sprint.Goal, sprint.IsOpen,
                members, found.GoalRules, [.. members.Select(member => member.TaskId)],
                new SprintCountsDto(1, 1, 1, 0, 0), new SprintCountsDto(1, 1, 1, 0, 0), 100, false, 10, 3, 0.3)));
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
        public Task<Result<SprintSeriesDto>> CloseSprintAsync(Guid seriesId, IReadOnlyDictionary<Guid, CloseOutcome> outcomes, string nextGoal, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
