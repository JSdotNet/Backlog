using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Modules.Backlog.Features.ReconcileRepositoryIds;

namespace Backlog.Modules.Backlog.UnitTests;

/// <summary>
/// The startup pass that settles what an entry's <c>repo_ids</c> holds.
/// <para>
/// A workspace that predates the identity change holds a mixture: aliases from
/// the text path, <c>owner/name</c> from every pushed entry, and whatever casing
/// somebody typed. This is the one pass that resolves it, and the properties
/// that matter are all about restraint — it must be idempotent, it must only
/// ever fill gaps, and a broken registry must not let it damage anything.
/// </para>
/// </summary>
public class ReconcileRepositoryIdsTests
{
    private static readonly BacklogRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");
    private static readonly BacklogRepositoryRef Docs = new("docs", "JSdotNet", "Docs");

    [Fact]
    public async Task An_alias_shaped_repo_id_becomes_owner_slash_name()
    {
        var store = StoreWith(["backlog"]);
        var directory = new FakeRepositoryDirectory([Backlog]);

        var changed = await Reconcile(store, directory);

        Assert.Equal(1, changed);
        Assert.Equal(["JSdotNet/Backlog"], store.Entries.Single().RepoIds);
    }

    /// <summary>
    /// The property that lets this run on every start with no once-flag: after one
    /// pass every value is either a registry id, which resolves to itself, or
    /// unresolvable and not id-shaped, which rule 3 leaves alone. A second pass is
    /// a pure read.
    /// </summary>
    [Fact]
    public async Task A_second_run_writes_nothing()
    {
        var store = StoreWith(["backlog"]);
        var directory = new FakeRepositoryDirectory([Backlog]);

        Assert.Equal(1, await Reconcile(store, directory));

        store.Writes = 0;
        Assert.Equal(0, await Reconcile(store, directory));
        Assert.Equal(0, store.Writes);
    }

    /// <summary>A value nothing recognises stays exactly as it is. Rewriting or
    /// dropping it would lose a token somebody typed with no error to notice it
    /// by; left alone, the row reads "No repo" until the repository is
    /// configured.</summary>
    [Fact]
    public async Task A_value_the_registry_cannot_resolve_is_left_alone()
    {
        var store = StoreWith(["mystery"]);
        var directory = new FakeRepositoryDirectory([Backlog]);

        var changed = await Reconcile(store, directory);

        Assert.Equal(0, changed);
        Assert.Equal(["mystery"], store.Entries.Single().RepoIds);
        Assert.Equal(0, store.Writes);
    }

    /// <summary>
    /// Part 3 of the approved scope. An assignment that arrived with a synced
    /// <c>backlog.db</c> names a repository this install has never configured, and
    /// a coordinate is enough to configure one — so it becomes an ordinary
    /// registered repository with no clone directory and no token, rather than an
    /// unresolvable string forever.
    /// </summary>
    [Fact]
    public async Task An_owner_slash_name_the_registry_does_not_know_is_registered_directory_less()
    {
        var store = StoreWith(["Someone/Thing"]);
        var directory = new FakeRepositoryDirectory([Backlog]);

        var changed = await Reconcile(store, directory);

        Assert.Equal(["Someone/Thing"], directory.Registered);
        Assert.Equal("Someone/Thing", directory.Resolve("Someone/Thing")!.Id);
        Assert.Equal("thing", directory.Resolve("Someone/Thing")!.Alias);

        // Already the registry's own spelling, so the stored value does not move
        // and nothing is written.
        Assert.Equal(0, changed);
        Assert.Equal(["Someone/Thing"], store.Entries.Single().RepoIds);
    }

    /// <summary>
    /// Where ADR 0004's line now sits. A typo'd <c>repo:xyz</c> is alias-shaped,
    /// resolves to nothing, and the shape guard means it is never registered — so
    /// a mistyped token never quietly adds a repository somebody then has to go
    /// and delete.
    /// </summary>
    [Fact]
    public async Task An_alias_shaped_value_is_never_registered()
    {
        var store = StoreWith(["xyz"]);
        var directory = new FakeRepositoryDirectory([Backlog]);

        await Reconcile(store, directory);

        Assert.Empty(directory.Registered);
        Assert.Single(directory.Repositories);
        Assert.Equal(["xyz"], store.Entries.Single().RepoIds);
    }

    /// <summary>
    /// A corrupt <c>config/repos.json</c> must not be able to damage
    /// <c>repo_ids</c>. An empty registry resolves nothing, so rule 1 never fires;
    /// and an alias-shaped value is not id-shaped, so rule 2 does not either. The
    /// handler writes nothing at all.
    /// </summary>
    [Fact]
    public async Task An_empty_registry_writes_nothing()
    {
        var store = StoreWith(["backlog", "docs"]);
        var directory = new FakeRepositoryDirectory();

        var changed = await Reconcile(store, directory);

        Assert.Equal(0, changed);
        Assert.Equal(0, store.Writes);
        Assert.Equal(["backlog", "docs"], store.Entries.Single().RepoIds);
    }

    /// <summary>An entry may name several repositories, and the pass is a
    /// canonicalisation rather than a choice between them — every target is
    /// resolved, in the order it was written.</summary>
    [Fact]
    public async Task An_entry_with_two_targets_keeps_both()
    {
        var store = StoreWith(["backlog", "docs"]);
        var directory = new FakeRepositoryDirectory([Backlog, Docs]);

        var changed = await Reconcile(store, directory);

        Assert.Equal(1, changed);
        Assert.Equal(["JSdotNet/Backlog", "JSdotNet/Docs"], store.Entries.Single().RepoIds);
    }

    /// <summary>Two casings of one repository resolve to one row, and
    /// de-duplication after canonicalisation collapses them into one target —
    /// which is why the parser can keep comparing values ordinally and stay
    /// ignorant of the registry.</summary>
    [Fact]
    public async Task Two_casings_of_one_repository_collapse_into_one_target()
    {
        var store = StoreWith(["JSdotNet/Backlog", "jsdotnet/backlog"]);
        var directory = new FakeRepositoryDirectory([Backlog]);

        var changed = await Reconcile(store, directory);

        Assert.Equal(1, changed);
        Assert.Equal(["JSdotNet/Backlog"], store.Entries.Single().RepoIds);
    }

    private static async Task<int> Reconcile(InMemoryTaskRepository store, FakeRepositoryDirectory directory)
    {
        var result = await new ReconcileRepositoryIdsCommandHandler(store, directory)
            .Handle(new ReconcileRepositoryIdsCommand());

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static InMemoryTaskRepository StoreWith(IEnumerable<string> repoIds)
    {
        var entry = new TaskItem("Ship it", string.Empty, EntryType.Task);
        entry.SetRepoIds(repoIds);

        var store = new InMemoryTaskRepository();
        store.Entries.Add(entry);
        return store;
    }

    /// <summary>Counts writes as well as holding entries, because "wrote nothing"
    /// is half of what this pass has to guarantee and a store that only held the
    /// rows could not tell a no-op from a rewrite to the same value.</summary>
    private sealed class InMemoryTaskRepository : ITaskRepository
    {
        public List<TaskItem> Entries { get; } = [];

        public int Writes { get; set; }

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>(Entries);

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.FirstOrDefault(entry => entry.Id == id));

        public Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
        {
            Writes++;
            Entries.RemoveAll(existing => existing.Id == task.Id);
            Entries.Add(task);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Entries.RemoveAll(entry => entry.Id == id);
            return Task.CompletedTask;
        }
    }
}
