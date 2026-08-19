using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The domain panel's selected chapter, now shown as the file it is.
/// <para>
/// This panel's document body was never one rendering: a summary, a metadata row,
/// diagrams, and a section list where each section carries its own state dropdown
/// and its own Copilot launch. Only the prose is replaced by the editing surface —
/// the controls are not content and there is nowhere else in the product to reach
/// them — so they are asserted alongside the surface rather than assumed.
/// </para>
/// </summary>
public sealed class DomainKnowledgePanelTests : IDisposable
{
    private const string ContextMapPath = ".domain/context-map.md";

    private const string ContextMap =
        "# Context Map\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n\n## Boundaries\n\nWhat divides the contexts.\n";

    private readonly List<string> _roots = [];

    [Fact]
    public async Task A_selected_chapter_renders_the_editing_surface()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);

        // The chapter is read off disk asynchronously, so it arrives on a render
        // after the first one.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));
        Assert.Single(component.FindAll("[data-testid='knowledge-chapter-edit']"));
        Assert.Contains("Original prose.", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_selected_chapter_is_shown_through_the_shared_file_view()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));

        // Inside the file view's body rather than merely somewhere on the panel.
        // The header is what keeps the identity on screen while the chapter
        // scrolls, and a body that landed beside the file view instead of in it
        // would take that away while still looking right in a screenshot.
        Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body'] [data-testid='knowledge-chapter-surface']"));

        component.AssertTheFileIsNamedOnce("Context Map", "[data-testid='domain-document']");

        // The kind label went with the title and the path: it is part of what the
        // file is, so it belongs on the header that says so.
        Assert.Contains("Strategic context map", component.Find(".file-view__meta").TextContent, StringComparison.Ordinal);
        Assert.Empty(component.FindAll(".domain-document__path"));
    }

    [Fact]
    public async Task The_chapter_article_gives_up_its_card_to_the_file_view()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));

        // The file view draws a frame, so the article holding it must not: two
        // borders around one chapter is the "document inside a document" this was
        // reported as. Asserted as the modifier rather than as the element's
        // absence, because the article has to stay — it is the scroll container
        // the knowledge stack sizes, the anchor the knowledge menu jumps to, and
        // what holds the chapter's controls and sections together.
        var article = component.Find("[data-testid='domain-document']");
        Assert.Contains("domain-document--chapter", article.GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_document_the_file_view_is_not_showing_keeps_its_card()
    {
        await using var harness = CreateHarness();

        // No selection, so this is the folder overview and every document on it is
        // one entry in a list. There the card is what tells the entries apart, and
        // withdrawing it for the whole class rather than for the shown chapter
        // would have taken it from all of them.
        var component = harness.Render(null);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='domain-document']")));
        Assert.All(
            component.FindAll("[data-testid='domain-document']"),
            article => Assert.DoesNotContain("domain-document--chapter", article.GetAttribute("class") ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Consecutive_relations_are_separate_tokens()
    {
        await using var harness = CreateHarness();

        // Two relations that differ only in their anchor, which is the pair that
        // was reported: run together they read as one path of twice the length,
        // and the anchor — the only part that differs — lands in the middle of it.
        var component = harness.RenderView(RelatedView(), ContextMapPath);

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll(".domain-metadata__link").Count));

        var relations = component.FindAll(".domain-metadata__link");
        Assert.Equal(".domain/backlog/domain.md#domain-event-aiworklogged", relations[0].TextContent.Trim());
        Assert.Equal(".domain/backlog/domain.md#domain-event-entrycompleted", relations[1].TextContent.Trim());
    }

    [Fact]
    public async Task The_edited_chapter_keeps_the_per_section_actions()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));

        // One dropdown for the document and one for its section, and a Copilot
        // launch beside each: swapping the prose for an editor must not take a
        // section's state or its Copilot away with it.
        Assert.Equal(2, component.FindAll("[data-testid='knowledge-state-select']").Count);
        Assert.Equal(2, component.FindAll("[data-testid='knowledge-copilot-cli-button']").Count);
        Assert.Contains("Boundaries", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chapter_whose_folder_is_not_there_offers_no_way_in()
    {
        await using var harness = CreateHarness();

        // A view handed in by a parent, naming a root this machine does not have.
        // The panel keeps rendering what it was given and offers no way in, which
        // is the answer for a chapter that cannot be placed on disk.
        var component = harness.RenderView(MissingRootView(), ContextMapPath);

        component.WaitForAssertion(() => Assert.Contains("A summary nobody can edit.", component.Markup, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-surface']"));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-edit']"));
    }

    [Fact]
    public async Task Changing_the_state_writes_the_pending_body_before_the_status()
    {
        await using var harness = CreateHarness();
        var chapterPath = Path.Combine(harness.Root, ".domain", "context-map.md");
        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-edit']")));

        component.Find("[data-testid='knowledge-chapter-edit']").Click();

        // The typed body moves the status field too, which is what makes the
        // order decidable from the file alone. The writer's merge lets the text
        // win a field the text changed, so a body written *after* the dropdown
        // leaves "candidate" behind; flushed first, the dropdown is the last word
        // and it reads "accepted".
        component.WaitForElement("textarea").Input("# Context Map\n\n```meta\nstatus: candidate\n```\n\nTyped prose.\n");

        // From here on, the first thing to ask the folder source where .domain is
        // will be the status write, so what the chapter says at that moment is
        // recorded. That is what makes the ordering decidable: the settled file
        // cannot tell the two orders apart, because the merge repairs both.
        harness.Folders.ArmStatusWriteSnapshot();

        // The document's own dropdown is the first; the second belongs to the
        // section below it.
        component.FindAll("[data-testid='knowledge-state-select'] select")[0].Change("accepted");

        component.WaitForAssertion(
            () =>
            {
                var written = File.ReadAllText(chapterPath);
                Assert.Contains("Typed prose.", written, StringComparison.Ordinal);
                Assert.Contains("status: accepted", written, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));

        var settled = File.ReadAllText(chapterPath);
        Assert.DoesNotContain("status: candidate", settled, StringComparison.Ordinal);
        Assert.DoesNotContain("Original prose.", settled, StringComparison.Ordinal);

        Assert.Contains(
            "Typed prose.",
            harness.Folders.ChapterWhenStatusWasWritten ?? "the status was never written",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Deletes the temp folders once every test has awaited its harness away, so
    /// nothing this class rendered can still be writing into one of them. The
    /// catch stays as a courtesy for a lock this class does not own — a scanner
    /// or an indexer holding a file open — rather than as the thing keeping the
    /// suite green.
    /// </summary>
    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static DomainKnowledgeView MissingRootView()
    {
        var absent = Path.Combine(Path.GetTempPath(), "backlog-domain-panel-absent", Guid.NewGuid().ToString("N"));

        return new DomainKnowledgeView(
            "JSdotNet/Backlog",
            absent,
            Path.Combine(absent, ".domain"),
            null,
            new DomainKnowledgeDocument(
                ContextMapPath,
                "Context Map",
                DomainKnowledgeDocumentKind.ContextMap,
                "draft",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                "A summary nobody can edit.",
                [],
                [],
                []),
            []);
    }

    /// <summary>A context map whose metadata names two relations, handed in rather
    /// than read: the strip is what is under test and a view built here says so
    /// without a folder on disk having to spell the fence.</summary>
    private static DomainKnowledgeView RelatedView()
    {
        var absent = Path.Combine(Path.GetTempPath(), "backlog-domain-panel-related", Guid.NewGuid().ToString("N"));

        return new DomainKnowledgeView(
            "JSdotNet/Backlog",
            absent,
            Path.Combine(absent, ".domain"),
            null,
            new DomainKnowledgeDocument(
                ContextMapPath,
                "Context Map",
                DomainKnowledgeDocumentKind.ContextMap,
                "draft",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["related"] = "[.domain/backlog/domain.md#domain-event-aiworklogged, .domain/backlog/domain.md#domain-event-entrycompleted]"
                },
                "A summary nobody can edit.",
                [],
                [],
                []),
            []);
    }

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-panel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain"));
        _roots.Add(root);
        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), ContextMap);

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(AppFeatureKeys.CopilotCli, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();

        // The markdown editor watches its textarea through interop for the
        // highlight layer, which is not what any of this is about.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var folders = new RecordingKnowledgeFolderSource(
            new KnowledgeFolderSource(gitHub, settings),
            Path.Combine(root, ".domain", "context-map.md"));

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IKnowledgeFolderSource>(folders);
        context.Services.AddSingleton(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<KnowledgeChapterWriter>();

        return new Harness(root, context, repository.Alias, folders);
    }

    private sealed record Harness(string Root, BunitContext Context, string RepositoryAlias, RecordingKnowledgeFolderSource Folders) : IAsyncDisposable
    {
        public IRenderedComponent<DomainKnowledgePanel> Render(string? selectedPath) =>
            Context.Render<DomainKnowledgePanel>(parameters => parameters
                .Add(panel => panel.RepositoryAlias, RepositoryAlias)
                .Add(panel => panel.SelectedPath, selectedPath));

        public IRenderedComponent<DomainKnowledgePanel> RenderView(DomainKnowledgeView view, string? selectedPath) =>
            Context.Render<DomainKnowledgePanel>(parameters => parameters
                .Add(panel => panel.View, view)
                .Add(panel => panel.SelectedPath, selectedPath));

        /// <summary>
        /// Awaited disposal, because the editing surface this harness renders
        /// writes its last pending save on the way out. A synchronous
        /// <c>Dispose</c> hands that save to the renderer's dispatcher and returns
        /// before it lands, so the folder delete that follows could arrive while
        /// the file was still being replaced — a locked temp file on a slow
        /// machine and a green suite on a fast one.
        /// </summary>
        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
