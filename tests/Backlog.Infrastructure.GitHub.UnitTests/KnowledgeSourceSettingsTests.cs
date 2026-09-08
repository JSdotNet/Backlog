using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// Where a repository's knowledge is read from, and which of the two files each
/// half of that answer lives in.
/// <para>
/// The branch is shared — "this repository's knowledge is read from main" is
/// true of the repository on every install of a workspace — so it travels in the
/// registry beside the alias and the hue. The decision to read a local clone
/// instead is machine data, because only a machine that has the clone can
/// answer it, and it stays in the per-user file beside the clone directory
/// itself. Asserted against the files rather than only through <c>Current</c>,
/// for the reason <see cref="RepositoryRegistrySplitTests"/> gives: a store that
/// composed the right answer while writing a local path into the synced folder
/// would pass every test that only read it back.
/// </para>
/// </summary>
public class KnowledgeSourceSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "knowledge-source-settings-tests-" + Guid.NewGuid().ToString("N"));

    private string WorkspaceRoot => Path.Combine(_root, "workspace");

    private string LocalPath(string install = "install-1") => Path.Combine(_root, install, "github.json");

    private GitHubSettingsStore Store(string install = "install-1") => new(LocalPath(install), () => WorkspaceRoot);

    private static GitHubRepositoryRef Repository(string alias, string name) => new(alias, "JSdotNet", name);

    private GitHubSettingsStore Configured(out string clone, string install = "install-1")
    {
        clone = Path.Combine(_root, "clone");
        Directory.CreateDirectory(clone);

        var store = Store(install);
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog")]));
        Assert.Null(store.SetCloneDirectory("backlog", clone));

        return store;
    }

    // --- The default, and the upgrade it has to survive -----------------------

    /// <summary>
    /// The single most consequential line in this change. Every repository
    /// configured before branch loading existed has a clone and no stored answer,
    /// and every one of them has to keep reading — and editing — the folder it
    /// always read. A plain <c>false</c> default would have turned all of them
    /// read-only on upgrade, silently.
    /// </summary>
    [Fact]
    public void A_repository_configured_before_branch_loading_keeps_reading_its_clone()
    {
        var store = Configured(out _);

        var repository = store.Current.Find("backlog");

        Assert.NotNull(repository);
        Assert.Null(repository.UseLocalKnowledgeFolder);
        Assert.Equal(KnowledgeSourceKind.LocalFolder, repository.KnowledgeSource);
    }

    [Fact]
    public void A_repository_with_no_clone_reads_its_branch()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog")]));

        var repository = store.Current.Find("backlog");

        Assert.NotNull(repository);
        Assert.Equal(KnowledgeSourceKind.Branch, repository.KnowledgeSource);
    }

    /// <summary>A stated preference for the clone does not conjure one. This is
    /// the case a workspace shared with somebody who never cloned anything
    /// produces, and it has to read rather than fail.</summary>
    [Fact]
    public void Choosing_the_clone_without_one_still_reads_the_branch()
    {
        var store = Store();
        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog")]));
        Assert.Null(store.SetKnowledgeSource("backlog", branch: null, useLocalFolder: true));

        Assert.Equal(KnowledgeSourceKind.Branch, store.Current.Find("backlog")!.KnowledgeSource);
    }

    // --- The split ------------------------------------------------------------

    [Fact]
    public void The_branch_travels_in_the_registry_and_the_clone_preference_stays_local()
    {
        var store = Configured(out _);
        Assert.Null(store.SetKnowledgeSource("backlog", "release/2.0", useLocalFolder: false));

        var registry = File.ReadAllText(store.RegistryPath);
        var local = File.ReadAllText(LocalPath());

        Assert.Contains("\"knowledgeBranch\": \"release/2.0\"", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("useLocalKnowledgeFolder", registry, StringComparison.Ordinal);

        Assert.Contains("\"useLocalKnowledgeFolder\": false", local, StringComparison.Ordinal);
        Assert.DoesNotContain("knowledgeBranch", local, StringComparison.Ordinal);
    }

    /// <summary>A second install over the same workspace inherits the branch and
    /// decides the clone question for itself — which, having no clone of its own,
    /// it answers by reading the branch.</summary>
    [Fact]
    public void A_second_install_inherits_the_branch_but_not_the_clone_preference()
    {
        var first = Configured(out _);
        Assert.Null(first.SetKnowledgeSource("backlog", "main", useLocalFolder: true));

        var second = Store("install-2").Current.Find("backlog");

        Assert.NotNull(second);
        Assert.Equal("main", second.KnowledgeBranch);
        Assert.Null(second.UseLocalKnowledgeFolder);
        Assert.Null(second.CloneDirectory);
        Assert.Equal(KnowledgeSourceKind.Branch, second.KnowledgeSource);
    }

    [Fact]
    public void A_workspace_where_nobody_picked_a_branch_writes_no_branch_field()
    {
        var store = Configured(out _);

        Assert.DoesNotContain("knowledgeBranch", File.ReadAllText(store.RegistryPath), StringComparison.Ordinal);
    }

    // --- Surviving a re-typed repository list ---------------------------------

    /// <summary>
    /// <c>SetRepositories</c> rebuilds every row from parsed text and the grammar
    /// carries neither of these fields, so anything not carried across is
    /// destroyed the moment somebody edits the repositories box. For the source
    /// that would mean silently sending a repository back to its default branch
    /// and turning the panels read-only on the next keystroke.
    /// </summary>
    [Fact]
    public void Editing_the_repository_list_keeps_the_knowledge_source()
    {
        var store = Configured(out _);
        Assert.Null(store.SetKnowledgeSource("backlog", "release/2.0", useLocalFolder: true));

        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog"), Repository("other", "Other")]));

        var repository = store.Current.Find("backlog");

        Assert.NotNull(repository);
        Assert.Equal("release/2.0", repository.KnowledgeBranch);
        Assert.True(repository.UseLocalKnowledgeFolder);
        Assert.Equal(KnowledgeSourceKind.LocalFolder, repository.KnowledgeSource);
    }

    [Fact]
    public void Renaming_the_alias_keeps_the_knowledge_source()
    {
        var store = Configured(out _);
        Assert.Null(store.SetKnowledgeSource("backlog", "main", useLocalFolder: false));

        Assert.Null(store.SetRepositories([Repository("work", "Backlog")]));

        Assert.Equal("main", store.Current.Find("work")!.KnowledgeBranch);
    }

    // --- Remembering, and reading back ----------------------------------------

    /// <summary>Switching to the clone for an afternoon's editing and back should
    /// not cost somebody the branch they picked.</summary>
    [Fact]
    public void Choosing_the_clone_remembers_the_branch()
    {
        var store = Configured(out _);
        Assert.Null(store.SetKnowledgeSource("backlog", "release/2.0", useLocalFolder: false));
        Assert.Null(store.SetKnowledgeSource("backlog", branch: null, useLocalFolder: true));

        var repository = store.Current.Find("backlog");

        Assert.Equal("release/2.0", repository!.KnowledgeBranch);
        Assert.Equal(KnowledgeSourceKind.LocalFolder, repository.KnowledgeSource);
    }

    [Fact]
    public void The_choice_survives_a_restart()
    {
        var first = Configured(out _);
        Assert.Null(first.SetKnowledgeSource("backlog", "release/2.0", useLocalFolder: false));

        var reopened = Store().Current.Find("backlog");

        Assert.Equal("release/2.0", reopened!.KnowledgeBranch);
        Assert.False(reopened.UseLocalKnowledgeFolder);
        Assert.Equal(KnowledgeSourceKind.Branch, reopened.KnowledgeSource);
    }

    /// <summary>A ref spelling is folded away because the two forms name the same
    /// branch and only one of them can be pasted into an archive URL.</summary>
    [Fact]
    public void A_refs_heads_prefix_is_folded_away()
    {
        var store = Configured(out _);
        Assert.Null(store.SetKnowledgeSource("backlog", "refs/heads/main", useLocalFolder: false));

        Assert.Equal("main", store.Current.Find("backlog")!.KnowledgeBranch);
    }

    [Fact]
    public void An_unknown_alias_is_reported_rather_than_written()
    {
        var store = Configured(out _);

        Assert.NotNull(store.SetKnowledgeSource("nothing", "main", useLocalFolder: false));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }
}
