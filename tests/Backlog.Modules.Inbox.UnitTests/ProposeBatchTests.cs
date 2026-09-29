using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.ProposeBatch;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The "Before you route" question: what a batch would do, asked before it is
/// done. The items are sorted as the route sorts them, the tag is minted as the
/// route mints it, and the dependencies are the Dependency Proposal's — read
/// against the backlog's open tasks when the host has them.
/// </summary>
public sealed class ProposeBatchTests
{
    private const string TagShape = @"^\+[a-z0-9-]+-[0-9a-f]{8}$";

    [Fact]
    public async Task A_selection_is_proposed_with_a_fresh_tag_its_routable_items_and_their_dependencies()
    {
        var store = new InMemoryInboxStore();
        var first = new InboxItem(Guid.CreateVersion7(), "Set up the pipeline", string.Empty, null, Items.Noon, Items.Noon,
            ContentKind.Text, new InboxSource(InboxEnumMap.ManualChannel, null), replicaBacked: false);
        first.SetRepoIds(["a/one"]);
        var second = new InboxItem(Guid.CreateVersion7(), "Deploy the preview", "Once we set up the pipeline.", null, Items.Noon, Items.Noon,
            ContentKind.Text, new InboxSource(InboxEnumMap.ManualChannel, null), replicaBacked: false);
        var routed = Items.Manual("Already routed");
        routed.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);
        store.Seed(first);
        store.Seed(second);
        store.Seed(routed);

        var result = await Propose(store, [first.Id, routed.Id, second.Id]);

        Assert.True(result.IsSuccess);
        Assert.Matches(TagShape, result.Value.PlanTag);
        Assert.StartsWith("+inbox-batch-", result.Value.PlanTag, StringComparison.Ordinal);
        Assert.Equal([first.Id, second.Id], result.Value.Items.Select(item => item.Id));
        Assert.Equal(["a/one"], result.Value.Items[0].RepoIds);
        Assert.Equal("Deploy the preview", result.Value.Items[1].Title);

        var dependency = Assert.Single(result.Value.Dependencies);
        Assert.Equal((second.Id, first.Id), (dependency.From, dependency.To.Id));
        Assert.Equal("set up the pipeline", dependency.Reason);

        Assert.Equal(routed.Id, Assert.Single(result.Value.Refused).Id);
        Assert.Null(result.Value.Deferred);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task The_backlogs_open_tasks_are_read_so_an_item_can_wait_on_one()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual("Follow up on https://github.com/a/one/pull/9");
        store.Seed(item);
        var task = new InboxTaskReferenceDto(Guid.CreateVersion7(), null, "Ship the parser", ["a/one"], [], ["https://github.com/a/one/pull/9"]);
        var references = new FakeTaskReferences(task);

        var result = await Propose(store, [item.Id], references: references);

        var dependency = Assert.Single(result.Value.Dependencies);
        Assert.Equal(DependencyTargetKind.Task, dependency.To.Kind);
        Assert.Equal(task.Id.ToString("D"), dependency.To.After);
        Assert.Equal(1, references.Calls);
    }

    [Fact]
    public async Task Without_the_task_port_only_the_batchs_own_dependencies_are_proposed()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual("Follow up on https://github.com/a/one/pull/9");
        store.Seed(item);

        var result = await Propose(store, [item.Id]);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Dependencies);
    }

    [Fact]
    public async Task A_list_is_proposed_under_its_name_with_the_count_of_deferred_items_that_stay()
    {
        var store = new InMemoryInboxStore();
        var list = InboxList.Create("Reading List", null, 0, Items.Noon);
        store.Seed(list);
        var open = Items.Manual("An article");
        open.MoveToList(list.Id);
        var later = Items.Manual("Later");
        later.MoveToList(list.Id);
        later.Defer(null, Items.Noon);
        var elsewhere = Items.Manual("Elsewhere");
        elsewhere.Defer(null, Items.Noon);
        store.Seed(open);
        store.Seed(later);
        store.Seed(elsewhere);

        var result = await Propose(store, [open.Id], list.Id);

        Assert.StartsWith("+reading-list-", result.Value.PlanTag, StringComparison.Ordinal);
        Assert.Equal(1, result.Value.Deferred);
    }

    [Fact]
    public async Task A_list_that_is_gone_is_not_found()
    {
        var result = await Propose(new InMemoryInboxStore(), [Guid.CreateVersion7()], Guid.CreateVersion7());

        Assert.Equal(InboxErrors.ListNotFound, result.Error);
    }

    [Fact]
    public async Task Every_proposal_mints_a_new_tag()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);

        var first = await Propose(store, [item.Id]);
        var second = await Propose(store, [item.Id]);

        Assert.NotEqual(first.Value.PlanTag, second.Value.PlanTag);
    }

    private static Task<Result<InboxBatchProposalDto>> Propose(
        InMemoryInboxStore store,
        IReadOnlyList<Guid> ids,
        Guid? listId = null,
        FakeTaskReferences? references = null) =>
        new ProposeBatchQueryHandler(store, store, references)
            .Handle(new ProposeBatchQuery(ids, listId), TestContext.Current.CancellationToken);
}
