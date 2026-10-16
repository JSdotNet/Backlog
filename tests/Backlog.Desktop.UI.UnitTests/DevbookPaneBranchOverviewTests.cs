using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The source picker once the pane loads the branch overview on opening a
/// repository: the load happens once, the list holds the branches that change the
/// devbook with their section glyphs and counts, the tabs wear the same glyphs, and
/// the version control belongs to the clone alone, armed by what the load found.
/// </summary>
public sealed class DevbookPaneBranchOverviewTests
{
    // --- Loading once -------------------------------------------------------

    /// <summary>One load on opening; the picker opened twice and a tab switched
    /// ask GitHub and git nothing more.</summary>
    [Fact]
    public async Task Opening_the_pane_loads_once_and_the_picker_and_tabs_ask_nothing_more()
    {
        await using var harness = CreateHarness(branches: ["main", "docs/adr"]);
        harness.Comparisons.Changes["sha-docs/adr"] = [Changed(".devbook/arc42/adr/0009.md")];

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(harness.Overview.Current(harness.RepositoryAlias)));

        OpenPicker(component);
        component.WaitForAssertion(() => Assert.Contains("docs/adr", OptionNames(component)));
        Raise(component, () => SourceInput(component).KeyDown(new KeyboardEventArgs { Key = "Escape" }));
        OpenPicker(component);
        component.WaitForAssertion(() => Assert.Contains("docs/adr", OptionNames(component)));
        Raise(component, () => component.FindAll("[role='tab']")[^1].Click());

        Assert.Equal(1, harness.Catalog.HeadListings);
        Assert.Single(harness.Comparisons.Compared);
        Assert.Equal(1, harness.Git.ChecksRequested);
        Assert.Equal(0, harness.Catalog.Listings);
    }

    // --- What the list holds -----------------------------------------------

    [Fact]
    public async Task Only_branches_changing_the_devbook_are_offered()
    {
        await using var harness = CreateHarness(branches: ["main", "docs/adr", "code/only"]);
        harness.Comparisons.Changes["sha-docs/adr"] = [Changed(".devbook/arc42/adr/0009.md")];
        harness.Comparisons.Changes["sha-code/only"] = [Changed("src/Program.cs")];

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(harness.Overview.Current(harness.RepositoryAlias)));

        OpenPicker(component);

        component.WaitForAssertion(() => Assert.Equal(
            [DevbookSourceSelection.DefaultBranchLabel, "docs/adr", DevbookSourceSelection.LocalFolderLabel, DevbookSourceSelection.RefreshBranchesLabel],
            OptionNames(component)));
    }

    /// <summary>One glyph per section the branch changes, each said in words.</summary>
    [Fact]
    public async Task A_branch_row_shows_a_glyph_for_each_section_it_changes_and_its_counts()
    {
        await using var harness = CreateHarness(branches: ["main", "docs/adr"]);
        harness.Comparisons.Changes["sha-docs/adr"] = [Changed(".devbook/design/tokens.md"), Changed(".devbook/arc42/README.md")];
        harness.Comparisons.Counts["sha-docs/adr"] = (2, 5);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(harness.Overview.Current(harness.RepositoryAlias)));
        OpenPicker(component);

        component.WaitForAssertion(() =>
        {
            var row = Option(component, "docs/adr");

            Assert.Equal(
                ["devbook-source-area-arc42", "devbook-source-area-design"],
                row.QuerySelectorAll("svg").Select(icon => icon.GetAttribute("data-testid")));
            Assert.All(row.QuerySelectorAll("svg"), icon => Assert.Equal("true", icon.GetAttribute("aria-hidden")));
            Assert.Contains("Changes Architecture", row.TextContent, StringComparison.Ordinal);
            Assert.Contains("Changes Design", row.TextContent, StringComparison.Ordinal);
            Assert.Contains("↑2 ↓5", row.QuerySelector("[data-testid='devbook-source-drift']")!.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task The_clone_row_shows_its_counts_against_its_upstream()
    {
        await using var harness = CreateHarness(branches: ["main"]);
        harness.Git.NextCheck = new LocalGitRepositoryUpdateCheck(LocalGitRepositoryCurrency.Diverged, 1, 4, false, "origin/main", "Diverged.");

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(harness.Overview.Current(harness.RepositoryAlias)));
        OpenPicker(component);

        component.WaitForAssertion(() => Assert.Contains(
            "↑1 ↓4",
            Option(component, DevbookSourceSelection.LocalFolderLabel).QuerySelector("[data-testid='devbook-source-drift']")!.TextContent,
            StringComparison.Ordinal));
    }

    /// <summary>The picker works as it did without the list, and says why it
    /// holds so little.</summary>
    [Fact]
    public async Task A_refused_load_says_why_and_the_picker_keeps_working()
    {
        await using var harness = CreateHarness(branches: ["main"], failure: "API rate limit exceeded.");

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.Equal(
            "API rate limit exceeded.",
            component.Find("[data-testid='devbook-source-error']").TextContent.Trim()));

        OpenPicker(component);
        component.WaitForAssertion(() => Assert.Equal(
            [DevbookSourceSelection.DefaultBranchLabel, DevbookSourceSelection.LocalFolderLabel, DevbookSourceSelection.RefreshBranchesLabel],
            OptionNames(component)));
    }

    /// <summary>"Refresh branches" is a request, not a source: while it runs the
    /// field goes on naming the source being read.</summary>
    [Fact]
    public async Task While_a_refresh_runs_the_field_keeps_naming_the_source_read()
    {
        await using var harness = CreateHarness(branches: ["main", "docs/adr"]);
        harness.Comparisons.Changes["sha-docs/adr"] = [Changed(".devbook/arc42/adr/0009.md")];

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(harness.Overview.Current(harness.RepositoryAlias)));
        var gate = new TaskCompletionSource();
        harness.Catalog.Gates[string.Empty] = gate;

        OpenPicker(component);
        component.WaitForAssertion(() => Assert.Contains(DevbookSourceSelection.RefreshBranchesLabel, OptionNames(component)));
        Raise(component, () => Option(component, DevbookSourceSelection.RefreshBranchesLabel).Click());

        component.WaitForAssertion(() =>
        {
            Assert.True(harness.Overview.IsLoading(harness.RepositoryAlias));
            Assert.Equal(DevbookSourceSelection.LocalFolderLabel, SourceInput(component).GetAttribute("value"));
        });

        gate.SetResult();
        component.WaitForAssertion(() => Assert.False(harness.Overview.IsLoading(harness.RepositoryAlias)));
        Assert.Equal(DevbookSourceSelection.LocalFolderLabel, SourceInput(component).GetAttribute("value"));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-error']"));
    }

    /// <summary>The first load says it is running, where the refresh entry will
    /// be; the entry does nothing when picked.</summary>
    [Fact]
    public async Task The_list_says_the_branches_are_loading_while_the_first_load_runs()
    {
        await using var harness = CreateHarness(branches: ["main", "docs/adr"]);
        var gate = new TaskCompletionSource();
        harness.Catalog.Gates[string.Empty] = gate;

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.True(harness.Overview.IsLoading(harness.RepositoryAlias)));
        OpenPicker(component);

        component.WaitForAssertion(() =>
        {
            Assert.Equal(DevbookSourceSelection.LoadingBranchesLabel, OptionNames(component)[^1]);
            Assert.DoesNotContain(DevbookSourceSelection.RefreshBranchesLabel, OptionNames(component));
        });

        Raise(component, () => Option(component, DevbookSourceSelection.LoadingBranchesLabel).Click());
        component.WaitForAssertion(() => Assert.Equal(DevbookSourceSelection.LocalFolderLabel, SourceInput(component).GetAttribute("value")));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-error']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-status']"));

        gate.SetResult();
        OpenPicker(component);
        component.WaitForAssertion(() => Assert.Equal(DevbookSourceSelection.RefreshBranchesLabel, OptionNames(component)[^1]));
    }

    // --- The section tabs ------------------------------------------------------

    [Fact]
    public async Task The_section_tabs_wear_their_glyph_before_their_name()
    {
        await using var harness = CreateHarness(branches: ["main"]);

        var component = harness.Render();

        component.WaitForAssertion(() =>
        {
            var tab = component.FindAll("[role='tab']").Single(tab => tab.TextContent.Trim() == "Architecture");
            var icon = tab.QuerySelector("svg[data-testid='devbook-tab-icon-arc42']");

            Assert.NotNull(icon);
            Assert.Equal("true", icon.GetAttribute("aria-hidden"));
            Assert.Same(icon, tab.QuerySelector("svg"));
        });
    }

    // --- The version control -------------------------------------------------

    /// <summary>The load already asked git where the clone stands, so a clone
    /// found behind offers the pull at once.</summary>
    [Fact]
    public async Task A_clone_found_behind_offers_the_pull_without_a_check_first()
    {
        await using var harness = CreateHarness(branches: ["main"]);
        harness.Git.NextCheck = new LocalGitRepositoryUpdateCheck(LocalGitRepositoryCurrency.Behind, 0, 3, false, "origin/main", "3 commits behind origin/main.");

        var component = harness.Render();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("Pull latest", component.Find("[data-testid='devbook-update-action']").TextContent.Trim());
            Assert.Equal("3 behind", component.Find("[data-testid='devbook-update-behind']").TextContent.Trim());
        });
        Assert.Equal(1, harness.Git.ChecksRequested);
        Assert.Empty(component.FindAll("[data-testid='devbook-update-status']"));
    }

    /// <summary>A pull moves the clone's row and asks GitHub nothing.</summary>
    [Fact]
    public async Task Pulling_moves_the_clone_row_and_lists_no_branches_again()
    {
        await using var harness = CreateHarness(branches: ["main"]);
        harness.Git.NextCheck = new LocalGitRepositoryUpdateCheck(LocalGitRepositoryCurrency.Behind, 0, 3, false, "origin/main", "3 commits behind origin/main.");
        harness.Git.NextPull = LocalGitRepositoryPullResult.Succeeded(
            "Pulled.",
            new LocalGitRepositoryUpdateCheck(LocalGitRepositoryCurrency.UpToDate, 0, 0, false, "origin/main", "On the latest version of origin/main."));

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal("Pull latest", component.Find("[data-testid='devbook-update-action']").TextContent.Trim()));

        Raise(component, () => component.Find("[data-testid='devbook-update-action']").Click());
        component.WaitForAssertion(() => Assert.Equal("Check now", component.Find("[data-testid='devbook-update-action']").TextContent.Trim()));

        OpenPicker(component);
        component.WaitForAssertion(() => Assert.Contains(
            "↑0 ↓0",
            Option(component, DevbookSourceSelection.LocalFolderLabel).QuerySelector("[data-testid='devbook-source-drift']")!.TextContent,
            StringComparison.Ordinal));
        Assert.Equal(1, harness.Catalog.HeadListings);
    }

    /// <summary>A branch is kept current by the snapshot's own cadence, so neither
    /// the button nor the badge is drawn for one — the default branch included.</summary>
    [Fact]
    public async Task A_branch_shows_no_version_control()
    {
        await using var harness = CreateHarness(branches: ["main", "docs/adr"], source: "docs/adr");
        harness.Comparisons.Changes["sha-docs/adr"] = [Changed(".devbook/arc42/adr/0009.md")];

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(harness.Overview.Current(harness.RepositoryAlias)));
        component.WaitForAssertion(() => Assert.Equal("docs/adr", SourceInput(component).GetAttribute("value")));

        Assert.Empty(component.FindAll("[data-testid='devbook-update-action']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-update-behind']"));
    }

    // --- Fixture -------------------------------------------------------------

    private static GitHubChangedFile Changed(string path) => new(path, null, GitHubFileStatus.Modified, "blob");

    private static AngleSharp.Dom.IElement SourceInput(IRenderedComponent<DevbookPane> component) =>
        component.Find("[data-testid='devbook-source-select'] input");

    private static void OpenPicker(IRenderedComponent<DevbookPane> component) => Raise(component, () => SourceInput(component).Click());

    /// <summary>Finds and raises an event on the renderer's dispatcher, so a
    /// background landing of the overview cannot re-render between the find and
    /// the raise and leave the handler id stale.</summary>
    private static void Raise(IRenderedComponent<DevbookPane> component, Action raise) =>
        component.InvokeAsync(raise).GetAwaiter().GetResult();

    private static List<string> OptionNames(IRenderedComponent<DevbookPane> component) =>
        [.. component.FindAll("[data-testid='devbook-source-select-list'] [role=option] .devbook-stack__source-name").Select(name => name.TextContent.Trim())];

    private static AngleSharp.Dom.IElement Option(IRenderedComponent<DevbookPane> component, string name) =>
        component.FindAll("[data-testid='devbook-source-select-list'] [role=option]")
            .Single(option => option.QuerySelector(".devbook-stack__source-name")?.TextContent.Trim() == name);

    private static Harness CreateHarness(List<string> branches, string? failure = null, string? source = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-devbook-pane-branch-overview-tests", Guid.NewGuid().ToString("n"));
        var clone = Path.Combine(root, "clone");
        Write(clone, ".github/instructions/already-here.instructions.md", "---\napplyTo: \"**\"\n---\n\n# Already here\n");
        Write(clone, ".arc42/01-introduction-and-goals.md", "# Introduction\n");

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = featureSettings.SetEnabled(DevbookFeatures.RepositoryDevbook, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var configuredRepository = Assert.Single(repositories) with
        {
            CloneDirectory = clone,
            DevbookFolders = DevbookFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([configuredRepository]));
        if (source is not null) Assert.Null(gitHubSettings.SetDevbookSource(configuredRepository.Alias, source, useLocalFolder: false));

        var git = new ScriptedGitRepositoryService();
        var catalog = new StubBranchCatalog { Failure = failure, Heads = { [string.Empty] = new GitHubBranchHead("main", "sha-main") } };
        catalog.Branches.AddRange(branches);
        var comparisons = new ScriptedBranchComparisons();

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHubSettings, store));
        context.Services.AddSingleton<ILocalGitRepositoryService>(git);
        // A snapshot cache, so a branch source has a version to check: the old
        // pane drew the control for it, and its absence is the change under test.
        context.Services.AddSingleton<IDevbookSnapshotCache>(new IdleSnapshotCache());
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<DevbookScope>();
        context.Services.AddScoped<DevbookOpenChapter>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(catalog);
        context.Services.AddSingleton<IGitHubBranchComparison>(comparisons);
        context.Services.AddSingleton<DevbookBranchComparisons>();
        context.Services.AddSingleton<DevbookBranchOverview>();
        context.Services.AddSingleton<DevbookUpdateService>();
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();
        context.Services.AddSingleton<DevbookChapterWriter>();

        return new Harness(root, context, configuredRepository.Alias, git, catalog, comparisons);
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed record Harness(
        string Root,
        BunitContext Context,
        string RepositoryAlias,
        ScriptedGitRepositoryService Git,
        StubBranchCatalog Catalog,
        ScriptedBranchComparisons Comparisons) : IAsyncDisposable
    {
        public DevbookBranchOverview Overview => Context.Services.GetRequiredService<DevbookBranchOverview>();

        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>A snapshot cache holding a snapshot and never moving it.</summary>
    private sealed class IdleSnapshotCache : IDevbookSnapshotCache
    {
        private static readonly DevbookSnapshot Snapshot = new("docs/adr", "sha-docs/adr", DateTimeOffset.UtcNow);

        public string SnapshotPath(GitHubRepositoryRef repository, string? branch) => Path.Combine(Path.GetTempPath(), "no-snapshot");

        public DevbookSnapshot? TryRead(GitHubRepositoryRef repository, string? branch) => Snapshot;

        public DevbookSnapshotIndex? TryReadIndex(GitHubRepositoryRef repository, string? branch) => null;

        public Task<DevbookSnapshotResult> FetchAsync(GitHubRepositoryRef repository, string? branch, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DevbookSnapshotResult(Snapshot, false, false, null));

        public Task<DevbookSnapshotResult> EnsureAsync(GitHubRepositoryRef repository, string? branch, IReadOnlyCollection<string> selection, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DevbookSnapshotResult(Snapshot, false, false, null));

        public Task<DevbookSnapshotResult> CheckAsync(GitHubRepositoryRef repository, string? branch, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DevbookSnapshotResult(Snapshot, false, false, null));
    }
}
