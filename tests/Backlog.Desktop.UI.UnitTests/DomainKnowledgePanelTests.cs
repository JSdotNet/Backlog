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
    public async Task The_panel_does_not_print_the_folder_it_read_from()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body']")));

        // Where the knowledge folder is on this machine is a fact about the
        // workspace, not about the chapter on screen. What the reader does need —
        // which file this is — the file view's header says, and it says it as the
        // file's own path.
        Assert.Empty(component.FindAll(".domain-knowledge__source"));
        Assert.Equal(ContextMapPath, component.Find(".file-view__path").TextContent);
    }

    [Fact]
    public async Task The_context_map_is_marked_as_the_context_map_whichever_way_it_was_reached()
    {
        await using var harness = CreateHarness();

        // Picked out of the knowledge menu, which is the route that used to lose
        // the modifier: the folder view names it when nothing is selected, and the
        // per-kind lookup had no entry for it. The stylesheet hangs the map's own
        // treatment off this class, so both routes have to carry it.
        var component = harness.Render(ContextMapPath);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body']")));
        Assert.Contains(
            "domain-document--context-map",
            component.Find("[data-testid='domain-document']").GetAttribute("class") ?? string.Empty,
            StringComparison.Ordinal);
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
        Assert.Equal(".domain/tasks/domain.md#domain-event-aiworklogged", relations[0].TextContent.Trim());
        Assert.Equal(".domain/tasks/domain.md#domain-event-entrycompleted", relations[1].TextContent.Trim());
    }

    [Fact]
    public async Task The_shown_chapter_is_the_file_and_nothing_after_it()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body']")));

        // The section list and the metadata strip are this panel's own reading of
        // the file, assembled from the parse it holds. Under a file view that has
        // just shown the same headings, the same fields and the same prose, they
        // are the chapter told a second time — and the two disagree the moment
        // either is typed into.
        Assert.Empty(component.FindAll(".domain-sections"));
        Assert.Empty(component.FindAll(".domain-metadata"));
        Assert.Empty(component.FindAll(".domain-document__summary"));

        // The headings themselves are still on screen, because the file has them.
        Assert.Contains("Boundaries", component.Find("[data-testid='domain-chapter-file-body']").TextContent, StringComparison.Ordinal);

        // One Copilot launch, for the document. The per-section ones went with the
        // sections; what is left is the one thing on this header that is genuinely
        // not in the file.
        Assert.Single(component.FindAll("[data-testid='knowledge-copilot-cli-button']"));
    }

    [Fact]
    public async Task A_document_the_file_view_is_not_showing_keeps_its_sections()
    {
        await using var harness = CreateHarness();

        // The folder overview, where every document is one entry in a list and the
        // section list is the only thing saying what is inside each. Withdrawing it
        // for the open chapter must not withdraw it for these.
        var component = harness.Render(null);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".domain-sections")));
        Assert.Contains("Boundaries", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_status_the_body_shows_is_not_offered_a_second_time_beside_it()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body']")));

        // The file states a status under its own title, and the file view is
        // drawing the control for it — in its header, beside the name that status
        // describes. Two controls for one field is how the pane used to disagree
        // with itself, so this panel offers none of its own.
        Assert.Single(component.FindAll(".file-view__header .knowledge-record__headline"));
        Assert.Empty(component.FindAll("[data-testid='domain-chapter-file-body'] .knowledge-record"));
        Assert.Empty(component.FindAll("[data-testid='knowledge-state-select']"));

        // And no raw fence left over: a status drawn as a code block is what the
        // reader was seeing before.
        Assert.DoesNotContain("status: draft", component.Find("[data-testid='domain-chapter-file-body']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_read_view_shows_the_status_and_not_the_rest_of_the_fence()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body']")));

        // The status has a visualization worth the line it is on. `order` and the
        // rest have none, and printed as label-and-value rows they are the fence
        // again with the fence taken off.
        Assert.Single(component.FindAll(".knowledge-record__headline .badge--status"));
        Assert.Empty(component.FindAll("dl.knowledge-fields"));
    }

    [Fact]
    public async Task While_editing_the_status_is_still_the_one_the_files_header_holds()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-edit']")));

        component.Find("[data-testid='domain-chapter-file-edit']").Click();

        // The record is in the file view's header, and the header is what stays on
        // screen while the body is being typed into — so the document's state is
        // still reachable and this panel still offers no second control for it.
        // It used to have to: the record was at the top of the read view, and the
        // read view is not what is on screen while writing.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));
        Assert.Single(component.FindAll(".file-view__header .knowledge-record__headline select"));
        Assert.Empty(component.FindAll("[data-testid='knowledge-state-select']"));
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
    public async Task The_committed_version_is_read_once_as_the_chapter_opens()
    {
        var history = StubGitFileHistory.Committed("# Context Map\n\n```meta\nstatus: draft\n```\n\nCommitted prose.\n");
        await using var harness = CreateHarness(history);

        var component = harness.Render(ContextMapPath);

        // As the chapter opens, not when a reader presses Compare. Whether there is
        // anything to compare is what decides that the button appears at all, so it
        // cannot be a question only the press can answer.
        component.WaitForAssertion(() => Assert.Equal(Path.Combine(harness.Root, ".domain", "context-map.md"), Assert.Single(history.Reads)));
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-compare']")));

        component.Find("[data-testid='domain-chapter-file-compare']").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-compare-view']")));

        component.Find("[data-testid='domain-chapter-file-baseline-committed']").Click();

        // And one read, still: the answer is kept for as long as the chapter is
        // open, so pressing through the modes does not spend a git process each
        // time.
        component.WaitForAssertion(() => Assert.Contains("Committed prose.", component.Find("[data-testid='domain-chapter-file-body']").TextContent, StringComparison.Ordinal));
        Assert.Single(history.Reads);
    }

    [Fact]
    public async Task A_chapter_that_says_what_its_commit_says_is_not_offered_a_comparison()
    {
        // The bug this pins: the button was on every chapter in the folder, on the
        // strength of a committed version nobody had read. A chapter with nothing
        // uncommitted in it has nothing to show, and the control is a promise that
        // there is something.
        var history = StubGitFileHistory.Committed(ContextMap);
        await using var harness = CreateHarness(history);

        var component = harness.Render(ContextMapPath);

        component.WaitForAssertion(() => Assert.Single(history.Reads));
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='domain-chapter-file-compare']")));

        // The file itself is on screen and can still be written to, which is what
        // makes the missing button a statement about the file rather than about the
        // panel having failed to render.
        Assert.Single(component.FindAll("[data-testid='domain-chapter-file-edit']"));
    }

    [Fact]
    public async Task A_clean_chapter_that_has_never_been_committed_is_not_offered_one_either()
    {
        // The stub answers "not tracked" by default, which is the state of a chapter
        // written this morning. There is no earlier version and the reader has typed
        // nothing, so both sides of the comparison would be this file.
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);

        component.WaitForAssertion(() => Assert.Single(harness.History.Reads));
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='domain-chapter-file-compare']")));
    }

    [Fact]
    public async Task A_chapter_changed_in_this_sitting_is_offered_a_comparison()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(harness.History.Reads));
        Assert.Empty(component.FindAll("[data-testid='domain-chapter-file-compare']"));

        ChangeTheChapterState(component);

        // "As opened" is now a different text from the one on screen, and that is a
        // comparison worth offering however the commit answered.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-compare']")));

        // The commit did not move while the reader typed, so the debounced reloads
        // that follow the write do not go back to git.
        Assert.Single(harness.History.Reads);
    }

    [Fact]
    public async Task A_chapter_with_no_commit_says_so_rather_than_calling_every_line_new()
    {
        await using var harness = CreateHarness();

        // Changed first, because a clean chapter that was never committed is not
        // offered a comparison at all now — the way to this message is a reader who
        // has changed something and then asks what the commit said.
        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.Single(harness.History.Reads));
        ChangeTheChapterState(component);

        // Back to reading first: the three modes are one at a time, and comparing is
        // what a reader does to a file they have stopped typing into.
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file-done']")));
        component.Find("[data-testid='domain-chapter-file-done']").Click();

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

    /// <summary>
    /// Changes the chapter the way a reader can change it without waiting for a
    /// debounce: the state dropdown writes the file and the panel re-reads it, so
    /// the body on screen stops being the body that was opened.
    /// <para>
    /// The dropdown is the file's own record, in the file view's header. It is the
    /// only one on the panel — the header stays on screen in every mode, so this
    /// panel offers no second control beside it — and the caller is left in the
    /// editing mode it entered.
    /// </para>
    /// </summary>
    private static void ChangeTheChapterState(IRenderedComponent<DomainKnowledgePanel> component)
    {
        component.Find("[data-testid='domain-chapter-file-edit']").Click();
        component.WaitForAssertion(() => Assert.Single(component.FindAll(".file-view__header .knowledge-record__headline select")));
        component.Find(".file-view__header .knowledge-record__headline select").Change("accepted");
    }

    [Fact]
    public async Task Following_a_domain_relation_asks_the_host_for_that_chapter()
    {
        var followed = new List<KnowledgeChapterLink>();
        await using var harness = CreateHarness();

        var component = harness.RenderView(RelatedView(), ContextMapPath, followed.Add);
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll(".domain-metadata__link").Count));

        component.FindAll(".domain-metadata__link")[0].Click();

        // The whole target, not the folder it starts with: which file is what the
        // reader pressed, and the heading slug is the part of it the old handler
        // dropped on the way.
        var target = Assert.Single(followed);
        Assert.Equal("domain", target.AreaKey);
        Assert.Equal(".domain/tasks/domain.md", target.Path);
        Assert.Equal("tasks/domain.md", target.RelativePath);
        Assert.Equal("domain-event-aiworklogged", target.Anchor);
    }

    [Fact]
    public async Task Following_an_architecture_relation_asks_for_that_section()
    {
        // The loose end this closes: an .arc42 reference rendered as something to
        // press and then did nothing, because the panel only understood its own
        // folder. Which section a reference belongs to is read from the path, so
        // every section the pane shows is reachable from every other.
        var followed = new List<KnowledgeChapterLink>();
        await using var harness = CreateHarness();

        var component = harness.RenderView(CrossAreaView(), ContextMapPath, followed.Add);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".domain-metadata__link")));

        component.Find(".domain-metadata__link").Click();

        var target = Assert.Single(followed);
        Assert.Equal("arc42", target.AreaKey);
        Assert.Equal(".arc42/03-context-and-scope.md", target.Path);
        Assert.Equal("03-context-and-scope.md", target.RelativePath);
    }

    [Fact]
    public async Task A_relation_naming_no_section_is_left_as_the_words_it_was_written_as()
    {
        // `.github/copilot-instructions.md` is a real file and not a chapter this
        // pane can show. A control on it would be a promise to go somewhere, and
        // there is nowhere — so it stays text, which is also what keeps it out of
        // the tab order.
        var followed = new List<KnowledgeChapterLink>();
        await using var harness = CreateHarness();

        var component = harness.RenderView(CrossAreaView(), ContextMapPath, followed.Add);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".domain-metadata")));

        Assert.Single(component.FindAll(".domain-metadata__link"));
        Assert.Contains(".github/copilot-instructions.md", component.Find(".domain-metadata").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(
            ".github/copilot-instructions.md",
            component.FindAll(".domain-metadata__link").Select(link => link.TextContent.Trim()));
        Assert.Empty(followed);
    }

    [Fact]
    public async Task A_remark_lands_in_the_margin_against_the_block_it_was_asked_for()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid^='markdown-comment-']")));

        // The affordance on a block opens a fresh remark straight into its own
        // textarea. Block 2 is the prose under the heading: block 1 is the `meta`
        // fence, which has no row of its own because the heading above it drew it.
        component.Find("[data-testid='markdown-comment-2']").Click();

        component.WaitForAssertion(() => Assert.Single(component.FindAll(".md-block-row[data-block='2'] .md-comment")));

        // Beside the prose rather than pushed into it: a chapter under review has
        // to keep reading as a chapter.
        Assert.Single(component.FindAll("[data-testid='domain-chapter-file-body'] .md-view--margin"));
    }

    [Fact]
    public async Task A_new_remark_opens_straight_into_its_own_textarea()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid^='markdown-comment-']")));

        component.Find("[data-testid='markdown-comment-2']").Click();

        // No second press on Edit: the affordance and the box to type into are
        // the same act now, so the box is already there and the read-mode Edit
        // button for this remark is not.
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".md-comment__edit textarea")));
        Assert.Empty(component.FindAll(".md-block-row[data-block='2'] .md-comment__actions"));

        var textarea = component.Find(".md-comment__edit textarea");
        textarea.Input("Worth flagging before this ships.");
        component.Find(".md-comment__edit-actions [data-testid^='markdown-comment-save-']").Click();

        component.WaitForAssertion(() =>
            Assert.Contains("Worth flagging before this ships.", component.Find(".md-block-row[data-block='2'] .md-comment__body").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancelling_a_fresh_remark_removes_it_rather_than_leaving_it_blank()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid^='markdown-comment-']")));

        component.Find("[data-testid='markdown-comment-2']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".md-comment__edit-actions [data-testid^='markdown-comment-cancel-']")));

        component.Find(".md-comment__edit-actions [data-testid^='markdown-comment-cancel-']").Click();

        component.WaitForAssertion(() => Assert.Empty(component.FindAll(".md-block-row[data-block='2'] .md-comment")));
    }

    [Fact]
    public async Task A_saved_remark_can_be_deleted_outright()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(ContextMapPath);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid^='markdown-comment-']")));

        component.Find("[data-testid='markdown-comment-2']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".md-comment__edit textarea")));
        component.Find(".md-comment__edit textarea").Input("Say which team owns this.");
        component.Find(".md-comment__edit-actions [data-testid^='markdown-comment-save-']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".md-block-row[data-block='2'] [data-testid^='markdown-comment-delete-']")));

        component.Find(".md-block-row[data-block='2'] [data-testid^='markdown-comment-delete-']").Click();

        component.WaitForAssertion(() => Assert.Empty(component.FindAll(".md-block-row[data-block='2'] .md-comment")));
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

        // The document's own dropdown, which is the only one on the panel: it is in
        // the file view's header, and the header is what stays on screen while the
        // body is being typed into.
        component.Find(".file-view__header .knowledge-record__headline select").Change("accepted");

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
                    ["related"] = "[.domain/tasks/domain.md#domain-event-aiworklogged, .domain/tasks/domain.md#domain-event-entrycompleted]"
                },
                "A summary nobody can edit.",
                [],
                [],
                []),
            []);
    }

    /// <summary>A context map whose relations leave the domain folder: one into the
    /// architecture section, and one into a folder this pane has no section for. The
    /// two answers a reference can have — somewhere to go, and nowhere — beside each
    /// other in one strip.</summary>
    private static DomainKnowledgeView CrossAreaView()
    {
        var absent = Path.Combine(Path.GetTempPath(), "backlog-domain-panel-cross-area", Guid.NewGuid().ToString("N"));

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
                    ["related"] = "[.arc42/03-context-and-scope.md, .github/copilot-instructions.md]"
                },
                "A summary nobody can edit.",
                [],
                [],
                []),
            []);
    }

    /// <summary>
    /// The badge used to be born open: <c>badge--gh-open</c> was a literal, so a
    /// closed issue was still drawn as an open one. Nothing on this path knows the
    /// state - <see cref="KnowledgeIssueLink"/> carries a repo, a number and a
    /// label and no more - so the fix is not to derive the state but to stop
    /// claiming it. Asserted as the absence of every state class rather than the
    /// presence of one, because that is the property that matters: whichever
    /// treatment the unknown case wears, the badge must not assert a state.
    /// </summary>
    [Fact]
    public async Task A_linked_issue_is_not_drawn_as_an_open_one()
    {
        await using var harness = CreateHarness();

        var component = harness.RenderView(IssueLinkedView(), ContextMapPath);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='knowledge-issue-badge']")));

        Assert.All(
            component.FindAll("[data-testid='knowledge-issue-badge']"),
            badge =>
            {
                var classes = badge.GetAttribute("class") ?? string.Empty;
                Assert.Contains("badge--gh", classes, StringComparison.Ordinal);
                Assert.DoesNotContain("badge--gh-open", classes, StringComparison.Ordinal);
                Assert.DoesNotContain("badge--gh-closed", classes, StringComparison.Ordinal);
                Assert.DoesNotContain("badge--gh-merged", classes, StringComparison.Ordinal);

                // The visible word was always honest about this - it says the issue
                // is linked, not that it is open - and it stays that way.
                Assert.Equal("linked", badge.QuerySelector(".badge__state")!.TextContent.Trim());
            });
    }

    /// <summary>A context map whose metadata links a GitHub issue. The link is what
    /// the badge is drawn from, and this is the only fixture that carries one, so a
    /// test about the badge does not have to reach through a folder on disk to get
    /// a fence written.</summary>
    private static DomainKnowledgeView IssueLinkedView()
    {
        var absent = Path.Combine(Path.GetTempPath(), "backlog-domain-panel-issue", Guid.NewGuid().ToString("N"));

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
                    ["issue"] = "42"
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

        /// <summary>A view handed in rather than read, optionally with the host that
        /// takes the panel's requests to open another chapter. Without one the panel
        /// is the standalone Domain page, which has no pane to ask.</summary>
        public IRenderedComponent<DomainKnowledgePanel> RenderView(
            DomainKnowledgeView view,
            string? selectedPath,
            Action<KnowledgeChapterLink>? onNavigate = null) =>
            Context.Render<DomainKnowledgePanel>(parameters =>
            {
                parameters
                    .Add(panel => panel.View, view)
                    .Add(panel => panel.SelectedPath, selectedPath);

                if (onNavigate is not null)
                {
                    parameters.Add(panel => panel.OnNavigateToChapter, onNavigate);
                }
            });

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
