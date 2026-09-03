using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.SaveTaskFromText;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// What a <c>repo:</c> token becomes when an entry is saved.
/// <para>
/// The token is a label somebody types; <c>repo_ids</c> holds the
/// <c>owner/name</c> identity that label refers to. Resolution is the only way a
/// value reaches the aggregate, which is what makes the identity stable: the
/// alias can be renamed in Settings afterwards and every entry filed against
/// that repository still points at it.
/// </para>
/// <para>
/// Asserted on the stored entry rather than on the text, because there is no
/// markdown to rewrite: <c>content_md</c> holds only the body, and the metadata
/// line is composed from the entry's fields. Migrating the field migrates the
/// canonical text, the raw-markdown escape hatch and every chip in one write.
/// </para>
/// </summary>
public class SaveTaskFromTextRepositoryResolutionTests
{
    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    [Fact]
    public async Task An_alias_is_stored_as_the_repositorys_id()
    {
        var stored = await Save("# Ship it\n`task` `repo:backlog`\n", Backlog);

        Assert.Equal(["JSdotNet/Backlog"], stored.RepoIds);
    }

    /// <summary>The registry is the authority on how a repository is spelled, so a
    /// coordinate typed in any casing is stored the way GitHub spells it — one
    /// target, not two that happen to look alike.</summary>
    [Fact]
    public async Task An_id_is_stored_as_the_registrys_casing()
    {
        var stored = await Save("# Ship it\n`task` `repo:jsdotnet/backlog`\n", Backlog);

        Assert.Equal(["JSdotNet/Backlog"], stored.RepoIds);
    }

    /// <summary>
    /// Kept verbatim rather than dropped. Dropping it would delete a token
    /// somebody typed with no error to notice it by; kept, the row simply reads
    /// "No repo" until the repository is configured, which is the token's general
    /// rule for a name nothing recognises.
    /// </summary>
    [Fact]
    public async Task A_name_the_registry_does_not_know_is_stored_as_it_was_typed()
    {
        var stored = await Save("# Ship it\n`task` `repo:Mystery`\n", Backlog);

        Assert.Equal(["Mystery"], stored.RepoIds);
    }

    /// <summary>
    /// De-duplication happens after canonicalisation, which is why the parser can
    /// keep its <c>StringComparer.Ordinal</c> and stay ignorant of the registry:
    /// before resolution these are genuinely two strings, and only the registry
    /// knows they name one repository.
    /// </summary>
    [Fact]
    public async Task Two_casings_of_one_repository_are_one_target()
    {
        var stored = await Save("# Ship it\n`task` `repo:JSdotNet/Backlog` `repo:jsdotnet/backlog`\n", Backlog);

        Assert.Equal(["JSdotNet/Backlog"], stored.RepoIds);
    }

    /// <summary>A workspace with nothing configured is the first-run state, not a
    /// failure. Every name stays exactly as it was typed, and the entry saves.</summary>
    [Fact]
    public async Task An_empty_registry_changes_nothing()
    {
        var directory = new FakeRepositoryDirectory();

        var stored = await Save("# Ship it\n`task` `repo:backlog` `repo:other/thing`\n", directory);

        Assert.Equal(["backlog", "other/thing"], stored.RepoIds);
    }

    /// <summary>
    /// The line ADR 0004 draws, asserted where it is easiest to cross. Import
    /// triggers registration; a <c>repo:</c> token somebody typed does not — so a
    /// typo does not quietly add a repository to the workspace that somebody then
    /// has to go and delete.
    /// </summary>
    [Fact]
    public async Task Saving_text_never_registers_a_repository()
    {
        var directory = new FakeRepositoryDirectory([Backlog]);

        var stored = await Save("# Ship it\n`task` `repo:xyz` `repo:foo/bar`\n", directory);

        Assert.Empty(directory.Registered);
        Assert.Single(directory.Repositories);

        // Both are stored, unresolved, waiting for somebody to configure them or
        // notice the typo.
        Assert.Equal(["xyz", "foo/bar"], stored.RepoIds);
    }

    private static Task<TaskItem> Save(string rawText, params TasksRepositoryRef[] known) =>
        Save(rawText, new FakeRepositoryDirectory(known));

    private static async Task<TaskItem> Save(string rawText, FakeRepositoryDirectory directory)
    {
        var store = new InMemoryTaskRepository();

        var result = await new SaveTaskFromTextCommandHandler(store, directory)
            .Handle(new SaveTaskFromTextCommand(null, rawText, 0));

        Assert.True(result.IsSuccess);
        return store.Entries.Single();
    }

    /// <summary>The store a host would supply, holding the aggregates themselves:
    /// what is under test is the value that reached the entry, not how it would be
    /// serialized.</summary>
    private sealed class InMemoryTaskRepository : ITaskRepository
    {
        public List<TaskItem> Entries { get; } = [];

        // Both reads hide a tombstoned entry, the way the port says they must.
        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. Entries.Where(entry => entry.DeletedAt is null)]);

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.FirstOrDefault(entry => entry.Id == id && entry.DeletedAt is null));

        public Task SaveAsync(TaskItem entry, CancellationToken cancellationToken = default)
        {
            Entries.RemoveAll(existing => existing.Id == entry.Id);
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
