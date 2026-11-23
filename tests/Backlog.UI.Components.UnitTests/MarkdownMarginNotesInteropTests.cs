using Microsoft.JSInterop;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The margin layout hands its rows to <c>backlogMarginNotes</c> (components.js),
/// which keeps a row from growing to the height of its remarks. What the script
/// does to the page is layout, which bUnit cannot see; what is asserted here is
/// the contract with it: attached once to the view it names, detached when the
/// margin goes or the view does, never asked of a view drawn any other way, and a
/// view that still renders when the script is not there.
/// </summary>
public sealed class MarkdownMarginNotesInteropTests
{
    private const string Attach = "backlogMarginNotes.attach";
    private const string Detach = "backlogMarginNotes.detach";

    private static readonly IReadOnlyList<MdBlock> Blocks =
        MarkdownPreview.ParseDocument("# A heading\n\nA paragraph.\n\n- A bullet");

    private static readonly MarkdownComment[] Remarks =
    [
        new("a1", 1, "In place"),
        new("o1", 99, "Adrift"),
    ];

    private static Action<ComponentParameterCollectionBuilder<MarkdownView>> Drawn(MarkdownCommentLayout layout) =>
        p => p
            .Add(v => v.Blocks, Blocks)
            .Add(v => v.Comments, Remarks)
            .Add(v => v.Layout, layout);

    [Fact]
    public void A_margin_view_is_handed_to_the_script_by_the_id_it_wears()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid(Attach, _ => true).SetVoidResult();

        var view = context.Render<MarkdownView>(Drawn(MarkdownCommentLayout.Margin));

        var id = view.Find(".md-view--margin").GetAttribute("data-md-margin");
        var invocation = Assert.Single(context.JSInterop.Invocations[Attach]);

        Assert.False(string.IsNullOrEmpty(id));
        Assert.Equal(id, invocation.Arguments[0]);

        // Attached once, not told about every render: the script watches the
        // view itself.
        view.Render(Drawn(MarkdownCommentLayout.Margin));
        Assert.Single(context.JSInterop.Invocations[Attach]);
    }

    [Fact]
    public void An_inline_view_wears_no_margin_id_and_never_calls_the_script()
    {
        // Strict, with nothing set up: any call at all would fail the render.
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(Drawn(MarkdownCommentLayout.Inline));

        Assert.Null(view.Find(".md-view").GetAttribute("data-md-margin"));
        Assert.Empty(context.JSInterop.Invocations);
    }

    [Fact]
    public async Task A_margin_view_lets_the_script_go_when_it_is_disposed()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid(Attach, _ => true).SetVoidResult();
        context.JSInterop.SetupVoid(Detach, _ => true).SetVoidResult();

        var view = context.Render<MarkdownView>(Drawn(MarkdownCommentLayout.Margin));
        var id = view.Find(".md-view--margin").GetAttribute("data-md-margin");

        await context.DisposeComponentsAsync();

        Assert.Equal(id, Assert.Single(context.JSInterop.Invocations[Detach]).Arguments[0]);
    }

    [Fact]
    public void A_view_that_stops_drawing_a_margin_lets_the_script_go()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid(Attach, _ => true).SetVoidResult();
        context.JSInterop.SetupVoid(Detach, _ => true).SetVoidResult();

        var view = context.Render<MarkdownView>(Drawn(MarkdownCommentLayout.Margin));
        var id = view.Find(".md-view--margin").GetAttribute("data-md-margin");

        view.Render(Drawn(MarkdownCommentLayout.Inline));

        Assert.Equal(id, Assert.Single(context.JSInterop.Invocations[Detach]).Arguments[0]);
        Assert.Null(view.Find(".md-view").GetAttribute("data-md-margin"));
    }

    [Fact]
    public void Without_the_script_the_margin_still_renders_and_is_not_asked_again()
    {
        // A host that does not load components.js: the rows stretch as they always
        // did, and every remark is still there.
        using var context = new BunitContext();
        context.JSInterop.SetupVoid(Attach, _ => true).SetException(new JSException("backlogMarginNotes is not defined"));

        var view = context.Render<MarkdownView>(Drawn(MarkdownCommentLayout.Margin));

        Assert.NotNull(view.Find(".md-block-row[data-block='1'] [data-testid='markdown-comment-body-a1']"));
        Assert.NotNull(view.Find(".md-block-row[data-block='2'] [data-testid='markdown-comment-body-o1']"));

        view.Render(Drawn(MarkdownCommentLayout.Margin));
        Assert.Single(context.JSInterop.Invocations[Attach]);
    }

    [Theory]
    [InlineData("timed out")]
    [InlineData("no runtime")]
    [InlineData("disconnected")]
    [InlineData("no script")]
    public void An_attach_that_fails_still_renders_and_still_focuses_the_box_that_opened(string how)
    {
        Exception failure = how switch
        {
            "timed out" => new TaskCanceledException("The JS call timed out."),
            "no runtime" => new InvalidOperationException("JavaScript interop calls cannot be issued at this time."),
            "disconnected" => new JSDisconnectedException("The circuit has gone."),
            _ => new JSException("backlogMarginNotes is not defined"),
        };

        // The attach runs first after a render; a failure escaping it took the
        // focus step queued behind it, so the box opened by Edit was never
        // focused. A timed-out call is how the Blazor Server harness fails.
        using var context = new BunitContext();
        context.JSInterop.SetupVoid(Attach, _ => true).SetException(failure);
        context.JSInterop.SetupVoid("backlogFocus", _ => true).SetVoidResult();

        var view = context.Render<MarkdownView>(p => Drawn(MarkdownCommentLayout.Margin)(p
            .Add(v => v.OnEditComment, _ => { })));

        Assert.NotNull(view.Find("[data-testid='markdown-comment-body-a1']"));

        view.Find("[data-testid='markdown-comment-edit-a1']").Click();

        var focus = Assert.Single(context.JSInterop.Invocations["backlogFocus"]);
        Assert.Equal("markdown-comment-editor-a1", focus.Arguments[0]);
        Assert.Single(context.JSInterop.Invocations[Attach]);
    }

    [Fact]
    public async Task A_detach_that_times_out_on_dispose_is_swallowed()
    {
        using var context = new BunitContext();
        context.JSInterop.SetupVoid(Attach, _ => true).SetVoidResult();
        context.JSInterop.SetupVoid(Detach, _ => true).SetException(new TaskCanceledException("The JS call timed out."));

        context.Render<MarkdownView>(Drawn(MarkdownCommentLayout.Margin));

        await context.DisposeComponentsAsync();

        Assert.Single(context.JSInterop.Invocations[Detach]);
    }
}
