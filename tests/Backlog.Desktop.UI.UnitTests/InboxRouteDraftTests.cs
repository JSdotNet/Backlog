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
    }
}
