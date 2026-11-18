using Backlog.SharedKernel.Markdown;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// A context, <c>#@pc</c>, reads as the tag <c>@pc</c> in the preview, as the entry
/// parser reads it (local ADR 0054 §5).
/// </summary>
public sealed class MarkdownPreviewContextTagTests
{
    [Fact]
    public void A_context_reads_as_one_tag()
    {
        var inlines = MarkdownPreview.ParseInlines("At the desk #@pc and #paper");

        Assert.Equal([new MdText("At the desk "), new MdTag("@pc"), new MdText(" and "), new MdTag("paper")], inlines);
    }

    [Fact]
    public void A_hash_welded_to_a_word_is_still_text()
    {
        var inlines = MarkdownPreview.ParseInlines("see issue#@pc");

        Assert.Equal([new MdText("see issue#@pc")], inlines);
    }
}
