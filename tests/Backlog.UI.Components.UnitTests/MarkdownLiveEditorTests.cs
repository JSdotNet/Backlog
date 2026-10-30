using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The live editor as a reader meets it: every block rendered, one of them open
/// as its own source, and the whole document raised on every keystroke.
/// <para>
/// The splitting and splicing rules are <c>MarkdownLiveStateTests</c>' and
/// <c>MarkdownSourceBlocksTests</c>'. What is pinned here is what the component
/// draws from them and what it raises: which block is a textarea, what that
/// textarea holds, and which string reaches the host. The keys at a block's edge
/// are the browser's to read, so they are driven here through the methods the
/// script calls, and the last test holds the two halves to the same names.
/// </para>
/// </summary>
public sealed class MarkdownLiveEditorTests
{
    /// <summary>One of every block the editor draws, a blank line apart — in
    /// <c>\n</c> line ends whatever the checkout wrote this file with, because the
    /// assertions below spell the expected source out in them.</summary>
    private static readonly string Document = RawDocument.ReplaceLineEndings("\n");

    private const string RawDocument = """
        # Live editing

        A paragraph that wraps
        over two lines.

        - [ ] Write the splitter
        - [x] Draw the blocks

        1. First
        2. Second

        > A quote.

        | Name | State |
        | ---- | ----- |
        | One  | Open  |

        ```csharp
        var x = 1;
        ```

        ```mermaid
        graph TD
          A --> B
        ```

        ---
        """;

    private const int BlockCount = 9;

    private static IRenderedComponent<MarkdownLiveEditor> Render(
        BunitContext context,
        string? value,
        Action<string>? changed = null,
        bool disabled = false)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<MarkdownLiveEditor>(parameters => parameters
            .Add(editor => editor.Value, value)
            .Add(editor => editor.ValueChanged, (string text) => changed?.Invoke(text))
            .Add(editor => editor.Disabled, disabled));
    }

    /// <summary>The rendered block whose text contains <paramref name="text"/>.</summary>
    private static AngleSharp.Dom.IElement BlockWith(IRenderedComponent<MarkdownLiveEditor> view, string text) =>
        view.FindAll("[data-live-block]").Single(block => block.TextContent.Contains(text, StringComparison.Ordinal));

    private static string? SourceValue(IRenderedComponent<MarkdownLiveEditor> view) =>
        view.Find("textarea").GetAttribute("value");

    [Fact]
    public void With_no_block_open_every_block_is_rendered_and_there_is_no_textarea()
    {
        using var context = new BunitContext();
        var view = Render(context, Document);

        Assert.Empty(view.FindAll("textarea"));
        Assert.Equal(BlockCount, view.FindAll("[data-live-block]").Count);

        Assert.Single(view.FindAll("p.md-heading"));
        Assert.Single(view.FindAll("p.md-p"));
        Assert.Equal(2, view.FindAll("ul.md-list, ol.md-list").Count);
        Assert.Equal(2, view.FindAll("button.md-check").Count);
        Assert.Single(view.FindAll("blockquote.md-quote"));
        Assert.Single(view.FindAll("table"));
        Assert.Single(view.FindAll("pre.md-code"));
        Assert.Single(view.FindAll("[data-testid='diagram-view']"));
        Assert.Single(view.FindAll("hr.md-divider"));
    }

    /// <summary>
    /// A rendered block is a tab stop that says what it is and what Enter does —
    /// it is not a native control, so nothing else would.
    /// </summary>
    [Fact]
    public void A_rendered_block_is_a_named_tab_stop()
    {
        using var context = new BunitContext();
        var view = Render(context, Document);

        var table = BlockWith(view, "Name");

        Assert.Equal("0", table.GetAttribute("tabindex"));
        Assert.Equal("Table. Press Enter to edit its Markdown.", table.GetAttribute("aria-label"));
        Assert.Equal("Diagram. Press Enter to edit its Markdown.", BlockWith(view, "Rendering").GetAttribute("aria-label"));
        Assert.Equal("Task list. Press Enter to edit its Markdown.", BlockWith(view, "Write the splitter").GetAttribute("aria-label"));
    }

    /// <summary>
    /// Footnote definitions are lines the parser lifts out of the flow, so they are
    /// no block: they are drawn as the source they are and said to be source. The
    /// notes they define are drawn once, at the end, numbered from the whole
    /// document.
    /// </summary>
    [Fact]
    public void Lines_no_block_claims_render_as_labelled_raw_markdown()
    {
        using var context = new BunitContext();
        var view = Render(context, "A claim.[^1]\n\n[^1]: The evidence.\n");

        var raw = view.Find(".markdown-live__block--raw");
        Assert.StartsWith("Raw Markdown", raw.GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Equal("Raw Markdown", raw.QuerySelector(".markdown-live__raw-label")!.TextContent);
        Assert.Equal("[^1]: The evidence.", raw.QuerySelector("pre.markdown-live__raw")!.TextContent);

        Assert.Contains("The evidence.", view.Find(".markdown-live__notes").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// A block is every line that belongs together, so a click anywhere in it
    /// opens all of them — the wrapped paragraph's two lines, the table's three,
    /// the whole list, the fence from fence to fence — and nothing else opens.
    /// </summary>
    [Theory]
    [InlineData("wraps", "A paragraph that wraps\nover two lines.")]
    [InlineData("Name", "| Name | State |\n| ---- | ----- |\n| One  | Open  |")]
    [InlineData("Write the splitter", "- [ ] Write the splitter\n- [x] Draw the blocks")]
    [InlineData("var x", "```csharp\nvar x = 1;\n```")]
    [InlineData("Rendering", "```mermaid\ngraph TD\n  A --> B\n```")]
    public void Clicking_a_block_opens_exactly_its_own_source(string inBlock, string source)
    {
        using var context = new BunitContext();
        var view = Render(context, Document);

        BlockWith(view, inBlock).Click();

        Assert.Single(view.FindAll("textarea"));
        Assert.Equal(source, SourceValue(view));
        Assert.Equal(BlockCount - 1, view.FindAll("[data-live-block]").Count);
    }

    /// <summary>Enter and Space, the two keys that activate anything — a rendered
    /// block is not a native control, so it answers both itself.</summary>
    [Theory]
    [InlineData("Enter")]
    [InlineData(" ")]
    public void Enter_or_space_on_a_block_opens_it(string key)
    {
        using var context = new BunitContext();
        var view = Render(context, Document);

        BlockWith(view, "A quote").KeyDown(new KeyboardEventArgs { Key = key });

        Assert.Equal("> A quote.", SourceValue(view));
    }

    /// <summary>A rendered block as a host with a handler for its references
    /// draws it: the reference is a button inside the paragraph.</summary>
    private static IRenderedComponent<MarkdownLiveEditor> RenderWithReference(
        BunitContext context,
        List<DevbookReference> followed,
        bool script)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        if (script) context.JSInterop.Setup<bool>("backlogMarkdownLive.watch", _ => true).SetResult(true);

        return context.Render<MarkdownLiveEditor>(parameters => parameters
            .Add(editor => editor.Value, "See `.domain/sessions/domain.md#invariants` for the rest.\n\n> A quote.")
            .Add(editor => editor.InlineTarget, DevbookInlineTargets.For(
                null,
                null,
                EventCallback.Factory.Create<DevbookReference>(new object(), followed.Add))));
    }

    /// <summary>
    /// A control inside a rendered block — a reference a host can follow, a
    /// diagram's own buttons — is the control's, not the block's. With the
    /// script running, the script decides from the click's target whether the
    /// block opens, so the reference is followed and the paragraph stays
    /// rendered under it.
    /// </summary>
    [Fact]
    public void With_the_script_a_button_inside_a_block_is_pressed_and_the_block_stays_rendered()
    {
        using var context = new BunitContext();
        var followed = new List<DevbookReference>();
        var view = RenderWithReference(context, followed, script: true);

        view.Find("button.devbook-ref--action").Click();
        view.Find("button.devbook-ref--action").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Single(followed);
        Assert.Empty(view.FindAll("textarea"));
    }

    /// <summary>And the script's own call opens the block it names, the way the
    /// click did before the script was there to tell the two apart.</summary>
    [Fact]
    public async Task With_the_script_a_block_is_opened_by_the_script()
    {
        using var context = new BunitContext();
        var view = RenderWithReference(context, [], script: true);

        BlockWith(view, "A quote").Click();
        Assert.Empty(view.FindAll("textarea"));

        await view.Instance.ActivateAsync(int.Parse(BlockWith(view, "A quote").GetAttribute("data-live-block")!));

        Assert.Equal("> A quote.", SourceValue(view));
    }

    /// <summary>Without it — prerendered, or a script that failed to load — the
    /// block's own click is the only way in, and it still opens.</summary>
    [Fact]
    public void Without_the_script_a_click_still_opens_the_block()
    {
        using var context = new BunitContext();
        var view = RenderWithReference(context, [], script: false);

        BlockWith(view, "A quote").Click();

        Assert.Equal("> A quote.", SourceValue(view));
    }

    /// <summary>
    /// Every keystroke raises the whole document, spliced: the lines above and
    /// below byte for byte. The blocks around the one being typed in hold still
    /// until the caret leaves — the document is not cut again under it.
    /// </summary>
    [Fact]
    public void Typing_raises_the_whole_source_and_leaves_the_other_blocks_alone()
    {
        using var context = new BunitContext();
        string? raised = null;
        var view = Render(context, Document, text => raised = text);

        // The block's contents rather than the block: the wrapper carries the
        // renderer's handler ids, and those are renumbered on every render.
        var quoteBefore = BlockWith(view, "A quote").InnerHtml;

        BlockWith(view, "wraps").Click();
        view.Find("textarea").Input("A rewritten paragraph.");

        Assert.Equal(
            Document.Replace("A paragraph that wraps\nover two lines.", "A rewritten paragraph.", StringComparison.Ordinal),
            raised);
        Assert.Equal(quoteBefore, BlockWith(view, "A quote").InnerHtml);
    }

    /// <summary>
    /// Half a fence typed into a paragraph would, cut at once, swallow every block
    /// after it. Cut when the caret leaves, it is what was typed — and a region
    /// typed as two paragraphs is two blocks from then on.
    /// </summary>
    [Fact]
    public async Task A_region_typed_as_two_paragraphs_renders_as_two_once_the_caret_leaves()
    {
        using var context = new BunitContext();
        var view = Render(context, Document);

        BlockWith(view, "wraps").Click();
        view.Find("textarea").Input("A\n\nB");

        // Still one box: nothing is cut while the caret is in it.
        Assert.Single(view.FindAll("textarea"));
        Assert.Empty(view.FindAll("p.md-p"));

        await view.Instance.CloseAsync();

        Assert.Empty(view.FindAll("textarea"));
        Assert.Equal(["A", "B"], view.FindAll("p.md-p").Select(paragraph => paragraph.TextContent));
    }

    [Fact]
    public async Task Escape_closes_the_block_and_cuts_the_document_again()
    {
        using var context = new BunitContext();
        var view = Render(context, Document);

        BlockWith(view, "A quote").Click();
        view.Find("textarea").Input("> One\n\n> Two");
        await view.Instance.LeaveAsync();

        Assert.Empty(view.FindAll("textarea"));
        Assert.Equal(2, view.FindAll("blockquote.md-quote").Count);
    }

    /// <summary>
    /// A task's checkbox is the task's, not the block's: ticking it rewrites that
    /// one line through the same toggle the read view uses, and opens nothing.
    /// </summary>
    [Fact]
    public void Ticking_a_task_raises_the_toggled_source_and_opens_nothing()
    {
        using var context = new BunitContext();
        string? raised = null;
        var view = Render(context, Document, text => raised = text);

        view.FindAll("button.md-check")[0].Click();

        Assert.Equal(MarkdownPreview.ToggleTask(Document, 0), raised);
        Assert.Empty(view.FindAll("textarea"));
        Assert.Equal("true", view.FindAll("button.md-check")[0].GetAttribute("aria-checked"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void An_empty_document_is_one_empty_box_ready_to_type_in(string? value)
    {
        using var context = new BunitContext();
        string? raised = null;
        var view = Render(context, value, text => raised = text);

        Assert.Single(view.FindAll("textarea"));
        Assert.Equal(string.Empty, SourceValue(view));
        Assert.Empty(view.FindAll("[data-live-block]"));

        view.Find("textarea").Input("# A title");
        Assert.Equal("# A title", raised);
    }

    [Fact]
    public void Leaving_the_box_raises_OnBlur()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var blurred = 0;
        var view = context.Render<MarkdownLiveEditor>(parameters => parameters
            .Add(editor => editor.Value, Document)
            .Add(editor => editor.OnBlur, () => blurred++));

        BlockWith(view, "wraps").Click();
        view.Find("textarea").Blur();

        Assert.Equal(1, blurred);
    }

    [Fact]
    public void Disabled_renders_every_block_and_opens_none()
    {
        using var context = new BunitContext();
        var view = Render(context, Document, disabled: true);

        BlockWith(view, "wraps").Click();

        Assert.Empty(view.FindAll("textarea"));
        Assert.Contains("markdown-live--disabled", view.Find(".markdown-live").ClassList);
        Assert.All(view.FindAll("[data-live-block]"), block => Assert.Null(block.GetAttribute("tabindex")));
        Assert.Empty(view.FindAll("button.md-check"));
    }

    /// <summary>
    /// The arrow at a block's edge, as the script reports it: the next block opens
    /// with the caret at its start, the one above with the caret at its end, and
    /// at the last block nothing moves.
    /// </summary>
    [Fact]
    public async Task A_step_opens_the_neighbouring_block_and_stops_at_the_last()
    {
        using var context = new BunitContext();
        var view = Render(context, Document);

        BlockWith(view, "A quote").Click();

        Assert.True(await view.Instance.StepAsync(1));
        Assert.StartsWith("| Name | State |", SourceValue(view), StringComparison.Ordinal);
        AssertFocusedAt(context, 0);

        Assert.True(await view.Instance.StepAsync(-1));
        Assert.Equal("> A quote.", SourceValue(view));
        AssertFocusedAt(context, "> A quote.".Length);

        view.FindAll("[data-live-block]")[^1].Click();
        Assert.False(await view.Instance.StepAsync(1));
        Assert.Equal("---", SourceValue(view));
    }

    /// <summary>Backspace at the start of a block grows the box over the block
    /// above and the blank line between, so the next Backspace takes the break
    /// away. Nothing is raised: the source has not changed yet.</summary>
    [Fact]
    public async Task A_join_grows_the_box_over_the_block_above()
    {
        using var context = new BunitContext();
        var raised = 0;
        var view = Render(context, Document, _ => raised++);

        BlockWith(view, "A quote").Click();
        await view.Instance.JoinAsync(-1);

        Assert.Equal("1. First\n2. Second\n\n> A quote.", SourceValue(view));
        Assert.Equal(BlockCount - 2, view.FindAll("[data-live-block]").Count);
        Assert.Equal(0, raised);
        AssertFocusedAt(context, "1. First\n2. Second\n\n".Length);
    }

    /// <summary>
    /// A host that stores the body through a parser hands it back trimmed. That is
    /// the editor's own text coming back, and the block being typed in stays
    /// open; any other value is a new document.
    /// </summary>
    [Fact]
    public void A_trimmed_echo_keeps_the_block_open_and_a_new_value_closes_it()
    {
        using var context = new BunitContext();
        var stored = Document;
        var view = Render(context, Document, text => stored = text.Trim('\n'));

        // The last block, so the blank line typed after it is the document's edge
        // and the host trims it away.
        view.FindAll("[data-live-block]")[^1].Click();
        view.Find("textarea").Input("---\n");
        Assert.Equal(Document, stored);

        view.Render(parameters => parameters.Add(editor => editor.Value, stored));

        Assert.Equal("---\n", SourceValue(view));

        view.Render(parameters => parameters.Add(editor => editor.Value, "Something else."));

        Assert.Empty(view.FindAll("textarea"));
        Assert.Equal("Something else.", view.Find("p.md-p").TextContent);
    }

    /// <summary>
    /// The two halves name each other by string, and a rename on one side fails
    /// nowhere but in a browser: the arrows simply stop crossing blocks. So every
    /// method the script calls is one the component offers, and every one it
    /// offers is called.
    /// </summary>
    [Fact]
    public void The_script_calls_exactly_the_methods_the_editor_offers()
    {
        var script = ComponentsScript.Region("window.backlogMarkdownLive", "window.backlogClipboard");

        // Every method name the script spells out — called directly, through a
        // move, or picked by a key — is quoted and ends in Async.
        var called = Regex.Matches(script, @"'(\w+Async)'")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var offered = typeof(MarkdownLiveEditor)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.GetCustomAttribute<JSInvokableAttribute>() is not null)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(offered);
        Assert.Equal(offered.Order(), called.Order());
    }

    /// <summary>A key is only taken at the box's edge: the arrows only with the
    /// caret on the first or last line and nothing selected, and never with a
    /// modifier, so Shift+Arrow still extends a selection.</summary>
    [Fact]
    public void The_script_takes_a_key_only_at_the_edge_and_never_with_a_modifier()
    {
        var script = ComponentsScript.Region("window.backlogMarkdownLive", "window.backlogClipboard");

        Assert.Contains("const modified = event.altKey || event.ctrlKey || event.metaKey || event.shiftKey;", script, StringComparison.Ordinal);
        Assert.Contains("if (modified) return;", script, StringComparison.Ordinal);
        Assert.Contains("if (!collapsed || !firstLine) return;", script, StringComparison.Ordinal);
        Assert.Contains("if (!collapsed || value.indexOf('\\n', end) !== -1) return;", script, StringComparison.Ordinal);
        Assert.Contains("if (!collapsed || start !== 0) return;", script, StringComparison.Ordinal);
        Assert.Contains("if (!collapsed || end !== value.length) return;", script, StringComparison.Ordinal);
    }

    /// <summary>The open box holds markdown — a fence, a raw block, a table's
    /// pipes — so it is never spellchecked: red under source is noise.</summary>
    [Fact]
    public void The_open_box_is_not_spellchecked()
    {
        using var context = new BunitContext();
        var view = Render(context, Document);

        BlockWith(view, "var x").Click();

        Assert.Equal("false", view.Find("textarea").GetAttribute("spellcheck"));
    }

    /// <summary>
    /// Undo is the document's. The edit was made in the paragraph and the caret
    /// has gone since; undo raises the source as it was and reopens the
    /// paragraph with the caret at the end of what came back, and redo puts the
    /// edit back.
    /// </summary>
    [Fact]
    public async Task Undo_and_redo_raise_the_whole_source_and_reopen_the_block_the_edit_was_in()
    {
        using var context = new BunitContext();
        string? raised = null;
        var view = Render(context, Document, text => raised = text);

        BlockWith(view, "wraps").Click();
        view.Find("textarea").Input("A rewritten paragraph.");
        var edited = raised;
        await view.Instance.CloseAsync();

        Assert.True(await view.Instance.UndoAsync());
        Assert.Equal(Document, raised);
        Assert.Equal("A paragraph that wraps\nover two lines.", SourceValue(view));

        // Where the edit ended in the text that came back: the full stop both
        // versions end on was never part of it.
        AssertFocusedAt(context, "A paragraph that wraps\nover two lines".Length);

        Assert.True(await view.Instance.RedoAsync());
        Assert.Equal(edited, raised);
        Assert.Equal("A rewritten paragraph.", SourceValue(view));
    }

    /// <summary>A tick is undone like any edit, and opens nothing; the script
    /// is told no box follows, so it does not wait for one.</summary>
    [Fact]
    public async Task A_ticked_task_is_undone_and_opens_nothing()
    {
        using var context = new BunitContext();
        string? raised = null;
        var view = Render(context, Document, text => raised = text);

        view.FindAll("button.md-check")[0].Click();

        Assert.False(await view.Instance.UndoAsync());
        Assert.Equal(Document, raised);
        Assert.Empty(view.FindAll("textarea"));
        Assert.Equal("false", view.FindAll("button.md-check")[0].GetAttribute("aria-checked"));
    }

    /// <summary>
    /// A tick re-renders the list — its source changed, so its block is drawn
    /// anew — and the checkbox that had focus is gone with it. Focus goes to the
    /// same task's checkbox in the new list, so the keyboard stays in the editor
    /// and the next Ctrl+Z has somewhere to land. A mouse tick, which never moved
    /// focus at all, ends there too.
    /// </summary>
    [Fact]
    public void Ticking_a_task_puts_focus_on_its_checkbox()
    {
        using var context = new BunitContext();
        var view = Render(context, Document);

        view.FindAll("button.md-check")[1].Click();

        AssertFocusedOnTask(context, BlockWith(view, "Write the splitter").Id!, 1);
    }

    /// <summary>Undoing a tick, or redoing it, opens no block, and puts focus
    /// back on the task it ticked — so a second Ctrl+Z still has the keyboard.</summary>
    [Fact]
    public async Task Undoing_a_tick_puts_focus_back_on_its_checkbox()
    {
        using var context = new BunitContext();
        var view = Render(context, Document);

        view.FindAll("button.md-check")[0].Click();
        await view.Instance.UndoAsync();
        AssertFocusedOnTask(context, BlockWith(view, "Write the splitter").Id!, 0);

        await view.Instance.RedoAsync();
        AssertFocusedOnTask(context, BlockWith(view, "Write the splitter").Id!, 0);
    }

    /// <summary>The script's focus takes a task's position in its block and
    /// lands on that checkbox, falling back to the block.</summary>
    [Fact]
    public void The_script_focuses_a_task_checkbox_inside_its_block()
    {
        var script = ComponentsScript.Region("focus(id, caret, owner, task) {", "window.backlogClipboard");

        Assert.Contains("element.querySelectorAll('button.md-check')[task] ?? element", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Undo in the editor is the document's, so the script takes the keys from
    /// the box and from a focused block alike — the textarea's own undo would
    /// take back only what was typed since the box last opened — and the
    /// browser's own undo commands as well.
    /// </summary>
    [Fact]
    public void The_script_takes_the_undo_keys_from_the_box_and_from_a_block()
    {
        var script = ComponentsScript.Region("window.backlogMarkdownLive", "window.backlogClipboard");

        Assert.Contains("const history = historyFor(event);", script, StringComparison.Ordinal);
        Assert.Contains("if (key === 'z') return event.shiftKey ? 'RedoAsync' : 'UndoAsync';", script, StringComparison.Ordinal);
        Assert.Contains("if (key === 'y' && event.ctrlKey && !event.shiftKey) return 'RedoAsync';", script, StringComparison.Ordinal);
        Assert.Contains("if (history && ours) {", script, StringComparison.Ordinal);
        Assert.Contains("event.inputType === 'historyUndo'", script, StringComparison.Ordinal);
        Assert.Contains("event.inputType === 'historyRedo'", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Keys typed while a move is in flight are kept, not dropped: printable keys
    /// and Enter go into a buffer, and the buffer goes into the box the move
    /// lands in — or back into the box they were typed in, when nothing moved.
    /// </summary>
    [Fact]
    public void The_script_keeps_the_keys_typed_during_a_move()
    {
        var script = ComponentsScript.Region("window.backlogMarkdownLive", "window.backlogClipboard");

        Assert.Contains("const printable = [...event.key].length === 1 && !event.metaKey && (altGraph || (!event.ctrlKey && !event.altKey));", script, StringComparison.Ordinal);
        Assert.Contains("if (printable || event.key === 'Enter') held += printable ? event.key : '\\n';", script, StringComparison.Ordinal);
        Assert.Contains("box.setRangeText(text, box.selectionStart ?? 0, box.selectionEnd ?? 0, 'end');", script, StringComparison.Ordinal);
        Assert.Contains("box.dispatchEvent(new Event('input', { bubbles: true }));", script, StringComparison.Ordinal);
    }

    /// <summary>A Backspace typed mid-move takes back the last held key, as it
    /// would have in the box, and an AltGr character — Ctrl and Alt together on
    /// Windows — is held as the character it types.</summary>
    [Fact]
    public void The_script_applies_backspace_to_the_held_keys_and_holds_altgr_characters()
    {
        var script = ComponentsScript.Region("window.backlogMarkdownLive", "window.backlogClipboard");

        Assert.Contains("const altGraph = event.ctrlKey && event.altKey && !event.metaKey;", script, StringComparison.Ordinal);
        Assert.Contains("else if (event.key === 'Backspace') held = [...held].slice(0, -1).join('');", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// When an arrow moved nothing — the first or last block — the caret goes
    /// where the key would have taken it before the held keys go back in, and is
    /// measured on the box as it is then. Applied after, it put the keys in the
    /// middle of the line and then moved the caret off them.
    /// </summary>
    [Fact]
    public void When_nothing_moved_the_caret_is_placed_before_the_held_keys_go_back_in()
    {
        var move = ComponentsScript.Region("const move = ", "const open = ");

        var placed = move.IndexOf("if (box && stay) stay(box);", StringComparison.Ordinal);
        var released = move.IndexOf("settle();", placed < 0 ? 0 : placed, StringComparison.Ordinal);
        Assert.True(placed >= 0 && released > placed, "The caret has to be placed before settle() releases the held keys.");

        var keys = ComponentsScript.Region("case 'ArrowUp':", "case 'Backspace':");
        Assert.Contains("move('StepAsync', [-1], (box) => box.setSelectionRange(0, 0));", keys, StringComparison.Ordinal);
        Assert.Contains("move('StepAsync', [1], (box) => box.setSelectionRange(box.value.length, box.value.length));", keys, StringComparison.Ordinal);
        Assert.DoesNotContain(".then((moved)", keys, StringComparison.Ordinal);
    }

    /// <summary>
    /// One move at a time: a move never starts while another is in flight, and so
    /// never lets the other's held keys go — they would land in the old block's
    /// box, still on screen, and splice its text over the region already opened.
    /// A click in that window is ignored.
    /// </summary>
    [Fact]
    public void A_move_never_starts_while_another_is_in_flight()
    {
        var move = ComponentsScript.Region("const move = ", "const open = ");

        Assert.StartsWith("const move = (method, args = [], stay = null) => {\n                if (moving) return Promise.resolve(undefined);", move.Trim(), StringComparison.Ordinal);
        Assert.DoesNotContain("settle();\n                moving = true;", move, StringComparison.Ordinal);

        var click = ComponentsScript.Region("const onClick = ", "const onFocusOut = ");
        Assert.StartsWith("const onClick = (event) => {\n                if (moving) return;", click.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The script opens a block only for a click on the block itself, never for
    /// one on a control inside it — and it never stops an event to do so. A stop
    /// below the document is a stop for every Blazor handler in the tree, the
    /// control's own included, and for the router's link interception.
    /// </summary>
    [Fact]
    public void The_script_opens_a_block_by_the_clicks_target_and_stops_nothing()
    {
        var script = ComponentsScript.Region("const backlogMarkdownLiveControls", "window.backlogClipboard");

        Assert.DoesNotContain("stopPropagation", script, StringComparison.Ordinal);
        Assert.Contains("'a[href]', 'button'", script, StringComparison.Ordinal);
        Assert.Contains("if (!block || onControl(event.target, block)) return;", script, StringComparison.Ordinal);
        Assert.Contains("move('ActivateAsync'", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Focus leaving the editor closes the open block wherever it leaves from —
    /// the box, or a rendered block Tab reached first. The script asks where
    /// focus landed, not what it left.
    /// </summary>
    [Fact]
    public void The_script_closes_the_block_when_focus_leaves_the_editor_from_anywhere_in_it()
    {
        var script = ComponentsScript.Region("const onFocusOut", "root.addEventListener('keydown'");

        Assert.DoesNotContain("isSource(", script, StringComparison.Ordinal);
        Assert.Contains("root.contains(document.activeElement)", script, StringComparison.Ordinal);
        Assert.Contains("call('CloseAsync')", script, StringComparison.Ordinal);
    }

    /// <summary>The empty box's placeholder is editor chrome, and editor chrome
    /// is set in the secondary text colour.</summary>
    [Fact]
    public void The_placeholder_is_set_as_chrome()
    {
        var css = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var rule = Regex.Match(css, @"\n\.markdown-live__input::placeholder \{(?<body>[^}]*)\}");

        Assert.True(rule.Success, "components.css has no rule for .markdown-live__input::placeholder.");
        Assert.Contains("color: var(--color-text-secondary);", rule.Groups["body"].Value, StringComparison.Ordinal);
    }

    /// <summary>The last focus call the editor made went to task
    /// <paramref name="task"/> of the block <paramref name="blockId"/>.</summary>
    private static void AssertFocusedOnTask(BunitContext context, string blockId, int task)
    {
        var focus = context.JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "backlogMarkdownLive.focus")
            .Last();

        Assert.Equal(blockId, focus.Arguments[0]);
        Assert.Equal(task, focus.Arguments[3]);
    }

    /// <summary>The last focus call the editor made put the caret at
    /// <paramref name="caret"/>.</summary>
    private static void AssertFocusedAt(BunitContext context, int caret)
    {
        var focus = context.JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "backlogMarkdownLive.focus")
            .Last();

        Assert.Equal(caret, focus.Arguments[1]);
    }
}
