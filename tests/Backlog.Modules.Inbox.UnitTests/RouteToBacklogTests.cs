using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.RouteToBacklog;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// Routing asks Tasks first and marks the item second, so a failure on the far
/// side leaves the item exactly as it was. What Tasks is asked for is the item's
/// facts in the Inbox's words: no entry text crosses this boundary.
/// </summary>
public sealed class RouteToBacklogTests
{
    private static readonly DateTimeOffset Decision = Items.Noon.AddHours(1);

    [Fact]
    public async Task Two_repositories_make_two_entries_and_the_item_remembers_both()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual("Ship the thing");
        item.SetTags([new InboxTag("deploy", false)]);
        item.SetRepoIds(["jsdotnet/backlog", "jsdotnet/other"]);
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TaskIds.Count);

        var request = Assert.Single(target.Requests);
        Assert.Equal(item.Id, request.InboxItemId);
        Assert.Equal("Ship the thing", request.Title);
        Assert.Equal(["deploy"], request.Tags);
        Assert.Equal(["jsdotnet/backlog", "jsdotnet/other"], request.RepoIds);

        Assert.Equal(InboxStatus.Triaged, item.Status);
        Assert.Equal(result.Value.TaskIds, item.Routing!.TaskIds);
        Assert.Equal(["jsdotnet/backlog", "jsdotnet/other"], item.Routing.RepoIds);
        Assert.Equal(Decision, item.Routing.RoutedAt);
    }

    [Fact]
    public async Task No_repository_makes_one_untargeted_entry()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.TaskIds);
        Assert.Empty(Assert.Single(target.Requests).RepoIds);
        Assert.Empty(item.Routing!.RepoIds);
    }

    [Fact]
    public async Task A_failure_from_tasks_leaves_the_item_untouched()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget { FailWith = Error.Validation("entry.needs_title", "nope") };
        var item = Items.Manual();
        store.Seed(item);

        var result = await Route(store, target, item.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("entry.needs_title", result.Error.Code);
        Assert.Equal(InboxStatus.Unprocessed, item.Status);
        Assert.False(item.IsRouted);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task Routing_twice_costs_no_second_set_of_entries()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);

        await Route(store, target, item.Id);
        var again = await Route(store, target, item.Id);

        Assert.True(again.IsFailure);
        Assert.Equal("inbox.item.invalid_transition", again.Error.Code);
        Assert.Single(target.Requests);
    }

    [Fact]
    public async Task An_item_that_is_not_there_is_not_found()
    {
        var result = await Route(new InMemoryInboxStore(), new FakeBacklogTarget(), Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.item.not_found", result.Error.Code);
    }

    [Fact]
    public async Task A_replica_capture_that_routes_owes_the_replica_an_acknowledgement()
    {
        var store = new InMemoryInboxStore();
        var item = Items.FromPhone();
        store.Seed(item);

        await Route(store, new FakeBacklogTarget(), item.Id);

        Assert.True(item.ReplicaAckPending);
    }

    private static Task<Result<Abstractions.DataTransferObjects.InboxRoutedDto>> Route(
        InMemoryInboxStore store,
        FakeBacklogTarget target,
        Guid id) =>
        new RouteToBacklogCommandHandler(store, target, new FakeTimeProvider(Decision))
            .Handle(new RouteToBacklogCommand(id), TestContext.Current.CancellationToken);
}
