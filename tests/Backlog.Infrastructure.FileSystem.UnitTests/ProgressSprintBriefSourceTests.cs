using Backlog.Infrastructure.FileSystem.Dashboard;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The weekly brief's Sprint block reads Progress only through its published surface,
/// and only here: the series marked for the weekly brief, each sprint of theirs that
/// overlaps the window, members crossed into the dashboard's own shape. Off, absent or
/// unmarked, the answer is empty and the brief stays as it was.
/// </summary>
public class ProgressSprintBriefSourceTests
{
    private static readonly DateOnly From = new(2026, 10, 5);
    private static readonly DateOnly To = new(2026, 10, 11);

    [Fact]
    public async Task Reads_the_overlapping_sprints_of_series_shown_in_the_weekly_brief()
    {
        var shown = Series("Fincent team", weekly: true,
            Sprint(41, new DateOnly(2026, 9, 21), new DateOnly(2026, 10, 4)),
            Sprint(42, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 18)));
        var hidden = Series("Hidden", weekly: false, Sprint(1, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 18)));
        var progress = new FakeProgress([shown, hidden]);
        var goalId = Guid.NewGuid();
        progress.Read[(shown.Id, 42)] = Progress(shown, 42, [goalId],
            Member(goalId, "Overzicht", SprintWorkState.Done, "rekeningen", new DateOnly(2026, 10, 7),
                new LinkedPullRequestDto("innovadis-dev/Fincent", "4573", LinkedPullRequestState.Merged, new DateOnly(2026, 10, 7), "jo")),
            Member(Guid.NewGuid(), "Openspec", SprintWorkState.Blocked, null, null) with { AddedAfterStart = true },
            Member(Guid.NewGuid(), "Dropped", SprintWorkState.NotStarted, null, null) with { Outcome = CloseOutcome.Drop });

        var source = new ProgressSprintBriefSource(progress, progress, new Features(on: true));

        var read = await source.ReadAsync(From, To, TestContext.Current.CancellationToken);

        var sprint = Assert.Single(read);
        Assert.Equal("Fincent team", sprint.SeriesName);
        Assert.Equal(["Fincent", "devbook"], sprint.Repositories);
        Assert.Equal(42, sprint.Number);
        Assert.Equal("Sprint 42", sprint.Name);
        Assert.Equal([(shown.Id, 42)], progress.Asked);

        var done = sprint.Members[0];
        Assert.True(done.IsGoal);
        Assert.True(done.IsDone);
        Assert.Equal("rekeningen", done.PlanTag);
        Assert.Equal(new DateOnly(2026, 10, 7), done.DoneOn);
        Assert.Equal(
            new SprintBriefPullRequest("innovadis-dev/Fincent", "4573", SprintBriefPullRequestState.Merged, new DateOnly(2026, 10, 7)),
            Assert.Single(done.PullRequests));

        var blocked = sprint.Members[1];
        Assert.True(blocked.IsBlocked);
        Assert.False(blocked.IsGoal);
        Assert.True(blocked.AddedAfterStart);

        Assert.True(sprint.Members[2].Removed);
    }

    /// <summary>
    /// What the block's Copy as table needs crosses too, so the Dashboard builds the
    /// sprint view's table without naming a Progress type: each member's in-progress
    /// state, repository, owners, note and open question, and the plans an "in these
    /// plans" goal rule names, bare and once each.
    /// </summary>
    [Fact]
    public async Task Carries_what_the_sprint_table_needs()
    {
        var shown = Series("Fincent team", weekly: true, Sprint(42, From, new DateOnly(2026, 10, 18)))
            with { GoalRules = [GoalRuleDto.InPlans("+rekeningen", "specs", "Rekeningen"), GoalRuleDto.MarkedAsGoal()] };
        var progress = new FakeProgress([shown]);
        progress.Read[(shown.Id, 42)] = Progress(shown, 42, [],
            Member(Guid.NewGuid(), "Overzicht", SprintWorkState.InProgress, "rekeningen", null)
                with { Owners = ["jo"], Note = "Waits on design", OpenQuestion = true },
            Member(Guid.NewGuid(), "Kern", SprintWorkState.NotStarted, null, null));

        var source = new ProgressSprintBriefSource(progress, progress, new Features(on: true));

        var sprint = Assert.Single(await source.ReadAsync(From, To, TestContext.Current.CancellationToken));

        Assert.Equal(["rekeningen", "specs"], sprint.GoalPlans);

        var working = sprint.Members[0];
        Assert.True(working.IsInProgress);
        Assert.Equal("Fincent", working.Repository);
        Assert.Equal(["jo"], working.Owners);
        Assert.Equal("Waits on design", working.Note);
        Assert.True(working.OpenQuestion);

        var waiting = sprint.Members[1];
        Assert.False(waiting.IsInProgress);
        Assert.Empty(waiting.Owners);
        Assert.Null(waiting.Note);
        Assert.False(waiting.OpenQuestion);
    }

    [Fact]
    public async Task Leaves_out_a_series_that_is_switched_off()
    {
        var off = Series("Resting", weekly: true, Sprint(1, From, To)) with { IsOn = false };
        var progress = new FakeProgress([off]);

        var source = new ProgressSprintBriefSource(progress, progress, new Features(on: true));

        Assert.Empty(await source.ReadAsync(From, To, TestContext.Current.CancellationToken));
        Assert.Empty(progress.Asked);
    }

    [Fact]
    public async Task Reads_nothing_while_progress_is_off()
    {
        var series = Series("Team", weekly: true, Sprint(1, From, To));
        var progress = new FakeProgress([series]);

        var source = new ProgressSprintBriefSource(progress, progress, new Features(on: false));

        Assert.Empty(await source.ReadAsync(From, To, TestContext.Current.CancellationToken));
        Assert.False(progress.Listed);
    }

    [Fact]
    public async Task Answers_empty_when_progress_is_not_composed()
    {
        var source = new ProgressSprintBriefSource(null, null, null);

        Assert.Empty(await source.ReadAsync(From, To, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Leaves_out_a_sprint_that_cannot_be_read()
    {
        var series = Series("Team", weekly: true, Sprint(1, From, To));
        var progress = new FakeProgress([series]);

        var source = new ProgressSprintBriefSource(progress, progress, new Features(on: true));

        Assert.Empty(await source.ReadAsync(From, To, TestContext.Current.CancellationToken));
        Assert.Equal([(series.Id, 1)], progress.Asked);
    }

    private static SprintSeriesDto Series(string name, bool weekly, params SprintDto[] sprints) =>
        new(Guid.NewGuid(), name, ["Fincent", "devbook"], SprintLength.TwoWeeks, DayOfWeek.Monday, "Sprint {n}",
            false, weekly, [GoalRuleDto.MarkedAsGoal()], 100, sprints);

    private static SprintDto Sprint(int number, DateOnly start, DateOnly end) =>
        new(number, $"Sprint {number}", start, end, "Goal", true, [], []);

    private static SprintProgressDto Progress(SprintSeriesDto series, int number, IReadOnlyList<Guid> goal, params SprintMemberDto[] members)
    {
        var sprint = series.Sprints.Single(candidate => candidate.Number == number);
        var counts = new SprintCountsDto(0, 0, 0, 0, 0);

        return new SprintProgressDto(series.Id, series.Name, number, sprint.Name, sprint.Start, sprint.End, sprint.Goal,
            sprint.IsOpen, members, series.GoalRules, goal, counts, counts, 100, false, 10, 5, 0d);
    }

    private static SprintMemberDto Member(
        Guid id,
        string title,
        SprintWorkState state,
        string? plan,
        DateOnly? doneOn,
        params LinkedPullRequestDto[] pulls) =>
        new(id, title, SprintTaskStatus.InProgress, state, plan, "Fincent", doneOn, pulls, [], false, true, false, null, false, null);

    private sealed class FakeProgress(IReadOnlyList<SprintSeriesDto> series) : ISprintSeriesPlanning, ISprintProgress
    {
        public event Action? Changed { add { } remove { } }

        public Dictionary<(Guid, int), SprintProgressDto> Read { get; } = [];

        public List<(Guid, int)> Asked { get; } = [];

        public bool Listed { get; private set; }

        public Task<Result<IReadOnlyList<SprintSeriesDto>>> ListSeriesAsync(CancellationToken cancellationToken = default)
        {
            Listed = true;
            return Task.FromResult(Result.Success(series));
        }

        public Task<Result<SprintProgressDto>> ReadAsync(Guid seriesId, int? number = null, CancellationToken cancellationToken = default)
        {
            Asked.Add((seriesId, number ?? 0));

            return Task.FromResult(Read.TryGetValue((seriesId, number ?? 0), out var progress)
                ? Result.Success(progress)
                : Result.Failure<SprintProgressDto>(ProgressErrors.SprintNotFound(number ?? 0)));
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

        public Task<Result> DeleteSeriesAsync(Guid seriesId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Features(bool on) : IAppFeatureSettings
    {
        public event Action? Changed { add { } remove { } }

        public AppFeatureSettings Current { get; } = new();

        public string SettingsPath => "features.json";

        public bool IsEnabled(string key) =>
            key == ProgressFeatures.Progress ? on : throw new ArgumentException("Unknown feature " + key, nameof(key));

        public string? SetEnabled(string key, bool enabled) => null;
    }
}
