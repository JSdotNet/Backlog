namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The two questions branch loading asks of GitHub before it downloads anything:
/// which branches are there, and where does one of them point.
/// </summary>
public class GitHubBranchCatalogTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    private static GitHubBranchCatalog Catalog(RoutingTransport transport) => new(transport);

    [Fact]
    public async Task Branches_are_listed_in_the_order_github_gives_them()
    {
        var transport = new RoutingTransport()
            .Returns("/branches", """[{"name":"main"},{"name":"release/2.0"},{"name":"spike"}]""");

        var branches = await Catalog(transport).ListBranchesAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(["main", "release/2.0", "spike"], branches);
    }

    /// <summary>A short page is the last one: a repository with fewer branches
    /// than a page holds costs one call.</summary>
    [Fact]
    public async Task A_short_first_page_is_the_only_call()
    {
        var transport = new RoutingTransport().Returns("/branches", "[]");

        await Catalog(transport).ListBranchesAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal("repos/JSdotNet/Backlog/branches?per_page=100&page=1", Assert.Single(transport.Paths));
    }

    /// <summary>Every page, until GitHub hands back a short one. One page used to
    /// be the whole answer, and seventy-four of this repository's 174 branches
    /// could be neither found nor typed.</summary>
    [Fact]
    public async Task Branches_past_the_first_page_are_listed()
    {
        var transport = new RoutingTransport()
            .ReturnsInTurn("/branches", Page(0, 100), Page(100, 74));

        var branches = await Catalog(transport).ListBranchesAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(174, branches.Count);
        Assert.Equal("branch-173", branches[^1]);
        Assert.Equal(
            ["repos/JSdotNet/Backlog/branches?per_page=100&page=1", "repos/JSdotNet/Backlog/branches?per_page=100&page=2"],
            transport.Paths);
    }

    /// <summary>A ceiling of a thousand, so a repository with thousands of
    /// branches does not turn one click into dozens of calls.</summary>
    [Fact]
    public async Task Listing_stops_at_the_ceiling()
    {
        var page = 0;
        var transport = new RoutingTransport()
            .ReturnsInTurn("/branches", [.. Enumerable.Range(0, 15).Select(_ => Page(100 * page++, 100))]);

        var branches = await Catalog(transport).ListBranchesAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(GitHubBranchCatalog.BranchListLimit, branches.Count);
        Assert.Equal(10, transport.CallsTo("/branches"));
    }

    private static string Page(int first, int count) =>
        "[" + string.Join(",", Enumerable.Range(first, count).Select(i => $$"""{"name":"branch-{{i}}"}""")) + "]";

    [Fact]
    public async Task A_row_with_no_name_is_dropped_rather_than_listed_blank()
    {
        var transport = new RoutingTransport()
            .Returns("/branches", """[{"name":"main"},{"sha":"abc"},{"name":"  "}]""");

        var branches = await Catalog(transport).ListBranchesAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(["main"], branches);
    }

    [Fact]
    public async Task A_named_branch_resolves_to_its_head_commit()
    {
        var transport = new RoutingTransport()
            .Returns("/branches/main", """{"name":"main","commit":{"sha":"abc123"}}""");

        var head = await Catalog(transport).ResolveHeadAsync(Repository, "main", TestContext.Current.CancellationToken);

        Assert.Equal(new GitHubBranchHead("main", "abc123"), head);
    }

    /// <summary>A caller that passed null gets back the name of the default
    /// branch, so it never has to ask a second time to find out what it read.</summary>
    [Fact]
    public async Task No_branch_resolves_against_the_repositorys_default()
    {
        var transport = new RoutingTransport()
            .Returns("/branches/develop", """{"commit":{"sha":"def456"}}""")
            .Returns("repos/JSdotNet/Backlog", """{"default_branch":"develop"}""");

        var head = await Catalog(transport).ResolveHeadAsync(Repository, null, TestContext.Current.CancellationToken);

        Assert.Equal(new GitHubBranchHead("develop", "def456"), head);
    }

    /// <summary>A branch name with a slash in it is an ordinary name, and the
    /// branches endpoint is why it stays unambiguous.</summary>
    [Fact]
    public async Task A_branch_name_containing_a_slash_resolves()
    {
        var transport = new RoutingTransport()
            .Returns("/branches/release%2F2.0", """{"commit":{"sha":"aaa"}}""");

        var head = await Catalog(transport).ResolveHeadAsync(Repository, "release/2.0", TestContext.Current.CancellationToken);

        Assert.Equal("release/2.0", head!.Branch);
        Assert.Equal("aaa", head.Sha);
    }

    /// <summary>A stored setting whose branch has since been deleted is stale
    /// configuration, not a fault: the caller shows "that branch is gone" and
    /// offers the list, which beats an error dialog.</summary>
    [Fact]
    public async Task A_branch_that_is_gone_resolves_to_null_rather_than_throwing()
    {
        var transport = new RoutingTransport().Refuses("/branches/", "Not Found");

        var head = await Catalog(transport).ResolveHeadAsync(Repository, "deleted", TestContext.Current.CancellationToken);

        Assert.Null(head);
    }

    [Fact]
    public async Task A_branch_with_no_commit_resolves_to_null()
    {
        var transport = new RoutingTransport().Returns("/branches/main", """{"name":"main"}""");

        Assert.Null(await Catalog(transport).ResolveHeadAsync(Repository, "main", TestContext.Current.CancellationToken));
    }
}
