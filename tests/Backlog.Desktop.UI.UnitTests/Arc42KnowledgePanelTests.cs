using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The architecture panel now shows the selected chapter as the file rather than
/// as a rendering of it, so what is worth pinning is where the way in appears,
/// where it does not, and what the state dropdown beside it has to do first.
/// <para>
/// The dropdown is the interesting one. It and the body debounce are two
/// read-modify-writes on one file, and the panel owes the editor a flush before
/// it writes the state — so the assertion is that the typed body is on disk by
/// the time the dropdown's handler is done, not merely that it gets there
/// eventually on the debounce.
/// </para>
/// </summary>
public sealed class Arc42KnowledgePanelTests : IDisposable
{
    private const string DecisionPath = ".arc42/adr/0001-decision.md";

    private const string Decision = "# ADR 0001: Test decision\n\n```meta\nstatus: draft\n```\n\nOriginal prose.\n";

    private readonly List<string> _roots = [];

    [Fact]
    public async Task A_selected_chapter_renders_the_editing_surface()
    {
        await using var harness = CreateHarness(withArc42Folder: true);

        var component = harness.Render(DecisionPath);

        // The catalog is read asynchronously, so the first render is the loading
        // line and the chapter arrives on a later one.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));
        Assert.Single(component.FindAll("[data-testid='knowledge-chapter-edit']"));
        Assert.Contains("Original prose.", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_selected_chapter_is_shown_through_the_shared_file_view()
    {
        await using var harness = CreateHarness(withArc42Folder: true);

        var component = harness.Render(DecisionPath);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));

        // Inside the file view's body rather than merely somewhere on the panel.
        // The header is what keeps the identity on screen while the chapter
        // scrolls, and a body that landed beside the file view instead of in it
        // would take that away while still looking right in a screenshot.
        Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-body'] [data-testid='knowledge-chapter-surface']"));

        component.AssertTheFileIsNamedOnce("ADR 0001: Test decision", "[data-testid='arc42-document']");
    }

    [Fact]
    public async Task The_document_state_survives_the_move_onto_the_file_view()
    {
        await using var harness = CreateHarness(withArc42Folder: true);

        var component = harness.Render(DecisionPath);

        // The panel's own header gave up the title and the path to the file view
        // and kept the state dropdown, which is the one thing on it that a file
        // view has no business carrying. Losing it here would leave a decision
        // record with no way to change its state at all.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-state-select']")));
        Assert.Empty(component.FindAll(".knowledge-document__path"));
    }

    [Fact]
    public async Task The_chapter_nav_and_the_summary_strip_survive_the_body_swap()
    {
        await using var harness = CreateHarness(withArc42Folder: true);

        // No SelectedPath is the browsing shape: the chapter list on the left and
        // the summary counts above it are what makes the pane navigable, and they
        // have nothing to do with which body renders inside it.
        var component = harness.Render(selectedPath: null);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-option']")));
        Assert.Contains("ADR/TDR", component.Markup, StringComparison.Ordinal);
        Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']"));
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
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-edit']"));
    }

    [Fact]
    public async Task Changing_the_state_writes_the_pending_body_before_the_status()
    {
        await using var harness = CreateHarness(withArc42Folder: true);
        var chapterPath = Path.Combine(harness.Root, ".arc42", "adr", "0001-decision.md");
        var component = harness.Render(DecisionPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-edit']")));

        component.Find("[data-testid='knowledge-chapter-edit']").Click();

        // The typed body moves the status field too, which is what makes the
        // order decidable from the file alone. The writer's merge lets the text
        // win a field the text changed, so a body written *after* the dropdown
        // leaves "candidate" behind; flushed first, the dropdown is the last word
        // and it reads "accepted".
        component.Find("textarea").Input("# ADR 0001: Test decision\n\n```meta\nstatus: candidate\n```\n\nTyped prose.\n");

        // From here on, the first thing to ask the folder source where .arc42 is
        // will be the status write, so what the chapter says at that moment is
        // recorded. That is what makes the ordering decidable: the settled file
        // cannot tell the two orders apart, because the merge repairs both.
        harness.Folders.ArmStatusWriteSnapshot();
        component.Find("[data-testid='knowledge-state-select'] select").Change("accepted");

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

    [Fact]
    public async Task A_debounced_save_leaves_the_editor_open_where_it_was()
    {
        await using var harness = CreateHarness(withArc42Folder: true);
        var chapterPath = Path.Combine(harness.Root, ".arc42", "adr", "0001-decision.md");
        var component = harness.Render(DecisionPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-edit']")));

        component.Find("[data-testid='knowledge-chapter-edit']").Click();

        // Waited for rather than found: the panel is still settling its first load
        // when the way in appears, and the render that puts the textarea there is
        // not always the one the click returned from.
        component.WaitForElement("textarea").Input("# ADR 0001: Test decision\n\n```meta\nstatus: draft\n```\n\nTyped and left alone.\n");

        // No gesture at all: the debounce saves, and the panel re-reads the catalog
        // so the counts and the chapter list follow the file. Re-reading is not
        // closing — the caret is still in a textarea afterwards, and the indicator
        // that was about to say "Saved" is still the one on screen to say it.
        component.WaitForAssertion(
            () => Assert.Contains("Typed and left alone.", File.ReadAllText(chapterPath), StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        component.WaitForAssertion(
            () => Assert.Equal("Saved", component.Find("[data-testid='knowledge-chapter-save-state']").TextContent.Trim()),
            TimeSpan.FromSeconds(5));
        Assert.NotEmpty(component.FindAll("textarea"));
        Assert.Single(component.FindAll("[data-testid='knowledge-chapter-done']"));
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
