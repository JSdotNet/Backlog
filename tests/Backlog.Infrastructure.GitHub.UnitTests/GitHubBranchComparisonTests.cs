using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The two questions reviewing a branch asks of GitHub: what does the branch add
/// over its base, and what did one commit change against its first parent —
/// read from the JSON the compare and commit endpoints actually return.
/// </summary>
public class GitHubBranchComparisonTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static GitHubBranchComparison Comparison(RoutingTransport transport) => new(transport);

    private const string CompareJson =
        """
        {
          "status": "diverged",
          "ahead_by": 2,
          "behind_by": 3,
          "total_commits": 2,
          "merge_base_commit": { "sha": "base000" },
          "commits": [
            {
              "sha": "c1",
              "parents": [ { "sha": "base000" } ],
              "commit": {
                "message": "docs: add the review chapter\n\nLonger body that is not the subject.",
                "author": { "name": "Job", "date": "2026-10-12T09:30:00Z" }
              }
            },
            {
              "sha": "c2",
              "parents": [ { "sha": "c1" }, { "sha": "other" } ],
              "commit": {
                "message": "Merge main",
                "author": { "name": "Ada", "date": "2026-10-13T08:00:00+02:00" }
              }
            }
          ],
          "files": [
            { "filename": ".devbook/arc42/review.md", "status": "added", "sha": "blob-added" },
            { "filename": ".devbook/arc42/old.md", "status": "removed", "sha": "blob-old" },
            { "filename": ".devbook/arc42/intro.md", "status": "modified", "sha": "blob-intro" },
            { "filename": ".devbook/design/new-name.md", "previous_filename": ".devbook/design/old-name.md", "status": "renamed", "sha": "blob-renamed" },
            { "filename": "src/Program.cs", "status": "changed", "sha": "blob-cs" }
          ]
        }
        """;

    [Fact]
    public async Task A_comparison_asks_for_the_three_dot_compare_of_the_two_refs()
    {
        var transport = new RoutingTransport().Returns("/compare/", CompareJson);

        await Comparison(transport).CompareAsync(Repository, "main", "release/2.0", Token);

        Assert.Equal("repos/JSdotNet/Backlog/compare/main...release%2F2.0", Assert.Single(transport.Paths));
    }

    [Fact]
    public async Task A_comparison_carries_the_merge_base_and_how_far_apart_the_two_are()
    {
        var transport = new RoutingTransport().Returns("/compare/", CompareJson);

        var comparison = await Comparison(transport).CompareAsync(Repository, "main", "feature", Token);

        Assert.Equal("base000", comparison.MergeBaseSha);
        Assert.Equal(2, comparison.AheadBy);
        Assert.Equal(3, comparison.BehindBy);
        Assert.Equal(2, comparison.TotalCommits);
        Assert.False(comparison.IsTruncated);
    }

    /// <summary>In GitHub's order, oldest first; which way round to show them is
    /// the caller's choice. The subject is the first line, the parent the
    /// first one — a merge commit is compared against the line it was made on.</summary>
    [Fact]
    public async Task Commits_are_read_with_their_first_parent_subject_and_author()
    {
        var transport = new RoutingTransport().Returns("/compare/", CompareJson);

        var comparison = await Comparison(transport).CompareAsync(Repository, "main", "feature", Token);

        Assert.Equal(
            [
                new GitHubComparedCommit("c1", "base000", "docs: add the review chapter", "Job", new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero)),
                new GitHubComparedCommit("c2", "c1", "Merge main", "Ada", new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.FromHours(2)))
            ],
            comparison.Commits);
    }

    [Fact]
    public async Task Added_removed_modified_and_renamed_files_are_told_apart()
    {
        var transport = new RoutingTransport().Returns("/compare/", CompareJson);

        var comparison = await Comparison(transport).CompareAsync(Repository, "main", "feature", Token);

        Assert.Equal(
            [
                new GitHubChangedFile(".devbook/arc42/review.md", null, GitHubFileStatus.Added, "blob-added"),
                new GitHubChangedFile(".devbook/arc42/old.md", null, GitHubFileStatus.Removed, null),
                new GitHubChangedFile(".devbook/arc42/intro.md", null, GitHubFileStatus.Modified, "blob-intro"),
                new GitHubChangedFile(".devbook/design/new-name.md", ".devbook/design/old-name.md", GitHubFileStatus.Renamed, "blob-renamed"),
                new GitHubChangedFile("src/Program.cs", null, GitHubFileStatus.Modified, "blob-cs")
            ],
            comparison.Files);
    }

    /// <summary>GitHub lists at most 250 commits in a comparison and says how many
    /// there really are; the gap is carried rather than hidden, so the caller can
    /// say "showing N of M".</summary>
    [Fact]
    public async Task A_comparison_with_more_commits_than_listed_says_it_is_truncated()
    {
        var transport = new RoutingTransport().Returns(
            "/compare/",
            """
            {
              "ahead_by": 300, "behind_by": 0, "total_commits": 300,
              "merge_base_commit": { "sha": "base000" },
              "commits": [ { "sha": "c1", "parents": [ { "sha": "base000" } ], "commit": { "message": "one", "author": { "name": "Job", "date": "2026-10-12T09:30:00Z" } } } ],
              "files": []
            }
            """);

        var comparison = await Comparison(transport).CompareAsync(Repository, "main", "feature", Token);

        Assert.Equal(300, comparison.TotalCommits);
        Assert.Single(comparison.Commits);
        Assert.True(comparison.IsTruncated);
    }

    /// <summary>GitHub stops listing files at 300 without saying how many there
    /// were, so a full list is the only sign that some are missing.</summary>
    [Fact]
    public async Task A_full_file_list_says_it_may_be_cut_short()
    {
        var files = string.Join(",", Enumerable.Range(0, 300).Select(i => $$"""{ "filename": "f{{i}}.md", "status": "added", "sha": "b{{i}}" }"""));
        var transport = new RoutingTransport().Returns(
            "/compare/",
            $$"""{ "total_commits": 0, "merge_base_commit": { "sha": "base000" }, "commits": [], "files": [ {{files}} ] }""");

        var comparison = await Comparison(transport).CompareAsync(Repository, "main", "feature", Token);

        Assert.Equal(300, comparison.Files.Count);
        Assert.True(comparison.FilesMayBeTruncated);
        Assert.False((comparison with { Files = [.. comparison.Files.Take(299)] }).FilesMayBeTruncated);
    }

    [Fact]
    public async Task A_missing_ref_surfaces_as_the_refusal_github_gave()
    {
        var transport = new RoutingTransport().Refuses(
            HttpMethod.Get, "/compare/", System.Net.HttpStatusCode.NotFound, "No common ancestor or no such ref.");

        var exception = await Assert.ThrowsAsync<GitHubException>(() =>
            Comparison(transport).CompareAsync(Repository, "main", "gone", Token));

        Assert.True(exception.IsNotFound);
    }

    [Fact]
    public async Task A_comparison_without_a_merge_base_is_refused()
    {
        var transport = new RoutingTransport().Returns("/compare/", """{ "commits": [], "files": [] }""");

        await Assert.ThrowsAsync<GitHubException>(() =>
            Comparison(transport).CompareAsync(Repository, "main", "feature", Token));
    }

    // --- One commit ---------------------------------------------------------

    [Fact]
    public async Task A_commit_is_read_against_its_first_parent()
    {
        var transport = new RoutingTransport().Returns(
            "/commits/c2",
            """
            {
              "sha": "c2",
              "parents": [ { "sha": "c1" }, { "sha": "other" } ],
              "commit": { "message": "Edit intro\n\nbody", "author": { "name": "Ada", "date": "2026-10-13T06:00:00Z" } },
              "files": [
                { "filename": ".devbook/arc42/intro.md", "status": "modified", "sha": "blob-intro-2" },
                { "filename": ".devbook/arc42/b.md", "previous_filename": ".devbook/arc42/a.md", "status": "renamed", "sha": "blob-b" }
              ]
            }
            """);

        var change = await Comparison(transport).ReadCommitAsync(Repository, "c2", Token);

        Assert.Equal("repos/JSdotNet/Backlog/commits/c2", Assert.Single(transport.Paths));
        Assert.Equal(new GitHubComparedCommit("c2", "c1", "Edit intro", "Ada", new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.Zero)), change.Commit);
        Assert.Equal(
            [
                new GitHubChangedFile(".devbook/arc42/intro.md", null, GitHubFileStatus.Modified, "blob-intro-2"),
                new GitHubChangedFile(".devbook/arc42/b.md", ".devbook/arc42/a.md", GitHubFileStatus.Renamed, "blob-b")
            ],
            change.Files);
    }

    /// <summary>A root commit has nothing before it, so whatever it holds is new —
    /// whatever status GitHub put on the row.</summary>
    [Fact]
    public async Task A_root_commit_has_no_parent_and_adds_every_file()
    {
        var transport = new RoutingTransport().Returns(
            "/commits/root",
            """
            {
              "sha": "root",
              "parents": [],
              "commit": { "message": "Initial commit", "author": { "name": "Job", "date": "2026-01-01T00:00:00Z" } },
              "files": [
                { "filename": "README.md", "status": "added", "sha": "blob-readme" },
                { "filename": ".devbook/arc42/intro.md", "status": "modified", "sha": "blob-intro" }
              ]
            }
            """);

        var change = await Comparison(transport).ReadCommitAsync(Repository, "root", Token);

        Assert.Null(change.Commit.ParentSha);
        Assert.All(change.Files, file => Assert.Equal(GitHubFileStatus.Added, file.Status));
        Assert.All(change.Files, file => Assert.Null(file.PreviousPath));
    }

    [Fact]
    public async Task A_commit_that_is_not_there_surfaces_as_the_refusal_github_gave()
    {
        var transport = new RoutingTransport().Refuses(
            HttpMethod.Get, "/commits/", System.Net.HttpStatusCode.NotFound, "No commit found for SHA: nope");

        var exception = await Assert.ThrowsAsync<GitHubException>(() =>
            Comparison(transport).ReadCommitAsync(Repository, "nope", Token));

        Assert.True(exception.IsNotFound);
    }
}
