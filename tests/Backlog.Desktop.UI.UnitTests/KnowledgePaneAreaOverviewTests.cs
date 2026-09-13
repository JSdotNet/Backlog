using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The whole-folder reading of <c>.design</c>, reached from the pane — and the
/// absence of the same thing in the two areas that never had one worth reaching.
///
/// <para>Three panels guarded an overview on "no path was handed in", and the pane
/// hands one in from the first render: <c>EnsureSelectedChapter</c> assigns a
/// chapter as soon as the menu loads and every route through the pane only ever
/// assigns another. Since the pane is the only host any of the three has, the
/// guard could not be satisfied anywhere in the product.</para>
///
/// <para>The three answers differ because the three branches did. arc42 and
/// Instructions drew a list of chapters beside the chapter — the standalone-page
/// shape, and the pane's menu is already that list, so a second one is a
/// duplicate and those branches are gone. <c>.design</c> drew something else: every
/// file at once, each subject's status beside its own heading, which is what the
/// folder is worth opening as a folder and which nothing else in the pane shows.
/// So that one gets a way in.</para>
///
/// <para>The way in is a row, for the reason the bounded-context folders are rows:
/// the path it selects is a real node of the tree, and that is what keeps
/// <c>EnsureSelectedChapter</c> from quietly replacing it on the next menu
/// load.</para>
/// </summary>
public sealed class KnowledgePaneAreaOverviewTests : IDisposable
{
    private const string Colors = """
        # Colors

        ```meta
        status: draft
        ```

        The palette and how it is applied.

        ## Palette

        ```meta
        status: agreed
        ```

        The tokens themselves.
        """;

    private const string Interaction = """
        # Interaction guidelines

        ```meta
        status: draft
        ```

        How the screens behave.

        ## Auto-save

        ```meta
        status: draft
        ```

        When a change is written.
        """;

    private readonly List<string> _roots = [];

    [Fact]
    public async Task Selecting_the_design_overview_row_opens_the_whole_folder()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        OpenDesign(component);

        // The section opens on a chapter, which is the pane's own rule and stays
        // one: the overview is what it shows *instead* of a chapter, never beside.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file']")));
        Assert.Empty(component.FindAll("nav[aria-label='Design knowledge documents']"));

        MenuItem(component, "All documents").Click();

        component.WaitForAssertion(() => Assert.Single(component.FindAll("nav[aria-label='Design knowledge documents']")));

        // Every file, not the first of them, and each subject carrying the status
        // its own fence states. That is the reading the folder is opened for, and
        // it is the one thing in this section the menu cannot give.
        Assert.Equal(2, component.FindAll(".design-document").Count);
        Assert.Equal(2, component.FindAll(".design-knowledge__nav-link").Count);
        Assert.NotEmpty(component.FindAll(".design-section .knowledge-record__headline .badge--status"));
        Assert.Empty(component.FindAll("[data-testid='design-chapter-file']"));
    }

    [Fact]
    public async Task The_design_overview_survives_leaving_the_section_and_coming_back()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        OpenDesign(component);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file']")));

        MenuItem(component, "All documents").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("nav[aria-label='Design knowledge documents']")));

        component.Find("#tab-arc42").Click();
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("#tab-arc42").GetAttribute("aria-selected")));

        component.Find("#tab-design").Click();

        // Switching section runs the pane's "make sure something is selected" pass.
        // The row's path is the folder's own, and the folder is in the tree, so
        // that pass leaves it alone — without which coming back would silently
        // swap the reader's overview for a chapter they did not pick.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("nav[aria-label='Design knowledge documents']")));
        Assert.Empty(component.FindAll("[data-testid='design-chapter-file']"));
    }

    [Fact]
    public async Task Opening_a_design_chapter_from_the_menu_leaves_the_overview()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        OpenDesign(component);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file']")));

        MenuItem(component, "All documents").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("nav[aria-label='Design knowledge documents']")));

        MenuItem(component, "Colors").Click();

        // The way out is the way in reversed, so the overview is not a mode a
        // reader can get stuck in.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file']")));
        Assert.Empty(component.FindAll("nav[aria-label='Design knowledge documents']"));
    }

    [Fact]
    public async Task The_architecture_section_lists_its_chapters_once()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("#tab-arc42").GetAttribute("aria-selected")));
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-document']")));

        // The menu is the list. The panel used to carry a second one beside the
        // chapter — title, status and diagram count per row — and it was the
        // standalone page's left column, reachable from no page this product has.
        // Nothing behind it was unique: the status is in the file view's own
        // header and the diagrams are in the chapter.
        Assert.Empty(component.FindAll("nav[aria-label='arc42 chapters']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-option']"));
        Assert.NotEmpty(component.FindAll(".knowledge-menu__item"));
    }

    /// <summary>Puts the pane on Design. Two sections are enabled so the section
    /// strip is real, and Architecture is the earlier of them, so Design is
    /// arrived at rather than opened on.</summary>
    private static void OpenDesign(IRenderedComponent<KnowledgePane> component)
    {
        component.WaitForAssertion(() => Assert.Single(component.FindAll("#tab-design")));
        component.Find("#tab-design").Click();
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("#tab-design").GetAttribute("aria-selected")));
    }

    /// <summary>One menu row, by the label a reader reads. Matched on the row's own
    /// label span rather than on its text, because a folder row also carries a
    /// twisty.</summary>
    private static IElement MenuItem(IRenderedComponent<KnowledgePane> component, string label) =>
        component.FindAll(".knowledge-menu__item")
            .Single(item => string.Equals(item.QuerySelector(".knowledge-menu__label")?.TextContent.Trim(), label, StringComparison.Ordinal));

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-knowledge-pane-area-overview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".design"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".design", "colors.md"), Colors);
        File.WriteAllText(Path.Combine(root, ".design", "interaction-guidelines.md"), Interaction);
        File.WriteAllText(Path.Combine(root, ".arc42", "03-context-and-scope.md"), "# Context and scope\n\nThe system in its surroundings.\n");
        File.WriteAllText(Path.Combine(root, ".arc42", "04-solution-strategy.md"), "# Solution strategy\n\nThe shape of the answer.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(KnowledgeFeatures.KnowledgeSections, true);
        _ = features.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            KnowledgeFolders = [.. KnowledgeFolderSetting.Defaults().Select(folder => folder with { Enabled = folder.Key is ".design" or ".arc42" })]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHub, settings));
        context.Services.AddSingleton<DesignKnowledgeProvider>();
        context.Services.AddSingleton<Arc42KnowledgeStore>();
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<KnowledgeChapterWriter>();
        context.Services.AddSingleton<KnowledgeMenu>();
        context.Services.AddSingleton<KnowledgeScope>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<KnowledgeUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<KnowledgeSourceSelection>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());

        return new Harness(context, repository.Alias);
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<KnowledgePane> Render() =>
            Context.Render<KnowledgePane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        /// <summary>Awaited disposal, because the pane renders the editing surface
        /// and that writes its last pending save on the way out.</summary>
        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
