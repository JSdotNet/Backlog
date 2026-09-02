namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// File metadata: the block under the <c>#</c> title, drawn wherever a surface says
/// which file this is.
///
/// <para>Two claims, and they are the two reasons this is a component rather than a
/// branch at a call site. It adds no markup to the record — the first test, for
/// <c>MetadataChapterViewTests</c>'s reason — and it owns what happens when there is
/// no record, which a header cannot get wrong twice if only one place asks.</para>
///
/// <para>The record's own drawing is <c>MetadataViewTests</c>'s, and the header it
/// ends up in is <c>FileViewFileMetadataTests</c>'s. What is here is the seam
/// between them.</para>
/// </summary>
public sealed class MetadataFileViewTests
{
    private const string Block = """
        status: adopted
        kind: runtime
        version: "10.0"
        """;

    /// <summary>The file's name and its details as one item of the headline row,
    /// which is what a host wraps them for: loose, they would sit beside each other
    /// with the status after both.</summary>
    private static readonly RenderFragment Headline = builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "file-view__headline");
        builder.OpenElement(2, "h3");
        builder.AddContent(3, "shared.md");
        builder.CloseElement();
        builder.CloseElement();
    };

    /// <summary>The same name with no row to be an item of, which is why it is a
    /// second fragment and not the same one used twice.</summary>
    private static readonly RenderFragment Alone = builder =>
    {
        builder.OpenElement(0, "h3");
        builder.AddContent(1, "shared.md");
        builder.CloseElement();
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_file_shape_renders_exactly_what_the_record_renders(bool showFields)
    {
        // Byte for byte, against the record rather than a captured literal. A
        // component that wraps another one can introduce a whitespace text node
        // that no CSS selector sees and the stylesheet does.
        //
        // Two contexts, one render each. bUnit numbers the event handlers it
        // writes into the markup per renderer, so rendering both in one context
        // gives the second one a different `blazor:onchange` id and the comparison
        // fails on a counter rather than on the markup. A fresh renderer for each
        // side means the two are numbered identically — and nothing is normalised
        // away, which for a whitespace-sensitive comparison is the point.
        using var fileContext = new BunitContext();
        using var recordContext = new BunitContext();

        var file = fileContext.Render<MetadataFileView>(parameters => parameters
            .Add(view => view.Metadata, MetadataReader.Parse(Block))
            .Add(view => view.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech))
            .Add(view => view.ShowFields, showFields)
            .Add(view => view.AriaLabel, "shared.md metadata")
            .Add(view => view.CssClass, "file-view__record")
            .Add(view => view.TestId, "file-metadata")
            .Add(view => view.Heading, Headline)
            .Add(view => view.HeadingAlone, Alone));

        var record = recordContext.Render<MetadataView>(parameters => parameters
            .Add(view => view.Metadata, MetadataReader.Parse(Block))
            .Add(view => view.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech))
            .Add(view => view.ShowFields, showFields)
            .Add(view => view.AriaLabel, "shared.md metadata")
            .Add(view => view.CssClass, "file-view__record")
            .Add(view => view.TestId, "file-metadata")
            .Add(view => view.Heading, Headline));

        Assert.Equal(record.Markup, file.Markup);
    }

    [Fact]
    public void A_file_with_no_block_of_its_own_is_still_named()
    {
        // The whole reason the emptiness question lives here. An empty record draws
        // no element at all, so a header that handed it the file's name would lose
        // the name to a wrapper that stood down.
        using var context = new BunitContext();

        var file = context.Render<MetadataFileView>(parameters => parameters
            .Add(view => view.Metadata, MetadataRecord.Empty)
            .Add(view => view.Heading, Headline)
            .Add(view => view.HeadingAlone, Alone));

        Assert.Empty(file.FindAll(".knowledge-record"));
        Assert.Equal("shared.md", file.Find("h3").TextContent);

        // The unwrapped fragment, because there is no row here to be an item of.
        Assert.Empty(file.FindAll(".file-view__headline"));
    }

    [Fact]
    public void No_record_at_all_reads_the_same_as_one_that_states_nothing()
    {
        // Null and an empty block are one answer — the file said nothing about
        // itself — so a host that has not read a record yet and one that read an
        // empty fence get the same header.
        using var context = new BunitContext();

        var absent = context.Render<MetadataFileView>(parameters => parameters
            .Add(view => view.Metadata, null)
            .Add(view => view.Heading, Headline)
            .Add(view => view.HeadingAlone, Alone));

        var empty = context.Render<MetadataFileView>(parameters => parameters
            .Add(view => view.Metadata, MetadataRecord.Empty)
            .Add(view => view.Heading, Headline)
            .Add(view => view.HeadingAlone, Alone));

        Assert.Equal(empty.Markup, absent.Markup);
    }

    [Fact]
    public void A_record_takes_the_wrapped_headline_and_not_the_bare_one()
    {
        // The two fragments are not interchangeable: inside the record the headline
        // is a row, and the wrapper is what gives it two items to align instead of
        // three.
        using var context = new BunitContext();

        var file = context.Render<MetadataFileView>(parameters => parameters
            .Add(view => view.Metadata, MetadataReader.Parse("status: adopted"))
            .Add(view => view.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech))
            .Add(view => view.Heading, Headline)
            .Add(view => view.HeadingAlone, Alone));

        var headline = file.Find(".knowledge-record__headline");
        Assert.Equal(["div", "label"], headline.Children.Select(child => child.LocalName));
        Assert.Contains("file-view__headline", headline.Children[0].ClassList);
    }

    [Fact]
    public void The_file_status_a_reader_picks_is_reported_to_the_host()
    {
        // The file's own status, and never a chapter's: a host that persists this
        // writes the block under the title.
        using var context = new BunitContext();

        string? reported = null;

        var file = context.Render<MetadataFileView>(parameters => parameters
            .Add(view => view.Metadata, MetadataReader.Parse("status: adopted"))
            .Add(view => view.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech))
            .Add(view => view.OnStatusChanged, EventCallback.Factory.Create<string?>(this, status => reported = status))
            .Add(view => view.Heading, Headline)
            .Add(view => view.HeadingAlone, Alone));

        file.Find(".knowledge-record__headline .status-editor select").Change("retired");

        Assert.Equal("retired", reported);
    }
}
