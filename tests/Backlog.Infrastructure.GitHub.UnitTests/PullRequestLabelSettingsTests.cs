using System.Text.Json.Nodes;

using Backlog.Modules.Sync.Abstractions.Services;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// A repository's pull request label filter: the labels its pull requests list is
/// narrowed to.
/// <para>
/// Shared, in the registry row beside the alias and the hue, so it travels to every
/// paired device inside the registry document (local ADR 0021) with no sync code of
/// its own. Asserted against the files as well as through <c>Current</c>, for the
/// reason <see cref="RepositoryRegistrySplitTests"/> gives.
/// </para>
/// </summary>
public sealed class PullRequestLabelSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "pull-request-label-settings-tests-" + Guid.NewGuid().ToString("N"));

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

    [Fact]
    public void A_repository_starts_with_no_label_filter()
    {
        var store = Configured();

        Assert.Empty(store.Current.Find("backlog")!.PullRequestLabels);
    }

    [Fact]
    public void Labels_are_trimmed_de_duplicated_without_regard_to_case_and_blanks_dropped()
    {
        var store = Configured();

        Assert.Null(store.SetPullRequestLabels("backlog", [" frontend ", "Frontend", "", "  ", "needs review"]));

        Assert.Equal(["frontend", "needs review"], store.Current.Find("backlog")!.PullRequestLabels);
        Assert.Empty(store.Current.Find("archify")!.PullRequestLabels);
    }

    [Fact]
    public void The_filter_can_be_set_by_id_as_well_as_by_alias()
    {
        var store = Configured();

        Assert.Null(store.SetPullRequestLabels("JSdotNet/Archify", ["backend"]));

        Assert.Equal(["backend"], store.Current.Find("archify")!.PullRequestLabels);
    }

    [Fact]
    public void Setting_the_filter_raises_changed()
    {
        var store = Configured();
        var raised = 0;
        store.Changed += () => raised++;

        store.SetPullRequestLabels("backlog", ["frontend"]);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void An_unknown_repository_is_refused()
    {
        var store = Configured();

        Assert.Equal("That repository is no longer configured.", store.SetPullRequestLabels("nope", ["x"]));
    }

    [Fact]
    public void The_filter_lives_in_the_shared_registry_and_survives_a_restart()
    {
        var store = Configured();
        Assert.Null(store.SetPullRequestLabels("backlog", ["frontend", "backend"]));

        var registry = JsonNode.Parse(File.ReadAllText(store.RegistryPath))!;
        var row = registry["repositories"]!.AsArray().Single(r => (string?)r!["id"] == "JSdotNet/Backlog")!;
        Assert.Equal(["frontend", "backend"], row["pullRequestLabels"]!.AsArray().Select(label => (string?)label));
        Assert.DoesNotContain("pullRequestLabels", File.ReadAllText(store.SettingsPath), StringComparison.Ordinal);

        Assert.Equal(["frontend", "backend"], Store().Current.Find("backlog")!.PullRequestLabels);
    }

    /// <summary>A workspace nobody has filtered writes the registry it always wrote, so
    /// an older build reads it unchanged.</summary>
    [Fact]
    public void An_empty_filter_is_absent_from_the_registry()
    {
        var store = Configured();
        Assert.Null(store.SetPullRequestLabels("backlog", ["frontend"]));
        Assert.Null(store.SetPullRequestLabels("backlog", []));

        Assert.DoesNotContain("pullRequestLabels", File.ReadAllText(store.RegistryPath), StringComparison.Ordinal);
        Assert.Empty(store.Current.Find("backlog")!.PullRequestLabels);
    }

    /// <summary>The repositories text box rebuilds every row from text that has no
    /// labels in it; the filter must not be lost to a keystroke there — nor to the
    /// other mutators that rebuild a row.</summary>
    [Fact]
    public void Every_other_mutator_keeps_the_filter()
    {
        var store = Configured();
        Assert.Null(store.SetPullRequestLabels("backlog", ["frontend"]));

        Assert.Null(store.SetRepositories([Repository("backlog", "Backlog"), Repository("archify", "Archify")]));
        Assert.Null(store.SetRepositoryColour("backlog", 2));
        Assert.Null(store.SetCloneDirectory("backlog", Path.Combine(_root, "clone")));
        Assert.Null(store.SetDevbookSource("backlog", "main", useLocalFolder: false));
        Assert.Null(store.RenameRepository("backlog", "JSdotNet/Backlog2", out _));

        Assert.Equal(["frontend"], store.Current.Find("backlog")!.PullRequestLabels);
        Assert.Equal(["frontend"], Store().Current.Find("backlog")!.PullRequestLabels);
    }

    [Fact]
    public async Task Changing_the_filter_gives_the_registry_document_a_new_version()
    {
        var store = Configured();
        var replication = new GitHubSettingsReplication(store);
        var before = (await replication.ReadAsync(GitHubReplicaDocument.Registry, TestContext.Current.CancellationToken))!.UpdatedAt;

        Assert.Null(store.SetPullRequestLabels("backlog", ["frontend"]));

        var after = (await replication.ReadAsync(GitHubReplicaDocument.Registry, TestContext.Current.CancellationToken))!.UpdatedAt;
        Assert.True(after > before);
    }

    [Fact]
    public async Task The_filter_travels_to_a_paired_device_in_the_registry_document()
    {
        var a = Configured("a");
        var b = Store("b");
        Assert.Null(a.SetPullRequestLabels("backlog", ["frontend"]));

        var copy = await new GitHubSettingsReplication(a).ReadAsync(GitHubReplicaDocument.Registry, TestContext.Current.CancellationToken);
        Assert.NotNull(copy);

        var outcome = await new GitHubSettingsReplication(b).ApplyAsync(GitHubReplicaDocument.Registry, copy, TestContext.Current.CancellationToken);

        Assert.Equal(GitHubReplicaOutcome.Taken, outcome);
        Assert.Equal(["frontend"], b.Current.Find("backlog")!.PullRequestLabels);
        Assert.Equal(["frontend"], Store("b").Current.Find("backlog")!.PullRequestLabels);
    }
}
