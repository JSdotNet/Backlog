using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Services;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The reading is a heuristic and is pinned as one — the same cases
/// <c>TasksDraftsTests</c> pinned while the Inbox showed backlog drafts, now
/// against the context that owns the question. A YouTube host is a video, a
/// Claude host is an artifact, an image extension is an image, a PDF is a
/// document, a fenced block is code, any other URL is an article or a bare
/// link, and the rest is text.
/// </summary>
public sealed class ContentKindDetectorTests
{
    [Theory]
    [InlineData("Aspire 13 walkthrough https://www.youtube.com/watch?v=abc", ContentKind.YouTube)]
    [InlineData("Short one https://youtu.be/abc", ContentKind.YouTube)]
    [InlineData("Inbox grouping canvas https://claude.ai/public/artifacts/123", ContentKind.ClaudeArtifact)]
    [InlineData("Whiteboard https://example.com/photos/board.png?size=large", ContentKind.Image)]
    [InlineData("The spec https://example.com/spec.pdf", ContentKind.Document)]
    [InlineData("Local-first sync patterns https://example.com/posts/local-first", ContentKind.Article)]
    [InlineData("https://example.com/posts/local-first", ContentKind.Link)]
    [InlineData("Ask about the trial length", ContentKind.Text)]
    public void The_kind_is_read_off_the_title(string title, ContentKind expected)
    {
        Assert.Equal(expected, ContentKindDetector.Detect(title));
    }

    [Fact]
    public void A_fenced_block_in_the_body_makes_code()
    {
        Assert.Equal(ContentKind.Code, ContentKindDetector.Detect("A snippet", "```csharp\nvar x = 1;\n```\n"));
    }

    [Fact]
    public void A_url_inside_prose_does_not_swallow_the_words_after_it()
    {
        // The closing parenthesis and the comma are prose, not address.
        Assert.Equal("https://example.com/a", ContentKindDetector.FirstUrl("See (https://example.com/a), then decide."));
        Assert.Equal(ContentKind.Article, ContentKindDetector.Detect("Read this", "See (https://example.com/a), then decide."));
    }

    [Fact]
    public void A_title_that_is_only_the_url_is_a_bare_link()
    {
        Assert.Equal(ContentKind.Link, ContentKindDetector.Detect("  https://example.com/a  "));
    }

    [Fact]
    public void Text_without_a_url_has_none()
    {
        Assert.Null(ContentKindDetector.FirstUrl("Ask about the trial length"));
        Assert.Null(ContentKindDetector.FirstUrl(null));
    }

    // --- A capture with files -------------------------------------------------

    /// <summary>A photo from the phone: the title is whatever the camera called
    /// it, and the picture is the whole of the content.</summary>
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png", "image/heic")]
    public void Pictures_and_nothing_else_are_an_image(params string[] types)
    {
        Assert.Equal(ContentKind.Image, ContentKindDetector.Detect("IMG_2041", null, null, types));
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/jpeg", "application/pdf")]
    [InlineData("text/plain")]
    public void Any_file_that_is_not_a_picture_makes_a_document(params string[] types)
    {
        // Words and a link beside it change nothing: the file is what was sent.
        Assert.Equal(
            ContentKind.Document,
            ContentKindDetector.Detect("Contract to sign https://example.com/deal", null, "Due Friday.", types));
    }

    [Fact]
    public void Pictures_with_words_beside_them_are_read_as_the_words_are()
    {
        string[] picture = ["image/jpeg"];

        Assert.Equal(ContentKind.Text, ContentKindDetector.Detect("Whiteboard", null, "The three options we drew.", picture));
        Assert.Equal(ContentKind.Article, ContentKindDetector.Detect("Board from the talk https://example.com/talk", null, null, picture));
    }

    [Fact]
    public void No_files_reads_the_text_as_before()
    {
        Assert.Equal(ContentKind.Text, ContentKindDetector.Detect("Ask about the trial length", null, null, []));
        Assert.Equal(ContentKind.Link, ContentKindDetector.Detect("https://example.com/a", null, null, null));
    }
}
