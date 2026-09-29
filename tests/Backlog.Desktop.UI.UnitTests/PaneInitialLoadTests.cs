using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Tasks pane and the desktop Inbox while their first load is out.
/// <para>
/// Both are started by the shell rather than by the pane, and both used to draw
/// nothing to say so: the Tasks list sat on "Nothing here yet." over a backlog
/// that was still being read, and the Inbox list was simply absent. A list on its
/// way is drawn in the shape of one (.devbook/design/interaction-guidelines.md,
/// Loading States), and an empty state is only ever the answer, never the wait.
/// </para>
/// </summary>
public sealed class PaneInitialLoadTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task The_tasks_list_is_a_skeleton_while_the_backlog_is_read_rather_than_an_empty_state()
    {
        var entries = new HeldTaskItems(TasksTestHost.EntriesFor(Store()));
        using var context = TasksContext(entries, out var state);

        var loading = state.InitializeAsync();
        var pane = context.Render<TasksPane>();

        var placeholder = pane.Find("[data-testid='backlog-loading']");
        Assert.NotEmpty(placeholder.QuerySelectorAll(".skeleton"));
        Assert.Equal("true", placeholder.GetAttribute("aria-hidden"));
        Assert.Empty(pane.FindAll("[data-testid='empty-state']"));

        entries.Release();
        await loading;

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='backlog-loading']"));
            Assert.NotEmpty(pane.FindAll("[data-testid='empty-state']"));
        });
    }

    [Fact]
    public void A_tasks_pane_nobody_is_loading_is_not_a_skeleton()
    {
        using var context = TasksContext(TasksTestHost.EntriesFor(Store()), out _);

        var pane = context.Render<TasksPane>();

        Assert.Empty(pane.FindAll("[data-testid='backlog-loading']"));
    }

    [Fact]
    public async Task The_inbox_list_is_a_skeleton_until_the_snapshot_has_arrived()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new GitHubSettingsStore(Path.Combine(Root(), "github.json")));
        TasksTestHost.AddToastChannel(context.Services);
        var inbox = InboxTestHost.AddInboxState(context.Services);
        var item = inbox.Seed("Captured before the pane opened");
        var state = context.Services.GetRequiredService<InboxDesktopState>();

        var pane = context.Render<InboxPane>();

        var placeholder = pane.Find("[data-testid='inbox-loading']");
        Assert.NotEmpty(placeholder.QuerySelectorAll(".skeleton"));
        Assert.Equal("true", placeholder.GetAttribute("aria-hidden"));
        Assert.Empty(pane.FindAll($"[data-testid='inbox-item-{item.Id:D}']"));

        await pane.InvokeAsync(state.InitializeAsync);

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='inbox-loading']"));
            Assert.NotEmpty(pane.FindAll($"[data-testid='inbox-item-{item.Id:D}']"));
        });
    }

    private BunitContext TasksContext(ITaskItems entries, out TasksDesktopState state)
    {
        var root = Root();
        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
        Assert.Null(store.TryUseRoot(Path.Combine(root, "local")));

        var gitHub = new GitHubIntegration(
            new GitHubSettingsStore(Path.Combine(root, "github.json")),
            new TasksPaneHost.FakeGitHubClient(),
            new UnconnectedProbe());
        state = new TasksDesktopState(TasksTestHost.TaskStoreFor(store), entries, gitHub);

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(state);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(
            new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features.json")));
        TasksTestHost.AddToastChannel(context.Services);
        return context;
    }

    private WorkspaceSettingsStore Store()
    {
        var root = Root();
        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
        Assert.Null(store.TryUseRoot(Path.Combine(root, "local")));
        return store;
    }

    private string Root()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-pane-initial-load", Guid.NewGuid().ToString("n"));
        _roots.Add(root);
        return root;
    }

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The module, with the first thing the load asks of it held until
    /// <see cref="Release"/>. Everything else delegates.</summary>
    private sealed class HeldTaskItems(ITaskItems inner) : ITaskItems
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public async Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default)
        {
            await _release.Task;
            return await inner.ReconcileRepositoryIdsAsync(cancellationToken);
        }

        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            inner.ListAsync(cancellationToken);

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default) =>
            inner.SaveFromTextAsync(id, rawText, order, sourceInboxId, cancellationToken);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(id, cancellationToken);

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) =>
            inner.ReorderAsync(idsInOrder, cancellationToken);

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            inner.LinkToIssueAsync(id, repoId, externalId, targetType, cancellationToken);

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) =>
            inner.RecordUsageAsync(id, action, cancellationToken);

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) =>
            inner.RenameRepositoryAsync(oldId, newId, cancellationToken);

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(
            string rawText,
            string? defaultRepo = null,
            IReadOnlyDictionary<string, string>? repoMatches = null,
            string? sourceInboxId = null,
            IReadOnlyDictionary<string, string>? sourceInboxIds = null,
            bool layOutOnRoadmap = false,
            CancellationToken cancellationToken = default) =>
            inner.ImportPlanAsync(rawText, defaultRepo, repoMatches, sourceInboxId, sourceInboxIds, layOutOnRoadmap, cancellationToken);
    }

    private sealed class UnconnectedProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not configured."));

        public void Invalidate()
        {
        }
    }
}

/// <summary>
/// The roadmap while its plan is being read: a whole-surface load with nothing to
/// lay out until it arrives, so the library's spinner — and not the "No roadmap
/// yet" empty state, which is an answer the band does not have yet.
/// </summary>
public sealed class RoadmapBandLoadingTests : RoadmapBandHarness
{
    [Fact]
    public void The_band_is_a_spinner_while_its_reads_are_out_rather_than_an_empty_roadmap()
    {
        var imported = new HeldImportedPlans();
        using var context = Context();
        context.Services.AddSingleton<IImportedPlanSource>(imported);

        var band = context.Render<RoadmapBand>();

        var spinner = band.Find("[data-testid='roadmap-band-loading']");
        Assert.Contains("spinner", spinner.ClassList);
        Assert.Equal("status", spinner.GetAttribute("role"));
        Assert.Empty(band.FindAll("[data-testid='roadmap-band-empty-state']"));

        imported.Release();

        band.WaitForAssertion(() =>
        {
            Assert.Empty(band.FindAll("[data-testid='roadmap-band-loading']"));
            Assert.NotEmpty(band.FindAll("[data-testid='roadmap-band-empty-state']"));
        });
    }

    private sealed class HeldImportedPlans : IImportedPlanSource
    {
        private readonly TaskCompletionSource<IReadOnlyList<ImportedPlanDto>> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _answer.TrySetResult([]);

        public Task<IReadOnlyList<ImportedPlanDto>> ListAsync(CancellationToken cancellationToken = default) => _answer.Task;
    }
}
