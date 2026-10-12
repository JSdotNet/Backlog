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

    /// <summary>A batch carries each item whole — its id and every repository —
    /// so the adapter can link a two- and a three-repository item's siblings
    /// inside the one document. The text is pinned in
    /// <c>InboxBacklogTargetTests</c>.</summary>
    [Fact]
    public async Task Two_and_three_repository_items_go_whole_so_their_siblings_can_name_each_other()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var two = Items.Manual("Ship the thing");
        two.SetRepoIds(["jsdotnet/backlog", "jsdotnet/other"]);
        var three = Items.Manual("Read the contract");
        three.SetRepoIds(["jsdotnet/backlog", "jsdotnet/other", "jsdotnet/third"]);
        store.Seed(two);
        store.Seed(three);

        await Route(store, target, [two.Id, three.Id]);

        var request = Assert.Single(target.BatchRequests);
        Assert.Equal([two.Id, three.Id], request.Items.Select(item => item.InboxItemId));
        Assert.Equal(["jsdotnet/backlog", "jsdotnet/other"], request.Items[0].RepoIds);
        Assert.Equal(["jsdotnet/backlog", "jsdotnet/other", "jsdotnet/third"], request.Items[1].RepoIds);
        Assert.Equal(2, two.Routing!.TaskIds.Count);
        Assert.Equal(3, three.Routing!.TaskIds.Count);
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

    // --- The panel's choices ----------------------------------------------------

    [Fact]
    public async Task The_proposals_tag_is_the_one_the_import_writes()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Route(store, target, [item.Id], choices: new InboxBatchRouteChoicesDto(PlanTag: " +inbox-batch-1a2b3c4d "));

        Assert.Equal("+inbox-batch-1a2b3c4d", Assert.Single(target.BatchRequests).PlanTag);
        Assert.Equal("+inbox-batch-1a2b3c4d", result.Value.PlanTag);
    }

    [Theory]
    [InlineData("inbox-batch-1a2b3c4d")]
    [InlineData("+2026-plan")]
    [InlineData("+two words")]
    public async Task A_tag_that_is_not_a_plan_tag_is_refused_before_tasks_is_asked(string tag)
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Route(store, target, [item.Id], choices: new InboxBatchRouteChoicesDto(PlanTag: tag));

        Assert.Equal("inbox.batch.plan_tag_invalid", result.Error.Code);
        Assert.Empty(target.BatchRequests);
        Assert.False(item.IsRouted);
    }

    [Fact]
    public async Task A_repository_chosen_in_the_panel_is_where_the_item_goes_and_what_its_routing_records()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var moved = Items.Manual("Moved");
        moved.SetRepoIds(["a/one"]);
        var kept = Items.Manual("Kept");
        kept.SetRepoIds(["a/one"]);
        store.Seed(moved);
        store.Seed(kept);
        var choices = new InboxBatchRouteChoicesDto(Repositories: new Dictionary<Guid, IReadOnlyList<string>>
        {
            [moved.Id] = ["a/two", " a/three ", "A/TWO", ""],
        });

        await Route(store, target, [moved.Id, kept.Id], choices: choices);

        var request = Assert.Single(target.BatchRequests);
        Assert.Equal(["a/two", "a/three"], request.Items[0].RepoIds);
        Assert.Equal(["a/one"], request.Items[1].RepoIds);
        Assert.Equal(["a/two", "a/three"], moved.Routing!.RepoIds);
        Assert.Equal(2, moved.Routing.TaskIds.Count);
        Assert.Equal(["a/one"], moved.RepoIds);
        Assert.Equal(["a/one"], kept.Routing!.RepoIds);
    }

    [Fact]
    public async Task Confirmed_dependencies_go_to_the_target_as_facts_and_put_the_document_in_their_order()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var later = Items.Manual("Deploy the preview");
        var first = Items.Manual("Set up the pipeline");
        store.Seed(later);
        store.Seed(first);
        var task = new InboxTaskReferenceDto(Guid.CreateVersion7(), "0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b", "Earlier task", [], [], []);
        var choices = new InboxBatchRouteChoicesDto(Dependencies:
        [
            new ProposedDependency(later.Id, DependencyTarget.ForItem(first.Id, first.Title), "set up the pipeline"),
            new ProposedDependency(later.Id, DependencyTarget.ForTask(task), "#12"),
        ]);

        var result = await Route(store, target, [later.Id, first.Id], choices: choices);

        var request = Assert.Single(target.BatchRequests);
        Assert.Equal([first.Id, later.Id], request.Items.Select(item => item.InboxItemId));
        Assert.Empty(request.Items[0].AfterItems ?? []);
        Assert.Equal([first.Id], request.Items[1].AfterItems);
        Assert.Equal(["0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b"], request.Items[1].AfterTasks);

        // Named back in the order asked, whatever order the document took.
        Assert.Equal([later.Id, first.Id], result.Value.Routed.Select(routed => routed.InboxItemId));
    }

    [Fact]
    public async Task A_dependency_on_an_item_that_is_not_going_is_dropped()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual("Going");
        var routed = Items.Manual("Already routed");
        routed.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);
        store.Seed(item);
        store.Seed(routed);
        var choices = new InboxBatchRouteChoicesDto(Dependencies:
        [
            new ProposedDependency(item.Id, DependencyTarget.ForItem(routed.Id, routed.Title), "Already routed"),
        ]);

        await Route(store, target, [item.Id, routed.Id], choices: choices);

        Assert.Empty(Assert.Single(Assert.Single(target.BatchRequests).Items).AfterItems ?? []);
    }

    [Fact]
    public async Task A_loop_among_the_confirmed_dependencies_refuses_the_batch_and_names_it_by_titles()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var notes = Items.Manual("Write the release notes");
        var ship = Items.Manual("Ship the release");
        store.Seed(notes);
        store.Seed(ship);
        var choices = new InboxBatchRouteChoicesDto(Dependencies:
        [
            new ProposedDependency(notes.Id, DependencyTarget.ForItem(ship.Id, ship.Title), "ship the release"),
            new ProposedDependency(ship.Id, DependencyTarget.ForItem(notes.Id, notes.Title), "Write the release notes"),
        ]);

        var result = await Route(store, target, [notes.Id, ship.Id], choices: choices);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.batch.dependency_loop", result.Error.Code);
        Assert.Contains("Write the release notes → Ship the release → Write the release notes", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("nothing was routed", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(target.BatchRequests);
        Assert.Empty(store.ItemWrites);
    }

    /// <summary>A task edge's value is written into a metadata token as it is,
    /// so one that is empty, has a space, or has a backtick would be a token that
    /// reads back as something else. Refused whole, before Tasks is asked.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two words")]
    [InlineData("tick`tock")]
    [InlineData(null)]
    public async Task A_task_dependency_whose_value_cannot_be_a_token_refuses_the_batch(string? after)
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual("Waits");
        store.Seed(item);
        var choices = new InboxBatchRouteChoicesDto(Dependencies:
        [
            new ProposedDependency(item.Id, new DependencyTarget(DependencyTargetKind.Task, Guid.CreateVersion7(), "A task", after), "#12"),
        ]);

        var result = await Route(store, target, [item.Id], choices: choices);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.batch.dependency_invalid", result.Error.Code);
        Assert.Contains("A task", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(target.BatchRequests);
        Assert.False(item.IsRouted);
    }

    private static Task<Result<InboxBatchRoutedDto>> Route(
        InMemoryInboxStore store,
        FakeBacklogTarget target,
        IReadOnlyList<Guid> ids,
        Guid? listId = null,
        InboxBatchRouteChoicesDto? choices = null) =>
        new RouteBatchToBacklogCommandHandler(store, store, target, new FakeTimeProvider(Decision))
            .Handle(new RouteBatchToBacklogCommand(ids, listId, choices), TestContext.Current.CancellationToken);
}
