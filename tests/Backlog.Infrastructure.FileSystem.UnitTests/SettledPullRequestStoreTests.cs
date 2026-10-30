using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The settled-fact memory behind Repository Management's one cache (local ADR 0042
/// §3, V4): merged pull requests come back after a restart, an unreadable folder
/// answers nothing rather than throwing, and nothing an open pull request carries —
/// checks, reviews, auto-merge, mergeability, how far behind — is ever written.
/// <para>
/// Real temp directories, as <see cref="PullRequestFinishMemoryTests"/> uses: the
/// behaviour under test is the disk.
/// </para>
/// </summary>
public sealed class SettledPullRequestStoreTests : IDisposable
{
    private static readonly DateTimeOffset Merged = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "settled-pull-request-store-tests-" + Guid.NewGuid().ToString("N"));

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

    private SettledPullRequestStore Store() => new(() => _root);

    private static SettledPullRequest Settled(int number, string repository = "JSdotNet/Backlog") =>
        new(repository, number, Merged, "plan/lobes-atlas", "main", $"Pull request {number}", $"https://github.com/{repository}/pull/{number}", "octocat")
        {
            HeadRefName = $"feature-{number}",
            ClosingIssues = [new IssueReference(repository, number + 1000)],
            Labels = ["frontend", "ready"]
        };

    [Fact]
    public void What_was_kept_comes_back_after_a_restart()
    {
        Store().Keep(Settled(1482));
        Store().Keep(Settled(7, "JSdotNet/Archify"));

        var read = Store().ReadAll().OrderBy(pull => pull.Number).ToList();

        Assert.Equal([("JSdotNet/Archify", 7), ("JSdotNet/Backlog", 1482)], read.Select(pull => (pull.RepositoryFullName, pull.Number)));
        var pull = read[1];
        Assert.Equal(
            (Merged, "plan/lobes-atlas", "main", "Pull request 1482", "https://github.com/JSdotNet/Backlog/pull/1482", "octocat", "feature-1482"),
            (pull.MergedAt, pull.BaseRefName, pull.DefaultBranch, pull.Title, pull.Url, pull.AuthorLogin, pull.HeadRefName));
        Assert.Equal([new IssueReference("JSdotNet/Backlog", 2482)], pull.ClosingIssues);
        Assert.Equal(["frontend", "ready"], pull.Labels);
    }

    /// <summary>A file the first version wrote — before the head branch, the closing
    /// issues and the labels were kept — still reads, with none of them.</summary>
    [Fact]
    public void A_file_the_first_version_wrote_still_reads()
    {
        var folder = Path.Combine(_root, "jsdotnet-backlog");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "1482.json"), """
            {
              "version": 1,
              "repository": "JSdotNet/Backlog",
              "number": 1482,
              "mergedAt": "2026-10-26T09:00:00+00:00",
              "baseRefName": "main",
              "defaultBranch": "main",
              "title": "Pull request 1482",
              "url": "https://github.com/JSdotNet/Backlog/pull/1482",
              "author": "octocat"
            }
            """);

        var read = Assert.Single(Store().ReadAll());

        Assert.Equal(1482, read.Number);
        Assert.Equal(Merged, read.MergedAt);
        Assert.Equal("", read.HeadRefName);
        Assert.Empty(read.ClosingIssues);
        Assert.Empty(read.Labels);
    }

    [Fact]
    public void Keeping_a_pull_request_again_replaces_it_rather_than_adding_a_second()
    {
        Store().Keep(Settled(1482) with { DefaultBranch = null });
        Store().Keep(Settled(1482));
        Store().Keep(Settled(1482) with { RepositoryFullName = "jsdotnet/backlog" });

        Assert.Single(Store().ReadAll());
    }

    [Fact]
    public void A_folder_that_cannot_be_read_answers_nothing()
    {
        Directory.CreateDirectory(_root);
        var notAFolder = Path.Combine(_root, "not-a-folder");
        File.WriteAllText(notAFolder, "a file where the folder should be");
        var store = new SettledPullRequestStore(() => notAFolder);

        store.Keep(Settled(1));

        Assert.Empty(store.ReadAll());
    }

    [Fact]
    public void A_file_this_version_cannot_read_is_passed_over_and_the_rest_still_come_back()
    {
        Store().Keep(Settled(1482));
        var folder = Directory.GetDirectories(_root).Single();
        File.WriteAllText(Path.Combine(folder, "9.json"), "{ half a file");

        Assert.Equal(1482, Assert.Single(Store().ReadAll()).Number);
    }

    /// <summary>A stale check, review or auto-merge kept across a restart would offer a
    /// merge GitHub no longer allows, so the file names none of them.</summary>
    [Fact]
    public void The_file_holds_no_check_review_auto_merge_mergeable_or_behind_state()
    {
        Store().Keep(Settled(1482));

        var json = File.ReadAllText(Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories).Single());

        foreach (var forbidden in new[] { "check", "review", "autoMerge", "mergeReady", "mergeable", "behind", "approval" })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("\"mergedAt\"", json, StringComparison.Ordinal);
    }
}
