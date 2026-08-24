using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The architecture panel now shows the selected chapter through the same file
/// view every other knowledge area uses, rather than drawing its own frame around
/// a rendering of it. So what is worth pinning is what the move was for: the way in
/// and out of editing on the file's own header, the status drawn once beside the
/// heading it belongs to rather than a second time above the file, a copy button
/// per chapter, and the fact that leaving the editor still re-reads the folder
/// without closing the surface under the caret.
/// <para>
/// The status is the one that used to be drawn twice. The file's own status is in
/// the file view's header now, beside the name it describes, and this panel offers
/// none of its own beside it — the disagreement between the panel's select and the
/// file's own fence was the reason the move happened.
/// </para>
/// </summary>
public sealed class Arc42KnowledgePanelTests : IDisposable
{
    private const string DecisionPath = ".arc42/adr/0001-decision.md";

    private const string Decision = "# ADR 0001: Test decision\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n";

    private readonly List<string> _roots = [];

    [Fact]
    public async Task A_selected_chapter_opens_as_the_file_read_and_offers_a_way_in()
    {
        await using var harness = CreateHarness(withArc42Folder: true);

        var component = harness.Render(DecisionPath);

        // Read is the resting state now, the same as every other knowledge area:
        // the editing surface is not on screen until someone asks for it, and what
        // is on screen is the chapter and the button that opens it, in the file
        // view's own header. The catalog is read asynchronously, so the first
        // render is the loading line and the chapter arrives on a later one.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-edit']")));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-surface']"));
        Assert.Contains("Original prose.", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_puts_the_editing_surface_in_the_file_views_own_body()
    {
        await using var harness = CreateHarness(withArc42Folder: true);

        var component = harness.Render(DecisionPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-edit']")));

        component.Find("[data-testid='arc42-chapter-file-edit']").Click();

        // Inside the file view's body rather than merely somewhere on the panel.
        // The header is what keeps the identity on screen while the chapter
        // scrolls, and a body that landed beside the file view instead of in it
        // would take that away while still looking right in a screenshot.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-body'] [data-testid='knowledge-chapter-surface']")));

        // The way out is where the way in was, and the Bare editing surface brings
        // no second one of its own: two Edit buttons, one under the other, is what
        // Bare exists to prevent.
        Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-done']"));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-edit']"));
    }

    [Fact]
    public async Task The_selected_chapter_is_shown_through_the_shared_file_view()
    {
        await using var harness = CreateHarness(withArc42Folder: true);

        var component = harness.Render(DecisionPath);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-body']")));

        component.AssertTheFileIsNamedOnce("ADR 0001: Test decision", "[data-testid='arc42-document']");

        // The kind label sits on the header beside the title and the path: arc42
        // keeps no per-file label of its own, so the header says the one thing its
        // folder does distinguish — a decision record from an ordinary chapter.
        Assert.Contains("Decision record", component.Find(".file-view__meta").TextContent, StringComparison.Ordinal);
        Assert.Empty(component.FindAll(".knowledge-document__path"));
    }

    [Fact]
    public async Task The_status_the_body_shows_is_not_offered_a_second_time_beside_it()
    {
        await using var harness = CreateHarness(withArc42Folder: true);

        var component = harness.Render(DecisionPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-body']")));

        // The file states a status under its own title, and the file view is
        // drawing the control for it — in its header, beside the name that status
        // describes, on the part of the pane that stays put while the chapter
        // scrolls. Two controls for one field is how the pane used to disagree
        // with itself — the panel's select and the file's fence — so this panel
        // offers none of its own, and its external select is gone.
        Assert.Single(component.FindAll(".file-view__header .knowledge-record__headline"));
        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-file-body'] .knowledge-record"));
        Assert.Empty(component.FindAll("[data-testid='knowledge-state-select']"));

        // And no raw fence left over: a status drawn as a code block is what the
        // reader was seeing under the panel's own select before.
        Assert.DoesNotContain("status: draft", component.Find("[data-testid='arc42-chapter-file-body']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chapter_can_be_copied_whole_or_a_chapter_at_a_time()
    {
        await using var harness = CreateHarness(withArc42Folder: true);

        var component = harness.Render(DecisionPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-body']")));

        // The file's own copy is in the header with the rest of its actions; the
        // heading in the body carries its chapter's — the same two ways to copy the
        // Domain page offers, in the same places.
        Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-copy']"));
        Assert.Single(component.FindAll(".md-chapter-copy"));
        Assert.Single(component.FindAll("[data-testid='markdown-chapter-copy-0']"));
    }

    [Fact]
    public async Task The_chapter_nav_survives_the_body_swap()
    {
        await using var harness = CreateHarness(withArc42Folder: true);

        // No SelectedPath is the browsing shape: the chapter list on the left is
        // what makes the pane navigable, and it is a list of other files rather
        // than anything about the one open inside it — so replacing the body with
        // the file view does not take it away.
        var component = harness.Render(selectedPath: null);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-option']")));
        Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-body']"));
    }

    [Fact]
    public async Task A_chapter_that_cannot_be_placed_offers_no_way_in()
    {
        // Nothing to place it against: without an .arc42 folder the panel has no
        // chapter to resolve, so it must not put an editing surface on screen for
        // the reader to type into.
        await using var harness = CreateHarness(withArc42Folder: false);

        var component = harness.Render(DecisionPath);

        component.WaitForAssertion(() => Assert.Contains("No arc42 folder here yet.", component.Markup, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-surface']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-file-edit']"));
    }

    [Fact]
    public async Task Changing_the_state_beside_the_heading_writes_it_to_the_file()
    {
        await using var harness = CreateHarness(withArc42Folder: true);
        var chapterPath = Path.Combine(harness.Root, ".arc42", "adr", "0001-decision.md");
        var component = harness.Render(DecisionPath);

        // The select lives in the file view's header, beside the name it belongs
        // to — the panel no longer draws one of its own. What it reports is the
        // file's top heading, so the write addresses the file itself and lands on
        // its own status fence.
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".file-view__header .knowledge-record__headline select")));
        component.Find(".file-view__header .knowledge-record__headline select").Change("accepted");

        component.WaitForAssertion(
            () => Assert.Contains("status: accepted", File.ReadAllText(chapterPath), StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        var settled = File.ReadAllText(chapterPath);
        Assert.DoesNotContain("status: draft", settled, StringComparison.Ordinal);

        // The prose is untouched: a status change is a merge into the one fence, not
        // a rewrite of the chapter around it.
        Assert.Contains("Original prose.", settled, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_debounced_save_leaves_the_editor_open_where_it_was()
    {
        await using var harness = CreateHarness(withArc42Folder: true);
        var chapterPath = Path.Combine(harness.Root, ".arc42", "adr", "0001-decision.md");
        var component = harness.Render(DecisionPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-edit']")));

        component.Find("[data-testid='arc42-chapter-file-edit']").Click();

        // Waited for rather than found: the panel is still settling its first load
        // when the way in appears, and the render that puts the textarea there is
        // not always the one the click returned from.
        component.WaitForElement("textarea").Input("# ADR 0001: Test decision\n\n```meta\nstatus: draft\n```\n\nTyped and left alone.\n");

        // No gesture at all: the debounce saves, and the panel re-reads the catalog
        // so the chapter list follows the file. Re-reading is not closing — the
        // caret is still in a textarea afterwards, and the way out is still the
        // file view's Done. This is the flush the same-file reload guard protects: a
        // same-path reload must not reset the file view's editing mode under a
        // reader mid-sentence.
        //
        // The save indicator is deliberately not asserted here. The Bare surface
        // hands editing to the file view, so its own edit flag stays down, and the
        // panel's re-read then adopts the freshly saved text and returns the
        // indicator to rest — the same as every other area's Bare surface does. What
        // must survive the reload is the open editor, not the word that was on the
        // indicator for the moment before it settled.
        component.WaitForAssertion(
            () => Assert.Contains("Typed and left alone.", File.ReadAllText(chapterPath), StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        component.WaitForAssertion(
            () => Assert.NotEmpty(component.FindAll("textarea")),
            TimeSpan.FromSeconds(5));
        Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-done']"));
    }

    [Fact]
    public async Task A_chapter_that_stops_being_readable_under_an_open_editor_falls_back_to_the_read_view()
    {
        // The hole this pins: editing mode is the file view's now, and it is held on
        // _editing alone until the markup ties it to the same readable-chapter test
        // CanEdit uses. A same-path reload can leave _editing set with the chapter
        // read come back empty — the file locked, gone, or newly unreadable between
        // the catalog read and the chapter read — and an edit mode that outlives the
        // chapter would win over the read-only fallback, blanking the reader's text
        // into a textarea with no chapter behind it and no way out, because the Done
        // button goes with CanEdit. The fallback is exactly what that state is for,
        // so it is what must render.
        await using var harness = CreateHarness(withArc42Folder: true);
        var gitHub = harness.Context.Services.GetRequiredService<GitHubSettingsStore>();

        var component = harness.Render(DecisionPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-edit']")));

        // A readable chapter, opened: the editing surface is on screen, which is the
        // state the reload has to find the editor in for the hole to be reachable.
        component.Find("[data-testid='arc42-chapter-file-edit']").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-body'] [data-testid='knowledge-chapter-surface']")));

        // The file goes unreadable on the chapter read of the very next reload, after
        // the catalog has already listed it — so the panel keeps a document to render
        // read-only while the chapter itself comes back None. Re-setting the same
        // repositories is the folder-changed signal the panel reloads on, the same
        // path it takes for a folder that moved underneath it.
        harness.Folders.BreakChapterOnNextReload();
        await component.InvokeAsync(() => gitHub.SetRepositories(gitHub.Current.Repositories));

        // The read-only fallback is what the catalog parsed, drawn through the shared
        // markdown view, and not a blank editor: the surface is gone, and the prose
        // the reader could no longer edit is at least still on the screen.
        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='arc42-chapter-file-body'] [data-testid='knowledge-chapter-surface']"));
            Assert.Contains(
                "Original prose.",
                component.Find("[data-testid='arc42-chapter-file-body'] .knowledge-p").TextContent,
                StringComparison.Ordinal);
        });

        // And no way in offered on an unreadable chapter, which is the CanEdit half
        // the mode is now tied to: no Done stranded on a surface that is not there,
        // and no Edit onto a file the first keystroke would fail to save.
        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-file-done']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-file-edit']"));
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

    private Harness CreateHarness(bool withArc42Folder)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-arc42-panel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);

        if (withArc42Folder)
        {
            Directory.CreateDirectory(Path.Combine(root, ".arc42", "adr"));
            Directory.CreateDirectory(Path.Combine(root, ".arc42", "_meta"));
            File.WriteAllText(Path.Combine(root, ".arc42", "01-introduction.md"), "# Introduction\n\nGoals.\n");
            File.WriteAllText(Path.Combine(root, ".arc42", "adr", "0001-decision.md"), Decision);

            // The reader only walks the top of the folder on its own, so the
            // decision record reaches the catalog through the index — which is
            // also how the real folder presents it, and the state dropdown under
            // test only appears for a decision record.
            File.WriteAllText(Path.Combine(root, ".arc42", "_meta", "index.json"), """
                {
                  "entries": [
                    { "type": "file", "path": ".arc42/adr/0001-decision.md" },
                    { "type": "file", "path": ".arc42/01-introduction.md" }
                  ]
                }
                """);
        }

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
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
            Path.Combine(root, ".arc42", "adr", "0001-decision.md"));

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IKnowledgeFolderSource>(folders);
        context.Services.AddSingleton<Arc42KnowledgeStore>();
        context.Services.AddSingleton<KnowledgeChapterWriter>();

        return new Harness(root, context, repository.Alias, folders);
    }

    private sealed record Harness(string Root, BunitContext Context, string RepositoryAlias, RecordingKnowledgeFolderSource Folders) : IAsyncDisposable
    {
        public IRenderedComponent<Arc42KnowledgePanel> Render(string? selectedPath) =>
            Context.Render<Arc42KnowledgePanel>(parameters => parameters
                .Add(panel => panel.RepositoryAlias, RepositoryAlias)
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
