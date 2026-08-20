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
public sealed class KnowledgePaneChapterNavigationTests : IDisposable
{
    /// <summary>Two references in the prose: one to another chapter of this
    /// section, one to a chapter of another. Both are written the way the
    /// convention writes them, in a code span carrying the repository path.</summary>
    private const string ContextMap = """
        # Context Map

        ```meta
        status: draft
        ```

        Work logging belongs to `.domain/backlog/domain.md#domain-event-aiworklogged`, and the
        system in its surroundings is drawn in `.arc42/03-context-and-scope.md`.
        """;

    private readonly List<string> _roots = [];

    [Fact]
    public async Task Following_a_reference_to_another_domain_chapter_opens_it()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal(".domain/context-map.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));

        Reference(component, ".domain/backlog/domain.md#domain-event-aiworklogged").Click();

        // The chapter the reference names, shown as the file it is — and the menu
        // beside it marking where the reader now is, because arriving from a
        // reference and arriving from the menu have to leave the same pane.
        component.WaitForAssertion(() => Assert.Equal(".domain/backlog/domain.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));
        Assert.Equal("Domain", component.Find(".knowledge-menu__item--active").TextContent.Trim());
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
        Assert.Contains("Context And Scope", component.Find(".knowledge-menu__item--active").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reference_to_a_chapter_the_menu_does_not_have_changes_nothing()
    {
        await using var harness = CreateHarness(contextMap: ContextMap + "\n\nAnd `.domain/backlog/renamed.md` no longer exists.\n");

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal(".domain/context-map.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));

        Reference(component, ".domain/backlog/renamed.md").Click();

        // A reference is prose, and prose goes stale. Selecting a path with nothing
        // behind it would empty the panel, which reads as the pane having broken
        // rather than as the reference having.
        component.WaitForAssertion(() => Assert.Equal(".domain/context-map.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent));
    }

    /// <summary>The reference as the reader sees it: a control in the prose, found
    /// by the path it carries rather than by its position, because the same chapter
    /// holds several.</summary>
    private static IElement Reference(IRenderedComponent<KnowledgePane> component, string raw) =>
        component.FindAll("button.knowledge-ref--action")
            .Single(button => string.Equals(button.GetAttribute("title"), raw, StringComparison.Ordinal));

    private Harness CreateHarness(string? contextMap = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-knowledge-pane-navigation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "backlog"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), contextMap ?? ContextMap);
        File.WriteAllText(Path.Combine(root, ".domain", "backlog", "domain.md"), "# Domain\n\n```meta\nstatus: draft\n```\n\nWhat the backlog context is.\n");
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
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();

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
        public IRenderedComponent<KnowledgePane> Render() =>
            Context.Render<KnowledgePane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        /// <summary>Awaited disposal, because the pane renders the editing surface
        /// and that writes its last pending save on the way out — see the same note
        /// on the panel's own harness.</summary>
        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
