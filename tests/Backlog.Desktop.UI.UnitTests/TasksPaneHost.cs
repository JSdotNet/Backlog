using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Renders <c>TasksPane</c> on its own, with the four services it injects and
/// nothing else.
/// <para>
/// The pane rather than the shell, deliberately. <see cref="HomeInitialLoadTests"/>
/// renders Home because what it is about is the shell announcing a finished load;
/// a test about what the pane puts on screen gains nothing from the twenty other
/// services Home wants, and would fail for any of twenty reasons that have
/// nothing to do with the markup under test.
/// </para>
/// </summary>
internal sealed class TasksPaneHost : IDisposable
{
    private readonly List<string> _tempDirs = [];

    private TasksPaneHost(
        BunitContext context,
        TasksDesktopState state,
        AppFeatureSettingsStore features,
        GitHubIntegration gitHub,
        FakeGitHubClient client,
        string root)
    {
        Context = context;
        State = state;
        Features = features;
        GitHub = gitHub;
        Client = client;
        _tempDirs.Add(root);
    }

    public BunitContext Context { get; }

    public TasksDesktopState State { get; }

    public AppFeatureSettingsStore Features { get; }

    public GitHubIntegration GitHub { get; }

    public FakeGitHubClient Client { get; }

    /// <summary>Composes the pane's world. <paramref name="repositories"/> is the
    /// settings text a person would have typed into Settings, so a test that wants
    /// GitHub configured says so in the same words the app does.</summary>
    public static Task<TasksPaneHost> CreateAsync(params string[] repositories) =>
        CreateAsync(roadmapTags: null, repositories);

    /// <summary>As <see cref="CreateAsync(string[])"/>, and with the roadmap tag
    /// source the backlog picker offers planned tags from. A host with a roadmap
    /// registers one; a test that cares about planned tags hands one in here.</summary>
    public static async Task<TasksPaneHost> CreateAsync(
        IRoadmapTagSource? roadmapTags,
        string[] repositories)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-pane-host", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
        Assert.Null(store.TryUseRoot(Path.Combine(root, "local")));

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        if (repositories.Length > 0)
        {
            var (parsed, _) = GitHubSettings.ParseText(string.Join('\n', repositories));
            gitHubSettings.SetRepositories(parsed);
        }

        var client = new FakeGitHubClient();
        var gitHub = new GitHubIntegration(gitHubSettings, client, new ConnectedProbe());
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features.json"));
        var state = TasksTestHost.StateFor(store, gitHub, copilot: null, roadmapTags: roadmapTags);

        await state.InitializeAsync();

        var context = new BunitContext();

        // The pane reaches for JS to put the caret back on a card that was just
        // moved with the keyboard. Nothing under test here is about that, and a
        // strict runtime would fail the render rather than the assertion.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(state);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);

        return new TasksPaneHost(context, state, features, gitHub, client, root);
    }

    public IRenderedComponent<TasksPane> Render() => Context.Render<TasksPane>();

    /// <summary>Writes an entry the way the screen does — a new row, text typed
    /// into it, and the editor left — so the row that comes back is persisted and
    /// carries the id everything else keys off.</summary>
    public async Task<EntryRow> WriteEntryAsync(string text)
    {
        State.NewRow();
        var row = State.Rows[^1];
        State.OnRawTextInput(row, text);
        await State.EndEditAsync(row);
        return row;
    }

    /// <summary>Opens a row in the detail pane. Every control over one entry lives
    /// there now, so a test about one has to say which entry is open — the pane
    /// beside the list is the subject, not the row in it.</summary>
    public Task OpenAsync(EntryRow row) => State.SelectAsync(row);

    /// <summary>
    /// Everything this host composed, given back in the order the app gives it
    /// back: the rendered pane first, then the state behind it, then the folder
    /// both were reading.
    /// <para>
    /// The state's turn is the one that is easy to leave out and the one that
    /// matters most here. It arms a debounce per keystroke and a timed flash per
    /// save, and a test finishes in milliseconds — so a host that dropped it
    /// instead of disposing it left both running, writing into the folder the
    /// next lines delete and, in CI, still queued on the runner's threads once
    /// the assembly was done. See <c>TasksDesktopStateLifetimeTests</c>.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        Context.Dispose();
        State.Dispose();

        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>A GitHub that answers rather than reaches the network. What it
    /// created is recorded so a test can assert the push actually happened.</summary>
    internal sealed class FakeGitHubClient : IGitHubClient
    {
        public string? CreatedRepository { get; private set; }

        public string? CreatedTitle { get; private set; }

        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default)
        {
            CreatedRepository = repository.FullName;
            CreatedTitle = title;

            return Task.FromResult(new GitHubIssue(
                7,
                $"https://github.com/{repository.FullName}/issues/7",
                title,
                GitHubItemState.Open,
                DateTimeOffset.UtcNow));
        }

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubIssueSnapshot(
                new GitHubIssue(number, $"https://github.com/{repository.FullName}/issues/{number}", "An issue", GitHubItemState.Open, null),
                [],
                DateTimeOffset.UtcNow));

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubUploadedFile(path, $"https://github.com/{repository.FullName}/blob/{branch}/{path}"));
    }

    private sealed class ConnectedProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(true, "Connected."));

        public void Invalidate()
        {
        }
    }
}
