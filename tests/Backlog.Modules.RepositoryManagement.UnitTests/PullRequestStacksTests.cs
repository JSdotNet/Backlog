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

    // --- Placing one pull request read by number (local ADR 0042, V2) --------------

    /// <summary>One its repository's open list holds takes the list's placing, with a
    /// parent or without one.</summary>
    [Fact]
    public void A_pull_request_the_open_list_holds_takes_the_lists_parent()
    {
        var parent = Pull(1, "feature/a", "main");
        var child = Pull(2, "feature/b", "feature/a");
        var listed = PullRequestStacks.WithParents([parent, child]);

        Assert.Equal(parent.Reference, PullRequestStacks.PlaceAmong(child with { DefaultBranch = "main" }, listed)!.StackParent);
        Assert.Null(PullRequestStacks.PlaceAmong(parent with { DefaultBranch = "main" }, listed)!.StackParent);
    }

    /// <summary>One based on its repository's default branch waits on no other pull
    /// request, whether or not any list holds it — a list that is capped, refused, or
    /// not kept for an unregistered repository.</summary>
    [Fact]
    public void A_pull_request_based_on_the_default_branch_is_placed_with_no_parent_whatever_the_list()
    {
        var pull = Pull(7, "feature/x", "main") with { DefaultBranch = "main", StackParent = new PullRequestReference("acme/app", 1) };

        Assert.Null(PullRequestStacks.PlaceAmong(pull, [])!.StackParent);
        Assert.Null(PullRequestStacks.PlaceAmong(pull, null)!.StackParent);
    }

    /// <summary>One the list does not hold, based on another open pull request's head
    /// that the list does hold, is stacked on that one.</summary>
    [Fact]
    public void A_pull_request_the_list_does_not_hold_is_stacked_on_the_listed_head_it_is_based_on()
    {
        var parent = Pull(1, "feature/a", "main");
        var child = Pull(2, "feature/b", "feature/a") with { DefaultBranch = "main" };

        Assert.Equal(parent.Reference, PullRequestStacks.PlaceAmong(child, [parent])!.StackParent);
    }

    /// <summary>Based on neither the default branch nor any head an open list that was
    /// read holds, it waits on no open pull request: placed with no parent — what the Pull
    /// requests pane concludes for the same pull request.</summary>
    [Fact]
    public void A_pull_request_whose_base_no_listed_head_explains_waits_on_nothing_when_a_list_was_read()
    {
        var other = Pull(1, "feature/a", "main");

        var gone = PullRequestStacks.PlaceAmong(Pull(2, "feature/b", "feature/gone") with { DefaultBranch = "main" }, [other]);
        var unnamed = PullRequestStacks.PlaceAmong(Pull(2, "feature/b", "main"), []);

        Assert.NotNull(gone);
        Assert.Null(gone.StackParent);
        Assert.NotNull(unnamed);
        Assert.Null(unnamed.StackParent);
    }

    /// <summary>With no list read, only the default branch places a pull request: one
    /// based elsewhere might wait on a pull request nobody could list, so its place
    /// cannot be told.</summary>
    [Fact]
    public void Without_a_list_a_pull_request_based_off_the_default_branch_cannot_be_placed()
    {
        Assert.Null(PullRequestStacks.PlaceAmong(Pull(2, "feature/b", "feature/a") with { DefaultBranch = "main" }, null));
        Assert.Null(PullRequestStacks.PlaceAmong(Pull(2, "feature/b", "main"), null));
    }

    /// <summary>A listing whose read of the pull request's repository failed is no list
    /// for that repository: the default branch still places it, nothing else does.</summary>
    [Fact]
    public void A_listing_that_failed_for_the_repository_is_no_list_for_it()
    {
        // A failed repository's rows are not in the listing; the failure names it.
        var listing = new PullRequestListing([], [new RepositoryReadFailure("acme/app", "Refused.")]);
        var elsewhere = new PullRequestListing([], [new RepositoryReadFailure("acme/other", "Refused.")]) { CompleteRepositories = ["acme/app"] };

        Assert.Null(PullRequestStacks.PlaceAmongListing(Pull(2, "feature/b", "feature/a") with { DefaultBranch = "main" }, listing));
        Assert.NotNull(PullRequestStacks.PlaceAmongListing(Pull(2, "feature/b", "main") with { DefaultBranch = "main" }, listing));
        Assert.Null(PullRequestStacks.PlaceAmongListing(Pull(2, "feature/b", "feature/gone") with { DefaultBranch = "main" }, elsewhere)!.StackParent);
        Assert.Null(PullRequestStacks.PlaceAmongListing(Pull(2, "feature/b", "feature/gone") with { DefaultBranch = "main" }, null));
    }

    /// <summary>
    /// A listing that may not hold every open pull request of the repository — it reached
    /// the source's cap, or the repository is not one it lists (unregistered, archived) —
    /// is no list for it: a parent beyond it could be open, so a pull request off the
    /// default branch it does not explain cannot be placed. The default branch still
    /// places one.
    /// </summary>
    [Fact]
    public void A_listing_that_may_not_hold_the_whole_repository_is_no_list_for_it()
    {
        var capped = new PullRequestListing([Pull(1, "feature/a", "main")], []) { CompleteRepositories = ["acme/other"] };
        var whole = new PullRequestListing([Pull(1, "feature/a", "main")], []) { CompleteRepositories = [" ACME/app "] };

        Assert.Null(PullRequestStacks.PlaceAmongListing(Pull(2, "feature/b", "feature/gone") with { DefaultBranch = "main" }, capped));
        Assert.NotNull(PullRequestStacks.PlaceAmongListing(Pull(2, "feature/b", "main") with { DefaultBranch = "main" }, capped));
        Assert.Null(PullRequestStacks.PlaceAmongListing(Pull(2, "feature/b", "feature/gone") with { DefaultBranch = "main" }, whole)!.StackParent);
    }

    /// <summary>
    /// A listing short of the whole repository still says what it does hold: a listed
    /// pull request keeps the parent the list gives it, and one based on a listed head is
    /// stacked on it. Only "no parent", which is read from absence — a listed row the list
    /// gave none included — needs the whole repository.
    /// </summary>
    [Fact]
    public void A_capped_listing_keeps_the_placements_it_holds()
    {
        var parent = Pull(119, "feature/a", "main");
        var listedChild = Pull(120, "feature/b", "feature/a") with { DefaultBranch = "main" };
        var listedLoose = Pull(121, "feature/c", "plan/foo") with { DefaultBranch = "main" };
        var rows = PullRequestStacks.WithParents([parent, listedChild, listedLoose]);
        var capped = new PullRequestListing(rows, []);
        var whole = capped with { CompleteRepositories = ["acme/app"] };
        var pinned = Pull(9, "feature/d", "feature/a") with { DefaultBranch = "main" };

        Assert.Equal(parent.Reference, PullRequestStacks.PlaceAmongListing(listedChild, capped)!.StackParent);
        Assert.Equal(parent.Reference, PullRequestStacks.PlaceAmongListing(pinned, capped)!.StackParent);
        Assert.Null(PullRequestStacks.PlaceAmongListing(listedLoose, capped));
        Assert.Null(PullRequestStacks.PlaceAmongListing(listedLoose, whole)!.StackParent);
        Assert.Null(PullRequestStacks.PlaceAmongListing(Pull(10, "feature/e", "plan/foo") with { DefaultBranch = "main" }, capped));
        Assert.Null(PullRequestStacks.PlaceAmongListing(Pull(10, "feature/e", "plan/foo") with { DefaultBranch = "main" }, whole)!.StackParent);
    }

    /// <summary>
    /// The stacks a screen draws over pull requests already placed: each sits on the one
    /// its <see cref="PullRequestStatus.StackParent"/> names, where that one is among them,
    /// whatever their branches say — so the tree a screen draws is the placing its verdicts
    /// read, never a second reading of the branches.
    /// </summary>
    [Fact]
    public void Stacks_over_placed_pull_requests_follow_the_parents_they_were_placed_with()
    {
        var bottom = Pull(1, "feature/a", "main");
        var middle = Pull(2, "feature/b", "feature/a") with { StackParent = bottom.Reference };
        var unstacked = Pull(3, "feature/c", "feature/b");
        var orphan = Pull(4, "feature/d", "feature/x") with { StackParent = new PullRequestReference("acme/app", 99) };

        var stacks = PullRequestStacks.OfPlaced([bottom, middle, unstacked, orphan]);

        var stack = Assert.Single(stacks.Stacks);
        Assert.Equal([1, 2], stack.Members.Select(member => member.Number));
        Assert.Equal(1, stacks.DepthOf(middle));
        Assert.Null(stacks.ParentOf(unstacked));
        Assert.Null(stacks.StackOf(orphan));
    }

    /// <summary>A merged or closed pull request has nothing to wait on: placed, with no
    /// parent.</summary>
    [Fact]
    public void A_merged_pull_request_is_placed_with_no_parent()
    {
        var merged = Pull(3, "feature/c", "feature/gone") with { State = PullRequestState.Merged, StackParent = new PullRequestReference("acme/app", 1) };

        Assert.Null(PullRequestStacks.PlaceAmong(merged, null)!.StackParent);
    }

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

    /// <summary>Placing one pull request in stacks derived elsewhere — the pane derives
    /// its own over the rows in scope — sets the parent those stacks name, clears one
    /// they do not, and leaves a merged one waiting on nothing.</summary>
    [Fact]
    public void Placing_a_pull_request_sets_the_parent_its_stacks_name_and_clears_a_stale_one()
    {
        var bottom = Pull(1, "feature/a", "main");
        var top = Pull(2, "feature/b", "feature/a");
        var stacks = PullRequestStacks.Of([bottom, top]);
        var stale = Pull(3, "feature/z", "main") with { StackParent = new PullRequestReference("acme/app", 1) };
        var merged = top with { State = PullRequestState.Merged };

        Assert.Equal(new PullRequestReference("acme/app", 1), stacks.Place(top).StackParent);
        Assert.Null(stacks.Place(stale).StackParent);
        Assert.Null(stacks.Place(merged).StackParent);
        Assert.Same(bottom, stacks.Place(bottom));
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
