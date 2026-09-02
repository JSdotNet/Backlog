namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Chapter metadata: the block under a <c>##</c> heading, and the heading it folds
/// into.
///
/// <para>The shape used to be assembled inside <c>MarkdownView</c> and the file
/// shape inside <c>FileView</c>, with nothing naming either. What this suite is
/// mostly for is the first test in it: the chapter shape adds no markup to the
/// record it draws. Everything else on this page — the headline order, the status
/// control, the field tier — is <c>MetadataViewTests</c>'s, and asserting it twice
/// would be two places to update when the record changes. Asserting equality with
/// the record instead says the one thing this component claims, and keeps saying it
/// however the record is redrawn.</para>
/// </summary>
public sealed class MetadataChapterViewTests
{
    private const string Block = """
        status: adopted
        kind: runtime
        version: "10.0"
        depends-on: [".tech/shared.md#c-language"]
        """;

    private static RenderFragment Heading(string text) => builder =>
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "class", "md-heading md-heading--2");
        builder.AddContent(2, text);
        builder.CloseElement();
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_chapter_shape_renders_exactly_what_the_record_renders(bool showFields)
    {
        // Byte for byte, and against the record itself rather than against a
        // captured literal: the claim is that naming the shape cost nothing, and a
        // literal would have to be recaptured every time the record is redrawn —
        // at which point it would stop being able to say that.
        //
        // Whitespace is the reason this is a whole-markup comparison. Razor emits
        // the whitespace between two sibling elements as content, so a component
        // wrapping another one can add a text node no CSS selector would ever see
        // and the stylesheet certainly would.
        //
        // Two contexts, one render each. bUnit numbers the event handlers it
        // writes into the markup per renderer, so rendering both in one context
        // gives the second one a different `blazor:onchange` id and the comparison
        // fails on a counter rather than on the markup. A fresh renderer for each
        // side means the two are numbered identically — and nothing is normalised
        // away, which for a whitespace-sensitive comparison is the point.
        using var chapterContext = new BunitContext();
        using var recordContext = new BunitContext();

        var chapter = chapterContext.Render<MetadataChapterView>(parameters => parameters
            .Add(view => view.Metadata, MetadataReader.Parse(Block))
            .Add(view => view.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech))
            .Add(view => view.ShowFields, showFields)
            .Add(view => view.Heading, Heading("The Runtime")));

        var record = recordContext.Render<MetadataView>(parameters => parameters
            .Add(view => view.Metadata, MetadataReader.Parse(Block))
            .Add(view => view.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech))
            .Add(view => view.ShowFields, showFields)
            .Add(view => view.Heading, Heading("The Runtime")));

        Assert.Equal(record.Markup, chapter.Markup);
    }

    [Fact]
    public void The_heading_is_the_records_headline_and_the_status_holds_its_line()
    {
        // The pairing itself, once: the two really do share a parent, because a
        // status right-aligned by a margin trick comes apart the moment the
        // heading wraps.
        using var context = new BunitContext();

        var chapter = context.Render<MetadataChapterView>(parameters => parameters
            .Add(view => view.Metadata, MetadataReader.Parse("status: adopted"))
            .Add(view => view.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech))
            .Add(view => view.Heading, Heading("The Runtime")));

        var headline = chapter.Find(".knowledge-record__headline");
        Assert.Equal(["p", "label"], headline.Children.Select(child => child.LocalName));
        Assert.Equal("The Runtime", headline.Children[0].TextContent);
    }

    [Fact]
    public void A_chapter_that_states_nothing_draws_nothing()
    {
        // Including its heading. That is not a loss: a host with a heading to draw
        // whether or not there is a record draws it itself — MarkdownView renders
        // the heading alone when no fence follows it — and a record standing down
        // must not take the surrounding document's markup with it.
        using var context = new BunitContext();

        var chapter = context.Render<MetadataChapterView>(parameters => parameters
            .Add(view => view.Metadata, MetadataRecord.Empty)
            .Add(view => view.Heading, Heading("The Runtime")));

        Assert.Equal(string.Empty, chapter.Markup);
    }

    [Fact]
    public void The_folder_reaches_the_status_through_the_shape()
    {
        // The plumbing, not the vocabulary: what the select offers is
        // KnowledgeStatus's and is tested there. What matters here is that a
        // parameter handed to the shape arrives at the record.
        using var context = new BunitContext();

        var chapter = context.Render<MetadataChapterView>(parameters => parameters
            .Add(view => view.Metadata, MetadataReader.Parse("status: adopted"))
            .Add(view => view.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech))
            .Add(view => view.Heading, Heading("The Runtime")));

        Assert.NotNull(chapter.Find(".knowledge-record__headline .status-editor select"));
    }

    [Fact]
    public void A_reader_who_picks_a_status_is_reported_to_the_host()
    {
        // A chapter's status is its own, so the surface wires this per chapter.
        // Nothing here writes anything: see KnowledgeStatusChange.
        using var context = new BunitContext();

        string? reported = null;

        var chapter = context.Render<MetadataChapterView>(parameters => parameters
            .Add(view => view.Metadata, MetadataReader.Parse("status: adopted"))
            .Add(view => view.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech))
            .Add(view => view.OnStatusChanged, EventCallback.Factory.Create<string?>(this, status => reported = status))
            .Add(view => view.Heading, Heading("The Runtime")));

        chapter.Find(".knowledge-record__headline .status-editor select").Change("hold");

        Assert.Equal("hold", reported);
    }
}
