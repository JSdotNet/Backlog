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

    /// <summary>One page, not every page. A repository with more branches than a
    /// page holds is one where scrolling to the four-hundredth was never the
    /// interaction, and paging the whole set would turn opening Settings into an
    /// unbounded number of calls.</summary>
    [Fact]
    public async Task Listing_asks_for_one_page()
    {
        var transport = new RoutingTransport().Returns("/branches", "[]");

        await Catalog(transport).ListBranchesAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Contains(transport.Paths, path => path.Contains("per_page=100", StringComparison.Ordinal));
        Assert.Single(transport.Paths);
    }

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
