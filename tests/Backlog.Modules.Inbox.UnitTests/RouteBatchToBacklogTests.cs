using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.RouteBatchToBacklog;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// A batch is one import, not one routing per item: one request to Tasks under
/// one new plan tag, all of it or none of it. What cannot go is refused before
/// Tasks is asked, and named; what can goes together, each item remembering
/// only the entries made from it.
/// </summary>
public sealed class RouteBatchToBacklogTests
{
    private static readonly DateTimeOffset Decision = Items.Noon.AddHours(1);

    private const string TagShape = @"^\+[a-z0-9-]+-[0-9a-f]{8}$";

    [Fact]
    public async Task A_selection_routes_as_one_inbox_batch_and_each_item_keeps_only_its_own_entries()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var first = Items.Manual("First");
        var second = Items.Manual("Second");
        store.Seed(first);
        store.Seed(second);

        var result = await Route(store, target, [first.Id, second.Id]);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(target.BatchRequests);
        Assert.Empty(target.Requests);
        Assert.Matches(TagShape, request.PlanTag);
        Assert.StartsWith("+inbox-batch-", request.PlanTag, StringComparison.Ordinal);
        Assert.Equal(request.PlanTag, result.Value.PlanTag);
        Assert.Equal([first.Id, second.Id], request.Items.Select(item => item.InboxItemId));

        Assert.Equal([first.Id, second.Id], result.Value.Routed.Select(routed => routed.InboxItemId));
        Assert.Empty(result.Value.Failed);

        foreach (var (item, routed) in new[] { first, second }.Zip(result.Value.Routed))
        {
            Assert.Equal(InboxStatus.Triaged, item.Status);
            Assert.Equal(routed.TaskIds, item.Routing!.TaskIds);
            Assert.Equal(Decision, item.Routing.RoutedAt);
        }

        Assert.Empty(first.Routing!.TaskIds.Intersect(second.Routing!.TaskIds));
        Assert.Equal([first.Id, second.Id], store.ItemWrites);
    }

    [Fact]
    public async Task Each_item_goes_with_its_own_facts_and_repositories()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var both = Items.Manual("Ship the thing");
        both.SetTags([new InboxTag("deploy", false)]);
        both.SetRepoIds(["jsdotnet/backlog", "jsdotnet/other"]);
        var none = Items.Manual("Think about it");
        store.Seed(both);
        store.Seed(none);

        var result = await Route(store, target, [both.Id, none.Id]);

        var request = Assert.Single(target.BatchRequests);
        var shipped = request.Items[0];
        Assert.Equal("Ship the thing", shipped.Title);
        Assert.Equal(["deploy"], shipped.Tags);
        Assert.Equal(["jsdotnet/backlog", "jsdotnet/other"], shipped.RepoIds);
        Assert.Empty(request.Items[1].RepoIds);

        Assert.Equal(2, both.Routing!.TaskIds.Count);
        Assert.Equal(["jsdotnet/backlog", "jsdotnet/other"], both.Routing.RepoIds);
        Assert.Single(none.Routing!.TaskIds);
        Assert.Equal(result.Value.Routed[0].TaskIds, both.Routing.TaskIds);
    }

    [Fact]
    public async Task Routed_archived_and_missing_items_are_refused_up_front_and_the_rest_still_go()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var open = Items.Manual("Open");
        var routed = Items.Manual("Already routed");
        routed.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);
        var archived = Items.Manual("Archived");
        archived.Archive(Items.Noon);
        var gone = Guid.CreateVersion7();
        store.Seed(open);
        store.Seed(routed);
        store.Seed(archived);

        var result = await Route(store, target, [routed.Id, open.Id, gone, archived.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal([open.Id], Assert.Single(target.BatchRequests).Items.Select(item => item.InboxItemId));
        Assert.Equal([open.Id], result.Value.Routed.Select(item => item.InboxItemId));

        Assert.Equal([routed.Id, gone, archived.Id], result.Value.Failed.Select(failure => failure.Id));
        Assert.Equal("inbox.item.invalid_transition", result.Value.Failed[0].Error.Code);
        Assert.Equal(InboxErrors.ItemNotFound, result.Value.Failed[1].Error);
        Assert.Equal("inbox.item.invalid_transition", result.Value.Failed[2].Error.Code);

        Assert.Equal(InboxStatus.Triaged, open.Status);
        Assert.Equal(InboxStatus.Archived, archived.Status);
        Assert.Equal([open.Id], store.ItemWrites);
    }

    [Fact]
    public async Task A_refusal_from_tasks_routes_nothing_and_says_the_whole_batch_was_refused()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget { FailWith = Error.Validation("import.duplicate_item_id", "Two entries both claim it.") };
        var first = Items.Manual("First");
        var second = Items.Manual("Second");
        var routed = Items.Manual("Already routed");
        routed.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);
        store.Seed(first);
        store.Seed(second);
        store.Seed(routed);

        var result = await Route(store, target, [first.Id, routed.Id, second.Id]);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Routed);
        Assert.Equal([first.Id, routed.Id, second.Id], result.Value.Failed.Select(failure => failure.Id));

        var refusal = result.Value.Failed[0].Error;
        Assert.Equal("inbox.batch.refused", refusal.Code);
        Assert.Contains("nothing was routed", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("every item is still in the Inbox", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Two entries both claim it.", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(refusal, result.Value.Failed[2].Error);
        Assert.Equal("inbox.item.invalid_transition", result.Value.Failed[1].Error.Code);

        Assert.False(first.IsRouted);
        Assert.False(second.IsRouted);
        Assert.Equal(InboxStatus.Unprocessed, first.Status);
        Assert.Empty(store.ItemWrites);
    }

    /// <summary>An item the adapter left out of the document — notes it could
    /// not keep apart, a repository the workspace does not know — is named with
    /// its own reason and stays unrouted; the rest of the batch still goes.</summary>
    [Fact]
    public async Task An_item_the_target_leaves_out_is_named_with_its_own_reason_and_the_rest_still_go()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var first = Items.Manual("First");
        var odd = Items.Manual("Odd one");
        var last = Items.Manual("Last");
        store.Seed(first);
        store.Seed(odd);
        store.Seed(last);
        target.RefuseItems[odd.Id] = InboxErrors.BatchUnknownRepository("gone/away");

        var result = await Route(store, target, [first.Id, odd.Id, last.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal([first.Id, last.Id], result.Value.Routed.Select(routed => routed.InboxItemId));
        var failure = Assert.Single(result.Value.Failed);
        Assert.Equal(odd.Id, failure.Id);
        Assert.Equal(InboxErrors.BatchUnknownRepository("gone/away"), failure.Error);
        Assert.False(odd.IsRouted);
        Assert.Equal([first.Id, last.Id], store.ItemWrites);
    }

    /// <summary>Tasks' refusal of the document is every sent item's, and only
    /// theirs: an item the adapter had already left out keeps its own reason.</summary>
    [Fact]
    public async Task A_refusal_from_tasks_is_wrapped_only_for_the_items_that_were_sent()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget { FailWith = Error.Validation("import.empty_plan", "Nothing parsed.") };
        var sent = Items.Manual("Sent");
        var left = Items.Manual("Left out");
        store.Seed(sent);
        store.Seed(left);
        target.RefuseItems[left.Id] = InboxErrors.BatchItemNotSeparable;

        var result = await Route(store, target, [sent.Id, left.Id]);

        Assert.Equal(InboxErrors.BatchRefusedCode, result.Value.Failed[0].Error.Code);
        Assert.Equal(InboxErrors.BatchItemNotSeparable, result.Value.Failed[1].Error);
        Assert.Empty(store.ItemWrites);
    }

    /// <summary>A missing entry happens after Tasks wrote the rest, so it is not
    /// "nothing was routed": it passes through under its own code.</summary>
    [Fact]
    public async Task A_missing_entry_is_passed_through_not_wrapped_as_a_refusal()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);
        var missing = InboxErrors.BatchItemMissing([]);
        target.RefuseItems[item.Id] = missing;

        var result = await Route(store, target, [item.Id]);

        Assert.Equal(missing, Assert.Single(result.Value.Failed).Error);
        Assert.Equal("inbox.batch.item_missing", missing.Code);
    }

    /// <summary>The import made the entries; a save that fails afterwards
    /// leaves that item looking unrouted. So it is named with the entries made
    /// for it, the items saved before it stay routed, and the ones after it are
    /// still tried.</summary>
    [Fact]
    public async Task A_save_that_fails_after_the_import_names_the_entries_already_made_and_keeps_the_rest()
    {
        var store = new InMemoryInboxStore { FailOnSave = 2 };
        var target = new FakeBacklogTarget();
        var first = Items.Manual("First");
        var second = Items.Manual("Second");
        var third = Items.Manual("Third");
        store.Seed(first);
        store.Seed(second);
        store.Seed(third);

        var result = await Route(store, target, [first.Id, second.Id, third.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal([first.Id, third.Id], result.Value.Routed.Select(routed => routed.InboxItemId));
        Assert.Equal([first.Id, third.Id], store.ItemWrites);

        var failure = Assert.Single(result.Value.Failed);
        Assert.Equal(second.Id, failure.Id);
        Assert.Equal("inbox.batch.save_failed", failure.Error.Code);
        Assert.Contains("The disk is full.", failure.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Routing it again would make them twice", failure.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_save_failure_message_names_every_entry_made_for_the_item()
    {
        var ids = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };

        var error = InboxErrors.BatchSaveFailed(ids, "The disk is full.");

        Assert.Equal(
            $"Its entries are in the backlog but it could not be marked routed (The disk is full.). Routing it again would make them twice; the 2 made for it: {ids[0]:D}, {ids[1]:D}.",
            error.Message);
    }

    [Fact]
    public async Task A_list_route_is_tagged_with_the_list_name_and_leaves_the_list_standing()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var list = InboxList.Create("Reading List", null, 0, Items.Noon);
        store.Seed(list);
        var item = Items.Manual("An article");
        item.MoveToList(list.Id);
        store.Seed(item);

        var result = await Route(store, target, [item.Id], list.Id);

        Assert.True(result.IsSuccess);
        var tag = Assert.Single(target.BatchRequests).PlanTag;
        Assert.Matches(TagShape, tag);
        Assert.StartsWith("+reading-list-", tag, StringComparison.Ordinal);
        Assert.True(store.Lists.ContainsKey(list.Id));
        Assert.True(item.IsRouted);
    }

    /// <summary>The pane shows the tag before the module mints it: the sigil
    /// and the slug, with the digits left out.</summary>
    [Theory]
    [InlineData("Reading List", "+reading-list-…")]
    [InlineData("!!!", "+inbox-plan-…")]
    [InlineData("2026 goals", "+plan-2026-goals-…")]
    public void The_preview_is_the_tag_without_its_digits(string name, string preview)
    {
        Assert.Equal(preview, InboxPlanTag.Preview(name));
        Assert.StartsWith(preview[..^1], "+" + InboxPlanTag.For(name, Guid.CreateVersion7()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_batch_is_a_new_plan()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var first = Items.Manual("First");
        var second = Items.Manual("Second");
        store.Seed(first);
        store.Seed(second);

        await Route(store, target, [first.Id]);
        await Route(store, target, [second.Id]);

        Assert.NotEqual(target.BatchRequests[0].PlanTag, target.BatchRequests[1].PlanTag);
    }

    [Fact]
    public async Task A_list_that_is_gone_is_not_found_and_nothing_is_asked()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Route(store, target, [item.Id], Guid.CreateVersion7());

        Assert.True(result.IsFailure);
        Assert.Equal(InboxErrors.ListNotFound, result.Error);
        Assert.Empty(target.BatchRequests);
        Assert.False(item.IsRouted);
    }

    [Fact]
    public async Task Nothing_routable_asks_tasks_nothing()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var routed = Items.Manual("Already routed");
        routed.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);
        store.Seed(routed);

        var result = await Route(store, target, [routed.Id]);

        Assert.True(result.IsSuccess);
        Assert.Empty(target.BatchRequests);
        Assert.Empty(result.Value.Routed);
        Assert.Equal(routed.Id, Assert.Single(result.Value.Failed).Id);
    }

    private static Task<Result<InboxBatchRoutedDto>> Route(
        InMemoryInboxStore store,
        FakeBacklogTarget target,
        IReadOnlyList<Guid> ids,
        Guid? listId = null) =>
        new RouteBatchToBacklogCommandHandler(store, store, target, new FakeTimeProvider(Decision))
            .Handle(new RouteBatchToBacklogCommand(ids, listId), TestContext.Current.CancellationToken);
}
