using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

namespace Backlog.Modules.RepositoryManagement.UnitTests;

/// <summary>
/// The stacks among open pull requests, read off nothing but their branches: a pull
/// request whose base is another open one's head sits on top of it. Bottom first, two on
/// one parent in number order, a loop in no stack, and every open pull request handed
/// back with the parent the verdict names.
/// </summary>
public sealed class PullRequestStacksTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static PullRequestStatus Pull(int number, string head, string @base, string repository = "acme/app", DateTimeOffset? updated = null) =>
        new(repository, number, $"https://github.com/{repository}/pull/{number}", $"Pull request {number}",
            PullRequestState.Open, head, @base, "octocat", ViewerDidAuthor: true, updated ?? Noon);

    [Fact]
    public void A_stack_lists_its_members_bottom_first()
    {
        var bottom = Pull(1, "feature/a", "main");
        var middle = Pull(2, "feature/b", "feature/a");
        var top = Pull(3, "feature/c", "feature/b");

        var stacks = PullRequestStacks.Of([top, bottom, middle]);

        var stack = Assert.Single(stacks.Stacks);
        Assert.Equal(1, stack.Bottom.Number);
        Assert.Equal([1, 2, 3], stack.Members.Select(member => member.Number));
        Assert.Equal(0, stacks.DepthOf(bottom));
        Assert.Equal(2, stacks.DepthOf(top));
        Assert.Equal(2, stacks.ParentOf(top)!.Number);
        Assert.Null(stacks.ParentOf(bottom));
    }

    [Fact]
    public void Two_on_one_parent_come_in_number_order_at_the_same_depth()
    {
        var bottom = Pull(1, "feature/a", "main");
        var later = Pull(9, "feature/later", "feature/a");
        var earlier = Pull(4, "feature/earlier", "feature/a");

        var stacks = PullRequestStacks.Of([later, bottom, earlier]);

        Assert.Equal([1, 4, 9], Assert.Single(stacks.Stacks).Members.Select(member => member.Number));
        Assert.Equal(1, stacks.DepthOf(later));
        Assert.Equal(1, stacks.DepthOf(earlier));
    }

    /// <summary>Two that target each other's branches have no bottom to merge first, so
    /// neither is in a stack rather than one picked at random.</summary>
    [Fact]
    public void A_loop_is_in_no_stack()
    {
        var one = Pull(1, "feature/a", "feature/b");
        var two = Pull(2, "feature/b", "feature/a");

        var stacks = PullRequestStacks.Of([one, two]);

        Assert.Empty(stacks.Stacks);
        Assert.Null(stacks.ParentOf(one));
        Assert.Null(stacks.StackOf(two));
    }

    [Fact]
    public void A_pull_request_on_its_own_is_in_no_stack()
    {
        var alone = Pull(1, "feature/a", "main");

        var stacks = PullRequestStacks.Of([alone]);

        Assert.Empty(stacks.Stacks);
        Assert.Null(stacks.DepthOf(alone));
    }

    [Fact]
    public void Branches_in_another_repository_do_not_stack()
    {
        var stacks = PullRequestStacks.Of([Pull(1, "feature/a", "main", "acme/app"), Pull(2, "feature/b", "feature/a", "acme/other")]);

        Assert.Empty(stacks.Stacks);
    }

    /// <summary>A pull request is found again by its repository and number, whatever
    /// else on it changed since the stacks were read — its parent set, or a newer read.</summary>
    [Fact]
    public void A_pull_request_is_found_by_its_reference_not_its_whole_record()
    {
        var bottom = Pull(1, "feature/a", "main");
        var top = Pull(2, "feature/b", "feature/a");

        var stacks = PullRequestStacks.Of([bottom, top]);

        Assert.Equal(1, stacks.ParentOf(top with { RepositoryFullName = "ACME/app", Title = "Renamed" })!.Number);
    }

    [Fact]
    public void With_parents_names_each_open_pull_requests_parent_and_keeps_the_order()
    {
        var bottom = Pull(1, "feature/a", "main");
        var top = Pull(2, "feature/b", "feature/a");
        var alone = Pull(3, "feature/z", "main");

        var placed = PullRequestStacks.WithParents([top, alone, bottom]);

        Assert.Equal([2, 3, 1], placed.Select(pull => pull.Number));
        Assert.Equal(new PullRequestReference("acme/app", 1), placed[0].StackParent);
        Assert.Null(placed[1].StackParent);
        Assert.Null(placed[2].StackParent);
    }

    /// <summary>A merged or closed pull request is in no stack and waits on nothing, and
    /// an open one's parent is never a merged one.</summary>
    [Fact]
    public void With_parents_leaves_merged_and_closed_pull_requests_out_of_every_stack()
    {
        var merged = Pull(1, "feature/a", "main") with { State = PullRequestState.Merged };
        var top = Pull(2, "feature/b", "feature/a");
        var closed = Pull(3, "feature/c", "feature/b") with { State = PullRequestState.Closed };

        var placed = PullRequestStacks.WithParents([merged, top, closed]);

        Assert.All(placed, pull => Assert.Null(pull.StackParent));
    }

    /// <summary>The verdict a stacked pull request gets once placed is the one that names
    /// its parent — the pane, the card and a task's row all place it this way.</summary>
    [Fact]
    public void A_placed_pull_request_gets_the_verdict_that_names_its_parent()
    {
        var placed = PullRequestStacks.WithParents([Pull(1, "feature/a", "main"), Pull(2, "feature/b", "feature/a")]);

        Assert.EndsWith("and it waits on #1, which has to merge first.", PullRequestVerdict.Of(placed[1])!.Reason, StringComparison.Ordinal);
    }
}
