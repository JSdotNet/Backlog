using Backlog.Desktop.UI.Knowledge;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The knowledge-source control the pane and the settings screen share: what it
/// offers, what it currently reads, and what picking an entry does.
/// </summary>
public sealed class KnowledgeSourceSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "knowledge-source-selection-tests-" + Guid.NewGuid().ToString("N"));

    public KnowledgeSourceSelectionTests() => Directory.CreateDirectory(_root);

    private GitHubSettingsStore Settings(bool withClone = false)
    {
        var store = new GitHubSettingsStore(Path.Combine(_root, "github.json"), () => Path.Combine(_root, "workspace"));
        Assert.Null(store.SetRepositories([new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog")]));

        if (withClone)
        {
            var clone = Path.Combine(_root, "clone");
            Directory.CreateDirectory(clone);
            Assert.Null(store.SetCloneDirectory("backlog", clone));
        }

        return store;
    }

    private static KnowledgeSourceSelection Selection(GitHubSettingsStore settings, StubBranchCatalog? catalog = null) =>
        new(settings, catalog ?? new StubBranchCatalog());

    // --- What it offers -------------------------------------------------------

    /// <summary>The storage folder is nobody's repository and nobody's branch, so
    /// the pane leaves the control out rather than showing one option.</summary>
    [Fact]
    public void The_storage_scope_has_no_source_to_choose()
    {
        var selection = Selection(Settings());

        Assert.False(selection.CanChoose(null));
        Assert.Empty(selection.Options(null));
    }

    [Fact]
    public void A_repository_with_no_clone_offers_the_default_branch_and_no_local_entry()
    {
        var selection = Selection(Settings());

        var values = selection.Options("backlog").Select(o => o.Value).ToList();

        Assert.Contains(string.Empty, values);
        Assert.DoesNotContain(KnowledgeSourceSelection.LocalFolderValue, values);
    }

    [Fact]
    public void A_repository_with_a_clone_offers_it()
    {
        var selection = Selection(Settings(withClone: true));

        Assert.Contains(KnowledgeSourceSelection.LocalFolderValue, selection.Options("backlog").Select(o => o.Value));
    }

    /// <summary>Offered rather than done on render: listing branches is a network
    /// call and opening a pane must never wait on GitHub.</summary>
    [Fact]
    public void The_branch_list_is_offered_until_it_has_been_loaded()
    {
        var settings = Settings();
        var catalog = new StubBranchCatalog { Branches = { "main", "release/2.0" } };
        var selection = Selection(settings, catalog);

        Assert.Contains(KnowledgeSourceSelection.LoadBranchesValue, selection.Options("backlog").Select(o => o.Value));
        Assert.Equal(0, catalog.Listings);
    }

    [Fact]
    public async Task Loading_replaces_the_offer_with_the_branches()
    {
        var settings = Settings();
        var catalog = new StubBranchCatalog { Branches = { "main", "release/2.0" } };
        var selection = Selection(settings, catalog);

        Assert.Null(await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        var values = selection.Options("backlog").Select(o => o.Value).ToList();

        Assert.Contains("main", values);
        Assert.Contains("release/2.0", values);
        Assert.DoesNotContain(KnowledgeSourceSelection.LoadBranchesValue, values);
        Assert.True(selection.HasBranches("backlog"));
    }

    /// <summary>A pane opened offline shows the choice somebody made rather than
    /// quietly falling back to the default entry and looking like it was never
    /// made.</summary>
    [Fact]
    public void A_configured_branch_is_listed_even_when_nothing_was_loaded()
    {
        var settings = Settings();
        Assert.Null(settings.SetKnowledgeSource("backlog", "release/2.0", useLocalFolder: false));

        var selection = Selection(settings);

        Assert.Contains("release/2.0", selection.Options("backlog").Select(o => o.Value));
        Assert.Equal("release/2.0", selection.CurrentValue("backlog"));
    }

    [Fact]
    public void A_configured_branch_is_not_listed_twice_once_it_is_loaded()
    {
        var settings = Settings();
        Assert.Null(settings.SetKnowledgeSource("backlog", "main", useLocalFolder: false));

        var selection = Selection(settings, new StubBranchCatalog { Branches = { "main" } });
        selection.LoadBranchesAsync("backlog").GetAwaiter().GetResult();

        Assert.Single(selection.Options("backlog").Where(o => o.Value == "main"));
    }

    // --- What it currently reads ---------------------------------------------

    /// <summary>The upgrade default, seen through the control: a repository with a
    /// clone and no stored answer shows the clone selected.</summary>
    [Fact]
    public void A_repository_configured_before_branch_loading_shows_its_clone()
    {
        var selection = Selection(Settings(withClone: true));

        Assert.Equal(KnowledgeSourceSelection.LocalFolderValue, selection.CurrentValue("backlog"));
        Assert.Equal("Local clone", selection.Label("backlog"));
        Assert.Contains("can be edited", selection.Summary("backlog"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_repository_reading_a_branch_says_so_and_says_it_is_read_only()
    {
        var settings = Settings(withClone: true);
        Assert.Null(settings.SetKnowledgeSource("backlog", "release/2.0", useLocalFolder: false));

        var selection = Selection(settings);

        Assert.Equal("release/2.0", selection.Label("backlog"));
        Assert.Contains("read-only", selection.Summary("backlog"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_repository_on_its_default_branch_says_so()
    {
        var selection = Selection(Settings());

        Assert.Equal(string.Empty, selection.CurrentValue("backlog"));
        Assert.Equal("Default branch", selection.Label("backlog"));
    }

    // --- What picking does ----------------------------------------------------

    [Fact]
    public void Choosing_a_branch_stores_it()
    {
        var settings = Settings(withClone: true);
        var selection = Selection(settings);

        Assert.Null(selection.Select("backlog", "release/2.0"));

        var repository = settings.Current.Find("backlog");
        Assert.Equal("release/2.0", repository!.KnowledgeBranch);
        Assert.Equal(KnowledgeSourceKind.Branch, repository.KnowledgeSource);
    }

    [Fact]
    public void Choosing_the_clone_stores_it_and_remembers_the_branch()
    {
        var settings = Settings(withClone: true);
        var selection = Selection(settings);

        Assert.Null(selection.Select("backlog", "release/2.0"));
        Assert.Null(selection.Select("backlog", KnowledgeSourceSelection.LocalFolderValue));

        var repository = settings.Current.Find("backlog");
        Assert.Equal(KnowledgeSourceKind.LocalFolder, repository!.KnowledgeSource);
        Assert.Equal("release/2.0", repository.KnowledgeBranch);
    }

    /// <summary>The load entry is a request, not a source. Picking it must not
    /// send the repository to a branch named "@load".</summary>
    [Fact]
    public void Picking_the_load_entry_changes_no_source()
    {
        var settings = Settings(withClone: true);
        var selection = Selection(settings);

        Assert.True(KnowledgeSourceSelection.IsLoadRequest(KnowledgeSourceSelection.LoadBranchesValue));
        Assert.Null(selection.Select("backlog", KnowledgeSourceSelection.LoadBranchesValue));

        var repository = settings.Current.Find("backlog");
        Assert.Null(repository!.KnowledgeBranch);
        Assert.Equal(KnowledgeSourceKind.LocalFolder, repository.KnowledgeSource);
    }

    /// <summary>
    /// After the list is loaded, the control still reads as the source that is
    /// actually configured.
    /// <para>
    /// The regression this pins was visible only in a browser: picking the load
    /// entry left the DOM select holding <c>@load</c>, that option then vanished
    /// from the rebuilt list, and the browser fell back to displaying a branch
    /// nobody had selected — while the stored source was still the default
    /// branch. The hosts key the control on this so it is rebuilt rather than
    /// re-rendered in place; what this asserts is the half that is testable here,
    /// namely that loading changes what is offered without changing what is
    /// current.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Loading_the_branches_does_not_change_what_is_current()
    {
        var settings = Settings(withClone: true);
        var selection = Selection(settings, new StubBranchCatalog { Branches = { "main", "didactic-waffle" } });

        Assert.Null(selection.Select("backlog", string.Empty));
        var before = selection.CurrentValue("backlog");

        Assert.Null(await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        Assert.Equal(before, selection.CurrentValue("backlog"));
        Assert.Equal(string.Empty, selection.CurrentValue("backlog"));
        Assert.Equal("Default branch", selection.Label("backlog"));

        // And the value it reads as is one the rebuilt list actually offers, which
        // is what the browser needs in order to show the right entry.
        Assert.Contains(selection.CurrentValue("backlog"), selection.Options("backlog").Select(o => o.Value));
    }

    /// <summary>The local-folder value uses a character git forbids in a branch
    /// name, so it can never collide with a branch actually called that.</summary>
    [Fact]
    public void The_local_value_cannot_be_a_branch_name()
    {
        Assert.Contains('@', KnowledgeSourceSelection.LocalFolderValue);
        Assert.NotEqual("local", KnowledgeSourceSelection.LocalFolderValue);
    }

    [Fact]
    public async Task A_refused_branch_listing_comes_back_as_a_message()
    {
        var selection = Selection(Settings(), new StubBranchCatalog { Failure = "GitHub refused the request." });

        Assert.Equal("GitHub refused the request.", await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_repository_with_no_branches_says_so()
    {
        var selection = Selection(Settings(), new StubBranchCatalog());

        Assert.Contains("no branches", await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken)!, StringComparison.OrdinalIgnoreCase);
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
    }
}
