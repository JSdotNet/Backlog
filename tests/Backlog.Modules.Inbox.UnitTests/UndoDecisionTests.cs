using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.ArchiveItem;
using Backlog.Modules.Inbox.Features.MergeIntoTask;
using Backlog.Modules.Inbox.Features.RestoreItem;
using Backlog.Modules.Inbox.Features.ReturnToInbox;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The module's two ways back for the session undo history: Restore takes an
/// archive back — plain, as a duplicate, or a merge into a task — and Return to
/// inbox takes a route or a link back, deleting the entries a route made unless
/// one of them has started. Either way the item ends unprocessed.
/// </summary>
public sealed class UndoDecisionTests
{
    private static readonly DateTimeOffset Undone = Items.Noon.AddHours(2);

    [Fact]
    public async Task Restoring_an_archived_item_makes_it_unprocessed_again()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.Archive(Items.Noon);
        store.Seed(item);

        var result = await Restore(store, item.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.True(item.IsOpen);
        Assert.Equal(Undone, item.UpdatedAt);
        Assert.Contains(item.Id, store.ItemWrites);
    }

    [Fact]
    public async Task Restoring_a_merged_capture_clears_DuplicateOf_and_leaves_the_comment_on_the_task()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual("Scroll jumps to top");
        store.Seed(item);
        var taskId = Guid.CreateVersion7();
        var clock = new FakeTimeProvider(Items.Noon);
        var merged = await new MergeIntoTaskCommandHandler(store, target, new ArchiveItemCommandHandler(store, clock))
            .Handle(new MergeIntoTaskCommand(item.Id, taskId), TestContext.Current.CancellationToken);
        Assert.True(merged.IsSuccess);

        var result = await Restore(store, item.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.Null(item.DuplicateOf);
        Assert.False(item.DuplicateOfTask);
        // Nothing asked the backlog to take the comment back.
        Assert.Single(target.Comments);
        Assert.Empty(target.Deletions);
    }

    [Fact]
    public async Task Restoring_withdraws_an_acknowledgement_the_outbox_has_not_sent()
    {
        var store = new InMemoryInboxStore();
        var item = Items.FromPhone();
        item.Archive(Items.Noon);
        Assert.True(item.ReplicaAckPending);
        store.Seed(item);

        await Restore(store, item.Id);

        Assert.False(item.ReplicaAckPending);
    }

    [Fact]
    public async Task Restoring_an_item_that_is_not_archived_is_refused_and_writes_nothing()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Restore(store, item.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.item.invalid_transition", result.Error.Code);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task Returning_a_routed_item_deletes_the_tasks_the_route_made_and_makes_it_unprocessed()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        Guid[] made = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        item.RouteToBacklog(made, ["Backlog", "Site"], Items.Noon);
        store.Seed(item);

        var result = await Return(store, target, item.Id, deleteTasks: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(made, Assert.Single(target.Deletions));
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.False(item.IsRouted);
        Assert.Equal(Undone, item.UpdatedAt);
        Assert.Contains(item.Id, store.ItemWrites);
    }

    [Fact]
    public async Task Returning_withdraws_an_acknowledgement_the_outbox_has_not_sent()
    {
        var store = new InMemoryInboxStore();
        var item = Items.FromPhone();
        item.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);
        Assert.True(item.ReplicaAckPending);
        store.Seed(item);

        await Return(store, new FakeBacklogTarget(), item.Id, deleteTasks: true);

        Assert.False(item.ReplicaAckPending);
    }

    [Fact]
    public async Task A_started_task_refuses_the_return_and_the_item_stays_routed()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget { FailDeleteWith = InboxErrors.UndoTaskStarted("Call the dentist") };
        var item = Items.Manual();
        item.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);
        store.Seed(item);

        var result = await Return(store, target, item.Id, deleteTasks: true);

        Assert.True(result.IsFailure);
        Assert.Equal(InboxErrors.UndoTaskStartedCode, result.Error.Code);
        Assert.Equal("Can't undo — Call the dentist has started", result.Error.Message);
        Assert.True(item.IsRouted);
        Assert.Equal(InboxStatus.Triaged, item.Status);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task Returning_a_linked_item_leaves_the_task_it_named_alone()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        item.LinkToTask(Guid.CreateVersion7(), [], Items.Noon);
        store.Seed(item);

        var result = await Return(store, target, item.Id, deleteTasks: false);

        Assert.True(result.IsSuccess);
        Assert.Empty(target.Deletions);
        Assert.False(item.IsRouted);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
    }

    [Fact]
    public async Task Returning_an_item_that_was_never_routed_is_refused_before_the_backlog_is_asked()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Return(store, target, item.Id, deleteTasks: true);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.item.invalid_transition", result.Error.Code);
        Assert.Empty(target.Deletions);
    }

    [Fact]
    public async Task An_unknown_item_is_not_found_both_ways()
    {
        var store = new InMemoryInboxStore();

        Assert.Equal(InboxErrors.ItemNotFound, (await Restore(store, Guid.CreateVersion7())).Error);
        Assert.Equal(InboxErrors.ItemNotFound, (await Return(store, new FakeBacklogTarget(), Guid.CreateVersion7(), true)).Error);
    }

    private static Task<Result> Restore(InMemoryInboxStore store, Guid id) =>
        new RestoreItemCommandHandler(store, new FakeTimeProvider(Undone))
            .Handle(new RestoreItemCommand(id), TestContext.Current.CancellationToken);

    private static Task<Result> Return(InMemoryInboxStore store, FakeBacklogTarget target, Guid id, bool deleteTasks) =>
        new ReturnToInboxCommandHandler(store, target, new FakeTimeProvider(Undone))
            .Handle(new ReturnToInboxCommand(id, deleteTasks), TestContext.Current.CancellationToken);
}
