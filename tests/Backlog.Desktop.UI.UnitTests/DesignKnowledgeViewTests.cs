using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Design knowledge is the one area whose reading view is worth more than its
/// editing view, and these tests are mostly about not trading the first for the
/// second.
/// <para>
/// With nothing selected the panel is a folder overview: every file, its token
/// strips, and each subject's metadata record. That is what a designer opens the
/// section for, so the editing surface is offered for a selected chapter only —
/// and the overview is asserted part by part rather than by its presence,
/// because the way to lose it is one branch too wide, not a deleted block.
/// </para>
/// <para>
/// A selected chapter reads before it writes: the file view renders the file and
/// the buffer arrives only when the header's Edit is pressed. That is not a
/// preference about editing but the condition for everything in the paragraph
/// below — a body the view never parses is a body whose records were never drawn.
/// </para>
/// <para>
/// The rest is where each block is drawn. A file's record belongs in the header
/// of the surface reading the file and a chapter's folded into its own heading,
/// once each — see <c>.design/content-editing.md</c>, "Knowledge Metadata
/// Blocks". This pane had drawn both twice and neither as the record it is.
/// </para>
/// </summary>
public sealed class DesignKnowledgeViewTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task A_selected_design_chapter_is_read_before_it_is_written()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("colors.md");

        // The way in is the file view's own header control, and until it is
        // pressed the body is the file rendered — which is what makes each
        // chapter's record and each diagram appear where the file puts them. An
        // editor mounted on arrival would have taken every one of those off the
        // screen for a reader who only came to read.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-edit']")));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-surface']"));
        Assert.Contains("The palette and how it is applied.", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Edit_puts_the_editing_surface_in_the_file_views_own_body()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("colors.md");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-edit']")));

        component.Find("[data-testid='design-chapter-file-edit']").Click();

        // Inside the file view's body rather than merely somewhere on the panel.
        // The header is what keeps the identity on screen while the chapter
        // scrolls, and a body that landed beside the file view instead of in it
        // would take that away while still looking right in a screenshot.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-body'] [data-testid='knowledge-chapter-surface']")));

        // The way out is where the way in was, and the Bare editing surface brings
        // no second one of its own: two Edit buttons, one under the other, is what
        // Bare exists to prevent.
        Assert.Single(component.FindAll("[data-testid='design-chapter-file-done']"));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-edit']"));
    }

    [Fact]
    public async Task The_selected_chapter_is_shown_through_the_shared_file_view()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("colors.md");

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-body']")));

        component.AssertTheFileIsNamedOnce("Colors", ".design-document");

        // Nothing above the file view introduces it any more: no header holding a
        // lone status badge, no folder path, no summary reprinting the file's
        // first paragraph. The file view's own header is the only thing that says
        // which file this is.
        Assert.Empty(component.FindAll(".design-document__header"));
        Assert.Empty(component.FindAll(".design-document__file"));
        Assert.Empty(component.FindAll(".design-document__summary"));
        Assert.Empty(component.FindAll(".design-knowledge__source"));

        // The token strip goes with them. Colors carries a token table, so this
        // is a strip that would have rendered — and its values are in the file
        // the view below is showing.
        Assert.Empty(component.FindAll(".design-token-strip"));
    }

    [Fact]
    public async Task A_selected_chapter_hands_its_scrollbar_to_the_file_view()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("colors.md");

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file']")));

        // The modifier is what caps the pane at the section's height, and Fill is
        // what makes the file view take that height and scroll its own body. A
        // body with a max-height instead would grow the pane past the section
        // already scrolling it, which is the second scrollbar this removed.
        Assert.Single(component.FindAll(".design-knowledge--chapter"));
        Assert.Contains("file-view--fill", component.Find("[data-testid='design-chapter-file']").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_selected_chapter_is_offered_whole_rather_than_reassembled_from_its_sections()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("colors.md");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-edit']")));

        component.Find("[data-testid='design-chapter-file-edit']").Click();

        // The meta fence is the tell. It is the part a parsed-and-restitched
        // buffer would drop, and dropping it would write the file back without
        // its status on the first debounce. Asked of the buffer rather than of the
        // read view, because the read view is where the fence is deliberately no
        // longer text — that is the assertion two tests below.
        component.WaitForAssertion(() => Assert.Contains(
            "status: accepted",
            component.Find("[data-testid='knowledge-chapter-surface']").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_chapter_of_the_open_file_carries_its_own_record_and_no_raw_fence()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("interaction-guidelines.md");

        var body = component.WaitForElement("[data-testid='design-chapter-file-body']");

        // Two chapters state a status and each gets its own record, folded into
        // its own heading (.design/content-editing.md, "Chapter level"). Before
        // this the body was an editor and no chapter had one at all.
        component.WaitForAssertion(() => Assert.Equal(2, body.QuerySelectorAll(".knowledge-record").Length));
        Assert.Equal(
            ["Auto-save", "Focus order"],
            body.QuerySelectorAll(".knowledge-record__headline .md-heading--2").Select(heading => heading.TextContent.Trim()));
        Assert.Equal(2, body.QuerySelectorAll(".knowledge-record__headline .badge--status").Length);

        // The chapter that states nothing keeps its heading and gains no status,
        // here exactly as in the folder overview.
        Assert.Contains("Motion", body.TextContent, StringComparison.Ordinal);
        Assert.Equal(2, body.QuerySelectorAll(".badge--status").Length);

        // And not one fence is left drawn as the listing it was written as —
        // "never twice" covers the raw fallback, and the raw fallback is what the
        // reader was actually looking at.
        Assert.DoesNotContain(
            component.FindAll("pre.md-code"),
            fence => fence.TextContent.TrimStart().StartsWith("status:", StringComparison.Ordinal));
        Assert.DoesNotContain("```", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_open_files_own_block_is_drawn_in_the_header_and_nowhere_else()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("interaction-guidelines.md");

        var body = component.WaitForElement("[data-testid='design-chapter-file-body']");
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".file-view__header [data-testid='design-chapter-file-file-metadata']")));

        // The file's own status is `draft`, and the header is the one place it may
        // appear: not as a second record in the body, and not as the fence the
        // record would otherwise have fallen back to.
        Assert.Contains("draft", component.Find("[data-testid='design-chapter-file-file-metadata']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("status: draft", body.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(
            body.QuerySelectorAll(".knowledge-record"),
            record => record.TextContent.Contains("Interaction guidelines", StringComparison.Ordinal));

        // The title still opens the body — it is the file's first heading and the
        // body is the file. What must not be there is a record around it.
        Assert.Equal("Interaction guidelines", body.QuerySelector(".md-heading--1")?.TextContent.Trim());
        Assert.Empty(body.QuerySelectorAll(".knowledge-record .md-heading--1"));
    }

    [Fact]
    public async Task A_status_picked_on_a_chapter_of_the_open_file_is_written_to_that_chapters_fence()
    {
        await using var harness = CreateHarness();
        var component = harness.Render("interaction-guidelines.md");
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='design-chapter-file-body'] .knowledge-record select")));

        component.FindAll("[data-testid='design-chapter-file-body'] .knowledge-record select")[0].Change("deprecated");

        // Auto-save's fence is the one that changes. The file's own block is the
        // heading directly above it in the source, and the pane used to be able to
        // write nothing but that one — so the file staying `draft` is half the
        // assertion.
        component.WaitForAssertion(
            () =>
            {
                var text = File.ReadAllText(Path.Combine(harness.DesignFolder, "interaction-guidelines.md"));
                Assert.Contains("status: deprecated", text, StringComparison.Ordinal);
                Assert.Contains("# Interaction guidelines\r\n```meta\r\nstatus: draft".ReplaceLineEndings(), text.ReplaceLineEndings(), StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_typed_design_chapter_reaches_the_file()
    {
        await using var harness = CreateHarness();
        var component = harness.Render("colors.md");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-edit']")));

        component.Find("[data-testid='design-chapter-file-edit']").Click();

        // Waited for rather than found: the click is dispatched onto a renderer
        // that may still be finishing the view's own load, and the render that
        // puts the textarea there is then not the one the click returned from.
        // Kept afterwards, because the blur belongs to the element the text was
        // typed into.
        var editor = component.WaitForElement("textarea");
        editor.Input("# Colors\n\nTyped into the design chapter.\n");
        editor.Blur();

        // The gestures are dispatched without awaiting the write, so the file is
        // polled rather than read once.
        component.WaitForAssertion(
            () => Assert.Contains(
                "Typed into the design chapter.",
                File.ReadAllText(Path.Combine(harness.DesignFolder, "colors.md")),
                StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_chapter_that_left_the_folder_offers_no_way_in()
    {
        await using var harness = CreateHarness();
        var component = harness.Render("colors.md");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file']")));

        // Listed and no longer readable. The panel keeps rendering what it
        // parsed before the file went away, and offers no edit that would fail
        // on the first keystroke.
        File.Delete(Path.Combine(harness.DesignFolder, "colors.md"));
        harness.Rerender(component, "colors.md");

        // Waited away rather than asserted away: the view re-reads the file before
        // it can conclude there is nothing to edit, so the file view leaves on a
        // later render than the parameter set returned from. The wait is not
        // vacuous — the view was pinned on screen above, so what is waited for is
        // a disappearance and not an absence that was always true.
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='design-chapter-file']")));
        Assert.Contains("Palette", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_nothing_selected_the_folder_overview_is_still_the_whole_folder()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(selectedPath: null);

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll(".design-document").Count));

        // Every part of the overview, one assertion each: the nav across files,
        // the token strip, the per-section blocks and the per-section status
        // badge. An editing surface here would have replaced the last two.
        Assert.Equal(2, component.FindAll(".design-knowledge__nav-link").Count);
        Assert.NotEmpty(component.FindAll(".design-token"));
        Assert.NotEmpty(component.FindAll(".design-section"));
        Assert.NotEmpty(component.FindAll(".design-section .knowledge-record__headline .badge--status"));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-surface']"));
    }

    [Fact]
    public async Task A_design_block_is_drawn_as_the_record_it_is_and_not_as_the_fence_it_was_written_in()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(selectedPath: null);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".design-document .knowledge-record")));

        // The status shares the heading's line, which is what the headline is for
        // — not a row of its own three lines down a list.
        Assert.NotEmpty(component.FindAll(".design-document .knowledge-record__headline .badge--status"));

        // And the legacy strip is gone with it. Both halves matter: the bare span
        // was the "raw metadata" that was reported, and the <code> per path was
        // the reference read back as the text of the fence rather than as the
        // reference it parses to.
        Assert.Empty(component.FindAll("span.knowledge-status"));
        Assert.Empty(component.FindAll("code.knowledge-related"));
    }

    [Fact]
    public async Task A_design_chapter_that_states_nothing_keeps_its_heading_and_gains_no_status()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(selectedPath: null);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".design-section")));

        var contrast = Assert.Single(
            component.FindAll(".design-section"),
            section => section.TextContent.Contains("Contrast", StringComparison.Ordinal));

        // The heading survives. A record that stood down for want of a status
        // would have taken it off the page, because the heading is drawn inside
        // the record.
        Assert.Equal("Contrast", contrast.QuerySelector("h4")?.TextContent.Trim());
        Assert.Empty(contrast.QuerySelectorAll(".badge--status"));

        // Absent means absent. "unknown" is not a status any design file states;
        // it was the parser's default, printed as though the file had said it.
        Assert.DoesNotContain("unknown", component.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Each_design_subject_shows_its_status_once()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(selectedPath: null);

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll(".design-document").Count));

        var colors = component.FindAll(".design-document")[0];

        // Two subjects state something — the file and the Palette chapter — so
        // two records. Contrast states nothing and gets none.
        Assert.Equal(2, colors.QuerySelectorAll(".knowledge-record").Length);

        // And every status on the document is inside one of them. The duplication
        // was a badge in a header *and* a strip under it, both answering for the
        // same subject, so the test is that no status is drawn loose.
        Assert.Equal(2, colors.QuerySelectorAll(".badge--status").Length);
        Assert.Equal(2, colors.QuerySelectorAll(".knowledge-record__headline .badge--status").Length);
    }

    [Fact]
    public async Task The_selected_chapters_own_record_is_drawn_in_the_file_views_header()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("colors.md");

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file-file-metadata']")));

        // The header is the part that stays put while the chapter scrolls, and
        // whether the file is current is not a question about the part of it
        // currently in view.
        Assert.Single(component.FindAll(".file-view__header [data-testid='design-chapter-file-file-metadata']"));

        // Nothing draws it a second time above the file view.
        Assert.Empty(component.FindAll(".design-document > .knowledge-record"));
        Assert.Empty(component.FindAll(".design-document > .knowledge-meta"));
    }

    [Fact]
    public async Task A_status_picked_on_a_design_file_is_written_to_the_file()
    {
        await using var harness = CreateHarness();
        var component = harness.Render(selectedPath: null);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".design-document__header select")));

        component.FindAll(".design-document__header select")[0].Change("deprecated");

        component.WaitForAssertion(
            () => Assert.Contains(
                "status: deprecated",
                File.ReadAllText(Path.Combine(harness.DesignFolder, "colors.md")),
                StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // The nav link reads its status off the parsed folder rather than off the
        // control that was changed, so it only says the new word once the pane has
        // re-read the file the write landed in.
        component.WaitForAssertion(() => Assert.Contains(
            "deprecated",
            component.FindAll(".design-knowledge__nav-link")[0].TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_status_picked_on_a_design_chapter_is_written_to_that_chapters_fence()
    {
        await using var harness = CreateHarness();
        var component = harness.Render(selectedPath: null);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".design-section .knowledge-record select")));

        component.FindAll(".design-section .knowledge-record select")[0].Change("deprecated");

        // Auto-save is the one chapter whose status the folder's vocabulary
        // offers a control for, and its fence is the one that must change — not
        // the file's, which is the heading directly above it in the source.
        component.WaitForAssertion(
            () =>
            {
                var text = File.ReadAllText(Path.Combine(harness.DesignFolder, "interaction-guidelines.md"));
                Assert.Contains("status: deprecated", text, StringComparison.Ordinal);
                Assert.Contains("status: draft", text, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
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

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-design-view-tests", Guid.NewGuid().ToString("n"));
        var repository = Path.Combine(root, "repo");
        var design = Path.Combine(repository, ".design");
        Directory.CreateDirectory(design);
        _roots.Add(root);

        File.WriteAllText(Path.Combine(design, "colors.md"), Colors);
        File.WriteAllText(Path.Combine(design, "interaction-guidelines.md"), InteractionGuidelines);

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(gitHubSettings.SetRepositories(repositories));
        gitHubSettings.SetCloneDirectory("backlog", repository);

        var context = new BunitContext();

        // The markdown editor watches its textarea through interop for the
        // highlight layer. None of that is what these tests are about.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHubSettings));
        context.Services.AddSingleton<DesignKnowledgeProvider>();
        context.Services.AddSingleton<KnowledgeChapterWriter>();

        return new Harness(context, design);
    }

    /// <summary>
    /// Three subjects and three shapes of block, because the record is drawn per
    /// subject and the shapes are what tell the placements apart. The file states
    /// a status the folder defines and a reference besides it; one chapter states
    /// a status the folder does not define; and one chapter states nothing at
    /// all, which is the case that must draw no record and still keep its
    /// heading.
    /// </summary>
    private const string Colors = """
        # Colors
        ```meta
        status: active
        related: [".design/interaction-guidelines.md"]
        ```

        The palette and how it is applied.

        ## Palette
        ```meta
        status: accepted
        ```

        | Token | Value | Usage |
        | --- | --- | --- |
        | --color-primary | #1f6feb | Primary actions |

        ## Contrast

        Contrast is checked against the darkest surface.
        """;

    /// <summary>
    /// The shape a real <c>.design</c> file has, and the shape the chapter view is
    /// asserted against: a title with its own block, several chapters each with
    /// theirs, and one chapter carrying none. Every status here is one the folder
    /// defines, which is what makes the records controls rather than pills — the
    /// chapter-level write has to be reachable from the body.
    /// </summary>
    private const string InteractionGuidelines = """
        # Interaction guidelines
        ```meta
        status: draft
        ```

        How the app behaves under the hand.

        ## Auto-save
        ```meta
        status: active
        ```

        Bodies persist on a debounce.

        ## Focus order
        ```meta
        status: draft
        ```

        Focus follows the reading order of the pane.

        ## Motion

        Motion is short, and never decorative.
        """;

    private sealed record Harness(BunitContext Context, string DesignFolder) : IAsyncDisposable
    {
        public IRenderedComponent<DesignKnowledgeView> Render(string? selectedPath) =>
            Context.Render<DesignKnowledgeView>(parameters => parameters
                .Add(view => view.RepositoryAlias, "backlog")
                .Add(view => view.SelectedPath, selectedPath));

        public void Rerender(IRenderedComponent<DesignKnowledgeView> component, string? selectedPath) =>
            component.Render(parameters => parameters
                .Add(view => view.RepositoryAlias, "backlog")
                .Add(view => view.SelectedPath, selectedPath));

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
