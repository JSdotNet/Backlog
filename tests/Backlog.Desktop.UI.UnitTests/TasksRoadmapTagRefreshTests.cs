using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The plan's tags on <see cref="TasksDesktopState.RoadmapTags"/> follow the plan, not
/// only the task list. They used to be read only when the task list reloaded, so a
/// roadmap item planned in Roadmap Planning stayed out of the Goals view's attach
/// search — which reads them through <c>GoalsCandidateSource</c> — until a full reload
/// (QA of plan <c>goals-standup</c>, entry <c>472cde67</c>). The port announces a plan
/// change, and the state reads the tags again and says so.
/// </summary>
public sealed class TasksRoadmapTagRefreshTests : IDisposable
{
    private readonly List<string> _tempDirs = [];
    private readonly List<TasksDesktopState> _states = [];

    /// <summary>A plan whose tags the test moves, and whose change it announces.</summary>
    private sealed class MovableRoadmapTags(params string[] tags) : IRoadmapTagSource
    {
        public IReadOnlyList<string> Tags { get; set; } = tags;

        public event Action? Changed;

        public Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Tags);

        public void Raise() => Changed?.Invoke();
    }

    [Fact]
    public async Task A_roadmap_item_planned_after_the_list_loaded_is_in_the_roadmap_tags_without_a_reload()
    {
        var plan = new MovableRoadmapTags("+earlier");
        var state = State(plan);
        await state.InitializeAsync();
        Assert.Equal(["+earlier"], state.RoadmapTags);

        // Planned elsewhere: no task was written, so nothing reloads the list.
        plan.Tags = ["+earlier", "+qa472-stale"];
        plan.Raise();

        await TasksTestHost.UntilAsync(state, () => state.RoadmapTags.Contains("+qa472-stale"));
        Assert.Equal(["+earlier", "+qa472-stale"], state.RoadmapTags);
    }

    [Fact]
    public async Task A_disposed_state_no_longer_hears_the_plan()
    {
        var plan = new MovableRoadmapTags("+earlier");
        var state = State(plan);
        await state.InitializeAsync();

        state.Dispose();
        plan.Tags = ["+earlier", "+later"];
        plan.Raise();

        Assert.Equal(["+earlier"], state.RoadmapTags);
    }

    private TasksDesktopState State(IRoadmapTagSource roadmapTags)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-roadmap-tag-refresh", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var state = TasksTestHost.StateFor(
            store,
            new GitHubIntegration(settings, new StubGitHubClient(), new StubProbe()),
            roadmapTags: roadmapTags);
        _states.Add(state);
        return state;
    }

    public void Dispose()
    {
        foreach (var state in _states)
        {
            state.Dispose();
        }

        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            TestDatabases.Release(dir);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
