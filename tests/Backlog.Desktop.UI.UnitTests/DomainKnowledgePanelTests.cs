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

    /// <summary>The same chapter with a diagram written into it, for the one
    /// question that needs the fence to be there: where it is drawn.</summary>
    private const string ContextMapWithDiagram =
        "# Context Map\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n\n```mermaid\nflowchart LR\n  A --> B\n```\n\n## Boundaries\n\nWhat divides the contexts.\n";

    private readonly List<string> _roots = [];

    [Fact]
    public async Task A_selected_chapter_opens_as_the_file_read_and_offers_a_way_in()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);

        // Read is the resting state, so the editing surface is not on screen until
        // someone asks for it — what is on screen is the chapter, and the button
        // that opens it, in the file view's own header.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-edit']")));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-surface']"));
        Assert.Contains("Original prose.", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_puts_the_editing_surface_in_the_file_views_own_body()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-edit']")));

        component.Find("[data-testid='domain-chapter-file-edit']").Click();

        // Inside the file view's body rather than merely somewhere on the panel.
        // The header is what keeps the identity on screen while the chapter
        // scrolls, and a body that landed beside the file view instead of in it
        // would take that away while still looking right in a screenshot.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body'] [data-testid='knowledge-chapter-surface']")));

        // The way out is where the way in was, and the editing surface brings no
        // second one of its own: two Edit buttons, one under the other, is what
        // Bare exists to prevent.
        Assert.Single(component.FindAll("[data-testid='domain-chapter-file-done']"));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-edit']"));
    }

    [Fact]
    public async Task The_selected_chapter_is_shown_through_the_shared_file_view()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body']")));

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
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body']")));

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
    public async Task The_shown_chapter_keeps_the_per_section_actions()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body']")));

        // A Copilot launch for the document and one for its section: showing the
        // file must not take a section's launch away with it, because nothing else
        // in the product reaches it.
        Assert.Equal(2, component.FindAll("[data-testid='knowledge-copilot-cli-button']").Count);
        Assert.Contains("Boundaries", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_status_the_body_shows_is_not_offered_a_second_time_beside_it()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body']")));

        // The file states a status under its own heading and states none under
        // "## Boundaries". So the body draws the record for the first, and this
        // panel keeps the only control there is for the second. Two controls for
        // one field is how the pane used to disagree with itself.
        Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body'] .knowledge-record__headline"));
        Assert.Single(component.FindAll("[data-testid='knowledge-state-select']"));

        // And no raw fence left over: a status drawn as a code block is what the
        // reader was seeing before.
        Assert.DoesNotContain("status: draft", component.Find("[data-testid='domain-chapter-file-body']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task While_editing_the_status_is_reachable_where_it_always_was()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-edit']")));

        component.Find("[data-testid='domain-chapter-file-edit']").Click();

        // The record lives in the read view, and the read view is not what is on
        // screen — so the panel's own control comes back rather than leaving the
        // document's state unreachable while its text is being written.
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='knowledge-state-select']").Count));
    }

    [Fact]
    public async Task A_chapters_diagram_is_drawn_where_it_was_written()
    {
        await using var harness = CreateHarness(diagramChapter: true);

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body']")));

        // One diagram, inside the file. It used to be rendered twice — once by the
        // view, once by this panel below the file — which is how a diagram came to
        // appear under the chapter that contained it.
        Assert.Single(component.FindAll("[data-testid='diagram-view']"));
        Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body'] [data-testid='diagram-view']"));
    }

    [Fact]
    public async Task A_chapter_can_be_copied_whole_or_a_chapter_at_a_time()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body']")));

        // The file's own copy is in the header with the rest of its actions; each
        // heading in the body carries its chapter's.
        Assert.Single(component.FindAll("[data-testid='domain-chapter-file-copy']"));
        Assert.Equal(2, component.FindAll(".md-chapter-copy").Count);
        Assert.Single(component.FindAll("[data-testid='markdown-chapter-copy-0']"));
        Assert.Single(component.FindAll("[data-testid='markdown-chapter-copy-3']"));
    }

    [Fact]
    public async Task Comparing_reads_the_committed_version_only_when_it_is_asked_for()
    {
        var history = StubGitFileHistory.Committed("# Context Map\n\n```meta\nstatus: draft\n```\n\nCommitted prose.\n");
        await using var harness = CreateHarness(history);

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-compare']")));

        // Reading a commit costs a git process, so nothing has been read while the
        // reader was only looking at the file.
        Assert.Empty(history.Reads);

        component.Find("[data-testid='domain-chapter-file-compare']").Click();

        // Compared against the text as it was opened first, which needs no
        // repository at all — so still nothing read.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-compare-view']")));
        Assert.Empty(history.Reads);

        component.Find("[data-testid='domain-chapter-file-baseline-committed']").Click();

        component.WaitForAssertion(() => Assert.Contains("Committed prose.", component.Find("[data-testid='domain-chapter-file-body']").TextContent, StringComparison.Ordinal));
        Assert.Equal(Path.Combine(harness.Root, ".domain", "context-map.md"), Assert.Single(history.Reads));
    }

    [Fact]
    public async Task A_chapter_with_no_commit_says_so_rather_than_calling_every_line_new()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-compare']")));

        component.Find("[data-testid='domain-chapter-file-compare']").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-baseline-committed']")));
        component.Find("[data-testid='domain-chapter-file-baseline-committed']").Click();

        // "Never committed" is an ordinary state for a chapter written this
        // morning, and it is not the same state as an empty file — which is what a
        // diff against nothing would have shown it as.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-compare-unavailable']")));
        Assert.Contains("not been committed", component.Find("[data-testid='domain-chapter-file-compare-unavailable']").TextContent, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("[data-testid='domain-chapter-file-compare-view']"));
    }

    [Fact]
    public async Task A_remark_lands_in_the_margin_against_the_block_it_was_asked_for()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid^='markdown-comment-']")));

        // The affordance on a block is what asks for a remark, because there is
        // nowhere to type one before it exists. Block 2 is the prose under the
        // heading: block 1 is the `meta` fence, which has no row of its own because
        // the heading above it drew it.
        component.Find("[data-testid='markdown-comment-2']").Click();

        component.WaitForAssertion(() => Assert.Single(component.FindAll(".md-block-row[data-block='2'] .md-comment")));

        // Beside the prose rather than pushed into it: a chapter under review has
        // to keep reading as a chapter.
        Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body'] .md-view--margin"));
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
        Assert.Empty(component.FindAll("[data-testid='domain-chapter-file-edit']"));
    }

    [Fact]
    public async Task Changing_the_state_writes_the_pending_body_before_the_status()
    {
        await using var harness = CreateHarness();
        var chapterPath = Path.Combine(harness.Root, ".domain", "context-map.md");
        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-edit']")));

        component.Find("[data-testid='domain-chapter-file-edit']").Click();

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

    private Harness CreateHarness(StubGitFileHistory? history = null, bool diagramChapter = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-panel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain"));
        _roots.Add(root);
        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), diagramChapter ? ContextMapWithDiagram : ContextMap);

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

        // Nothing in these folders has ever been committed, so the default answer
        // is the honest one. A test comparing against a commit brings its own.
        history ??= new StubGitFileHistory();
        context.Services.AddSingleton<IGitFileHistoryService>(history);

        return new Harness(root, context, repository.Alias, folders, history);
    }

    private sealed record Harness(
        string Root,
        BunitContext Context,
        string RepositoryAlias,
        RecordingKnowledgeFolderSource Folders,
        StubGitFileHistory History) : IAsyncDisposable
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
