using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The counts-only baseline: how much was merged and closed in each block, at a
/// price that does not grow with how much there was.
/// <para>
/// Search is the right endpoint here and the wrong one for listing, and the tests
/// below are what keeps that distinction from being "tidied" away. The listing
/// client rejects search because of the thousand-hit cap on the results array and
/// the index lag; neither objection touches <c>total_count</c> on a block that
/// closed weeks ago.
/// </para>
/// </summary>
public sealed class GitHubActivityBaselineClientTests
{
    private static readonly ActivityBlock Block = new(
        new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero));

    private static readonly GitHubRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    [Fact]
    public async Task A_block_is_two_calls_and_reads_only_the_total_count()
    {
        var transport = new RoutingTransport()
            .Returns("is:pr", """{ "total_count": 17, "incomplete_results": false, "items": [] }""")
            .Returns("is:issue", """{ "total_count": 4, "incomplete_results": false, "items": [] }""");

        var baseline = await new GitHubActivityBaselineClient(transport)
            .GetBaselineAsync([Backlog], [Block], "jsdotnet", TestContext.Current.CancellationToken);

        var block = Assert.Single(baseline.Blocks);
        Assert.Equal(17, block.MergedPullRequests);
        Assert.Equal(4, block.ClosedIssues);
        Assert.True(baseline.Complete);

        // Two calls for the whole block, however much it contains. That is the
        // property that makes a long history drawable at all.
        Assert.Equal(2, transport.Paths.Count);
        Assert.All(transport.Paths, path => Assert.Contains("search/issues", path, StringComparison.Ordinal));

        // One row asked for, because the rows are not wanted — only the count is.
        Assert.All(transport.Paths, path => Assert.Contains("per_page=1", path, StringComparison.Ordinal));

        // The window travels in the query rather than being filtered afterwards.
        Assert.All(transport.Paths, path =>
            Assert.Contains("2026-06-01T00:00:00Z..2026-06-08T00:00:00Z", path, StringComparison.Ordinal));
    }

    /// <summary>
    /// No single credential is guaranteed to satisfy a query naming repositories
    /// under two owners, and the failure would not be an error — it would be a count
    /// silently missing whatever that credential cannot see.
    /// </summary>
    [Fact]
    public async Task Repositories_under_different_owners_are_asked_in_separate_queries()
    {
        var transport = new RoutingTransport()
            .Returns("search/issues", """{ "total_count": 1, "incomplete_results": false, "items": [] }""");

        var baseline = await new GitHubActivityBaselineClient(transport).GetBaselineAsync(
            [Backlog, new GitHubRepositoryRef("spec", "innovadis-dev", "spec-manager")],
            [Block],
            "jsdotnet",
            TestContext.Current.CancellationToken);

        // Two owners, two kinds, one block.
        Assert.Equal(4, transport.Paths.Count);

        Assert.All(transport.Paths, path => Assert.False(
            path.Contains("repo:JSdotNet/", StringComparison.OrdinalIgnoreCase)
            && path.Contains("repo:innovadis-dev/", StringComparison.OrdinalIgnoreCase),
            $"Two owners in one query: {path}"));

        // And the counts from both still add up into the one block.
        Assert.Equal(2, Assert.Single(baseline.Blocks).MergedPullRequests);
    }

    /// <summary>Repositories under one owner do share a query — the split is about
    /// credentials, not about tidiness, so it goes no finer than it has to.</summary>
    [Fact]
    public async Task Repositories_under_one_owner_share_a_query()
    {
        var transport = new RoutingTransport()
            .Returns("search/issues", """{ "total_count": 3, "incomplete_results": false, "items": [] }""");

        _ = await new GitHubActivityBaselineClient(transport).GetBaselineAsync(
            [Backlog, new GitHubRepositoryRef("tools", "JSdotNet", "Tools")],
            [Block],
            "jsdotnet",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.Paths.Count);
        Assert.All(transport.Paths, path =>
        {
            Assert.Contains("repo:JSdotNet/Backlog", path, StringComparison.Ordinal);
            Assert.Contains("repo:JSdotNet/Tools", path, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Eleven weeks of history with one gap is worth drawing. A thrown exception is
    /// not — and a zero in the gap would be indistinguishable from a quiet week,
    /// which is exactly the confusion the flag prevents.
    /// </summary>
    [Fact]
    public async Task A_refused_search_makes_the_baseline_incomplete_rather_than_throwing()
    {
        var transport = new RoutingTransport()
            .Refuses("is:issue")
            .Returns("is:pr", """{ "total_count": 9, "incomplete_results": false, "items": [] }""");

        var baseline = await new GitHubActivityBaselineClient(transport)
            .GetBaselineAsync([Backlog], [Block], "jsdotnet", TestContext.Current.CancellationToken);

        Assert.False(baseline.Complete);

        // The half that answered is still reported.
        var block = Assert.Single(baseline.Blocks);
        Assert.Equal(9, block.MergedPullRequests);
        Assert.Equal(0, block.ClosedIssues);
    }

    /// <summary>Every block comes back, in the order it was asked for, so a caller
    /// can line the rows up against its own axis without matching on dates.</summary>
    [Fact]
    public async Task Every_block_comes_back_in_the_order_it_was_asked_for()
    {
        var transport = new RoutingTransport()
            .Returns("search/issues", """{ "total_count": 1, "incomplete_results": false, "items": [] }""");

        var second = new ActivityBlock(Block.To, Block.To.AddDays(7));

        var baseline = await new GitHubActivityBaselineClient(transport)
            .GetBaselineAsync([Backlog], [Block, second], "jsdotnet", TestContext.Current.CancellationToken);

        Assert.Equal(2, baseline.Blocks.Count);
        Assert.Equal(Block.From, baseline.Blocks[0].From);
        Assert.Equal(second.From, baseline.Blocks[1].From);
    }

    [Fact]
    public async Task Nothing_configured_costs_no_calls()
    {
        var transport = new RoutingTransport();

        var baseline = await new GitHubActivityBaselineClient(transport)
            .GetBaselineAsync([], [Block], "jsdotnet", TestContext.Current.CancellationToken);

        Assert.Empty(baseline.Blocks);
        Assert.True(baseline.Complete);
        Assert.Empty(transport.Paths);
    }
}
