using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The bounded-context overview, reached from the pane.
///
/// <para>The panel draws a row of context tabs when no chapter is open, and until
/// now the pane could not produce that state: it assigns a chapter the moment the
/// menu loads and every route through it only ever assigns another one. So the row
/// existed and no reader could reach it — the standalone <c>/knowledge/domain</c>
/// page was the only way in.</para>
///
/// <para>That mattered beyond the markup. The context list, its order and the
/// status beside each name are what <c>DomainKnowledgeStore</c> builds from the
/// generated knowledge database, and the per-file drift check in local ADR 0004 is
/// what keeps them honest between refreshes. None of it was on screen in the pane,
/// so none of it could be validated by running the app.</para>
///
/// <para>The route is the context folder in the menu. It is already a row, it
/// already carries the context's path, and clicking it did nothing but open the
/// twisty — selecting the folder is what a tree row is for.</para>
/// </summary>
public sealed class KnowledgePaneContextOverviewTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task Selecting_a_context_folder_in_the_menu_opens_the_context_overview()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal(".domain/context-map.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));

        // No tabs while a chapter is open, which is the panel's own rule and stays
        // one: the overview is what the pane shows *instead* of a chapter.
        Assert.Empty(component.FindAll("nav[aria-label='Bounded contexts']"));

        MenuItem(component, "Tasks").Click();

        component.WaitForAssertion(() => Assert.Single(component.FindAll("nav[aria-label='Bounded contexts']")));

        // The folder the reader pressed is the context the overview opens on, and
        // the tab row says so — not the first context the panel happened to load.
        Assert.Equal("Tasks", component.Find("button.domain-context-tab[aria-pressed='true']").TextContent.Trim());
        Assert.Single(component.FindAll("[data-testid='domain-context']"));
        Assert.Empty(component.FindAll("[data-testid='domain-chapter-file']"));
    }

    [Fact]
    public async Task Every_bounded_context_is_a_tab_and_the_context_map_is_the_first_of_them()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file']")));

        MenuItem(component, "Tasks").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("nav[aria-label='Bounded contexts']")));

        // The whole list, in the order the store built it, with the map ahead of
        // the contexts. This row is the only screen in the product that shows the
        // context list the knowledge database answers with.
        Assert.Equal(
            ["Context map", "Sessions", "Tasks"],
            component.FindAll("button.domain-context-tab").Select(tab => tab.TextContent.Trim()));

        // And back to the map from the row itself, without going through the menu.
        component.Find("button.domain-context-tab").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-document']")));
        Assert.Single(component.FindAll("nav[aria-label='Bounded contexts']"));
    }

    [Fact]
    public async Task The_overview_survives_leaving_the_section_and_coming_back()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file']")));

        MenuItem(component, "Tasks").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("nav[aria-label='Bounded contexts']")));

        component.Find("#tab-arc42").Click();
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("#tab-arc42").GetAttribute("aria-selected")));

        component.Find("#tab-domain").Click();

        // Switching section runs the pane's "make sure something is selected" pass,
        // and a folder is something: without that, coming back would silently
        // replace the reader's overview with the context map chapter.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("nav[aria-label='Bounded contexts']")));
        Assert.Equal("Tasks", component.Find("button.domain-context-tab[aria-pressed='true']").TextContent.Trim());
    }

    [Fact]
    public async Task Opening_a_chapter_from_the_menu_leaves_the_overview()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file']")));

        MenuItem(component, "Tasks").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("nav[aria-label='Bounded contexts']")));

        MenuItem(component, "Domain").Click();

        // The way out is the way in reversed. A chapter row selects a chapter, so
        // the overview is not a mode the reader can get stuck in.
        component.WaitForAssertion(() => Assert.Equal(".domain/tasks/domain.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));
        Assert.Empty(component.FindAll("nav[aria-label='Bounded contexts']"));
    }

    /// <summary>One menu row, by the label a reader reads. Matched on the row's own
    /// label span rather than on its text, because a folder row also carries a
    /// twisty and a domain row carries a type mark.</summary>
    private static IElement MenuItem(IRenderedComponent<KnowledgePane> component, string label) =>
        component.FindAll(".knowledge-menu__item")
            .Single(item => string.Equals(item.QuerySelector(".knowledge-menu__label")?.TextContent.Trim(), label, StringComparison.Ordinal));

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-knowledge-pane-context-overview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "sessions"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), "# Domain: Tasks\n\n```meta\nstatus: draft\n```\n\nWhat the tasks context is.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "sessions", "domain.md"), "# Domain: Sessions\n\n```meta\nstatus: proposed\n```\n\nWhat the sessions context is.\n");
        File.WriteAllText(Path.Combine(root, ".arc42", "03-context-and-scope.md"), "# Context and scope\n\nThe system in its surroundings.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(KnowledgeFeatures.KnowledgeSections, true);
        _ = features.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        // Two sections and no more, so the pane opens on Domain and the test is not
        // also a test of the folders this machine happens to have.
        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            KnowledgeFolders = [.. KnowledgeFolderSetting.Defaults().Select(folder => folder with { Enabled = folder.Key is ".domain" or ".arc42" })]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHub, settings));
        context.Services.AddSingleton(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
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
