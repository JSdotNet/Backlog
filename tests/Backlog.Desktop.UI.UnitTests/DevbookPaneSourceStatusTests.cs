using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Switching the source has to be visible even when it changes nothing on
/// screen. A clone and a branch at the same commit hold the same files, so the
/// panels re-read and draw the same chapters; the one trace of the switch was
/// the status control giving way to a badge, and a reader who did not know to
/// look for that saw a select that did nothing. The sentence under the strip is
/// the confirmation, and these assert it on the rendered markup — including that
/// it makes way for the version answer rather than stacking under it.
/// </summary>
public sealed class DevbookPaneSourceStatusTests
{
    [Fact]
    public async Task Nothing_is_said_about_the_source_until_somebody_switches_it()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-status']"));
    }

    [Fact]
    public async Task Switching_to_a_branch_says_the_branch_is_being_read_as_a_snapshot()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        CommitSource(component, DevbookSourceSelection.DefaultBranchLabel);

        component.WaitForAssertion(() =>
        {
            var status = component.Find("[data-testid='devbook-source-status']");
            Assert.Equal("status", status.GetAttribute("role"));
            Assert.StartsWith("Reading the default branch as a read-only snapshot.", status.TextContent.Trim(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Switching_back_to_the_clone_says_the_clone_is_being_read()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        CommitSource(component, DevbookSourceSelection.DefaultBranchLabel);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-source-status']")));

        CommitSource(component, DevbookSourceSelection.LocalFolderLabel);

        component.WaitForAssertion(() => Assert.Equal(
            "Reading the local clone. Devbook can be edited here.",
            component.Find("[data-testid='devbook-source-status']").TextContent.Trim()));
    }

    /// <summary>The row is one row. A check answers the question the switch
    /// sentence was standing in for, so the answer replaces it.</summary>
    [Fact]
    public async Task A_version_check_takes_the_row_back_from_the_switch_sentence()
    {
        await using var harness = CreateHarness();
        harness.Git.NextCheck = new LocalGitRepositoryUpdateCheck(
            LocalGitRepositoryCurrency.UpToDate, 0, 0, false, "origin/main", "On the latest version of origin/main.");

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        // Away and back: the clone is already being read, and committing the
        // source already read is no switch and says nothing.
        CommitSource(component, DevbookSourceSelection.DefaultBranchLabel);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-source-status']")));
        CommitSource(component, DevbookSourceSelection.LocalFolderLabel);
        component.WaitForAssertion(() => Assert.Equal(
            "Reading the local clone. Devbook can be edited here.",
            component.Find("[data-testid='devbook-source-status']").TextContent.Trim()));

        component.Find("[data-testid='devbook-update-action']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(
                "On the latest version of origin/main.",
                component.Find("[data-testid='devbook-update-status']").TextContent.Trim());
            Assert.Empty(component.FindAll("[data-testid='devbook-source-status']"));
        });
    }

    /// <summary>The sentence names a repository, so it leaves with it.</summary>
    [Fact]
    public async Task Moving_the_scope_drops_the_switch_sentence()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        CommitSource(component, DevbookSourceSelection.DefaultBranchLabel);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-source-status']")));

        component.Render(parameters => parameters.Add(pane => pane.RepositoryAlias, null));

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='devbook-source-status']")));
    }

    // --- Searching and typing a branch ----------------------------------------

    /// <summary>A name the repository does not have is refused where it was
    /// typed: the reason beside the field, the field back on the source being
    /// read, and the source itself untouched.</summary>
    [Fact]
    public async Task Typing_an_unknown_branch_says_so_and_leaves_the_source_alone()
    {
        await using var harness = CreateHarness(branches: ["main", "claude/search-picker"]);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        CommitSource(component, "no-such-branch");

        component.WaitForAssertion(() =>
        {
            Assert.Equal(
                "No branch named \"no-such-branch\" in JSdotNet/Backlog.",
                component.Find("[data-testid='devbook-source-error']").TextContent.Trim());
            Assert.Equal(DevbookSourceSelection.LocalFolderLabel, SourceInput(component).GetAttribute("value"));
            Assert.Empty(component.FindAll("[data-testid='devbook-source-status']"));
        });

        Assert.Equal(DevbookSourceKind.LocalFolder, harness.Settings.Current.Find(harness.RepositoryAlias)!.DevbookSource);
    }

    /// <summary>Refused while a branch is being read, the field goes back to that
    /// branch's name, and the reason sits under the strip rather than on it — a
    /// sentence on the strip squeezed the field to an empty sliver and the tabs into
    /// one column.</summary>
    [Fact]
    public async Task A_refusal_on_a_branch_puts_the_branch_back_and_says_why_under_the_strip()
    {
        await using var harness = CreateHarness(branches: ["main", "release/2.0"]);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        CommitSource(component, "release/2.0");
        component.WaitForAssertion(() => Assert.Equal("release/2.0", SourceInput(component).GetAttribute("value")));

        CommitSource(component, "no-such-branch");

        component.WaitForAssertion(() =>
        {
            Assert.Equal("release/2.0", SourceInput(component).GetAttribute("value"));
            Assert.Equal(
                "No branch named \"no-such-branch\" in JSdotNet/Backlog.",
                component.Find("[data-testid='devbook-source-error']").TextContent.Trim());
        });

        Assert.Empty(component.FindAll(".devbook-stack__strip [data-testid='devbook-source-error']"));
        Assert.Equal("release/2.0", harness.Settings.Current.Find(harness.RepositoryAlias)!.DevbookBranch);
    }

    /// <summary>Nobody has to load the list before naming a branch: the name is
    /// checked against the list the pane fetches for it.</summary>
    [Fact]
    public async Task Typing_a_branch_before_the_list_is_loaded_fetches_it_and_switches()
    {
        await using var harness = CreateHarness(branches: ["main", "release/2.0"]);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        CommitSource(component, "RELEASE/2.0");

        component.WaitForAssertion(() =>
        {
            Assert.StartsWith(
                "Reading release/2.0 as a read-only snapshot.",
                component.Find("[data-testid='devbook-source-status']").TextContent.Trim(),
                StringComparison.Ordinal);
            Assert.Equal("release/2.0", SourceInput(component).GetAttribute("value"));
        });

        Assert.Equal("release/2.0", harness.Settings.Current.Find(harness.RepositoryAlias)!.DevbookBranch);
    }

    /// <summary>The list stops at a ceiling and was fetched a while ago, so a
    /// typed name it lacks is asked of GitHub before it is refused — and, being
    /// there, is switched to and joins the list.</summary>
    [Fact]
    public async Task Typing_a_branch_the_list_lacks_asks_github_and_switches()
    {
        const string Branch = "inbox-redesign/inbox-dialogs-over-panes";
        await using var harness = CreateHarness(
            branches: ["main"],
            heads: new(StringComparer.Ordinal) { [Branch] = new GitHubBranchHead(Branch, "abc123") });

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        CommitSource(component, Branch);

        component.WaitForAssertion(() =>
        {
            Assert.StartsWith(
                $"Reading {Branch} as a read-only snapshot.",
                component.Find("[data-testid='devbook-source-status']").TextContent.Trim(),
                StringComparison.Ordinal);
            Assert.Equal(Branch, SourceInput(component).GetAttribute("value"));
            Assert.Empty(component.FindAll("[data-testid='devbook-source-error']"));
        });

        Assert.Equal(Branch, harness.Settings.Current.Find(harness.RepositoryAlias)!.DevbookBranch);
        Assert.Contains(Branch, harness.Context.Services.GetRequiredService<DevbookSourceSelection>()
            .Suggestions(harness.RepositoryAlias, "dialogs").Select(option => option.Value));
    }

    [Fact]
    public async Task Typing_narrows_the_list_to_the_branches_that_match()
    {
        await using var harness = CreateHarness(branches: ["main", "claude/search-picker", "codex/fix-search", "release/2.0"]);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        CommitSource(component, DevbookSourceSelection.LoadBranchesLabel);
        component.WaitForAssertion(() =>
            Assert.Equal(DevbookSourceSelection.LocalFolderLabel, SourceInput(component).GetAttribute("value")));

        SourceInput(component).Input("search");
        SourceInput(component).Click();

        component.WaitForAssertion(() => Assert.Equal(
            ["claude/search-picker", "codex/fix-search"],
            component.FindAll("[data-testid='devbook-source-select-list'] [role=option]").Select(option => option.TextContent.Trim())));
    }

    /// <summary>Enter on the field as it stands commits the source already being
    /// read. That is no switch, so nothing announces one.</summary>
    [Fact]
    public async Task Committing_the_source_already_read_says_nothing()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        CommitSource(component, DevbookSourceSelection.LocalFolderLabel);

        component.WaitForAssertion(() => Assert.Equal(DevbookSourceSelection.LocalFolderLabel, SourceInput(component).GetAttribute("value")));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-status']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-error']"));
    }

    /// <summary>Leaving the field after typing part of a name abandons the search:
    /// nothing is fetched, nothing switches, and nothing is said.</summary>
    [Fact]
    public async Task Leaving_the_field_mid_search_fetches_nothing_and_switches_nothing()
    {
        await using var harness = CreateHarness(branches: ["main", "copilot/fix"]);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        SourceInput(component).Input("copil");
        SourceInput(component).Change("copil");

        component.WaitForAssertion(() =>
            Assert.Equal(DevbookSourceSelection.LocalFolderLabel, SourceInput(component).GetAttribute("value")));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-error']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-status']"));
        Assert.Equal(0, harness.Catalog.Listings);
        Assert.Equal(DevbookSourceKind.LocalFolder, harness.Settings.Current.Find(harness.RepositoryAlias)!.DevbookSource);
    }

    /// <summary>Leaving the field on text that already names a source is a
    /// finished choice, and is taken.</summary>
    [Fact]
    public async Task Leaving_the_field_on_a_named_source_switches_to_it()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        SourceInput(component).Change(DevbookSourceSelection.DefaultBranchLabel);

        component.WaitForAssertion(() => Assert.StartsWith(
            "Reading the default branch as a read-only snapshot.",
            component.Find("[data-testid='devbook-source-status']").TextContent.Trim(),
            StringComparison.Ordinal));
        Assert.Equal(0, harness.Catalog.Listings);
    }

    /// <summary>Escape on a closed list empties the field; leaving it then must
    /// not read as choosing the default branch.</summary>
    [Fact]
    public async Task Leaving_an_emptied_field_switches_nothing()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        SourceInput(component).KeyDown(new KeyboardEventArgs { Key = "Escape" });
        component.WaitForAssertion(() => Assert.Equal(string.Empty, SourceInput(component).GetAttribute("value") ?? string.Empty));
        SourceInput(component).Change(string.Empty);

        component.WaitForAssertion(() =>
            Assert.Equal(DevbookSourceSelection.LocalFolderLabel, SourceInput(component).GetAttribute("value")));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-error']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-status']"));
        Assert.Equal(DevbookSourceKind.LocalFolder, harness.Settings.Current.Find(harness.RepositoryAlias)!.DevbookSource);
    }

    [Fact]
    public async Task Entering_an_emptied_field_switches_nothing_and_fetches_nothing()
    {
        await using var harness = CreateHarness(branches: ["main"]);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        CommitSource(component, "   ");

        component.WaitForAssertion(() =>
            Assert.Equal(DevbookSourceSelection.LocalFolderLabel, SourceInput(component).GetAttribute("value")));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-error']"));
        Assert.Empty(component.FindAll("[data-testid='devbook-source-status']"));
        Assert.Equal(0, harness.Catalog.Listings);
    }

    /// <summary>The field's own text is no filter, so a click in it offers every
    /// source rather than only the one already being read.</summary>
    [Fact]
    public async Task A_click_in_the_untouched_field_lists_every_source()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item")));

        SourceInput(component).Click();

        component.WaitForAssertion(() => Assert.Equal(
            [DevbookSourceSelection.DefaultBranchLabel, DevbookSourceSelection.LocalFolderLabel, DevbookSourceSelection.LoadBranchesLabel],
            component.FindAll("[data-testid='devbook-source-select-list'] [role=option]").Select(option => option.TextContent.Trim())));
    }

    private static AngleSharp.Dom.IElement SourceInput(IRenderedComponent<DevbookPane> component) =>
        component.Find("[data-testid='devbook-source-select'] input");

    /// <summary>Types the text into the source field and presses Enter, which
    /// commits what was typed while no option is highlighted.</summary>
    private static void CommitSource(IRenderedComponent<DevbookPane> component, string text)
    {
        SourceInput(component).Input(text);
        SourceInput(component).KeyDown(new KeyboardEventArgs { Key = "Enter" });
    }

    private static Harness CreateHarness(List<string>? branches = null, Dictionary<string, GitHubBranchHead>? heads = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-devbook-pane-source-status-tests", Guid.NewGuid().ToString("n"));
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

        var git = new ScriptedGitRepositoryService();

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHubSettings, store));
        context.Services.AddSingleton<ILocalGitRepositoryService>(git);
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<DevbookScope>();
        // The pane publishes its open chapter here for the Ask AI source; a pane
        // rendered without it would fail on inject, as the application hosts would.
        context.Services.AddScoped<DevbookOpenChapter>();
        context.Services.AddSingleton<DevbookUpdateService>();
        var catalog = new StubBranchCatalog { Branches = branches ?? [], Heads = heads ?? new(StringComparer.Ordinal) };
        context.Services.AddSingleton<IGitHubBranchCatalog>(catalog);
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();
        context.Services.AddSingleton<DevbookChapterWriter>();

        return new Harness(root, context, configuredRepository.Alias, git, gitHubSettings, catalog);
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
        GitHubSettingsStore Settings,
        StubBranchCatalog Catalog) : IAsyncDisposable
    {
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
}
