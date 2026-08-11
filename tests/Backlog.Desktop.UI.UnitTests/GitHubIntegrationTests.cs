using System.Text.Json;
using Backlog.Modules.Backlog;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Infrastructure.GitHub;
using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What a person types into Settings, and what GitHub answers with, are the two
/// places this integration can be wrong in a way nobody notices — so both are
/// tested directly rather than only through the screen that calls them.
/// </summary>
public sealed class GitHubSettingsTests
{
    [Theory]
    [InlineData("JSdotNet/Backlog", "backlog", "JSdotNet", "Backlog")]
    [InlineData("  JSdotNet/Backlog  ", "backlog", "JSdotNet", "Backlog")]
    [InlineData("https://github.com/JSdotNet/Backlog", "backlog", "JSdotNet", "Backlog")]
    [InlineData("https://github.com/JSdotNet/Backlog.git", "backlog", "JSdotNet", "Backlog")]
    [InlineData("docs = JSdotNet/Backlog-docs", "docs", "JSdotNet", "Backlog-docs")]
    [InlineData("Docs=JSdotNet/Backlog-docs", "docs", "JSdotNet", "Backlog-docs")]
    public void Reads_a_configured_repository(string line, string alias, string owner, string name)
    {
        var parsed = GitHubRepositoryRef.TryParse(line, out var error);

        Assert.Null(error);
        Assert.NotNull(parsed);
        Assert.Equal(alias, parsed!.Alias);
        Assert.Equal(owner, parsed.Owner);
        Assert.Equal(name, parsed.Name);
    }

    [Fact]
    public void An_unparseable_line_is_reported_not_thrown_on()
    {
        var parsed = GitHubRepositoryRef.TryParse("just-a-word", out var error);

        Assert.Null(parsed);
        Assert.NotNull(error);
    }

    [Fact]
    public void Blank_and_commented_lines_are_neither_repositories_nor_errors()
    {
        Assert.Null(GitHubRepositoryRef.TryParse("   ", out var blank));
        Assert.Null(blank);

        Assert.Null(GitHubRepositoryRef.TryParse("# a note to self", out var comment));
        Assert.Null(comment);
    }

    [Fact]
    public void A_bad_line_does_not_take_the_good_ones_with_it()
    {
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog\nnonsense\ndocs = JSdotNet/Backlog-docs");

        Assert.Equal(["backlog", "docs"], repositories.Select(r => r.Alias));
        Assert.Single(errors);
    }

    [Fact]
    public void An_alias_cannot_point_at_two_repositories()
    {
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog\nbacklog = someone/else");

        Assert.Single(repositories);
        Assert.Single(errors);
    }

    [Fact]
    public void Round_trips_through_the_settings_text()
    {
        var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog\ndocs = JSdotNet/Backlog-docs");
        var settings = new GitHubSettings { Repositories = repositories };

        Assert.Equal("JSdotNet/Backlog\ndocs = JSdotNet/Backlog-docs", settings.ToText());
    }

    [Fact]
    public void An_area_finds_its_repository_however_it_was_typed()
    {
        var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog\ndocs = JSdotNet/Backlog-docs");
        var settings = new GitHubSettings { Repositories = repositories };

        Assert.NotNull(settings.Find("backlog"));
        Assert.NotNull(settings.Find("Backlog"));
        Assert.NotNull(settings.Find("JSdotNet/Backlog"));
        Assert.Equal("JSdotNet/Backlog-docs", settings.Find("docs")!.FullName);
    }

    [Fact]
    public void The_primary_repository_owns_unassigned_work()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-github-tests", Guid.NewGuid().ToString("n"), "github.json");

        try
        {
            var store = new GitHubSettingsStore(path);
            var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog\ndocs = JSdotNet/Backlog-docs");
            store.SetRepositories(repositories);
            store.SetPrimaryRepository("docs");

            Assert.Equal("JSdotNet/Backlog-docs", store.Current.Find(null)!.FullName);
            Assert.Equal("JSdotNet/Backlog-docs", store.Current.Find("something-else")!.FullName);
            Assert.Equal("JSdotNet/Backlog", store.Current.Find("backlog")!.FullName);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void A_repository_token_survives_a_restart_and_can_be_forgotten()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-github-tests", Guid.NewGuid().ToString("n"), "github.json");

        try
        {
            var store = new GitHubSettingsStore(path);
            var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
            store.SetRepositories(repositories);
            store.SetToken("ghp_example");

            var reopened = new GitHubSettingsStore(path);
            var repository = Assert.Single(reopened.Current.Repositories);
            Assert.Equal("backlog", repository.Alias);
            Assert.Equal("ghp_example", repository.Token);
            Assert.Equal("ghp_example", reopened.Current.TokenForPath("repos/JSdotNet/Backlog/issues"));
            Assert.True(repository.IsPrimary);

            reopened.SetRepositoryToken("backlog", null);
            Assert.Null(new GitHubSettingsStore(path).Current.Repositories[0].Token);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Repository_local_settings_survive_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-github-tests", Guid.NewGuid().ToString("n"), "github.json");

        try
        {
            var store = new GitHubSettingsStore(path);
            var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog\ndocs = JSdotNet/Backlog-docs");
            store.SetRepositories(repositories);
            store.SetPrimaryRepository("docs");
            store.SetCloneDirectory("docs", @"D:\Repos\Backlog-docs");
            store.SetKnowledgeFolder("docs", ".tech", enabled: false, path: null);
            store.SetKnowledgeFolder("docs", ".domain", enabled: true, path: @"knowledge\domain");
            store.SetKnowledgeFolder("docs", "instructions", enabled: false, path: @"custom\instructions");

            var reopened = new GitHubSettingsStore(path);
            var docs = reopened.Current.Find("docs")!;

            Assert.True(docs.IsPrimary);
            Assert.Equal(@"D:\Repos\Backlog-docs", docs.CloneDirectory);
            Assert.False(docs.KnowledgeFolders.Single(f => f.Key == ".tech").Enabled);
            Assert.Equal(@"knowledge\domain", docs.KnowledgeFolders.Single(f => f.Key == ".domain").Path);
            Assert.Equal(".arc42", docs.KnowledgeFolders.Single(f => f.Key == ".arc42").EffectivePath);
            var instructions = docs.KnowledgeFolders.Single(f => f.Key == "instructions");
            Assert.False(instructions.Enabled);
            Assert.Null(instructions.Path);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void A_corrupt_settings_file_never_stops_the_app_from_opening()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-github-tests", Guid.NewGuid().ToString("n"), "github.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        try
        {
            var store = new GitHubSettingsStore(path);
            Assert.Empty(store.Current.Repositories);
            Assert.Null(store.Current.Token);
            Assert.False(store.Current.HasRepositoryToken);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch (IOException) { }
        }
    }
}

/// <summary>
/// The shapes GitHub answers with, mapped to what the list shows. Fixtures are
/// trimmed copies of real REST payloads.
/// </summary>
public sealed class GitHubClientMappingTests
{
    [Fact]
    public void An_open_issue_reads_as_open()
    {
        var issue = GitHubClient.ReadIssue(Json("""
            { "number": 42, "state": "open", "title": "Add GitHub support",
              "html_url": "https://github.com/JSdotNet/Backlog/issues/42",
              "updated_at": "2026-08-08T10:00:00Z" }
            """));

        Assert.Equal(42, issue.Number);
        Assert.Equal(GitHubItemState.Open, issue.State);
        Assert.Equal("https://github.com/JSdotNet/Backlog/issues/42", issue.Url);
        Assert.NotNull(issue.UpdatedAt);
    }

    [Fact]
    public void An_issue_without_a_number_is_not_an_issue()
    {
        Assert.Throws<GitHubException>(() => GitHubClient.ReadIssue(Json("""{ "state": "open" }""")));
    }

    [Fact]
    public void A_merged_pull_request_is_not_merely_closed()
    {
        var pulls = GitHubClient.ReadLinkedPullRequests(Json("""
            [
              { "event": "cross-referenced", "source": { "issue": {
                  "number": 7, "state": "closed", "title": "Add GitHub support",
                  "html_url": "https://github.com/JSdotNet/Backlog/pull/7",
                  "repository": { "full_name": "JSdotNet/Backlog" },
                  "pull_request": { "merged_at": "2026-08-08T12:00:00Z" } } } }
            ]
            """));

        var pull = Assert.Single(pulls);
        Assert.Equal(7, pull.Number);
        Assert.Equal(GitHubItemState.Merged, pull.State);
        Assert.Equal("JSdotNet/Backlog", pull.RepositoryFullName);
    }

    [Fact]
    public void A_comment_is_not_a_pull_request()
    {
        var pulls = GitHubClient.ReadLinkedPullRequests(Json("""
            [
              { "event": "commented", "body": "looks good" },
              { "event": "cross-referenced", "source": { "issue": {
                  "number": 9, "state": "open", "title": "Unrelated issue",
                  "html_url": "https://github.com/JSdotNet/Backlog/issues/9" } } }
            ]
            """));

        Assert.Empty(pulls);
    }

    [Fact]
    public void The_same_pull_request_referenced_twice_is_listed_once_at_its_latest_state()
    {
        var pulls = GitHubClient.ReadLinkedPullRequests(Json("""
            [
              { "event": "cross-referenced", "source": { "issue": {
                  "number": 7, "state": "open", "html_url": "https://github.com/JSdotNet/Backlog/pull/7",
                  "repository": { "full_name": "JSdotNet/Backlog" },
                  "pull_request": { "merged_at": null } } } },
              { "event": "cross-referenced", "source": { "issue": {
                  "number": 7, "state": "closed", "html_url": "https://github.com/JSdotNet/Backlog/pull/7",
                  "repository": { "full_name": "JSdotNet/Backlog" },
                  "pull_request": { "merged_at": "2026-08-08T12:00:00Z" } } } }
            ]
            """));

        Assert.Equal(GitHubItemState.Merged, Assert.Single(pulls).State);
    }

    [Fact]
    public void A_merged_pull_request_is_the_one_worth_showing()
    {
        var snapshot = new GitHubIssueSnapshot(
            new GitHubIssue(1, "u", "t", GitHubItemState.Closed, null),
            [
                new GitHubPullRequest(2, "u", "open one", GitHubItemState.Open, null),
                new GitHubPullRequest(3, "u", "merged one", GitHubItemState.Merged, null)
            ],
            DateTimeOffset.UtcNow);

        Assert.Equal(3, snapshot.Headline!.Number);
    }

    [Fact]
    public void A_timeline_that_is_not_an_array_yields_no_pull_requests()
    {
        Assert.Empty(GitHubClient.ReadLinkedPullRequests(Json("null")));
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
}

/// <summary>
/// The link an entry keeps after it has been pushed. It lives on the entry as a
/// projection, so it has to survive a reload of the markdown file.
/// </summary>
public sealed class GitHubLinkTests
{
    [Fact]
    public void An_unpushed_entry_has_no_link()
    {
        var entry = new BacklogEntry("Add GitHub support", string.Empty, EntryType.Task);

        Assert.Null(GitHubIntegration.FindLink(entry));
    }

    [Fact]
    public void A_pushed_entry_remembers_which_issue_it_became()
    {
        var entry = new BacklogEntry("Add GitHub support", string.Empty, EntryType.Task);
        entry.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "42", GitHubIntegration.IssueTargetType));

        var link = GitHubIntegration.FindLink(entry);

        Assert.NotNull(link);
        Assert.Equal(42, link!.IssueNumber);
        Assert.Equal("https://github.com/JSdotNet/Backlog/issues/42", link.Url);
        Assert.Equal("#42", link.Label);
    }

    [Fact]
    public void A_projection_to_something_else_is_not_a_github_issue()
    {
        var entry = new BacklogEntry("Add GitHub support", string.Empty, EntryType.Task);
        entry.AddProjectionRef(new ProjectionRef("JSdotNet/Backlog", "ADO-1", "work-item"));

        Assert.Null(GitHubIntegration.FindLink(entry));
    }
}
