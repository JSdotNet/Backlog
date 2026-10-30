using Backlog.Desktop.UI.InProgress;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Which repositories a bare pull request number in a goal's attach search is read in
/// (<c>.devbook/domain/progress/features.md#attaching-work-to-a-goal</c>): the one the
/// header's scope holds when it holds one, each it holds when it holds several, and every
/// registered one when it holds none.
/// </summary>
public sealed class GoalsCandidateSourceTests
{
    private static readonly IReadOnlyList<GitHubRepositoryRef> Registered =
    [
        new("backlog", "JSdotNet", "Backlog"),
        new("marketplace", "JSdotNet", "Marketplace"),
        new("site", "JSdotNet", "Site")
    ];

    [Fact]
    public void One_repository_in_scope_is_the_one_a_number_is_read_in()
    {
        Assert.Equal(["JSdotNet/Marketplace"], GoalsCandidateSource.PullRequestRepositories(["marketplace"], Registered));
    }

    [Fact]
    public void Several_in_scope_are_each_read_in_the_scopes_order()
    {
        Assert.Equal(
            ["JSdotNet/Site", "JSdotNet/Backlog"],
            GoalsCandidateSource.PullRequestRepositories(["site", "backlog"], Registered));
    }

    [Fact]
    public void Nothing_in_scope_reads_a_number_in_every_registered_repository()
    {
        Assert.Equal(
            ["JSdotNet/Backlog", "JSdotNet/Marketplace", "JSdotNet/Site"],
            GoalsCandidateSource.PullRequestRepositories([], Registered));
    }

    [Fact]
    public void The_brain_names_no_repository_so_beside_one_that_one_is_read_alone()
    {
        Assert.Equal(["JSdotNet/Backlog"], GoalsCandidateSource.PullRequestRepositories([BrainScope.Value, "backlog"], Registered));
    }

    [Fact]
    public void A_scope_naming_no_registered_repository_reads_a_number_in_every_registered_one()
    {
        Assert.Equal(
            ["JSdotNet/Backlog", "JSdotNet/Marketplace", "JSdotNet/Site"],
            GoalsCandidateSource.PullRequestRepositories([BrainScope.Value], Registered));
    }
}
