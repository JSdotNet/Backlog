using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.RenameRepository;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// The entries' half of a repository rename. The registry carries its own row
/// across when a line keeps its alias and moves its <c>owner/name</c>; this
/// command is what stops the entries filed against the old coordinate from
/// being the reason the next reconcile pass registers it back as a ghost.
/// The properties are the reconcile pass's own: it moves exactly what named the
/// old id, it is idempotent, and it never touches a tombstone.
/// </summary>
public class RenameRepositoryTests
{
    private const string OldId = "JSdotNet/Backlog";
    private const string NewId = "JSdotNet/Backlog-renamed";

    [Fact]
    public async Task An_assignment_to_the_old_id_names_the_new_id_afterwards()
    {
        var store = StoreWith(Entry([OldId, "JSdotNet/Docs"]));

        var changed = await Rename(store);

        Assert.Equal(1, changed);
        Assert.Equal([NewId, "JSdotNet/Docs"], store.Entries.Single().RepoIds);
    }

    /// <summary>An issue link is a projection into a repository, and the issue
    /// still exists under the new name — GitHub carries issues across a rename —
    /// so the link follows rather than going stale.</summary>
    [Fact]
    public async Task An_issue_link_into_the_old_id_follows_the_rename()
    {
        var entry = Entry([OldId]);
        entry.AddProjectionRef(new ProjectionRef(OldId, "42", "issue"));
        entry.AddProjectionRef(new ProjectionRef("JSdotNet/Docs", "7", "issue"));
        var store = StoreWith(entry);

        await Rename(store);

        Assert.Equal(
            [new ProjectionRef(NewId, "42", "issue"), new ProjectionRef("JSdotNet/Docs", "7", "issue")],
            store.Entries.Single().ProjectionRefs);
    }

    /// <summary>Registry ids are compared without regard to case everywhere
    /// else, so a stored spelling that differs only in case is the same
    /// repository here too.</summary>
    [Fact]
    public async Task The_old_id_is_matched_without_regard_to_case()
    {
        var store = StoreWith(Entry(["jsdotnet/backlog"]));

        var changed = await Rename(store);

        Assert.Equal(1, changed);
        Assert.Equal([NewId], store.Entries.Single().RepoIds);
    }

    [Fact]
    public async Task An_entry_that_never_named_the_old_id_is_not_written()
    {
        var store = StoreWith(Entry(["JSdotNet/Docs"]), Entry([]));

        var changed = await Rename(store);

        Assert.Equal(0, changed);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task A_second_run_writes_nothing()
    {
        var store = StoreWith(Entry([OldId]));

        Assert.Equal(1, await Rename(store));

        store.Writes = 0;
        Assert.Equal(0, await Rename(store));
        Assert.Equal(0, store.Writes);
    }

    /// <summary>An entry that somehow named both spellings names the new one
    /// once, as <c>SetRepoIds</c> through the resolver would have left it.</summary>
    [Fact]
    public async Task An_entry_naming_both_ids_ends_up_naming_the_new_one_once()
    {
        var store = StoreWith(Entry([OldId, NewId]));

        await Rename(store);

        Assert.Equal([NewId], store.Entries.Single().RepoIds);
    }

    /// <summary>A tombstone is not read back into a chip and is not seen by the
    /// reconcile pass either, so a stale id on one registers nothing and there
    /// is no reason to restamp it.</summary>
    [Fact]
    public async Task A_deleted_entry_is_left_alone()
    {
        var deleted = Entry([OldId]);
        deleted.MarkDeleted();
        var store = StoreWith(deleted);

        var changed = await Rename(store);

        Assert.Equal(0, changed);
        Assert.Equal([OldId], store.Entries.Single().RepoIds);
    }

    [Fact]
    public async Task Renaming_an_id_to_itself_is_a_no_op()
    {
        var store = StoreWith(Entry([OldId]));

        var result = await new RenameRepositoryCommandHandler(store).Handle(new RenameRepositoryCommand(OldId, "jsdotnet/backlog"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task A_blank_id_is_refused()
    {
        var store = StoreWith(Entry([OldId]));

        var result = await new RenameRepositoryCommandHandler(store).Handle(new RenameRepositoryCommand(OldId, " "));

        Assert.True(result.IsFailure);
        Assert.Equal(RenameRepositoryCommand.BlankId, result.Error);
        Assert.Equal(0, store.Writes);
    }

    private static async Task<int> Rename(InMemoryTaskRepository store)
    {
        var result = await new RenameRepositoryCommandHandler(store).Handle(new RenameRepositoryCommand(OldId, NewId));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static TaskItem Entry(IEnumerable<string> repoIds)
    {
        var entry = new TaskItem("Ship it", string.Empty, EntryType.Task);
        entry.SetRepoIds(repoIds);
        return entry;
    }

    private static InMemoryTaskRepository StoreWith(params TaskItem[] entries)
    {
        var store = new InMemoryTaskRepository();
        store.Entries.AddRange(entries);
        return store;
    }

    private sealed class InMemoryTaskRepository : ITaskRepository
    {
        public List<TaskItem> Entries { get; } = [];

        public int Writes { get; set; }

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. Entries.Where(entry => entry.DeletedAt is null)]);

        public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(
            DateTimeOffset since,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>(
                [.. Entries.Where(entry => entry.UpdatedAt > since).OrderBy(entry => entry.UpdatedAt)]);

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.FirstOrDefault(entry => entry.Id == id && entry.DeletedAt is null));

        public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.FirstOrDefault(entry => entry.Id == id));

        public Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
        {
            Writes++;
            Entries.RemoveAll(existing => existing.Id == task.Id);
            Entries.Add(task);
            return Task.CompletedTask;
        }
    }
}
