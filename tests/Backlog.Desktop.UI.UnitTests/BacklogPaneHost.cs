using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Renders <c>BacklogPane</c> on its own, with the four services it injects and
/// nothing else.
/// <para>
/// The pane rather than the shell, deliberately. <see cref="HomeInitialLoadTests"/>
/// renders Home because what it is about is the shell announcing a finished load;
/// a test about what the pane puts on screen gains nothing from the twenty other
/// services Home wants, and would fail for any of twenty reasons that have
/// nothing to do with the markup under test.
/// </para>
/// </summary>
internal sealed class BacklogPaneHost : IDisposable
{
    private readonly List<string> _tempDirs = [];

    private BacklogPaneHost(
        BunitContext context,
        BacklogDesktopState state,
        AppFeatureSettingsStore features,
        GitHubIntegration gitHub,
        FakeGitHubClient client,
        string root,
        string? repositoryBacklogFile)
    {
        Context = context;
        State = state;
        Features = features;
        GitHub = gitHub;
        Client = client;
        RepositoryBacklogFile = repositoryBacklogFile;
        _tempDirs.Add(root);
    }

    public BunitContext Context { get; }

    public BacklogDesktopState State { get; }

    public AppFeatureSettingsStore Features { get; }

    public GitHubIntegration GitHub { get; }

    public FakeGitHubClient Client { get; }

    /// <summary>The committed <c>.backlog</c> file behind a repository-origin row,
    /// or null when there is none. Held so a test can read what the writer actually
    /// put back: for these rows the file is the store.</summary>
    public string? RepositoryBacklogFile { get; }

    /// <summary>Composes the pane's world. <paramref name="repositories"/> is the
    /// settings text a person would have typed into Settings, so a test that wants
    /// GitHub configured says so in the same words the app does.</summary>
    public static Task<BacklogPaneHost> CreateAsync(params string[] repositories) =>
        CreateAsync(null, repositories);

    /// <summary>
    /// The same world with a committed <c>.backlog</c> file in a cloned
    /// repository, which is how a repository-origin row gets into the list.
    /// <para>
    /// Those rows are worth rendering because of what they cannot do rather than
    /// what they can: they were never written by the local store and so have no
    /// id, which is why no dependency control is offered on one.
    /// </para>
    /// </summary>
    public static Task<BacklogPaneHost> CreateWithRepositoryBacklogAsync(string markdown) =>
        CreateAsync(markdown, ["docs = JSdotNet/Backlog-docs"]);

    private static async Task<BacklogPaneHost> CreateAsync(string? repositoryBacklogMarkdown, string[] repositories)
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

        RepositoryBacklogSource? repositoryBacklog = null;
        string? repositoryBacklogFile = null;
        if (repositoryBacklogMarkdown is not null)
        {
            var clone = Path.Combine(root, "clone");
            Directory.CreateDirectory(Path.Combine(clone, ".backlog"));
            repositoryBacklogFile = Path.Combine(clone, ".backlog", "plan.md");
            File.WriteAllText(repositoryBacklogFile, repositoryBacklogMarkdown);
            gitHubSettings.SetCloneDirectory("docs", clone);

            repositoryBacklog = new RepositoryBacklogSource(
                BacklogTestHost.BacklogStoreFor(store, new KnowledgeFolderSource(gitHubSettings)));
        }

        var client = new FakeGitHubClient();
        var gitHub = new GitHubIntegration(gitHubSettings, client, new ConnectedProbe());
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features.json"));
        var state = BacklogTestHost.StateFor(store, gitHub, copilot: null, repositoryBacklog: repositoryBacklog);

        await state.InitializeAsync();

        var context = new BunitContext();

        // The pane reaches for JS to put the caret back on a card that was just
        // moved with the keyboard. Nothing under test here is about that, and a
        // strict runtime would fail the render rather than the assertion.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(state);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);

        return new BacklogPaneHost(context, state, features, gitHub, client, root, repositoryBacklogFile);
    }

    public IRenderedComponent<BacklogPane> Render() => Context.Render<BacklogPane>();

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

    public void Dispose()
    {
        Context.Dispose();

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
