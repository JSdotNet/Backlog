using Backlog.Desktop.UI.Services;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.DomainModels;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A configured repository's own <c>.backlog</c> folder shows up in the one list
/// the person already works in, filed under that repository's area — but it is
/// somebody else's file, so the list may show it and must not write to it.
/// </summary>
[Collection(BacklogStoreCollection.Name)]
public sealed class RepositoryBacklogRowsTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task A_repository_backlog_file_joins_the_list_under_that_repositorys_area()
    {
        var harness = Build("# Roadmap planning\n\n```meta\nstatus: active\n```\n\n## Add the view\n");

        await harness.State.InitializeAsync();

        var row = Assert.Single(harness.State.Rows);
        Assert.Equal("docs", row.Area);
        Assert.Equal(EntryStatus.InProgress, row.Status);
        Assert.Equal(".backlog/plan.md", row.Origin!.RelativePath);
        Assert.Equal("JSdotNet/Backlog-docs", row.Origin.RepositoryFullName);
        Assert.Contains("Roadmap planning", row.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_area_and_status_are_what_the_filters_see()
    {
        var harness = Build("# Roadmap planning\n\n```meta\nstatus: done\n```\n");

        await harness.State.InitializeAsync();

        var row = Assert.Single(harness.State.Rows);
        Assert.Equal("docs", row.PreviewArea);
        Assert.Equal(EntryStatus.Done, row.PreviewStatus);
    }

    [Fact]
    public async Task Selecting_a_repository_scopes_repository_backlog_rows()
    {
        var harness = BuildRepositories(
            ("docs", "JSdotNet", "Backlog-docs", "# Docs roadmap\n\n```meta\nstatus: active\n```\n"),
            ("app", "JSdotNet", "Backlog", "# App roadmap\n\n```meta\nstatus: done\n```\n"));

        await harness.State.InitializeAsync();

        Assert.Equal(["docs", "app"], harness.State.Rows.Select(row => row.Area));

        harness.State.SetRepositoryFilter("app");

        var row = Assert.Single(harness.State.FilteredRows);
        Assert.Equal("app", row.Area);
        Assert.Contains("App roadmap", row.RawText, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.State.FilteredRows, row => row.Area == "docs");
    }

    [Fact]
    public async Task Repository_aliases_do_not_appear_as_area_filter_chips()
    {
        var harness = BuildRepositories(
            ("docs", "JSdotNet", "Backlog-docs", "# Docs roadmap\n"),
            ("app", "JSdotNet", "Backlog", "# App roadmap\n"));

        await harness.State.InitializeAsync();

        Assert.Equal(["All"], harness.State.AreaFilters.Select(filter => filter.Label));
    }

    [Fact]
    public async Task Repository_rows_are_editable_and_changes_persist_to_the_source_file()
    {
        var harness = Build("# Roadmap planning\n\n```meta\nstatus: active\n```\n\n## Add the view\n");
        await harness.State.InitializeAsync();
        var row = Assert.Single(harness.State.Rows);

        // Repository rows are now editable.
        harness.State.BeginEdit(row);
        Assert.NotNull(harness.State.EditingRow);

        // Changing status persists back to the file.
        await harness.State.ChangeStatusAsync(row, EntryStatus.Done);
        var fileContent = File.ReadAllText(harness.BacklogFile);
        Assert.Contains("done", fileContent, StringComparison.OrdinalIgnoreCase);

        Assert.Single(harness.State.Rows);
    }

    private Harness Build(string backlogMarkdown)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-repo-rows", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

        var store = new BacklogStore(root, Path.Combine(root, "settings.json"));
        Assert.Null(store.TryUseRoot(Path.Combine(root, "local")));

        var clone = Path.Combine(root, "clone");
        var backlogDirectory = Path.Combine(clone, ".backlog");
        Directory.CreateDirectory(backlogDirectory);
        var backlogFile = Path.Combine(backlogDirectory, "plan.md");
        File.WriteAllText(backlogFile, backlogMarkdown);

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        settings.SetRepositories([new GitHubRepositoryRef("docs", "JSdotNet", "Backlog-docs")]);
        settings.SetCloneDirectory("docs", clone);

        var integration = new GitHubIntegration(settings, new StubGitHubClient(), new StubProbe());
        var repositoryBacklog = new RepositoryBacklogSource(new KnowledgeFolderSource(settings));

        return new Harness(
            new BacklogDesktopState(store, integration, copilot: null, repositoryBacklog),
            backlogFile);
    }

    private Harness BuildRepositories(params (string Alias, string Owner, string Name, string Markdown)[] repositories)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-repo-rows", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

        var store = new BacklogStore(root, Path.Combine(root, "settings.json"));
        Assert.Null(store.TryUseRoot(Path.Combine(root, "local")));

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        settings.SetRepositories(repositories.Select(repo => new GitHubRepositoryRef(repo.Alias, repo.Owner, repo.Name)).ToArray());

        foreach (var repository in repositories)
        {
            var clone = Path.Combine(root, "clone-" + repository.Alias);
            var backlogDirectory = Path.Combine(clone, ".backlog");
            Directory.CreateDirectory(backlogDirectory);
            File.WriteAllText(Path.Combine(backlogDirectory, "plan.md"), repository.Markdown);
            settings.SetCloneDirectory(repository.Alias, clone);
        }

        var integration = new GitHubIntegration(settings, new StubGitHubClient(), new StubProbe());
        var repositoryBacklog = new RepositoryBacklogSource(new KnowledgeFolderSource(settings));

        return new Harness(
            new BacklogDesktopState(store, integration, copilot: null, repositoryBacklog),
            Path.Combine(root, "clone-" + repositories[0].Alias, ".backlog", "plan.md"));
    }

    private sealed record Harness(BacklogDesktopState State, string BacklogFile);

    private sealed class StubGitHubClient : IGitHubClient
    {
        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not configured."));

        public void Invalidate()
        {
        }
    }
}
