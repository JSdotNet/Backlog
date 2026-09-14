using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.CreateGroup;
using Backlog.Modules.Inbox.Features.CreateList;
using Backlog.Modules.Inbox.Features.DeleteList;
using Backlog.Modules.Inbox.Features.EnsureDefaultOrganizer;
using Backlog.Modules.Inbox.Features.MoveListToGroup;
using Backlog.Modules.Inbox.Features.MoveToList;
using Backlog.Modules.Inbox.Features.RenameGroup;
using Backlog.Modules.Inbox.Features.RenameList;
using Backlog.Modules.Inbox.Features.UngroupLists;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// Lists and groups: the seed that only runs into an empty organiser, and the
/// two deletes that have to leave nothing pointing at what they removed.
/// </summary>
public sealed class OrganizerTests
{
    private static readonly FakeTimeProvider Clock = new(Items.Noon);

    [Fact]
    public async Task The_default_organizer_is_seeded_once_into_an_empty_workspace()
    {
        var store = new InMemoryInboxStore();
        var handler = new EnsureDefaultOrganizerCommandHandler(store, Clock);

        await handler.Handle(new EnsureDefaultOrganizerCommand(), TestContext.Current.CancellationToken);
        await handler.Handle(new EnsureDefaultOrganizerCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Areas", "Projects", "Archive"],
            (await store.ListGroupsAsync(TestContext.Current.CancellationToken)).Select(group => group.Name));
        Assert.Equal(
            ["Resources", "Someday/Maybe", "Updates", "Wishlist"],
            (await store.ListListsAsync(TestContext.Current.CancellationToken)).Select(list => list.Name));
        Assert.All(store.Lists.Values, list => Assert.Null(list.GroupId));
    }

    [Fact]
    public async Task A_workspace_with_any_list_or_group_of_its_own_is_left_alone()
    {
        var store = new InMemoryInboxStore();
        store.Seed(InboxGroup.Create("Mine", 0, Items.Noon));

        await new EnsureDefaultOrganizerCommandHandler(store, Clock)
            .Handle(new EnsureDefaultOrganizerCommand(), TestContext.Current.CancellationToken);

        Assert.Equal("Mine", Assert.Single(store.Groups.Values).Name);
        Assert.Empty(store.Lists);
    }

    [Fact]
    public async Task Deleting_a_list_returns_its_items_to_the_inbox()
    {
        var store = new InMemoryInboxStore();
        var list = InboxList.Create("Resources", null, 0, Items.Noon);
        var other = InboxList.Create("Updates", null, 1, Items.Noon);
        store.Seed(list);
        store.Seed(other);

        var filed = Items.Manual("Filed");
        filed.MoveToList(list.Id);
        var elsewhere = Items.Manual("Elsewhere");
        elsewhere.MoveToList(other.Id);
        store.Seed(filed);
        store.Seed(elsewhere);

        var result = await new DeleteListCommandHandler(store, store)
            .Handle(new DeleteListCommand(list.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(list.Id, store.Lists.Keys);
        Assert.Null(filed.ListId);
        Assert.Equal(other.Id, elsewhere.ListId);
    }

    [Fact]
    public async Task Ungrouping_moves_the_lists_to_the_top_level_and_removes_the_group()
    {
        var store = new InMemoryInboxStore();
        var group = InboxGroup.Create("Projects", 0, Items.Noon);
        store.Seed(group);
        var inside = InboxList.Create("Backlog", group.Id, 0, Items.Noon);
        var alreadyTop = InboxList.Create("Resources", null, 0, Items.Noon);
        store.Seed(inside);
        store.Seed(alreadyTop);

        var result = await new UngroupListsCommandHandler(store)
            .Handle(new UngroupListsCommand(group.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(store.Groups);
        Assert.Null(inside.GroupId);
        Assert.Equal(1, inside.Order);
    }

    [Fact]
    public async Task A_duplicate_name_among_siblings_is_refused_whatever_its_case()
    {
        var store = new InMemoryInboxStore();
        store.Seed(InboxList.Create("Resources", null, 0, Items.Noon));

        var create = await new CreateListCommandHandler(store, Clock)
            .Handle(new CreateListCommand(" resources "), TestContext.Current.CancellationToken);

        Assert.Equal("inbox.list.duplicate_name", create.Error.Code);
        Assert.Single(store.Lists);

        // The same name inside a group is a different place, and allowed.
        var group = InboxGroup.Create("Projects", 0, Items.Noon);
        store.Seed(group);

        var inGroup = await new CreateListCommandHandler(store, Clock)
            .Handle(new CreateListCommand("Resources", group.Id), TestContext.Current.CancellationToken);

        Assert.True(inGroup.IsSuccess);
        Assert.Equal(group.Id, inGroup.Value.GroupId);
    }

    [Fact]
    public async Task Renaming_trims_and_refuses_a_blank()
    {
        var store = new InMemoryInboxStore();
        var list = InboxList.Create("Resources", null, 0, Items.Noon);
        store.Seed(list);

        var trimmed = await new RenameListCommandHandler(store)
            .Handle(new RenameListCommand(list.Id, "  Reading  "), TestContext.Current.CancellationToken);
        var blank = await new RenameListCommandHandler(store)
            .Handle(new RenameListCommand(list.Id, "   "), TestContext.Current.CancellationToken);

        Assert.True(trimmed.IsSuccess);
        Assert.Equal("Reading", list.Name);
        Assert.Equal("inbox.list.needs_name", blank.Error.Code);
        Assert.Equal("Reading", list.Name);
    }

    [Fact]
    public async Task A_group_is_named_uniquely_and_renamed_under_the_same_rule()
    {
        var store = new InMemoryInboxStore();

        var first = await new CreateGroupCommandHandler(store, Clock)
            .Handle(new CreateGroupCommand("Areas"), TestContext.Current.CancellationToken);
        var second = await new CreateGroupCommandHandler(store, Clock)
            .Handle(new CreateGroupCommand("Projects"), TestContext.Current.CancellationToken);
        var duplicate = await new CreateGroupCommandHandler(store, Clock)
            .Handle(new CreateGroupCommand("areas"), TestContext.Current.CancellationToken);
        var collide = await new RenameGroupCommandHandler(store)
            .Handle(new RenameGroupCommand(second.Value.Id, "AREAS"), TestContext.Current.CancellationToken);

        Assert.True(first.IsSuccess);
        Assert.Equal(1, second.Value.Order);
        Assert.Equal("inbox.group.duplicate_name", duplicate.Error.Code);
        Assert.Equal("inbox.group.duplicate_name", collide.Error.Code);
    }

    [Fact]
    public async Task Moving_a_list_into_a_group_places_it_last_among_its_new_siblings()
    {
        var store = new InMemoryInboxStore();
        var group = InboxGroup.Create("Projects", 0, Items.Noon);
        store.Seed(group);
        store.Seed(InboxList.Create("Backlog", group.Id, 3, Items.Noon));
        var moving = InboxList.Create("Resources", null, 0, Items.Noon);
        store.Seed(moving);

        var result = await new MoveListToGroupCommandHandler(store)
            .Handle(new MoveListToGroupCommand(moving.Id, group.Id), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(group.Id, moving.GroupId);
        Assert.Equal(4, moving.Order);
    }

    [Fact]
    public async Task Filing_an_item_in_a_list_that_does_not_exist_is_refused()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);

        var result = await new MoveToListCommandHandler(store, store)
            .Handle(new MoveToListCommand(item.Id, Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        Assert.Equal("inbox.list.not_found", result.Error.Code);
        Assert.Null(item.ListId);
    }
}
