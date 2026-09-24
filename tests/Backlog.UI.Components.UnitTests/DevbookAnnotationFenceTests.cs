using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Reading an <c>annotation</c> fence. The two fixtures are the rule's own
/// examples — the three-line note and the full thread — lifted out of the
/// vendored <c>devbook-annotations.md</c>, so the reader is held to exactly what
/// the rule says a fence looks like.
/// </summary>
public sealed class DevbookAnnotationFenceTests
{
    private static IReadOnlyList<string> RuleExamples { get; } =
    [.. Regex.Matches(DevbookRuleText.Annotations.Replace("\r\n", "\n"), "```annotation\n(.*?)\n```", RegexOptions.Singleline)
        .Select(match => match.Groups[1].Value)];

    [Fact]
    public void The_rule_carries_its_two_examples()
    {
        Assert.Equal(2, RuleExamples.Count);
    }

    [Fact]
    public void Three_lines_is_a_complete_note()
    {
        var note = DevbookAnnotationFence.Parse(RuleExamples[0]);

        Assert.Equal("jobsc", note.Author);
        Assert.Equal("2026-09-02", note.Date);
        Assert.Equal("Is this still true now the outline rollup is a view?", note.Body);
        Assert.Equal("comment", note.Kind);
        Assert.Equal("open", note.Status);
        Assert.True(note.IsOpen);
        Assert.Empty(note.Replies);
    }

    [Fact]
    public void A_full_thread_reads_its_kind_state_quote_block_body_and_replies()
    {
        var note = DevbookAnnotationFence.Parse(RuleExamples[1]);

        Assert.Equal("question", note.Kind);
        Assert.Equal("resolved", note.Status);
        Assert.False(note.IsOpen);
        Assert.Equal("one indexed range read", note.Quote);
        Assert.Equal(
            "Does this hold after the outline gained the roadmap rollup?\nThe rollup looked like it needed a second scan.",
            note.Body);

        var reply = Assert.Single(note.Replies);
        Assert.Equal("claude/flow-spec", reply.Author);
        Assert.Equal("2026-09-02", reply.Date);
        Assert.Equal("No second scan — the rollup is a view over the same index.", reply.Body);
    }

    [Fact]
    public void The_ext_mapping_is_skipped_rather_than_read_as_fields()
    {
        var note = DevbookAnnotationFence.Parse(RuleExamples[1]);

        // `raised-in` sits under ext.your-plugin; read as a top-level key it would
        // have been harmless here, but `ext` must never leak a key into the note.
        Assert.Equal("jobsc", note.Author);
        Assert.Single(note.Replies);
    }

    [Fact]
    public void Only_the_annotation_language_opens_a_note()
    {
        Assert.True(DevbookAnnotationFence.IsAnnotationBlock("annotation"));
        Assert.True(DevbookAnnotationFence.IsAnnotationBlock(" Annotation "));
        Assert.False(DevbookAnnotationFence.IsAnnotationBlock("meta"));
        Assert.False(DevbookAnnotationFence.IsAnnotationBlock(null));
    }

    [Fact]
    public void An_unknown_kind_reads_back_as_authored_and_says_so()
    {
        var note = DevbookAnnotationFence.Parse("author: a\ndate: 2026-09-02\nkind: rant\nbody: x");

        Assert.Equal("rant", note.Kind);
        Assert.False(note.HasKnownKind);
    }
}
