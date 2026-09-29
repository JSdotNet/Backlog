using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Features.ProposeBatch;
using Backlog.Modules.Inbox.Features.RouteBatchToBacklog;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// Merge, don't chain: duplicates inside a batch are offered as one merge, and
/// a merge the person turned on keeps one item — routed — and archives the
/// others as its duplicates, in the one command. Without a merge, nothing about
/// a batch changes.
/// </summary>
public sealed class MergeOnRouteTests
{
    private static readonly DateTimeOffset Decision = Items.Noon.AddHours(1);

    [Fact]
    public async Task A_merge_routes_the_kept_item_alone_and_archives_the_others_as_its_duplicates()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var kept = Items.Manual("Read the design doc");
        var duplicate = Items.Manual("Read the design docs");
        var other = Items.Manual("Buy milk");
        store.Seed(kept);
        store.Seed(duplicate);
        store.Seed(other);

        var result = await Route(store, target, [kept.Id, duplicate.Id, other.Id], new InboxBatchRouteChoicesDto(
            Merges: new Dictionary<Guid, IReadOnlyList<Guid>> { [kept.Id] = [duplicate.Id] }));

        Assert.True(result.IsSuccess);
        Assert.Equal([kept.Id, other.Id], Assert.Single(target.BatchRequests).Items.Select(item => item.InboxItemId));
        Assert.Equal([kept.Id, other.Id], result.Value.Routed.Select(routed => routed.InboxItemId));
        Assert.Equal([duplicate.Id], result.Value.Archived);
        Assert.Empty(result.Value.Failed);

        Assert.True(kept.IsRouted);
        Assert.Equal(InboxStatus.Archived, duplicate.Status);
        Assert.Equal(kept.Id, duplicate.DuplicateOf);
        Assert.Contains(duplicate.Id, store.ItemWrites);
    }

    [Fact]
    public async Task A_dependency_on_a_merged_item_is_carried_onto_the_kept_one()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var kept = Items.Manual("Read the design doc");
        var duplicate = Items.Manual("Read the design docs");
        var after = Items.Manual("Plan the talk");
        store.Seed(kept);
        store.Seed(duplicate);
        store.Seed(after);

        var result = await Route(store, target, [after.Id, kept.Id, duplicate.Id], new InboxBatchRouteChoicesDto(
            Dependencies: [new ProposedDependency(after.Id, DependencyTarget.ForItem(duplicate.Id, duplicate.Title), "the docs")],
            Merges: Merge(kept.Id, duplicate.Id)));

        Assert.True(result.IsSuccess);
        var request = Assert.Single(target.BatchRequests);
        Assert.Equal([kept.Id, after.Id], request.Items.Select(item => item.InboxItemId));
        Assert.Equal([kept.Id], request.Items.Single(item => item.InboxItemId == after.Id).AfterItems);
    }

    [Fact]
    public async Task Dependencies_from_a_merged_item_are_carried_onto_the_kept_one_for_items_and_tasks_alike()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var kept = Items.Manual("Read the design doc");
        var duplicate = Items.Manual("Read the design docs");
        var first = Items.Manual("Set up the pipeline");
        store.Seed(kept);
        store.Seed(duplicate);
        store.Seed(first);
        var task = new DependencyTarget(DependencyTargetKind.Task, Guid.CreateVersion7(), "Ship the parser", "0199-parser");

        var result = await Route(store, target, [kept.Id, duplicate.Id, first.Id], new InboxBatchRouteChoicesDto(
            Dependencies:
            [
                new ProposedDependency(duplicate.Id, DependencyTarget.ForItem(first.Id, first.Title), "the pipeline"),
                new ProposedDependency(duplicate.Id, task, "#12"),
                new ProposedDependency(kept.Id, task, "#12"),
            ],
            Merges: Merge(kept.Id, duplicate.Id)));

        Assert.True(result.IsSuccess);
        var request = Assert.Single(target.BatchRequests);
        Assert.Equal([first.Id, kept.Id], request.Items.Select(item => item.InboxItemId));
        var sent = request.Items.Single(item => item.InboxItemId == kept.Id);
        Assert.Equal([first.Id], sent.AfterItems);
        Assert.Equal(["0199-parser"], sent.AfterTasks);
    }

    [Fact]
    public async Task A_carried_dependency_between_the_merged_pair_is_dropped_and_a_carried_loop_is_refused()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var kept = Items.Manual("Read the design doc");
        var duplicate = Items.Manual("Read the design docs");
        var other = Items.Manual("Plan the talk");
        store.Seed(kept);
        store.Seed(duplicate);
        store.Seed(other);

        // The pair waiting on each other folds into the kept item waiting on
        // itself, which is no dependency at all.
        var pair = await Route(store, target, [kept.Id, duplicate.Id], new InboxBatchRouteChoicesDto(
            Dependencies: [new ProposedDependency(duplicate.Id, DependencyTarget.ForItem(kept.Id, kept.Title), "the doc")],
            Merges: Merge(kept.Id, duplicate.Id)));

        Assert.True(pair.IsSuccess);
        Assert.All(target.BatchRequests[0].Items, item => Assert.Empty(item.AfterItems ?? []));

        // The kept item after the other, and the other after the duplicate, is
        // a loop once the duplicate's place is the kept item's.
        var store2 = new InMemoryInboxStore();
        var kept2 = Items.Manual("Read the design doc");
        var duplicate2 = Items.Manual("Read the design docs");
        var other2 = Items.Manual("Plan the talk");
        store2.Seed(kept2);
        store2.Seed(duplicate2);
        store2.Seed(other2);
        var looped = await Route(store2, new FakeBacklogTarget(), [kept2.Id, duplicate2.Id, other2.Id], new InboxBatchRouteChoicesDto(
            Dependencies:
            [
                new ProposedDependency(kept2.Id, DependencyTarget.ForItem(other2.Id, other2.Title), "the talk"),
                new ProposedDependency(other2.Id, DependencyTarget.ForItem(duplicate2.Id, duplicate2.Title), "the docs"),
            ],
            Merges: Merge(kept2.Id, duplicate2.Id)));

        Assert.True(looped.IsFailure);
        Assert.Equal("inbox.batch.dependency_loop", looped.Error.Code);
    }

    private static Dictionary<Guid, IReadOnlyList<Guid>> Merge(Guid kept, params Guid[] duplicates) =>
        new() { [kept] = duplicates };

    [Fact]
    public async Task When_the_kept_item_is_not_routed_its_duplicates_stay_in_the_inbox_and_are_named()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var kept = Items.Manual("Read the design doc");
        var duplicate = Items.Manual("Read the design docs");
        var other = Items.Manual("Buy milk");
        store.Seed(kept);
        store.Seed(duplicate);
        store.Seed(other);
        target.RefuseItems[kept.Id] = InboxErrors.BatchItemNotSeparable;

        var result = await Route(store, target, [kept.Id, duplicate.Id, other.Id], new InboxBatchRouteChoicesDto(
            Merges: new Dictionary<Guid, IReadOnlyList<Guid>> { [kept.Id] = [duplicate.Id] }));

        Assert.Empty(result.Value.Archived);
        Assert.Equal(InboxStatus.Unprocessed, duplicate.Status);
        Assert.Null(duplicate.DuplicateOf);
        Assert.Equal(InboxErrors.BatchMergeNotRouted, result.Value.Failed.Single(failure => failure.Id == duplicate.Id).Error);
    }

    [Fact]
    public async Task When_tasks_refuses_the_document_nothing_is_archived_either()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget { FailWith = Error.Validation("tasks.refused", "No.") };
        var kept = Items.Manual("Read the design doc");
        var duplicate = Items.Manual("Read the design docs");
        store.Seed(kept);
        store.Seed(duplicate);

        var result = await Route(store, target, [kept.Id, duplicate.Id], new InboxBatchRouteChoicesDto(
            Merges: new Dictionary<Guid, IReadOnlyList<Guid>> { [kept.Id] = [duplicate.Id] }));

        Assert.Empty(result.Value.Archived);
        Assert.Equal(InboxStatus.Unprocessed, duplicate.Status);
        Assert.Equal([kept.Id, duplicate.Id], result.Value.Failed.Select(failure => failure.Id));
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task A_merge_naming_an_item_outside_the_batch_or_the_kept_item_itself_is_ignored()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var kept = Items.Manual("Read the design doc");
        var other = Items.Manual("Buy milk");
        store.Seed(kept);
        store.Seed(other);

        var result = await Route(store, target, [kept.Id, other.Id], new InboxBatchRouteChoicesDto(
            Merges: new Dictionary<Guid, IReadOnlyList<Guid>> { [kept.Id] = [kept.Id, Guid.CreateVersion7()] }));

        Assert.Equal([kept.Id, other.Id], result.Value.Routed.Select(routed => routed.InboxItemId));
        Assert.Empty(result.Value.Archived);
    }

    [Fact]
    public async Task Without_a_merge_duplicates_are_routed_as_they_always_were()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var first = Items.Manual("Read the design doc");
        var second = Items.Manual("Read the design docs");
        store.Seed(first);
        store.Seed(second);

        var result = await Route(store, target, [first.Id, second.Id], new InboxBatchRouteChoicesDto());

        Assert.Equal([first.Id, second.Id], result.Value.Routed.Select(routed => routed.InboxItemId));
        Assert.Empty(result.Value.Archived);
    }

    [Fact]
    public async Task The_proposal_offers_a_duplicate_group_as_a_hint_and_proposes_no_edge_inside_it()
    {
        var store = new InMemoryInboxStore();
        var first = Items.Manual("Read the design doc");

        // Its notes name the first item's title, which on its own is a tier-one
        // edge; inside a duplicate group it is the same thought captured twice.
        var second = new DomainModels.InboxItem(Guid.CreateVersion7(), "Read the design docs", "After Read the design doc.", null,
            Items.Noon, Items.Noon, ContentKind.Text, new DomainModels.InboxSource(InboxEnumMap.ManualChannel, null), replicaBacked: false);
        store.Seed(first);
        store.Seed(second);

        var result = await new ProposeBatchQueryHandler(store, store)
            .Handle(new ProposeBatchQuery([first.Id, second.Id]), TestContext.Current.CancellationToken);

        var hint = Assert.Single(result.Value.Hints, hint => hint.Kind == OrderingHintKind.Duplicate);
        Assert.Equal([first.Id, second.Id], hint.Items);
        Assert.Empty(result.Value.Dependencies);
    }

    [Fact]
    public async Task A_proposal_of_one_item_carries_no_hints()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual("Set up the pipeline");
        store.Seed(item);

        var result = await new ProposeBatchQueryHandler(store, store)
            .Handle(new ProposeBatchQuery([item.Id]), TestContext.Current.CancellationToken);

        Assert.Empty(result.Value.Hints);
    }

    private static Task<Result<InboxBatchRoutedDto>> Route(
        InMemoryInboxStore store,
        FakeBacklogTarget target,
        IReadOnlyList<Guid> ids,
        InboxBatchRouteChoicesDto? choices = null) =>
        new RouteBatchToBacklogCommandHandler(store, store, target, new FakeTimeProvider(Decision))
            .Handle(new RouteBatchToBacklogCommand(ids, null, choices), TestContext.Current.CancellationToken);
}
