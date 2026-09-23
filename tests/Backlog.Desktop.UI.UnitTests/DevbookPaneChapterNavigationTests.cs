using AngleSharp.Dom;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Following a reference in a chapter, from the pane that owns the selection.
/// <para>
/// The panel renders the reference and the pane decides what is shown, so neither
/// half proves the feature on its own: a panel test can only say the request was
/// made, and a pane test with no panel in it has nobody to make one. These render
/// the pane against a folder on disk and press the reference the way a reader
/// does, which is also the shape of the report — the reference looked right and
/// the chapter did not move.
/// </para>
/// </summary>
public sealed class DevbookPaneChapterNavigationTests : IDisposable
{
    /// <summary>Two references in the prose: one to another chapter of this
    /// section, one to a chapter of another. Both are written the way the
    /// convention writes them, in a code span carrying the repository path.</summary>
    private const string ContextMap = """
        # Context Map

        ```meta
        status: draft
        ```

        Work logging belongs to `.domain/tasks/domain.md#domain-event-aiworklogged`, and the
        system in its surroundings is drawn in `.arc42/03-context-and-scope.md`.
        """;

    private readonly List<string> _roots = [];

    [Fact]
    public async Task Following_a_reference_to_another_domain_chapter_opens_it()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal(".domain/context-map.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));

        Reference(component, ".domain/tasks/domain.md#domain-event-aiworklogged").Click();

        // The chapter the reference names, shown as the file it is — and the menu
        // beside it marking where the reader now is, because arriving from a
        // reference and arriving from the menu have to leave the same pane.
        component.WaitForAssertion(() => Assert.Equal(".domain/tasks/domain.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));
        Assert.Equal("Domain", component.Find(".devbook-menu__item--active").TextContent.Trim());
    }

    [Fact]
    public async Task Following_a_reference_into_another_section_switches_to_it()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file']")));
        Assert.Equal("true", component.Find("#tab-domain").GetAttribute("aria-selected"));

        Reference(component, ".arc42/03-context-and-scope.md").Click();

        // The section strip follows the reference too. Selecting a chapter of the
        // architecture folder while the Domain tab was still the open one would
        // leave the pane pointing at a chapter it is not showing.
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("#tab-arc42").GetAttribute("aria-selected")));
        Assert.Contains("Context And Scope", component.Find(".devbook-menu__item--active").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reference_to_a_chapter_the_menu_does_not_have_changes_nothing()
    {
        await using var harness = CreateHarness(contextMap: ContextMap + "\n\nAnd `.domain/tasks/renamed.md` no longer exists.\n");

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal(".domain/context-map.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));

        Reference(component, ".domain/tasks/renamed.md").Click();

        // A reference is prose, and prose goes stale. Selecting a path with nothing
        // behind it would empty the panel, which reads as the pane having broken
        // rather than as the reference having.
        component.WaitForAssertion(() => Assert.Equal(".domain/context-map.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));
    }

    /// <summary>A chapter linking the way the knowledge folders actually link: the
    /// author writes the path their own editor resolves, which is relative to the
    /// file they are in and never rooted at the repository.</summary>
    private const string RelativeContextMap = """
        # Context Map

        ```meta
        status: draft
        ```

        Work is logged against a [task](tasks/domain.md#domain-event-aiworklogged),
        the system in its surroundings is drawn in [context and scope](../.arc42/03-context-and-scope.md),
        and the shape of it is [in the picture](assets/context.png).
        """;

    [Fact]
    public async Task Following_a_relative_reference_opens_the_chapter_it_resolves_to()
    {
        await using var harness = CreateHarness(contextMap: RelativeContextMap);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal(".domain/context-map.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));

        Reference(component, ".domain/tasks/domain.md#domain-event-aiworklogged").Click();

        // The same landing as the rooted form: the chapter as a file, and the menu
        // marking where the reader now is. It used to be an anchor with
        // target="_blank" on `tasks/domain.md`, which is the reader's browser
        // asking the app's own origin for a repository path.
        component.WaitForAssertion(() => Assert.Equal(".domain/tasks/domain.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));
        Assert.Equal("Domain", component.Find(".devbook-menu__item--active").TextContent.Trim());
    }

    [Fact]
    public async Task Following_a_relative_reference_across_folders_switches_section()
    {
        await using var harness = CreateHarness(contextMap: RelativeContextMap);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file']")));

        Reference(component, ".arc42/03-context-and-scope.md").Click();

        component.WaitForAssertion(() => Assert.Equal("true", component.Find("#tab-arc42").GetAttribute("aria-selected")));
        Assert.Contains("Context And Scope", component.Find(".devbook-menu__item--active").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_relative_link_that_is_not_a_chapter_is_not_a_way_out_to_the_browser()
    {
        await using var harness = CreateHarness(contextMap: RelativeContextMap);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file']")));

        // An image beside the chapter is not a chapter, and nothing in this pane
        // can open one. The author's words stay because they wrote them; the
        // destination goes, because there was never one to offer.
        var body = component.Find("[data-testid='domain-chapter-file'] .file-view__body");

        Assert.Contains("in the picture", body.QuerySelector("span.md-link--inert")!.TextContent, StringComparison.Ordinal);
        Assert.Empty(body.QuerySelectorAll("a.md-link"));
    }

    [Fact]
    public async Task A_decision_record_under_the_adr_folder_is_a_chapter_the_pane_opens()
    {
        // The shape of the report: the row went active and the prose did not
        // change. arc42 keeps its decision records a folder down, and a selection
        // the panel's catalog cannot match leaves the chapter already on screen —
        // so the reader sees the chapter they came from under the name of the one
        // they asked for. Nothing has indexed this folder, which is what made the
        // catalog and the menu disagree about what it holds.
        await using var harness = CreateHarness(withDecisionRecords: true);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file']")));

        Reference(component, ".arc42/03-context-and-scope.md").Click();
        WaitForChapter(component, ".arc42/03-context-and-scope.md", "The system in its surroundings");

        Reference(component, ".arc42/adr/0004-index.md").Click();

        WaitForChapter(component, ".arc42/adr/0004-index.md", "The index is generated");
    }

    [Fact]
    public async Task A_sibling_reference_inside_a_decision_record_lands_beside_it()
    {
        // A relative link resolves against the chapter holding it, so the base the
        // panel hands the resolver has to be the chapter's own path and no other.
        // Written as `0005-replica.md` from inside `adr/`, the only chapter it can
        // mean is the one next to it — and a base with the area folder counted
        // twice would send it a level deeper than anything on disk, which is a
        // reference that reads correctly and goes nowhere.
        await using var harness = CreateHarness(withDecisionRecords: true);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file']")));

        Reference(component, ".arc42/03-context-and-scope.md").Click();
        WaitForChapter(component, ".arc42/03-context-and-scope.md", "The system in its surroundings");

        Reference(component, ".arc42/adr/0004-index.md").Click();
        WaitForChapter(component, ".arc42/adr/0004-index.md", "The index is generated");

        // The resolved destination is on the control itself, so a base that named
        // the folder twice fails here — on the reference — rather than later on
        // what the pane did with it.
        Reference(component, ".arc42/adr/0005-replica.md").Click();

        WaitForChapter(component, ".arc42/adr/0005-replica.md", "A replica of the store.");
    }

    /// <summary>The chapter the arc42 panel is showing, waited for as one thing.
    /// The file view's path is set from the selection and its body is read off disk
    /// after it, so a check on the path alone passes while the prose underneath is
    /// still the chapter the reader came from — which is the very confusion these
    /// tests are about.</summary>
    private static void WaitForChapter(IRenderedComponent<DevbookPane> component, string path, string prose) =>
        component.WaitForAssertion(() =>
        {
            Assert.Equal(path, component.Find("[data-testid='arc42-chapter-file'] .file-view__path").TextContent);
            Assert.Contains(prose, component.Find("[data-testid='arc42-chapter-file'] .file-view__body").TextContent, StringComparison.Ordinal);
        });

    /// <summary>The reference as the reader sees it: a control in the prose, found
    /// by the path it carries rather than by its position, because the same chapter
    /// holds several.</summary>
    private static IElement Reference(IRenderedComponent<DevbookPane> component, string raw) =>
        component.FindAll("button.devbook-ref--action")
            .Single(button => string.Equals(button.GetAttribute("title"), raw, StringComparison.Ordinal));

    private Harness CreateHarness(string? contextMap = null, bool withDecisionRecords = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-devbook-pane-navigation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), contextMap ?? ContextMap);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), "# Domain\n\n```meta\nstatus: draft\n```\n\nWhat the tasks context is.\n");
        File.WriteAllText(Path.Combine(root, ".arc42", "03-context-and-scope.md"), $"# Context and scope\n\nThe system in its surroundings{(withDecisionRecords ? ", decided in `.arc42/adr/0004-index.md`" : string.Empty)}.\n");

        if (withDecisionRecords)
        {
            // Nothing indexes this folder, which is the state of a fresh clone and
            // of every machine that has not built the database yet. The decision
            // records still have to be reachable, and a sibling link inside one
            // still has to land beside it.
            Directory.CreateDirectory(Path.Combine(root, ".arc42", "adr"));
            File.WriteAllText(Path.Combine(root, ".arc42", "adr", "0004-index.md"), "# ADR 0004: The index\n\nThe index is generated, as [ADR 0005](0005-replica.md) assumes.\n");
            File.WriteAllText(Path.Combine(root, ".arc42", "adr", "0005-replica.md"), "# ADR 0005: The replica\n\nA replica of the store.\n");
        }

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(DevbookFeatures.DevbookSections, true);
        _ = features.SetEnabled(DevbookFeatures.RepositoryDevbook, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        // Two sections and no more, so the pane opens on Domain and the test is not
        // also a test of the folders this machine happens to have.
        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            DevbookFolders = [.. DevbookFolderSetting.Defaults().Select(folder => folder with { Enabled = folder.Key is ".domain" or ".arc42" })]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHub, settings));
        context.Services.AddSingleton(sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));
        context.Services.AddSingleton<Arc42DevbookStore>();
        context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<DevbookChapterWriter>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<DevbookScope>();
        // The pane publishes its open chapter here for the Ask AI source; a pane
        // rendered without it would fail on inject, as the application hosts would.
        context.Services.AddScoped<DevbookOpenChapter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<DevbookUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();

        // Nothing here has ever been committed, which is the honest answer for a
        // temp folder and keeps the compare control out of the way.
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
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        /// <summary>Awaited disposal, because the pane renders the editing surface
        /// and that writes its last pending save on the way out — see the same note
        /// on the panel's own harness.</summary>
        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
