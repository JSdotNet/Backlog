using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.ArchiveItem;
using Backlog.Modules.Inbox.Features.LinkToTask;
using Backlog.Modules.Inbox.Features.Related;
using Backlog.Modules.Inbox.Services;

using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The two acts a relation leads to, and the read behind them. "Archive as
/// duplicate of…" archives an open item and remembers which item it duplicates;
/// "Link to task…" records an open item as routed to a task that already exists
/// and makes nothing. Both refuse what the aggregate refuses and leave the item
/// as it was.
/// </summary>
public sealed class RelationActsTests
{
    private static readonly DateTimeOffset Later = Items.Noon.AddHours(1);

    private static readonly FakeTimeProvider Clock = new(Later);

    // --- The aggregate ---------------------------------------------------------------

    [Theory]
    [InlineData(InboxStatus.Unprocessed)]
    [InlineData(InboxStatus.Deferred)]
    public void An_open_item_archived_as_a_duplicate_remembers_the_item_it_duplicates(InboxStatus status)
    {
        var item = Items.FromPhone();
        if (status == InboxStatus.Deferred) item.Defer(null, Items.Noon);
        var original = Guid.CreateVersion7();

        item.Archive(Later, original);

        Assert.Equal(InboxStatus.Archived, item.Status);
        Assert.Equal(original, item.DuplicateOf);
        Assert.True(item.ReplicaAckPending);
        Assert.Equal(Later, item.UpdatedAt);
    }

    [Fact]
    public void An_item_is_never_a_duplicate_of_itself()
    {
        var item = Items.Manual();

        Assert.Throws<ArgumentException>(() => item.Archive(Later, item.Id));

        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Null(item.DuplicateOf);
    }

    [Fact]
    public void Only_an_open_item_is_archived_as_a_duplicate()
    {
        var triaged = Items.Manual();
        triaged.LoadState(InboxStatus.Triaged, null, routing: null, replicaAckPending: false, Items.Noon);

        var refused = Assert.Throws<InvalidInboxTransitionException>(() => triaged.Archive(Later, Guid.CreateVersion7()));

        Assert.Equal(InboxStatus.Triaged, refused.From);
        Assert.Null(triaged.DuplicateOf);
    }

    [Fact]
    public void A_plain_archive_names_no_duplicate()
    {
        var item = Items.Manual();

        item.Archive(Later);

        Assert.Null(item.DuplicateOf);
    }

    [Fact]
    public void Linking_to_a_task_routes_the_item_to_that_one_task()
    {
        var item = Items.FromPhone();
        var task = Guid.CreateVersion7();

        item.LinkToTask(task, ["jsdotnet/backlog"], Later);

        Assert.Equal(InboxStatus.Triaged, item.Status);
        Assert.Equal([task], item.Routing!.TaskIds);
        Assert.Equal(["jsdotnet/backlog"], item.Routing.RepoIds);
        Assert.Equal(RoutingDomain.Tasks, item.Routing.Domain);
        Assert.True(item.ReplicaAckPending);
    }

    [Fact]
    public void A_decided_item_is_not_linked()
    {
        var archived = Items.Manual();
        archived.Archive(Items.Noon);
        var routed = Items.Manual();
        routed.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);

        Assert.Throws<InvalidInboxTransitionException>(() => archived.LinkToTask(Guid.CreateVersion7(), [], Later));
        Assert.Throws<InvalidInboxTransitionException>(() => routed.LinkToTask(Guid.CreateVersion7(), [], Later));
        Assert.Null(archived.Routing);
    }

    // --- Archive as duplicate of… ------------------------------------------------------

    [Fact]
    public async Task Archiving_as_a_duplicate_saves_the_item_with_the_one_it_duplicates()
    {
        var store = new InMemoryInboxStore();
        var original = Items.Manual("Read the post");
        var duplicate = Items.Manual("Read the post again");
        store.Seed(original);
        store.Seed(duplicate);

        var result = await Archive(store, duplicate.Id, original.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(InboxStatus.Archived, duplicate.Status);
        Assert.Equal(original.Id, duplicate.DuplicateOf);
        Assert.Equal(original.Id, duplicate.ToDto().DuplicateOf);
        Assert.Equal([duplicate.Id], store.ItemWrites);
    }

    [Fact]
    public async Task A_duplicate_of_an_item_that_is_gone_is_refused_and_nothing_is_written()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Archive(store, item.Id, Guid.CreateVersion7());

        Assert.Equal(InboxErrors.DuplicateTargetNotFound, result.Error);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task A_duplicate_of_itself_is_refused()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Archive(store, item.Id, item.Id);

        Assert.Equal(InboxErrors.DuplicateOfItself, result.Error);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task Archiving_an_item_as_a_duplicate_of_its_own_duplicate_is_refused_as_circular()
    {
        var store = new InMemoryInboxStore();
        var a = Items.Manual("Read the post");
        var b = Items.Manual("Read the post again");
        store.Seed(a);
        store.Seed(b);
        Assert.True((await Archive(store, b.Id, a.Id)).IsSuccess);

        var result = await Archive(store, a.Id, b.Id);

        Assert.Equal(InboxErrors.DuplicateCircular, result.Error);
        Assert.Equal(InboxStatus.Unprocessed, a.Status);
        Assert.Null(a.DuplicateOf);
        Assert.Equal([b.Id], store.ItemWrites);
    }

    [Fact]
    public async Task A_duplicate_of_a_duplicate_records_the_item_at_the_root_of_the_chain()
    {
        var store = new InMemoryInboxStore();
        var root = Items.Manual("Read the post");
        var middle = Items.Manual("Read the post again");
        var item = Items.Manual("Read the post, third time");
        store.Seed(root);
        store.Seed(middle);
        store.Seed(item);
        Assert.True((await Archive(store, middle.Id, root.Id)).IsSuccess);

        var result = await Archive(store, item.Id, middle.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(root.Id, item.DuplicateOf);
    }

    [Fact]
    public async Task A_chain_that_leads_back_to_the_item_is_refused_as_circular()
    {
        var store = new InMemoryInboxStore();
        var a = Items.Manual("Read the post");
        var b = Items.Manual("Read the post again");
        var c = Items.Manual("Read the post, third time");
        store.Seed(a);
        store.Seed(b);
        store.Seed(c);
        Assert.True((await Archive(store, b.Id, a.Id)).IsSuccess);
        Assert.True((await Archive(store, c.Id, b.Id)).IsSuccess);
        Assert.Equal(a.Id, c.DuplicateOf);

        var result = await Archive(store, a.Id, c.Id);

        Assert.Equal(InboxErrors.DuplicateCircular, result.Error);
        Assert.Null(a.DuplicateOf);
    }

    [Fact]
    public async Task A_chain_whose_root_was_deleted_ends_at_the_last_item_still_there()
    {
        var store = new InMemoryInboxStore();
        var middle = Items.Manual("Read the post again");
        var item = Items.Manual("Read the post, third time");
        var gone = Guid.CreateVersion7();
        middle.Archive(Items.Noon, gone);
        store.Seed(middle);
        store.Seed(item);

        var result = await Archive(store, item.Id, middle.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(middle.Id, item.DuplicateOf);
    }

    [Fact]
    public async Task An_item_already_archived_is_not_archived_as_a_duplicate()
    {
        var store = new InMemoryInboxStore();
        var original = Items.Manual("Read the post");
        var item = Items.Manual("Read the post again");
        item.Archive(Items.Noon);
        store.Seed(original);
        store.Seed(item);

        var result = await Archive(store, item.Id, original.Id);

        Assert.Equal("inbox.item.invalid_transition", result.Error.Code);
        Assert.Null(item.DuplicateOf);
        Assert.Empty(store.ItemWrites);
    }

    // --- Link to task… -----------------------------------------------------------------

    [Fact]
    public async Task Linking_records_the_item_as_routed_to_the_task_with_its_repositories()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.SetRepoIds(["a/other"]);
        store.Seed(item);
        var task = new InboxTaskReferenceDto(Guid.CreateVersion7(), null, "Ship the parser", ["JSdotNet/Backlog"], [], []);

        var result = await Link(store, item.Id, task.Id, new FakeTaskReferences(task));

        Assert.True(result.IsSuccess);
        Assert.Equal([task.Id], item.Routing!.TaskIds);
        Assert.Equal(["JSdotNet/Backlog"], item.Routing.RepoIds);
        Assert.Equal(Later, item.Routing.RoutedAt);
        Assert.Equal([item.Id], store.ItemWrites);
    }

    [Fact]
    public async Task A_task_the_backlog_no_longer_has_is_refused_and_the_item_is_left_as_it_was()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Link(store, item.Id, Guid.CreateVersion7(), new FakeTaskReferences());

        Assert.Equal(InboxErrors.TaskNotFound, result.Error);
        Assert.False(item.IsRouted);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task Without_the_task_port_the_id_is_taken_as_given_with_the_items_repositories()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.SetRepoIds(["a/one"]);
        store.Seed(item);
        var task = Guid.CreateVersion7();

        var result = await Link(store, item.Id, task);

        Assert.True(result.IsSuccess);
        Assert.Equal([task], item.Routing!.TaskIds);
        Assert.Equal(["a/one"], item.Routing.RepoIds);
    }

    [Fact]
    public async Task A_routed_item_is_not_linked_again()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);
        store.Seed(item);

        var result = await Link(store, item.Id, Guid.CreateVersion7());

        Assert.Equal("inbox.item.invalid_transition", result.Error.Code);
        Assert.Empty(store.ItemWrites);
    }

    // --- The read ------------------------------------------------------------------------

    [Fact]
    public async Task Relations_are_read_against_every_item_and_every_task_and_offer_the_open_tasks()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual("Read the design doc");
        var twin = Items.Manual("Read the design docs");
        store.Seed(item);
        store.Seed(twin);
        var routed = new InboxTaskReferenceDto(Guid.CreateVersion7(), null, "Read the design doc", [], [], [], item.Id, null, IsOpen: false);
        var open = new InboxTaskReferenceDto(Guid.CreateVersion7(), null, "Buy milk", [], [], []);

        var result = await new RelatedQueryHandler(store, new FakeTaskReferences(routed, open))
            .Handle(new RelatedQuery(item.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(item.Id, result.Value.ItemId);
        Assert.Equal(twin.Id, Assert.Single(result.Value.Items).Id);
        var task = Assert.Single(result.Value.Tasks);
        Assert.Equal((routed.Id, InboxRelationKind.RoutedFromItem, false), (task.Id, task.Kind, task.IsOpen));
        Assert.Equal([new InboxTaskOptionDto(open.Id, "Buy milk")], result.Value.OpenTasks);
    }

    [Fact]
    public async Task Without_the_task_port_items_still_relate()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual("Read the design doc");
        store.Seed(item);
        store.Seed(Items.Manual("Read the design doc"));

        var result = await new RelatedQueryHandler(store).Handle(new RelatedQuery(item.Id), TestContext.Current.CancellationToken);

        Assert.Single(result.Value.Items);
        Assert.Empty(result.Value.Tasks);
        Assert.Empty(result.Value.OpenTasks);
    }

    [Fact]
    public async Task Relations_of_an_item_that_is_gone_are_refused()
    {
        var result = await new RelatedQueryHandler(new InMemoryInboxStore())
            .Handle(new RelatedQuery(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        Assert.Equal(InboxErrors.ItemNotFound, result.Error);
    }

    private static Task<Result> Archive(InMemoryInboxStore store, Guid id, Guid duplicateOf) =>
        new ArchiveItemCommandHandler(store, Clock).Handle(new ArchiveItemCommand(id, duplicateOf), TestContext.Current.CancellationToken);

    private static Task<Result> Link(InMemoryInboxStore store, Guid id, Guid taskId, FakeTaskReferences? references = null) =>
        new LinkToTaskCommandHandler(store, Clock, references).Handle(new LinkToTaskCommand(id, taskId), TestContext.Current.CancellationToken);
}
