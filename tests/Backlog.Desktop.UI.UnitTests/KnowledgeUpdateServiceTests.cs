using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The service's whole job is translation: git's seven ways of not being on the
/// latest version become the four things a reader can do something about, and a
/// successful pull becomes news the panels hear. Both halves are asserted here,
/// because the pane trusts them and cannot check them.
/// </summary>
public sealed class KnowledgeUpdateServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-knowledge-update-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void Knowledge_kept_in_the_storage_folder_has_no_latest_version_to_be_on()
    {
        var harness = CreateHarness();

        Assert.False(harness.Service.CanCheck(null));
        Assert.Null(harness.Service.Repository(null));
    }

    [Fact]
    public void A_repository_nobody_has_cloned_yet_cannot_be_checked()
    {
        var harness = CreateHarness(cloneDirectory: null);

        Assert.False(harness.Service.CanCheck(harness.Alias));
    }

    [Fact]
    public void An_alias_that_matches_no_configured_repository_cannot_be_checked()
    {
        var harness = CreateHarness();

        Assert.False(harness.Service.CanCheck("not-a-repository"));
    }

    [Fact]
    public async Task Checking_a_scope_with_no_clone_behind_it_asks_git_nothing()
    {
        var harness = CreateHarness();

        var state = await harness.Service.CheckAsync(null);

        Assert.Equal(KnowledgeUpdateAvailability.NotApplicable, state.Availability);
        Assert.Equal(0, harness.Git.ChecksRequested);
    }

    [Fact]
    public async Task A_clone_with_everything_the_remote_has_reports_up_to_date_and_offers_no_pull()
    {
        var harness = CreateHarness();
        harness.Git.NextCheck = new LocalGitRepositoryUpdateCheck(
            LocalGitRepositoryCurrency.UpToDate, 0, 0, false, "origin/main", "On the latest version of origin/main.");

        var state = await harness.Service.CheckAsync(harness.Alias);

        Assert.Equal(KnowledgeUpdateAvailability.UpToDate, state.Availability);
        Assert.False(state.CanPull);
        Assert.Equal("On the latest version of origin/main.", state.Message);
    }

    [Fact]
    public async Task A_clone_the_remote_has_moved_past_offers_a_pull_and_says_how_far_behind()
    {
        var harness = CreateHarness();
        harness.Git.NextCheck = new LocalGitRepositoryUpdateCheck(
            LocalGitRepositoryCurrency.Behind, 0, 3, false, "origin/main", "3 commits behind origin/main.");

        var state = await harness.Service.CheckAsync(harness.Alias);

        Assert.Equal(KnowledgeUpdateAvailability.UpdateAvailable, state.Availability);
        Assert.True(state.CanPull);
        Assert.Equal(3, state.BehindBy);
        Assert.Equal("3 behind", KnowledgeUpdatePresentation.BehindLabel(state));
        Assert.Equal("Pull latest", KnowledgeUpdatePresentation.ActionLabel(state));
    }

    /// <summary>
    /// Four git states, one screen state. Each still carries git's own sentence,
    /// because the reason is the useful half and this service has no better way of
    /// saying it than the tool that refused.
    /// </summary>
    [Theory]
    [InlineData(LocalGitRepositoryCurrency.Ahead, 0, false)]
    [InlineData(LocalGitRepositoryCurrency.Diverged, 2, false)]
    [InlineData(LocalGitRepositoryCurrency.NoUpstream, 0, false)]
    [InlineData(LocalGitRepositoryCurrency.Detached, 0, false)]
    [InlineData(LocalGitRepositoryCurrency.Unknown, 0, false)]
    [InlineData(LocalGitRepositoryCurrency.Behind, 4, true)]
    public async Task Everything_a_pull_cannot_fix_is_reported_as_blocked_with_gits_own_reason(
        LocalGitRepositoryCurrency currency,
        int behind,
        bool hasLocalChanges)
    {
        var harness = CreateHarness();
        harness.Git.NextCheck = new LocalGitRepositoryUpdateCheck(
            currency, 0, behind, hasLocalChanges, "origin/main", "the reason git gave");

        var state = await harness.Service.CheckAsync(harness.Alias);

        Assert.Equal(KnowledgeUpdateAvailability.Blocked, state.Availability);
        Assert.False(state.CanPull);
        Assert.Equal("the reason git gave", state.Message);
        Assert.Null(KnowledgeUpdatePresentation.BehindLabel(state));
        Assert.Equal("Check now", KnowledgeUpdatePresentation.ActionLabel(state));
    }

    [Fact]
    public async Task A_successful_pull_tells_the_folder_source_its_content_was_replaced()
    {
        var harness = CreateHarness();
        harness.Git.NextPull = LocalGitRepositoryPullResult.Succeeded(
            "Pulled the latest JSdotNet/Backlog into the clone.",
            new LocalGitRepositoryUpdateCheck(LocalGitRepositoryCurrency.UpToDate, 0, 0, false, "origin/main", "On the latest version of origin/main."));

        var state = await harness.Service.PullAsync(harness.Alias);

        Assert.Equal(1, harness.Folders.ContentChangedAnnouncements);
        Assert.Equal(KnowledgeUpdateAvailability.UpToDate, state.Availability);
        Assert.Equal("Pulled the latest JSdotNet/Backlog into the clone.", state.Message);
    }

    [Fact]
    public async Task A_refused_pull_leaves_the_panels_alone_and_carries_the_refusal_up()
    {
        var harness = CreateHarness();
        harness.Git.NextPull = LocalGitRepositoryPullResult.Failed("There are local changes in the clone.");

        var state = await harness.Service.PullAsync(harness.Alias);

        Assert.Equal(0, harness.Folders.ContentChangedAnnouncements);
        Assert.Equal(KnowledgeUpdateAvailability.Blocked, state.Availability);
        Assert.Equal("There are local changes in the clone.", state.Message);
    }

    [Fact]
    public async Task Pulling_a_scope_with_no_clone_behind_it_asks_git_nothing_and_announces_nothing()
    {
        var harness = CreateHarness();

        var state = await harness.Service.PullAsync(null);

        Assert.Equal(KnowledgeUpdateAvailability.NotApplicable, state.Availability);
        Assert.Equal(0, harness.Git.PullsRequested);
        Assert.Equal(0, harness.Folders.ContentChangedAnnouncements);
    }

    [Fact]
    public async Task The_check_is_asked_about_the_clone_the_knowledge_is_actually_read_from()
    {
        var harness = CreateHarness();

        await harness.Service.CheckAsync(harness.Alias);

        Assert.Equal(harness.CloneDirectory, harness.Git.LastCloneDirectory);
        Assert.Equal("JSdotNet/Backlog", harness.Git.LastRepository?.FullName);
    }

    [Fact]
    public void The_busy_word_follows_which_of_the_two_things_the_button_is_doing()
    {
        Assert.Equal("Checking", KnowledgeUpdatePresentation.BusyLabel(KnowledgeUpdateState.NotChecked));
        Assert.Equal("Pulling", KnowledgeUpdatePresentation.BusyLabel(
            new KnowledgeUpdateState(KnowledgeUpdateAvailability.UpdateAvailable, 1, null)));
    }

    [Fact]
    public void One_commit_behind_still_reads_as_a_count_rather_than_as_a_bare_number()
    {
        var state = new KnowledgeUpdateState(KnowledgeUpdateAvailability.UpdateAvailable, 1, null);

        Assert.Equal("1 behind", KnowledgeUpdatePresentation.BehindLabel(state));
    }

    /// <summary>
    /// Three states, three colours. Asserted because the class is the only thing
    /// that tells "on the latest version" apart from "git refused" for somebody
    /// scanning the line rather than reading it.
    /// </summary>
    [Fact]
    public void The_status_line_wears_a_different_modifier_for_each_state_worth_distinguishing()
    {
        var classes = new[]
        {
            KnowledgeUpdateAvailability.NotChecked,
            KnowledgeUpdateAvailability.UpToDate,
            KnowledgeUpdateAvailability.UpdateAvailable,
            KnowledgeUpdateAvailability.Blocked
        }.Select(availability => KnowledgeUpdatePresentation.StatusClass(new KnowledgeUpdateState(availability, 0, null))).ToArray();

        Assert.All(classes, css => Assert.StartsWith("knowledge-stack__update-status", css, StringComparison.Ordinal));
        Assert.Equal(classes.Length, classes.Distinct(StringComparer.Ordinal).Count());
    }

    private Harness CreateHarness(string? cloneDirectory = "clone")
    {
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(_root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var resolvedClone = cloneDirectory is null ? null : Path.Combine(_root, cloneDirectory);
        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = resolvedClone,
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([repository]));

        var git = new ScriptedGitRepositoryService();
        var folders = new AnnouncementCountingFolderSource();

        return new Harness(
            new KnowledgeUpdateService(gitHubSettings, git, folders),
            git,
            folders,
            repository.Alias,
            resolvedClone);
    }

    private sealed record Harness(
        KnowledgeUpdateService Service,
        ScriptedGitRepositoryService Git,
        AnnouncementCountingFolderSource Folders,
        string Alias,
        string? CloneDirectory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>
/// A git adapter that answers what a test told it to and records what it was
/// asked. The real one is exercised against real repositories in
/// <c>LocalGitRepositoryUpdateTests</c>; what is under test here is the
/// translation on top of it, which needs the answers held still.
/// </summary>
internal sealed class ScriptedGitRepositoryService : ILocalGitRepositoryService
{
    internal LocalGitRepositoryUpdateCheck NextCheck { get; set; } =
        new(LocalGitRepositoryCurrency.UpToDate, 0, 0, false, "origin/main", "On the latest version of origin/main.");

    internal LocalGitRepositoryPullResult NextPull { get; set; } =
        LocalGitRepositoryPullResult.Succeeded("Pulled.", new LocalGitRepositoryUpdateCheck(
            LocalGitRepositoryCurrency.UpToDate, 0, 0, false, "origin/main", "On the latest version of origin/main."));

    internal int ChecksRequested { get; private set; }

    internal int PullsRequested { get; private set; }

    internal GitHubRepositoryRef? LastRepository { get; private set; }

    internal string? LastCloneDirectory { get; private set; }

    /// <summary>What the pull does to the folder on disk, for a test that wants
    /// to see whether the pane re-read it. A real pull replaces files; nothing
    /// else about this fake can.</summary>
    internal Action? OnPull { get; set; }

    public LocalGitRepositoryStatus GetStatus(GitHubRepositoryRef repository, string? cloneDirectory) =>
        new(cloneDirectory, IsCloned: true, CanClone: false, Summary: "Local clone is ready.");

    public Task<LocalGitRepositoryCloneResult> CloneAsync(GitHubRepositoryRef repository, string? cloneDirectory, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("These tests never clone.");

    public Task<LocalGitRepositoryUpdateCheck> CheckForUpdatesAsync(GitHubRepositoryRef repository, string? cloneDirectory, CancellationToken cancellationToken = default)
    {
        ChecksRequested++;
        LastRepository = repository;
        LastCloneDirectory = cloneDirectory;
        return Task.FromResult(NextCheck);
    }

    public Task<LocalGitRepositoryPullResult> PullAsync(GitHubRepositoryRef repository, string? cloneDirectory, CancellationToken cancellationToken = default)
    {
        PullsRequested++;
        LastRepository = repository;
        LastCloneDirectory = cloneDirectory;
        if (NextPull.Success) OnPull?.Invoke();
        return Task.FromResult(NextPull);
    }
}

/// <summary>
/// A folder source that resolves nothing and counts the one thing this test cares
/// about: whether the panels were told to re-read.
/// </summary>
internal sealed class AnnouncementCountingFolderSource : IKnowledgeFolderSource
{
    public event Action? Changed;

    internal int ContentChangedAnnouncements { get; private set; }

    public string StorageDirectory => Path.GetTempPath();

    public IReadOnlyList<KnowledgeFolderSetting> Folders(string? repositoryAlias) => KnowledgeFolderSetting.Defaults();

    public KnowledgeFolderLocation Resolve(string key, string? repositoryAlias = null) =>
        KnowledgeFolderLocation.Unavailable(key, "Nothing is configured in this fixture.");

    public void NotifyContentChanged()
    {
        ContentChangedAnnouncements++;
        Changed?.Invoke();
    }
}
