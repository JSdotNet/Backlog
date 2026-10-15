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

    /// <summary>
    /// Settings and back remounts the shell, which reads the backlog again over the
    /// rows already in memory. Those rows stay on screen, marked busy, rather than
    /// giving way to placeholders: a skeleton is for a list that has nothing yet
    /// (.devbook/design/interaction-guidelines.md#scoped-loading).
    /// </summary>
    [Fact]
    public async Task A_reload_over_a_populated_backlog_keeps_its_rows_and_marks_the_list_busy()
    {
        var inner = TasksTestHost.EntriesFor(Store());
        await inner.SaveFromTextAsync(null, "# Kept on screen\n`task`\n", 0, cancellationToken: TestContext.Current.CancellationToken);
        var entries = new HeldTaskItems(inner);
        using var context = TasksContext(entries, out var state);
        entries.Release();
        await state.InitializeAsync();
        Assert.NotEmpty(state.Rows);

        entries.Hold();
        var reloading = state.InitializeAsync();
        var remounted = context.Render<TasksPane>();

        Assert.Empty(remounted.FindAll("[data-testid='backlog-loading']"));
        // The rows are what reload, so the busy mark is on the scroller that holds
        // them and not on the filter bar above; the bar carries the spinner that
        // says so (held invisible by CSS for its first 300 ms or so).
        Assert.Null(remounted.Find(".backlog-list").GetAttribute("aria-busy"));
        var rows = remounted.Find("[data-testid='backlog-list-scroll']");
        Assert.Contains("Kept on screen", rows.TextContent, StringComparison.Ordinal);
        Assert.Equal("true", rows.GetAttribute("aria-busy"));
        var spinner = remounted.Find(".filter-bar [data-testid='backlog-refreshing']");
        Assert.Contains("region-refresh", spinner.ClassList);

        entries.Release();
        await reloading;

        remounted.WaitForAssertion(() =>
        {
            Assert.Null(remounted.Find("[data-testid='backlog-list-scroll']").GetAttribute("aria-busy"));
            Assert.Empty(remounted.FindAll("[data-testid='backlog-refreshing']"));
            Assert.Contains("Kept on screen", remounted.Find(".backlog-list").TextContent, StringComparison.Ordinal);
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

    /// <summary>Shown again, the Inbox reads its snapshot again behind the rows
    /// from last time: they stay, and the list says it is busy until the new
    /// snapshot lands.</summary>
    [Fact]
    public async Task A_reopened_inbox_keeps_its_rows_and_is_busy_until_the_reload_lands()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new GitHubSettingsStore(Path.Combine(Root(), "github.json")));
        TasksTestHost.AddToastChannel(context.Services);
        var inbox = InboxTestHost.AddInboxState(context.Services);
        var item = inbox.Seed("Captured last time");
        var state = context.Services.GetRequiredService<InboxDesktopState>();
        var pane = context.Render<InboxPane>();
        await pane.InvokeAsync(state.InitializeAsync);
        pane.WaitForAssertion(() =>
            Assert.Null(pane.Find("[data-testid='inbox-pane-list']").GetAttribute("aria-busy")));

        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        inbox.BeforeSnapshot = () => held.Task;
        var reopening = pane.InvokeAsync(state.OpenedAsync);

        pane.WaitForAssertion(() =>
        {
            // The rows are marked busy, not the column with its pills; the toolbar
            // carries the spinner.
            Assert.Null(pane.Find("[data-testid='inbox-pane-column-list']").GetAttribute("aria-busy"));
            Assert.Equal("true", pane.Find("[data-testid='inbox-pane-list']").GetAttribute("aria-busy"));
            Assert.Contains("region-refresh", pane.Find(".inbox-pane__toolbar [data-testid='inbox-refreshing']").ClassList);
            Assert.NotEmpty(pane.FindAll($"[data-testid='inbox-item-{item.Id:D}']"));
            Assert.Empty(pane.FindAll("[data-testid='inbox-loading']"));
        });

        held.SetResult();
        await reopening;

        pane.WaitForAssertion(() =>
        {
            Assert.Null(pane.Find("[data-testid='inbox-pane-list']").GetAttribute("aria-busy"));
            Assert.Empty(pane.FindAll("[data-testid='inbox-refreshing']"));
        });
        Assert.False(state.Reopening);
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
            TestDatabases.Release(root);
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>The module, with the first thing the load asks of it held until
    /// <see cref="Release"/>. Everything else delegates.</summary>
    private sealed class HeldTaskItems(ITaskItems inner) : ITaskItems
    {
        private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        /// <summary>Holds the next load as the first one was held.</summary>
        public void Hold() => _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        public Task<Result<TaskItemDto>> UnlinkFromIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            inner.UnlinkFromIssueAsync(id, repoId, externalId, targetType, cancellationToken);

        public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(Guid id, IReadOnlyList<string> references, CancellationToken cancellationToken = default) =>
            inner.SetDevbookReferencesAsync(id, references, cancellationToken);

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
