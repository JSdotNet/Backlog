using Backlog.Desktop.UI.Goals;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Progress;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Which repositories a bare pull request number in a goal's attach search is read in
/// (<c>.devbook/domain/progress/features.md#attaching-work-to-a-goal</c>): the one the
/// header's scope holds when it holds one, each it holds when it holds several, and every
/// registered one when it holds none. And how a task's status and its <c>blocked:</c> mark
/// are handed to the Goals view.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
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

    /// <summary>A link stored as <c>#582</c> is one the bar reads and the row cannot draw, so it
    /// is handed over in the stored list and stays out of <c>PullRequests</c>, which the Pull
    /// requests column and No goal read.</summary>
    [Fact]
    public async Task A_link_spelled_with_a_hash_is_handed_over_stored_and_left_out_of_the_drawn_ones()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Bank sample files\n`task` `!in-progress`\n");

        Assert.Null(await host.State.LinkWorkAsync(row, "JSdotNet/Backlog", "#582", EntryProjectionDto.PullRequestTargetType));

        var task = Assert.Single(GoalsCandidateSource.Of(host.State, new WorkInProgressReader(null, null)).Tasks);
        Assert.Empty(task.PullRequests);
        Assert.Equal([new PullRequestRefDto("JSdotNet/Backlog", "#582")], task.StoredPullRequests);
        Assert.Equal([new PullRequestRefDto("JSdotNet/Backlog", "#582")], task.LinksForStanding);
    }

    /// <summary>A <c>blocked:</c> mark reaches the Goals view apart from the status: the status
    /// says blocked only of a task in progress, so No goal goes on leaving out one not started.</summary>
    [Theory]
    [InlineData("`task` `!ready`", GoalTaskStatus.ToDo, GoalTaskStatus.ToDo)]
    [InlineData("`task` `!in-progress`", GoalTaskStatus.Doing, GoalTaskStatus.Blocked)]
    public async Task A_blocked_mark_is_handed_over_apart_from_the_status(string metadata, GoalTaskStatus unmarked, GoalTaskStatus marked)
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync($"# Bank sample files\n{metadata}\n");

        var before = Assert.Single(GoalsCandidateSource.Of(host.State, new WorkInProgressReader(null, null)).Tasks);
        Assert.Equal((unmarked, false), (before.Status, before.MarkedBlocked));

        await host.State.ChangeBlockedAsync(row, blocked: true, new DateOnly(2026, 10, 5));

        var after = Assert.Single(GoalsCandidateSource.Of(host.State, new WorkInProgressReader(null, null)).Tasks);
        Assert.Equal((marked, true), (after.Status, after.MarkedBlocked));
    }
}
