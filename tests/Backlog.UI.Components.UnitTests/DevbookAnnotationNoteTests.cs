namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// One <c>annotation</c> fence drawn as the review note it is: labelled as one,
/// its kind and state stated, its thread in order — and nothing on it that
/// could write the fence, because the app never does.
/// </summary>
public sealed class DevbookAnnotationNoteTests
{
    private static IRenderedComponent<DevbookAnnotationNote> Render(BunitContext context, string fenceBody) =>
        context.Render<DevbookAnnotationNote>(parameters => parameters
            .Add(note => note.Note, DevbookAnnotationFence.Parse(fenceBody)));

    [Fact]
    public void The_three_line_note_is_an_open_comment_labelled_as_a_review_note()
    {
        using var context = new BunitContext();

        var note = Render(context, DevbookAnnotationSamples.RuleExamples[0]);

        var root = note.Find("[data-testid='devbook-annotation-note']");
        Assert.Equal("note", root.GetAttribute("role"));
        Assert.Contains("devbook-note--open", root.ClassList);
        Assert.Equal("Review note, comment, open", root.GetAttribute("aria-label"));
        Assert.Equal("Review note", note.Find(".devbook-note__label").TextContent);
        Assert.Equal("comment", note.Find("[data-testid='devbook-annotation-note-kind']").TextContent);
        Assert.Equal("Open", note.Find("[data-testid='devbook-annotation-note-status']").TextContent);
        Assert.Equal("jobsc", note.Find("[data-testid='devbook-annotation-note-author']").TextContent);
        Assert.Equal("2026-09-02", note.Find("[data-testid='devbook-annotation-note-date']").GetAttribute("datetime"));
        Assert.Equal(
            "Is this still true now the outline rollup is a view?",
            note.Find("[data-testid='devbook-annotation-note-body']").TextContent.Trim());
        Assert.Empty(note.FindAll("[data-testid='devbook-annotation-note-quote']"));
        Assert.Empty(note.FindAll("[data-testid='devbook-annotation-note-replies']"));
    }

    [Fact]
    public void A_resolved_question_is_subdued_and_carries_its_quote_and_reply()
    {
        using var context = new BunitContext();

        var note = Render(context, DevbookAnnotationSamples.RuleExamples[1]);

        var root = note.Find("[data-testid='devbook-annotation-note']");
        Assert.Contains("devbook-note--resolved", root.ClassList);
        Assert.DoesNotContain("devbook-note--open", root.ClassList);
        Assert.Contains("badge--note-state-resolved", note.Find("[data-testid='devbook-annotation-note-status']").ClassList);
        Assert.Equal("Resolved", note.Find("[data-testid='devbook-annotation-note-status']").TextContent);
        Assert.Contains("badge--note-kind-question", note.Find("[data-testid='devbook-annotation-note-kind']").ClassList);
        Assert.Equal("one indexed range read", note.Find("[data-testid='devbook-annotation-note-quote']").TextContent);

        // The block scalar is Markdown: its two lines are one paragraph, as they
        // would be anywhere else in the chapter.
        Assert.Equal(
            "Does this hold after the outline gained the roadmap rollup? The rollup looked like it needed a second scan.",
            note.Find("[data-testid='devbook-annotation-note-body'] p").TextContent);

        var reply = note.Find("[data-testid='devbook-annotation-note-reply-0']");
        Assert.Equal("claude/flow-spec", reply.QuerySelector(".devbook-note__author")!.TextContent);
        Assert.Equal("2026-09-02", reply.QuerySelector(".devbook-note__date")!.TextContent);
        Assert.Equal("No second scan — the rollup is a view over the same index.", reply.QuerySelector(".devbook-note__body")!.TextContent.Trim());
    }

    [Fact]
    public void An_unknown_kind_is_shown_as_authored_and_flagged()
    {
        using var context = new BunitContext();

        var note = Render(context, "author: a\ndate: 2026-09-02\nkind: rant\nbody: x");

        var kind = note.Find("[data-testid='devbook-annotation-note-kind']");
        Assert.Equal("rant", kind.TextContent);
        Assert.Contains("badge--note-kind-unknown", kind.ClassList);
        Assert.False(string.IsNullOrEmpty(kind.GetAttribute("title")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("this is not yaml at all")]
    public void An_empty_or_unreadable_fence_is_still_a_note_shell_and_never_a_listing(string body)
    {
        using var context = new BunitContext();

        var note = Render(context, body);

        Assert.Equal("Review note", note.Find(".devbook-note__label").TextContent);
        Assert.Equal("This note says nothing yet.", note.Find(".devbook-note__none").TextContent);
        Assert.Empty(note.FindAll("pre"));
    }

    [Fact]
    public void A_code_sample_in_the_body_is_drawn_as_code_inside_the_note()
    {
        using var context = new BunitContext();
        var fence = DevbookAnnotationSamples.NoteWithCodeSample.Split('\n');

        // The fence's inside, as the parser hands it over: everything between
        // the four-backtick lines.
        var note = Render(context, string.Join('\n', fence[1..^1]));

        var body = note.Find("[data-testid='devbook-annotation-note-body']");
        Assert.Equal(DevbookAnnotationSamples.CodeSampleLine, body.QuerySelector("pre.md-code code")!.TextContent);
        Assert.Contains(DevbookAnnotationSamples.CodeSampleLead, body.TextContent, StringComparison.Ordinal);
        Assert.Contains(DevbookAnnotationSamples.CodeSampleTail, body.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_note_offers_nothing_to_press_because_the_app_never_writes_a_fence()
    {
        using var context = new BunitContext();

        var note = Render(context, DevbookAnnotationSamples.RuleExamples[1]);

        Assert.Empty(note.FindAll("button, input, textarea, select"));
    }

    [Fact]
    public void A_note_does_not_wear_the_margin_remarks_classes()
    {
        // Local ADR 0011: the person's remark and the repository's note are two
        // things, and a reader must be able to tell them apart on one page.
        using var context = new BunitContext();

        var note = Render(context, DevbookAnnotationSamples.RuleExamples[1]);

        Assert.DoesNotContain("md-comment", note.Markup, StringComparison.Ordinal);
    }
}
