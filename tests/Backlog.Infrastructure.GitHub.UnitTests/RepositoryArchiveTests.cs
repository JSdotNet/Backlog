using System.Text.Json.Nodes;

using Backlog.Modules.Sync.Abstractions.Services;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// An archived repository (local ADR 0029): a third, reversible state beside active
/// and removed, that keeps everything the repository is.
/// <para>
/// The flag is shared, in the registry row beside the alias and the hue, so it travels
/// to every paired device inside the registry document (local ADR 0021) with no sync
/// code of its own; <see cref="GitHubSettingsReplicationTests"/> carries it across.
/// Asserted against the files as well as through <c>Current</c>, for the reason
/// <see cref="RepositoryRegistrySplitTests"/> gives.
/// </para>
/// </summary>
public sealed class RepositoryArchiveTests : IDisposable
{
    private const string NotConfigured = "That repository is no longer configured.";

    private const string LastActive = "One repository must stay active.";

    private const string RegistryUnreadable =
        "The repository registry couldn't be read, so repositories can't be changed right now.";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "repository-archive-tests-" + Guid.NewGuid().ToString("N"));

    private string WorkspaceRoot(string device = "a") => Path.Combine(_root, device, "workspace");

    private string LocalPath(string device = "a") => Path.Combine(_root, device, "local", "github.json");

    private GitHubSettingsStore Store(string device = "a") => new(LocalPath(device), () => WorkspaceRoot(device));

    private static GitHubRepositoryRef Repository(string alias, string name) => new(alias, "JSdotNet", name);

    private GitHubSettingsStore Configured(string device = "a")
    {
        var store = Store(device);
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog"), Repository("archify", "Archify")]));
        return store;
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static JsonNode RegistryRow(GitHubSettingsStore store, string id) =>
        JsonNode.Parse(File.ReadAllText(store.RegistryPath))!["repositories"]!.AsArray()
            .Single(r => (string?)r!["id"] == id)!;

    // --- The flag and its document ---------------------------------------------

    [Fact]
    public void A_repository_starts_active()
    {
        var store = Configured();

        Assert.False(store.Current.Find("backlog")!.Archived);
        Assert.False(store.Current.Find("archify")!.Archived);
    }

    [Fact]
    public void An_archived_repository_writes_archived_true_to_the_registry_and_survives_a_reload()
    {
        var store = Configured();

        Assert.Null(store.SetRepositoryArchived("archify", archived: true));

        Assert.True(store.Current.Find("archify")!.Archived);
        Assert.True((bool)RegistryRow(store, "JSdotNet/Archify")["archived"]!);
        Assert.DoesNotContain("archived", File.ReadAllText(store.SettingsPath), StringComparison.Ordinal);

        Assert.True(Store().Current.Find("archify")!.Archived);
        Assert.False(Store().Current.Find("backlog")!.Archived);
    }

    /// <summary>A workspace nobody archived anything in writes the registry rows it
    /// always wrote, so an older build reads them unchanged.</summary>
    [Fact]
    public void An_active_row_omits_the_key_and_its_rows_are_byte_identical_to_never_archived()
    {
        var untouched = Configured("untouched");
        var toggled = Configured("toggled");

        Assert.Null(toggled.SetRepositoryArchived("archify", archived: true));
        Assert.Null(toggled.SetRepositoryArchived("archify", archived: false));

        Assert.DoesNotContain("archived", File.ReadAllText(toggled.RegistryPath), StringComparison.Ordinal);
        Assert.Equal(
            JsonNode.Parse(File.ReadAllText(untouched.RegistryPath))!["repositories"]!.ToJsonString(),
            JsonNode.Parse(File.ReadAllText(toggled.RegistryPath))!["repositories"]!.ToJsonString());
    }

    [Fact]
    public void Archiving_can_be_asked_by_id_as_well_as_by_alias_and_raises_changed()
    {
        var store = Configured();
        var raised = 0;
        store.Changed += () => raised++;

        Assert.Null(store.SetRepositoryArchived("JSdotNet/Archify", archived: true));

        Assert.True(store.Current.Find("archify")!.Archived);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Archiving_gives_the_registry_document_a_new_version()
    {
        var store = Configured();
        var replication = new GitHubSettingsReplication(store);
        var before = (await replication.ReadAsync(GitHubReplicaDocument.Registry, TestContext.Current.CancellationToken))!.UpdatedAt;

        Assert.Null(store.SetRepositoryArchived("archify", archived: true));

        var after = (await replication.ReadAsync(GitHubReplicaDocument.Registry, TestContext.Current.CancellationToken))!.UpdatedAt;
        Assert.True(after > before);
    }

    // --- Archiving keeps identity and settings (I1) -------------------------------

    [Fact]
    public void Archiving_and_unarchiving_change_nothing_but_the_flag()
    {
        var store = Configured();
        var clone = Path.Combine(_root, "clone");
        Assert.Null(store.SetRepositoryColour("archify", 4));
        Assert.Null(store.SetRepositoryToken("archify", "ghp_secret"));
        Assert.Null(store.SetCloneDirectory("archify", clone));
        Assert.Null(store.SetPullRequestLabels("archify", ["frontend"]));
        Assert.Null(store.SetDevbookSource("archify", "release", useLocalFolder: false));

        Assert.Null(store.SetRepositoryArchived("archify", archived: true));
        AssertSettingsKept(Store().Current.Find("archify")!);

        Assert.Null(store.SetRepositoryArchived("archify", archived: false));
        var restored = Store().Current.Find("archify")!;
        Assert.False(restored.Archived);
        AssertSettingsKept(restored);

        void AssertSettingsKept(GitHubRepositoryRef repository)
        {
            Assert.Equal("JSdotNet/Archify", repository.FullName);
            Assert.Equal("archify", repository.Alias);
            Assert.Equal(4, repository.Colour);
            Assert.Equal("ghp_secret", repository.Token);
            Assert.Equal(clone, repository.CloneDirectory);
            Assert.Equal(["frontend"], repository.PullRequestLabels);
            Assert.Equal("release", repository.DevbookBranch);
            Assert.False(repository.UseLocalDevbookFolder);
        }
    }

    [Fact]
    public void An_archived_repository_is_still_configured_and_found_by_alias_and_by_id()
    {
        var store = Configured();
        Assert.Null(store.SetRepositoryArchived("archify", archived: true));

        Assert.Equal(2, store.Current.Repositories.Count);
        Assert.Equal("JSdotNet/Archify", store.Current.Find("archify")!.FullName);
        Assert.Equal("archify", store.Current.Find("JSdotNet/Archify")!.Alias);
    }

    // --- Refusals (I2) ---------------------------------------------------------------

    [Fact]
    public void The_last_active_repository_cannot_be_archived()
    {
        var store = Configured();
        Assert.Null(store.SetRepositoryArchived("archify", archived: true));

        Assert.Equal(LastActive, store.SetRepositoryArchived("backlog", archived: true));

        Assert.False(store.Current.Find("backlog")!.Archived);
        Assert.False(Store().Current.Find("backlog")!.Archived);
    }

    [Fact]
    public void A_single_repository_cannot_be_archived()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog")]));

        Assert.Equal(LastActive, store.SetRepositoryArchived("backlog", archived: true));
        Assert.False(store.Current.Find("backlog")!.Archived);
    }

    /// <summary>The registry keeps an active repository however the last one would go:
    /// removed from its card, dropped from the re-typed list, or merged into an archived
    /// one. Each is refused with the archive sentence and changes nothing.</summary>
    [Fact]
    public void The_last_active_repository_cannot_be_removed_dropped_or_merged_away_while_others_are_archived()
    {
        var store = Configured();
        Assert.Null(store.SetRepositoryArchived("archify", archived: true));

        Assert.Equal(LastActive, store.RemoveRepository("backlog"));
        Assert.Equal(LastActive, store.SetRepositories([Repository("archify", "Archify")]));
        Assert.Equal(LastActive, store.RenameRepository("backlog", "JSdotNet/Archify", out var rename));

        Assert.Null(rename);
        Assert.Equal(["backlog", "archify"], Store().Current.Repositories.Select(r => r.Alias));
        Assert.Equal("backlog", Store().Current.DefaultRepository!.Alias);
        Assert.Empty(Store().Current.Renames);
        Assert.Empty(Store().Current.Removals);
    }

    [Fact]
    public void Removing_every_repository_is_not_refused()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog")]));

        Assert.Null(store.RemoveRepository("backlog"));
        Assert.Empty(store.Current.Repositories);
    }

    [Fact]
    public void Archiving_an_archived_repository_again_is_not_refused_as_the_last_active_one()
    {
        var store = Configured();
        Assert.Null(store.SetRepositoryArchived("archify", archived: true));

        Assert.Null(store.SetRepositoryArchived("archify", archived: true));
        Assert.True(store.Current.Find("archify")!.Archived);
    }

    [Fact]
    public void An_unknown_repository_is_refused()
    {
        var store = Configured();

        Assert.Equal(NotConfigured, store.SetRepositoryArchived("nope", archived: true));
    }

    [Fact]
    public void Archiving_is_refused_while_the_registry_is_unreadable()
    {
        var store = Configured();
        File.WriteAllText(store.RegistryPath, "{ this is not json");
        var reopened = Store();

        Assert.Equal(RegistryUnreadable, reopened.SetRepositoryArchived("backlog", archived: true));
        Assert.Equal("{ this is not json", File.ReadAllText(reopened.RegistryPath));
    }

    // --- The default repository is the first active one (I3) ---------------------

    [Fact]
    public void The_default_repository_skips_an_archived_first_entry()
    {
        var store = Configured();
        Assert.Equal("backlog", store.Current.DefaultRepository!.Alias);

        Assert.Null(store.SetRepositoryArchived("backlog", archived: true));

        Assert.Equal("archify", store.Current.DefaultRepository!.Alias);
        Assert.Equal(["archify"], store.Current.ActiveRepositories.Select(r => r.Alias));
    }

    [Fact]
    public void A_settings_value_with_no_repositories_has_no_default()
    {
        var settings = new GitHubSettings();

        Assert.Null(settings.DefaultRepository);
        Assert.Empty(settings.ActiveRepositories);
    }

    // --- Every other mutator keeps the flag (I4 and the re-typed text box) ----------

    /// <summary>The repositories text box rebuilds every row from text that has no flag
    /// in it; a keystroke there must not unarchive every repository.</summary>
    [Fact]
    public void A_re_typed_repositories_list_keeps_the_flag()
    {
        var store = Configured();
        Assert.Null(store.SetRepositoryArchived("archify", archived: true));

        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog"), Repository("archify", "Archify")]));

        Assert.True(store.Current.Find("archify")!.Archived);
        Assert.True(Store().Current.Find("archify")!.Archived);
    }

    [Fact]
    public void Every_other_mutator_keeps_the_flag()
    {
        var store = Configured();
        Assert.Null(store.SetRepositoryArchived("archify", archived: true));

        Assert.Null(store.SetRepositoryColour("archify", 2));
        Assert.Null(store.SetCloneDirectory("archify", Path.Combine(_root, "clone")));
        Assert.Null(store.SetPullRequestLabels("archify", ["frontend"]));
        Assert.Null(store.SetDevbookSource("archify", "main", useLocalFolder: false));
        Assert.Null(store.SetRepositoryToken("archify", "ghp_secret"));

        Assert.True(Store().Current.Find("archify")!.Archived);
    }

    [Fact]
    public void A_rename_keeps_an_archived_repository_archived()
    {
        var store = Configured();
        Assert.Null(store.SetRepositoryArchived("archify", archived: true));

        Assert.Null(store.RenameRepository("archify", "JSdotNet/Archify2", out _));

        Assert.True(store.Current.Find("JSdotNet/Archify2")!.Archived);
        Assert.True(Store().Current.Find("JSdotNet/Archify")!.Archived);
    }

    [Fact]
    public void A_merge_keeps_the_repository_it_keeps_archived_as_it_was()
    {
        var store = Store();
        Assert.Null(store.SetRepositories(
        [
            Repository("backlog", "Backlog"),
            Repository("archify", "Archify"),
            Repository("placeholder", "Placeholder")
        ]));
        Assert.Null(store.SetRepositoryArchived("archify", archived: true));

        // An active row merged into an archived one leaves it archived.
        Assert.Null(store.RenameRepository("placeholder", "JSdotNet/Archify", out _));
        Assert.True(Store().Current.Find("archify")!.Archived);
    }

    [Fact]
    public void A_merge_of_an_archived_row_into_an_active_one_leaves_it_active()
    {
        var store = Store();
        Assert.Null(store.SetRepositories(
        [
            Repository("backlog", "Backlog"),
            Repository("archify", "Archify"),
            Repository("placeholder", "Placeholder")
        ]));
        Assert.Null(store.SetRepositoryArchived("placeholder", archived: true));

        Assert.Null(store.RenameRepository("placeholder", "JSdotNet/Archify", out _));

        Assert.False(Store().Current.Find("archify")!.Archived);
        Assert.Equal(2, Store().Current.Repositories.Count);
    }
}
