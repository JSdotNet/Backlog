using Backlog.Desktop.UI.Devbook;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The devbook-source control the pane and the settings screen share: what it
/// offers, what it currently reads, and what picking an entry does.
/// </summary>
public sealed class DevbookSourceSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "devbook-source-selection-tests-" + Guid.NewGuid().ToString("N"));

    public DevbookSourceSelectionTests() => Directory.CreateDirectory(_root);

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

    private static DevbookSourceSelection Selection(GitHubSettingsStore settings, StubBranchCatalog? catalog = null) =>
        new(settings, catalog ?? new StubBranchCatalog());

    // --- What it offers -------------------------------------------------------

    /// <summary>No scope is nobody's repository and nobody's branch, so the pane
    /// leaves the control out rather than showing one option.</summary>
    [Fact]
    public void No_scope_has_no_source_to_choose()
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
        Assert.DoesNotContain(DevbookSourceSelection.LocalFolderValue, values);
    }

    [Fact]
    public void A_repository_with_a_clone_offers_it()
    {
        var selection = Selection(Settings(withClone: true));

        Assert.Contains(DevbookSourceSelection.LocalFolderValue, selection.Options("backlog").Select(o => o.Value));
    }

    /// <summary>Offered rather than done on render: listing branches is a network
    /// call and opening a pane must never wait on GitHub.</summary>
    [Fact]
    public void The_branch_list_is_offered_until_it_has_been_loaded()
    {
        var settings = Settings();
        var catalog = new StubBranchCatalog { Branches = { "main", "release/2.0" } };
        var selection = Selection(settings, catalog);

        Assert.Contains(DevbookSourceSelection.LoadBranchesValue, selection.Options("backlog").Select(o => o.Value));
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
        Assert.DoesNotContain(DevbookSourceSelection.LoadBranchesValue, values);
        Assert.True(selection.HasBranches("backlog"));
    }

    /// <summary>A pane opened offline shows the choice somebody made rather than
    /// quietly falling back to the default entry and looking like it was never
    /// made.</summary>
    [Fact]
    public void A_configured_branch_is_listed_even_when_nothing_was_loaded()
    {
        var settings = Settings();
        Assert.Null(settings.SetDevbookSource("backlog", "release/2.0", useLocalFolder: false));

        var selection = Selection(settings);

        Assert.Contains("release/2.0", selection.Options("backlog").Select(o => o.Value));
        Assert.Equal("release/2.0", selection.CurrentValue("backlog"));
    }

    [Fact]
    public async Task A_configured_branch_is_not_listed_twice_once_it_is_loaded()
    {
        var settings = Settings();
        Assert.Null(settings.SetDevbookSource("backlog", "main", useLocalFolder: false));

        var selection = Selection(settings, new StubBranchCatalog { Branches = { "main" } });
        Assert.Null(await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        Assert.Single(selection.Options("backlog"), option => option.Value == "main");
    }

    /// <summary>
    /// One section, holding the branches and nothing else.
    ///
    /// <para>The list mixes four kinds of entry — a default, the branches, the local
    /// clone, and an action that fetches more — and only the branches are the same
    /// kind of thing as each other. <c>SelectField</c> draws that as an
    /// <c>optgroup</c>, so the group names decide what the reader sees; a stored
    /// branch that named a different section than a loaded one would split the
    /// branches into two headings that mean the same thing.</para>
    ///
    /// <para>Asserted as positions rather than as a set, because a section is a run:
    /// a branch that drifted below "Local clone" would open a second heading without
    /// any group name having changed.</para>
    /// </summary>
    [Fact]
    public async Task Only_the_branches_form_a_section_and_they_form_one_section()
    {
        var settings = Settings(withClone: true);
        Assert.Null(settings.SetDevbookSource("backlog", "release/2.0", useLocalFolder: false));

        var selection = Selection(settings, new StubBranchCatalog { Branches = { "main" } });
        Assert.Null(await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        Assert.Equal(
            [null, "Branches", "Branches", null],
            selection.Options("backlog").Select(option => option.Group));

        // The configured branch names the section; "Configured" is its hint, which
        // is a different field and reaches the reader a different way.
        var configured = selection.Options("backlog").Single(option => option.Value == "release/2.0");
        Assert.Equal("Branches", configured.Group);
        Assert.Equal("Configured", configured.Hint);
    }

    // --- What it currently reads ---------------------------------------------

    /// <summary>The upgrade default, seen through the control: a repository with a
    /// clone and no stored answer shows the clone selected.</summary>
    [Fact]
    public void A_repository_configured_before_branch_loading_shows_its_clone()
    {
        var selection = Selection(Settings(withClone: true));

        Assert.Equal(DevbookSourceSelection.LocalFolderValue, selection.CurrentValue("backlog"));
        Assert.Equal("Local clone", selection.Label("backlog"));
        Assert.Contains("can be edited", selection.Summary("backlog"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_repository_reading_a_branch_says_so_and_says_it_is_read_only()
    {
        var settings = Settings(withClone: true);
        Assert.Null(settings.SetDevbookSource("backlog", "release/2.0", useLocalFolder: false));

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
        Assert.Equal("release/2.0", repository!.DevbookBranch);
        Assert.Equal(DevbookSourceKind.Branch, repository.DevbookSource);
    }

    [Fact]
    public void Choosing_the_clone_stores_it_and_remembers_the_branch()
    {
        var settings = Settings(withClone: true);
        var selection = Selection(settings);

        Assert.Null(selection.Select("backlog", "release/2.0"));
        Assert.Null(selection.Select("backlog", DevbookSourceSelection.LocalFolderValue));

        var repository = settings.Current.Find("backlog");
        Assert.Equal(DevbookSourceKind.LocalFolder, repository!.DevbookSource);
        Assert.Equal("release/2.0", repository.DevbookBranch);
    }

    /// <summary>The load entry is a request, not a source. Picking it must not
    /// send the repository to a branch named "@load".</summary>
    [Fact]
    public void Picking_the_load_entry_changes_no_source()
    {
        var settings = Settings(withClone: true);
        var selection = Selection(settings);

        Assert.True(DevbookSourceSelection.IsLoadRequest(DevbookSourceSelection.LoadBranchesValue));
        Assert.Null(selection.Select("backlog", DevbookSourceSelection.LoadBranchesValue));

        var repository = settings.Current.Find("backlog");
        Assert.Null(repository!.DevbookBranch);
        Assert.Equal(DevbookSourceKind.LocalFolder, repository.DevbookSource);
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
        Assert.Contains('@', DevbookSourceSelection.LocalFolderValue);
        Assert.NotEqual("local", DevbookSourceSelection.LocalFolderValue);
    }

    // --- What the search field suggests --------------------------------------

    /// <summary>The field takes a picked suggestion's value as its text, so every
    /// value is the words the reader sees, never a marker like <c>@local</c>.</summary>
    [Fact]
    public async Task Suggestions_carry_their_labels_as_their_values()
    {
        var settings = Settings(withClone: true);
        var selection = Selection(settings, new StubBranchCatalog { Branches = { "main", "release/2.0" } });
        Assert.Null(await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        var suggestions = selection.Suggestions("backlog", null);

        Assert.All(suggestions, option => Assert.Equal(option.Label, option.Value));
        Assert.Equal(
            [DevbookSourceSelection.DefaultBranchLabel, "main", "release/2.0", DevbookSourceSelection.LocalFolderLabel],
            suggestions.Select(option => option.Value));
    }

    [Fact]
    public async Task A_query_keeps_the_suggestions_containing_it_whatever_the_case()
    {
        var selection = Selection(
            Settings(withClone: true),
            new StubBranchCatalog { Branches = { "main", "claude/search-picker", "codex/Search-fix", "release/2.0" } });
        Assert.Null(await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        var suggestions = selection.Suggestions("backlog", "SEARCH");

        Assert.Equal(["claude/search-picker", "codex/Search-fix"], suggestions.Select(option => option.Value));
    }

    /// <summary>A search with nothing loaded has nothing to find yet, so the entry
    /// that fetches the list stays on offer whatever was typed.</summary>
    [Fact]
    public void The_load_entry_survives_any_query_until_the_branches_are_in()
    {
        var selection = Selection(Settings());

        var suggestions = selection.Suggestions("backlog", "feature/x");

        Assert.Equal([DevbookSourceSelection.LoadBranchesLabel], suggestions.Select(option => option.Value));
    }

    [Fact]
    public void A_blank_query_is_no_filter()
    {
        var selection = Selection(Settings(withClone: true));

        Assert.Equal(
            selection.Suggestions("backlog", null).Select(option => option.Value),
            selection.Suggestions("backlog", "  ").Select(option => option.Value));
    }

    [Fact]
    public void No_scope_suggests_nothing()
    {
        Assert.Empty(Selection(Settings()).Suggestions(null, null));
    }

    // --- What typed text means ------------------------------------------------

    [Fact]
    public void The_default_label_means_the_default_branch()
    {
        Assert.Equal(string.Empty, Selection(Settings()).ValueFor("backlog", DevbookSourceSelection.DefaultBranchLabel));
    }

    /// <summary>An emptied field is no answer; the default branch has its own
    /// words, so blank cannot be mistaken for choosing it.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_text_means_nothing(string? text)
    {
        Assert.Null(Selection(Settings()).ValueFor("backlog", text));
    }

    [Fact]
    public void The_clone_label_means_the_clone_where_there_is_one()
    {
        Assert.Equal(
            DevbookSourceSelection.LocalFolderValue,
            Selection(Settings(withClone: true)).ValueFor("backlog", DevbookSourceSelection.LocalFolderLabel));
    }

    [Fact]
    public void The_clone_label_means_nothing_without_a_clone()
    {
        Assert.Null(Selection(Settings()).ValueFor("backlog", DevbookSourceSelection.LocalFolderLabel));
    }

    [Fact]
    public void The_load_label_is_the_load_request_while_nothing_is_loaded()
    {
        Assert.Equal(
            DevbookSourceSelection.LoadBranchesValue,
            Selection(Settings()).ValueFor("backlog", DevbookSourceSelection.LoadBranchesLabel));
    }

    [Fact]
    public async Task A_loaded_branch_means_itself()
    {
        var selection = Selection(Settings(), new StubBranchCatalog { Branches = { "main", "release/2.0" } });
        Assert.Null(await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        Assert.Equal("release/2.0", selection.ValueFor("backlog", "release/2.0"));
    }

    [Fact]
    public void The_configured_branch_means_itself_with_nothing_loaded()
    {
        var settings = Settings();
        Assert.Null(settings.SetDevbookSource("backlog", "release/2.0", useLocalFolder: false));

        Assert.Equal("release/2.0", Selection(settings).ValueFor("backlog", "release/2.0"));
    }

    /// <summary>The stored name is the branch's own spelling: git compares names
    /// exactly, so a branch typed in the wrong case is taken as the one it matches.</summary>
    [Fact]
    public async Task A_branch_typed_in_another_case_means_the_branch_as_it_is_spelled()
    {
        var selection = Selection(Settings(), new StubBranchCatalog { Branches = { "main", "Feature/Search" } });
        Assert.Null(await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        Assert.Equal("Feature/Search", selection.ValueFor("backlog", "feature/search"));
    }

    [Fact]
    public async Task A_case_insensitive_match_naming_two_branches_means_nothing()
    {
        var selection = Selection(Settings(), new StubBranchCatalog { Branches = { "Fix", "fix" } });
        Assert.Null(await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        Assert.Null(selection.ValueFor("backlog", "FIX"));
        Assert.Equal("fix", selection.ValueFor("backlog", "fix"));
    }

    [Fact]
    public async Task A_branch_the_repository_does_not_have_means_nothing()
    {
        var selection = Selection(Settings(), new StubBranchCatalog { Branches = { "main" } });
        Assert.Null(await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        Assert.Null(selection.ValueFor("backlog", "no-such-branch"));
    }

    [Fact]
    public void An_unknown_branch_means_nothing_before_the_list_is_loaded()
    {
        Assert.Null(Selection(Settings()).ValueFor("backlog", "main"));
    }

    /// <summary>Git forbids a space in a branch name, so a label for one of the
    /// other entries can never be read as a branch somebody created.</summary>
    [Fact]
    public void The_entry_labels_cannot_be_branch_names()
    {
        Assert.Contains(' ', DevbookSourceSelection.DefaultBranchLabel);
        Assert.Contains(' ', DevbookSourceSelection.LocalFolderLabel);
        Assert.Contains(' ', DevbookSourceSelection.LoadBranchesLabel);
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

    // --- With the branch overview -----------------------------------------------

    private static (DevbookSourceSelection Selection, DevbookBranchOverview Overview, StubBranchCatalog Catalog, ScriptedBranchComparisons Comparisons, ScriptedGitRepositoryService Git) WithOverview(
        GitHubSettingsStore settings,
        params string[] branches)
    {
        var catalog = new StubBranchCatalog { Heads = { [string.Empty] = new GitHubBranchHead("main", "sha-main") } };
        catalog.Branches.AddRange(branches);
        var comparisons = new ScriptedBranchComparisons();
        var git = new ScriptedGitRepositoryService();
        var overview = new DevbookBranchOverview(settings, catalog, new DevbookBranchComparisons(comparisons), git);

        return (new DevbookSourceSelection(settings, catalog, overview), overview, catalog, comparisons, git);
    }

    private static GitHubChangedFile Changed(string path) => new(path, null, GitHubFileStatus.Modified, "blob");

    /// <summary>The picker lists the branches that change the devbook; a branch
    /// changing only code is no source anybody reads the devbook from.</summary>
    [Fact]
    public async Task Once_the_overview_is_in_only_branches_changing_the_devbook_are_listed()
    {
        var (selection, overview, _, comparisons, _) = WithOverview(Settings(withClone: true), "main", "docs/adr", "code/only");
        comparisons.Changes["sha-docs/adr"] = [Changed(".devbook/arc42/adr/0009.md")];
        comparisons.Changes["sha-code/only"] = [Changed("src/Program.cs")];

        await overview.LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Equal(
            [string.Empty, "docs/adr", DevbookSourceSelection.LocalFolderValue, DevbookSourceSelection.LoadBranchesValue],
            selection.Options("backlog").Select(option => option.Value));
    }

    /// <summary>The choice somebody made stays in sight even when it changes
    /// nothing in the devbook.</summary>
    [Fact]
    public async Task The_configured_branch_is_listed_even_when_it_changes_no_devbook()
    {
        var settings = Settings();
        Assert.Null(settings.SetDevbookSource("backlog", "code/only", useLocalFolder: false));
        var (selection, overview, _, comparisons, _) = WithOverview(settings, "main", "code/only");
        comparisons.Changes["sha-code/only"] = [Changed("src/Program.cs")];

        await overview.LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Single(selection.Options("backlog"), option => option.Value == "code/only");
    }

    /// <summary>The load replaced the request to fetch the list; what is left is
    /// the request to ask again.</summary>
    [Fact]
    public async Task The_load_entry_becomes_a_refresh_once_the_overview_is_in()
    {
        var (selection, overview, _, _, _) = WithOverview(Settings(), "main");

        await overview.LoadAsync("backlog", TestContext.Current.CancellationToken);

        var entry = Assert.Single(selection.Options("backlog"), option => DevbookSourceSelection.IsLoadRequest(option.Value));
        Assert.Equal(DevbookSourceSelection.RefreshBranchesLabel, entry.Label);
        Assert.Equal(DevbookSourceSelection.LoadBranchesValue, selection.ValueFor("backlog", DevbookSourceSelection.RefreshBranchesLabel));
        Assert.Null(selection.ValueFor("backlog", DevbookSourceSelection.LoadBranchesLabel));
    }

    [Fact]
    public async Task No_load_entry_is_offered_while_the_overview_loads()
    {
        var (selection, overview, _, comparisons, _) = WithOverview(Settings(), "main", "docs/adr");
        comparisons.Gate = new TaskCompletionSource();

        var load = overview.LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(selection.Options("backlog"), option => DevbookSourceSelection.IsLoadRequest(option.Value));
        comparisons.Gate.SetResult();
        await load;
    }

    /// <summary>Settings' Load button and the refresh entry both ask the overview
    /// again, rather than fetching a second list beside it.</summary>
    [Fact]
    public async Task Loading_branches_refreshes_the_overview()
    {
        var (selection, overview, catalog, _, _) = WithOverview(Settings(), "main");
        await overview.LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Null(await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        Assert.Equal(2, catalog.HeadListings);
        Assert.Equal(0, catalog.Listings);
    }

    /// <summary>The reason goes where the pane shows a refused source, and the
    /// picker keeps offering what it always did.</summary>
    [Fact]
    public async Task A_refused_overview_keeps_the_default_the_clone_and_the_configured_branch()
    {
        var settings = Settings(withClone: true);
        Assert.Null(settings.SetDevbookSource("backlog", "release/2.0", useLocalFolder: true));
        var catalog = new StubBranchCatalog { Failure = "No network." };
        var overview = new DevbookBranchOverview(settings, catalog, new DevbookBranchComparisons(new ScriptedBranchComparisons()), new ScriptedGitRepositoryService());
        var selection = new DevbookSourceSelection(settings, catalog, overview);

        Assert.Equal("No network.", await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        Assert.Equal(
            [string.Empty, "release/2.0", DevbookSourceSelection.LocalFolderValue, DevbookSourceSelection.LoadBranchesValue],
            selection.Options("backlog").Select(option => option.Value));
    }

    /// <summary>Typing a listed branch that changes no devbook still names it: the
    /// filter decides what is suggested, not what exists.</summary>
    [Fact]
    public async Task A_listed_branch_left_out_by_the_filter_still_means_itself()
    {
        var (selection, overview, _, _, _) = WithOverview(Settings(), "main", "code/only");
        await overview.LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Equal("code/only", selection.ValueFor("backlog", "code/only"));
        Assert.Equal("main", selection.ValueFor("backlog", "main"));
    }

    [Fact]
    public async Task A_typed_branch_is_still_resolved_through_github_and_then_listed()
    {
        var (selection, overview, catalog, _, _) = WithOverview(Settings(), "main");
        await overview.LoadAsync("backlog", TestContext.Current.CancellationToken);
        catalog.Heads["past/the-ceiling"] = new GitHubBranchHead("past/the-ceiling", "sha-past");

        Assert.Equal("past/the-ceiling", await selection.ResolveTypedBranchAsync("backlog", "past/the-ceiling", TestContext.Current.CancellationToken));

        Assert.Contains("past/the-ceiling", selection.Options("backlog").Select(option => option.Value));
    }

    /// <summary>The settings screen keys its select on this, so the control is
    /// rebuilt after every load — a refresh that changes nothing else included —
    /// rather than keeping the refresh entry shown as chosen.</summary>
    [Fact]
    public async Task Every_load_moves_the_list_generation()
    {
        var (selection, overview, _, _, _) = WithOverview(Settings(), "main");
        var before = selection.ListGeneration("backlog");

        await overview.LoadAsync("backlog", TestContext.Current.CancellationToken);
        var afterLoad = selection.ListGeneration("backlog");
        await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken);

        Assert.NotEqual(before, afterLoad);
        Assert.NotEqual(afterLoad, selection.ListGeneration("backlog"));
    }

    [Fact]
    public async Task Every_load_moves_the_list_generation_without_an_overview_too()
    {
        var selection = Selection(Settings(), new StubBranchCatalog { Branches = { "main" } });
        var before = selection.ListGeneration("backlog");

        await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken);

        Assert.NotEqual(before, selection.ListGeneration("backlog"));
    }

    /// <summary>A refusal on asking again leaves the list that was in, in.</summary>
    [Fact]
    public async Task A_refused_refresh_keeps_the_list_in()
    {
        var (selection, overview, catalog, comparisons, _) = WithOverview(Settings(), "main", "docs/adr");
        comparisons.Changes["sha-docs/adr"] = [Changed(".devbook/arc42/x.md")];
        await overview.LoadAsync("backlog", TestContext.Current.CancellationToken);
        catalog.Failure = "No network.";

        Assert.Equal("No network.", await selection.LoadBranchesAsync("backlog", TestContext.Current.CancellationToken));

        Assert.True(selection.HasBranches("backlog"));
        Assert.Contains("docs/adr", selection.Options("backlog").Select(option => option.Value));
    }

    /// <summary>What a row of the picker shows beside its name.</summary>
    [Fact]
    public async Task A_branch_row_carries_its_sections_and_counts()
    {
        var (selection, overview, _, comparisons, _) = WithOverview(Settings(), "main", "docs/adr");
        comparisons.Changes["sha-docs/adr"] = [Changed(".devbook/design/x.md"), Changed(".devbook/arc42/y.md")];
        comparisons.Counts["sha-docs/adr"] = (3, 7);
        await overview.LoadAsync("backlog", TestContext.Current.CancellationToken);

        var row = selection.Row("backlog", "docs/adr");

        Assert.Equal(new DevbookSourceRow(["arc42", "design"], 3, 7, false), row, DevbookSourceRowComparer.Instance);
    }

    [Fact]
    public async Task The_clone_row_carries_its_counts_against_its_upstream()
    {
        var (selection, overview, _, _, git) = WithOverview(Settings(withClone: true), "main");
        git.NextCheck = new LocalGitRepositoryUpdateCheck(LocalGitRepositoryCurrency.Behind, 1, 4, false, "origin/main", "4 behind.");
        await overview.LoadAsync("backlog", TestContext.Current.CancellationToken);

        var row = selection.Row("backlog", DevbookSourceSelection.LocalFolderLabel);

        Assert.Equal(1, row!.AheadBy);
        Assert.Equal(4, row.BehindBy);
    }

    /// <summary>A count nobody could read is shown as nothing, never as 0.</summary>
    [Fact]
    public async Task A_clone_git_could_not_place_has_no_counts()
    {
        var (selection, overview, _, _, git) = WithOverview(Settings(withClone: true), "main");
        git.NextCheck = LocalGitRepositoryUpdateCheck.Undetermined("No upstream.");
        await overview.LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Null(selection.Row("backlog", DevbookSourceSelection.LocalFolderLabel));
    }

    /// <summary>A clone tracking nothing, or on no branch, has no upstream to be
    /// ahead of or behind: git answers 0 and 0, which is no count at all.</summary>
    [Theory]
    [InlineData(LocalGitRepositoryCurrency.NoUpstream)]
    [InlineData(LocalGitRepositoryCurrency.Detached)]
    public async Task A_clone_with_no_upstream_has_no_counts(LocalGitRepositoryCurrency currency)
    {
        var (selection, overview, _, _, git) = WithOverview(Settings(withClone: true), "main");
        git.NextCheck = new LocalGitRepositoryUpdateCheck(currency, 0, 0, false, null, "No upstream.");
        await overview.LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Null(selection.Row("backlog", DevbookSourceSelection.LocalFolderLabel));
    }

    /// <summary>While the list loads, the pane's list says so where the refresh
    /// entry will be, and the entry is inert: it names no source and no request.</summary>
    [Fact]
    public async Task While_the_overview_loads_the_suggestions_say_so_and_the_entry_names_nothing()
    {
        var settings = Settings(withClone: true);
        var (selection, overview, _, comparisons, _) = WithOverview(settings, "main", "docs/adr");
        comparisons.Gate = new TaskCompletionSource();

        var load = overview.LoadAsync("backlog", TestContext.Current.CancellationToken);

        var labels = selection.Suggestions("backlog", null).Select(option => option.Label).ToList();
        Assert.Equal(DevbookSourceSelection.LoadingBranchesLabel, labels[^1]);
        Assert.DoesNotContain(DevbookSourceSelection.RefreshBranchesLabel, labels);
        Assert.DoesNotContain(selection.Options("backlog"), option => option.Label == DevbookSourceSelection.LoadingBranchesLabel);
        Assert.True(DevbookSourceSelection.IsLoadingEntry(selection.ValueFor("backlog", DevbookSourceSelection.LoadingBranchesLabel)));

        Assert.Null(selection.Select("backlog", DevbookSourceSelection.LoadingBranchesValue));
        Assert.Equal(DevbookSourceSelection.LocalFolderValue, selection.CurrentValue("backlog"));

        comparisons.Gate.SetResult();
        await load;
        Assert.DoesNotContain(selection.Suggestions("backlog", null), option => option.Label == DevbookSourceSelection.LoadingBranchesLabel);
    }

    [Fact]
    public void Nothing_loaded_means_no_row()
    {
        var (selection, _, _, _, _) = WithOverview(Settings(), "main", "docs/adr");

        Assert.Null(selection.Row("backlog", "docs/adr"));
        Assert.Null(selection.Row("backlog", DevbookSourceSelection.DefaultBranchLabel));
    }

    private sealed class DevbookSourceRowComparer : IEqualityComparer<DevbookSourceRow?>
    {
        public static readonly DevbookSourceRowComparer Instance = new();

        public bool Equals(DevbookSourceRow? x, DevbookSourceRow? y) =>
            x is not null && y is not null
            && x.Areas.SequenceEqual(y.Areas)
            && x.AheadBy == y.AheadBy
            && x.BehindBy == y.BehindBy
            && x.MayChange == y.MayChange;

        public int GetHashCode(DevbookSourceRow? obj) => 0;
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
