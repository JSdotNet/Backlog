using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.ArchiveItem;
using Backlog.Modules.Inbox.Features.AssignRepositories;
using Backlog.Modules.Inbox.Features.MoveToList;
using Backlog.Modules.Inbox.Features.SetTags;
using Backlog.Modules.Inbox.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The four acts across a selection, through the published port: each runs the
/// single-item command once per item, so every rule the command enforces holds
/// per item, and a refusal names its item without stopping the rest.
/// </summary>
public sealed class BatchTests
{
    private static readonly FakeTimeProvider Clock = new(Items.Noon);

    [Fact]
    public async Task Archiving_several_archives_each_and_names_the_one_its_command_refused()
    {
        var store = new InMemoryInboxStore();
        var first = Items.Manual("First");
        var routed = Items.Manual("Already routed");
        routed.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);
        var last = Items.Manual("Last");
        store.Seed(first);
        store.Seed(routed);
        store.Seed(last);

        var result = await Port(store).ArchiveAsync([first.Id, routed.Id, last.Id], TestContext.Current.CancellationToken);

        Assert.Equal([first.Id, last.Id], result.Changed);
        var failure = Assert.Single(result.Failed);
        Assert.Equal(routed.Id, failure.Id);
        Assert.Equal("inbox.item.invalid_transition", failure.Error.Code);

        Assert.Equal(InboxStatus.Archived, store.Items[first.Id].Status);
        Assert.Equal(InboxStatus.Archived, store.Items[last.Id].Status);
        Assert.Equal(InboxStatus.Triaged, store.Items[routed.Id].Status);
    }

    [Fact]
    public async Task An_item_that_has_gone_is_named_and_the_rest_still_move()
    {
        var store = new InMemoryInboxStore();
        var list = InboxList.Create("Reading", null, 0, Items.Noon);
        store.Seed(list);
        var kept = Items.Manual("Kept");
        store.Seed(kept);
        var gone = Guid.CreateVersion7();

        var result = await Port(store).MoveToListAsync([gone, kept.Id], list.Id, TestContext.Current.CancellationToken);

        Assert.Equal([kept.Id], result.Changed);
        Assert.Equal(InboxErrors.ItemNotFound, Assert.Single(result.Failed).Error);
        Assert.Equal(list.Id, store.Items[kept.Id].ListId);
    }

    [Fact]
    public async Task Moving_to_a_list_that_has_gone_refuses_every_item_and_writes_none()
    {
        var store = new InMemoryInboxStore();
        var a = Items.Manual("A");
        var b = Items.Manual("B");
        store.Seed(a);
        store.Seed(b);

        var result = await Port(store).MoveToListAsync([a.Id, b.Id], Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        Assert.Empty(result.Changed);
        Assert.All(result.Failed, failure => Assert.Equal(InboxErrors.ListNotFound, failure.Error));
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task Each_item_gets_its_own_tag_set_and_a_person_is_refused_for_the_item_that_carried_it()
    {
        var store = new InMemoryInboxStore();
        var a = Items.Manual("A");
        var b = Items.Manual("B");
        store.Seed(a);
        store.Seed(b);

        var result = await Port(store).SetTagsAsync(
            new Dictionary<Guid, IReadOnlyList<string>>
            {
                [a.Id] = ["reading", "q4"],
                [b.Id] = ["@bob"]
            },
            TestContext.Current.CancellationToken);

        Assert.Equal([a.Id], result.Changed);
        Assert.Equal(InboxErrors.PersonIsNotATag, Assert.Single(result.Failed).Error);
        Assert.Equal(["reading", "q4"], store.Items[a.Id].Tags.Select(tag => tag.Name));
        Assert.Empty(store.Items[b.Id].Tags);
    }

    [Fact]
    public async Task Assigning_repositories_replaces_them_on_every_item()
    {
        var store = new InMemoryInboxStore();
        var a = Items.Manual("A");
        a.SetRepoIds(["JSdotNet/Old"]);
        var b = Items.Manual("B");
        store.Seed(a);
        store.Seed(b);

        var result = await Port(store).AssignRepositoriesAsync([a.Id, b.Id], ["JSdotNet/Backlog"], TestContext.Current.CancellationToken);

        Assert.Equal([a.Id, b.Id], result.Changed);
        Assert.Empty(result.Failed);
        Assert.Equal(["JSdotNet/Backlog"], store.Items[a.Id].RepoIds);
        Assert.Equal(["JSdotNet/Backlog"], store.Items[b.Id].RepoIds);
    }

    [Fact]
    public async Task Asking_about_nothing_does_nothing()
    {
        var store = new InMemoryInboxStore();

        var result = await Port(store).ArchiveAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(result.Changed);
        Assert.Empty(result.Failed);
        Assert.Empty(store.ItemWrites);
    }

    /// <summary>The port over the four handlers these tests exercise. The rest
    /// are never reached from a batch, so they are left unwired rather than
    /// composed for nothing.</summary>
    private static InboxItems Port(InMemoryInboxStore store) =>
        new(
            snapshot: null!,
            capture: null!,
            setTags: new SetTagsCommandHandler(store),
            assignRepositories: new AssignRepositoriesCommandHandler(store),
            renameRepository: null!,
            moveToList: new MoveToListCommandHandler(store, store),
            archive: new ArchiveItemCommandHandler(store, Clock),
            delete: null!,
            defer: null!,
            resurface: null!,
            resurfaceDue: null!,
            routeToBacklog: null!,
            createPlan: null!,
            routeBatchToBacklog: null!,
            proposeBatch: null!,
            inferBatchOrder: null!,
            createList: null!,
            renameList: null!,
            deleteList: null!,
            moveListToGroup: null!,
            createGroup: null!,
            renameGroup: null!,
            ungroup: null!,
            ensureDefaultOrganizer: null!,
            retryAttachment: null!,
            readAttachment: null!,
            openAttachment: null!,
            suggest: null!,
            dismissSuggestion: null!,
            related: null!,
            linkToTask: null!);
}
