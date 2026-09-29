using Backlog.Desktop.UI.Inbox;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The "Before you route" panel's arithmetic, without a DOM: the order and the
/// loops follow the switches, the count follows the repositories, and the
/// choices are exactly what the panel shows.
/// </summary>
public sealed class InboxRouteDraftTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static InboxRouteDraft Draft(IReadOnlyList<string>? aRepos = null, IReadOnlyList<string>? bRepos = null, params ProposedDependency[] dependencies) =>
        new(
            new InboxBatchProposalDto(
                "+inbox-batch-1a2b3c4d",
                [new InboxBatchProposalItemDto(A, "Alpha", aRepos ?? []), new InboxBatchProposalItemDto(B, "Beta", bRepos ?? [])],
                dependencies,
                []),
            listId: null,
            picked: []);

    private static ProposedDependency After(Guid from, Guid to, string title) => new(from, DependencyTarget.ForItem(to, title), title);

    [Fact]
    public void Every_dependency_starts_on_and_the_order_and_loops_follow_the_switches()
    {
        var draft = Draft(dependencies: [After(A, B, "Beta"), After(B, A, "Alpha")]);

        Assert.True(draft.IsEnabled(0));
        Assert.Equal(["Alpha → Beta → Alpha"], draft.Loops);
        Assert.False(draft.CanConfirm);

        draft.SetEnabled(1, false);

        Assert.Empty(draft.Loops);
        Assert.True(draft.CanConfirm);
        Assert.Equal([B, A], draft.OrderedItems.Select(item => item.Id));
        Assert.Equal("Alpha after Beta", draft.Describe(draft.Dependencies[0]));
    }

    [Fact]
    public void The_count_is_one_task_per_repository_or_one_for_none()
    {
        var draft = Draft(["a/one", "a/two"], []);

        Assert.Equal(3, draft.TaskCount);

        draft.SetOneForAll(true);
        Assert.Equal(["a/one", "a/two"], draft.SharedRepositories);
        Assert.Equal(4, draft.TaskCount);

        draft.SetSharedRepositories([]);
        Assert.Equal(2, draft.TaskCount);
    }

    [Fact]
    public void The_choices_carry_the_tag_every_items_repositories_and_only_the_dependencies_left_on()
    {
        var kept = After(A, B, "Beta");
        var draft = Draft(["a/one"], ["a/two"], kept, After(B, A, "Alpha"));
        draft.SetEnabled(1, false);
        draft.SetRepositories(B, []);

        var choices = draft.Choices();

        Assert.Equal("+inbox-batch-1a2b3c4d", choices.PlanTag);
        Assert.Equal([kept], choices.Dependencies);
        Assert.Equal(["a/one"], choices.Repositories![A]);
        Assert.Empty(choices.Repositories[B]);
        Assert.Null(choices.Merges);
    }

    // --- Hints ---------------------------------------------------------------------

    private static readonly Guid C = Guid.NewGuid();

    private static InboxRouteDraft Hinted(params OrderingHint[] hints) =>
        new(
            new InboxBatchProposalDto(
                "+inbox-batch-1a2b3c4d",
                [
                    new InboxBatchProposalItemDto(A, "Alpha", ["a/one"]),
                    new InboxBatchProposalItemDto(B, "Alpha again", ["a/one"]),
                    new InboxBatchProposalItemDto(C, "Gamma", []),
                ],
                [After(C, B, "Alpha again")],
                [])
            {
                Hints = hints,
            },
            listId: null,
            picked: []);

    [Fact]
    public void A_merge_starts_off_and_on_it_folds_the_duplicates_into_the_first_and_says_so_in_the_choices()
    {
        var draft = Hinted(new OrderingHint(OrderingHintKind.Duplicate, [A, B], "Nearly the same title"));

        Assert.False(draft.IsMerged(0));
        Assert.Null(draft.Choices().Merges);
        Assert.Equal(3, draft.TaskCount);

        draft.SetMerged(0, true);

        var merges = draft.Choices().Merges;
        Assert.NotNull(merges);
        Assert.Equal([B], merges[A]);
        Assert.Equal([A, C], draft.GoingItems.Select(item => item.Id));
        Assert.Equal([A, C], draft.OrderedItems.Select(item => item.Id));
        Assert.Equal(2, draft.TaskCount);
        Assert.Equal("Keep “Alpha”, archive “Alpha again”", draft.DescribeMerge(draft.Hints[0]));
    }

    [Fact]
    public void A_merge_carries_the_folded_items_dependencies_onto_the_kept_one_so_the_panel_sees_the_loop_the_route_would()
    {
        var draft = new InboxRouteDraft(
            new InboxBatchProposalDto(
                "+inbox-batch-1a2b3c4d",
                [
                    new InboxBatchProposalItemDto(A, "Alpha", []),
                    new InboxBatchProposalItemDto(B, "Alpha again", []),
                    new InboxBatchProposalItemDto(C, "Gamma", []),
                ],
                [After(A, C, "Gamma"), After(C, B, "Alpha again")],
                [])
            {
                Hints = [new OrderingHint(OrderingHintKind.Duplicate, [A, B], "Nearly the same title")],
            },
            listId: null,
            picked: []);

        Assert.Empty(draft.Loops);

        draft.SetMerged(0, true);

        Assert.Equal(["Alpha → Gamma → Alpha"], draft.Loops);
        Assert.False(draft.CanConfirm);
    }

    [Fact]
    public void A_hint_that_is_not_a_duplicate_cannot_be_turned_on_and_never_touches_the_order_or_the_loops()
    {
        var draft = Hinted(
            new OrderingHint(OrderingHintKind.CaptureOrder, [C, B, A], "Captured in another order"),
            new OrderingHint(OrderingHintKind.SetupFirst, [C], "Reads like setup, so it may belong ahead of the rest"),
            new OrderingHint(OrderingHintKind.SameRepository, [A, B], "Same repository: a/one"));

        draft.SetMerged(0, true);

        Assert.False(draft.IsMerged(0));
        Assert.Null(draft.Choices().Merges);
        Assert.Equal([A, B, C], draft.OrderedItems.Select(item => item.Id));
        Assert.Empty(draft.Loops);
        Assert.Equal([After(C, B, "Alpha again")], draft.Choices().Dependencies);
        Assert.Equal("Captured in another order: Gamma → Alpha again → Alpha", draft.DescribeHint(draft.Hints[0]));
        Assert.Equal("“Gamma” reads like setup, so it may belong ahead of the rest", draft.DescribeHint(draft.Hints[1]));
        Assert.Equal("Same repository: a/one — Alpha, Alpha again", draft.DescribeHint(draft.Hints[2]));
    }
}
