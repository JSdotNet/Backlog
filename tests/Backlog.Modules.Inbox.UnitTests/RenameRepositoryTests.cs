using Backlog.Modules.Inbox.Features.RenameRepository;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The inbox's half of a repository rename: an item assigned to the old
/// <c>owner/name</c> is assigned to the new one afterwards, and nothing else
/// about it moves. The routing record in particular stays as written — it is
/// where the item went, not a live link.
/// </summary>
public sealed class RenameRepositoryTests
{
    private const string OldId = "JSdotNet/Backlog";
    private const string NewId = "JSdotNet/Backlog-renamed";

    [Fact]
    public async Task An_assignment_to_the_old_id_names_the_new_id_afterwards()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.SetRepoIds([OldId, "JSdotNet/Docs"]);
        store.Seed(item);

        var changed = await Rename(store);

        Assert.Equal(1, changed);
        Assert.Equal([NewId, "JSdotNet/Docs"], store.Items[item.Id].RepoIds);
    }

    [Fact]
    public async Task The_old_id_is_matched_without_regard_to_case()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.SetRepoIds(["jsdotnet/backlog"]);
        store.Seed(item);

        await Rename(store);

        Assert.Equal([NewId], store.Items[item.Id].RepoIds);
    }

    [Fact]
    public async Task An_item_that_never_named_the_old_id_is_not_written()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.SetRepoIds(["JSdotNet/Docs"]);
        store.Seed(item);

        var changed = await Rename(store);

        Assert.Equal(0, changed);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task A_second_run_writes_nothing()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.SetRepoIds([OldId]);
        store.Seed(item);

        Assert.Equal(1, await Rename(store));

        store.ItemWrites.Clear();
        Assert.Equal(0, await Rename(store));
        Assert.Empty(store.ItemWrites);
    }

    /// <summary>The routing record is the decision as taken, and the domain
    /// says it is never edited. The assignment on the same item still follows,
    /// because that is the live half.</summary>
    [Fact]
    public async Task A_routing_record_is_left_as_written()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.SetRepoIds([OldId]);
        item.RouteToBacklog([Guid.CreateVersion7()], [OldId], Items.Noon);
        store.Seed(item);

        await Rename(store);

        Assert.Equal([NewId], store.Items[item.Id].RepoIds);
        Assert.Equal([OldId], store.Items[item.Id].Routing!.RepoIds);
    }

    [Fact]
    public async Task An_archived_item_follows_too()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        item.SetRepoIds([OldId]);
        item.Archive(Items.Noon);
        store.Seed(item);

        var changed = await Rename(store);

        Assert.Equal(1, changed);
        Assert.Equal([NewId], store.Items[item.Id].RepoIds);
    }

    [Fact]
    public async Task A_blank_id_is_refused()
    {
        var store = new InMemoryInboxStore();

        var result = await new RenameRepositoryCommandHandler(store).Handle(new RenameRepositoryCommand(" ", NewId));

        Assert.True(result.IsFailure);
        Assert.Equal(RenameRepositoryCommand.BlankId, result.Error);
    }

    private static async Task<int> Rename(InMemoryInboxStore store)
    {
        var result = await new RenameRepositoryCommandHandler(store).Handle(new RenameRepositoryCommand(OldId, NewId));

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
