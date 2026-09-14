namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A reference is the only address a chapter has, so the shape of it is load
/// bearing: get the split wrong and every chapter link in the repository points
/// at a file instead.
/// </summary>
public sealed class DevbookReferenceTests
{
    [Fact]
    public void A_path_with_a_slug_addresses_a_chapter()
    {
        var reference = DevbookReference.Parse(".domain/order-management/domain.md#aggregate-order");

        Assert.NotNull(reference);
        Assert.Equal(".domain/order-management/domain.md", reference.Path);
        Assert.Equal("aggregate-order", reference.Slug);
        Assert.True(reference.IsChapter);
        Assert.False(reference.IsFile);
    }

    [Fact]
    public void A_bare_path_addresses_the_file_as_a_whole()
    {
        var reference = DevbookReference.Parse(".domain/order-management/dependencies.md");

        Assert.NotNull(reference);
        Assert.Equal(".domain/order-management/dependencies.md", reference.Path);
        Assert.Null(reference.Slug);
        Assert.True(reference.IsFile);
        Assert.False(reference.IsChapter);
    }

    [Fact]
    public void A_trailing_hash_with_nothing_after_it_is_still_a_file()
    {
        var reference = DevbookReference.Parse(".tech/shared.md#");

        Assert.NotNull(reference);
        Assert.Equal(".tech/shared.md", reference.Path);
        Assert.Null(reference.Slug);
        Assert.True(reference.IsFile);
    }

    [Fact]
    public void Only_the_first_hash_splits_the_reference()
    {
        // A heading slug can carry a hash of its own. Splitting on the last one
        // would move part of the slug into the path and resolve to nothing.
        var reference = DevbookReference.Parse(".backlog/domain-backlog.md#item-c#-and-net");

        Assert.NotNull(reference);
        Assert.Equal(".backlog/domain-backlog.md", reference.Path);
        Assert.Equal("item-c#-and-net", reference.Slug);
    }

    [Theory]
    [InlineData(".arc42/01-introduction.md", DevbookFolder.Arc42)]
    [InlineData(".domain/tasks/domain.md#aggregate-entry", DevbookFolder.Domain)]
    [InlineData(".backlog/domain-backlog.md", DevbookFolder.Backlog)]
    [InlineData(".tech/shared.md#markdown", DevbookFolder.Tech)]
    [InlineData(".design/color-scheme.md", DevbookFolder.Design)]
    [InlineData("docs/readme.md", DevbookFolder.Unknown)]
    public void The_folder_comes_from_the_first_segment_of_the_path(string raw, DevbookFolder expected)
    {
        var reference = DevbookReference.Parse(raw);

        Assert.NotNull(reference);
        Assert.Equal(expected, reference.Folder);
    }

    [Theory]
    [InlineData("/.tech/shared.md")]
    [InlineData("./.tech/shared.md")]
    [InlineData(".TECH/shared.md")]
    public void A_leading_slash_or_a_stray_capital_is_the_same_folder(string raw)
    {
        var reference = DevbookReference.Parse(raw);

        Assert.NotNull(reference);
        Assert.Equal(DevbookFolder.Tech, reference.Folder);
    }

    [Fact]
    public void Quotes_belong_to_the_yaml_and_not_to_the_reference()
    {
        var quoted = DevbookReference.Parse("  \".tech/technology-graph.md\"  ");
        var single = DevbookReference.Parse("'.tech/technology-graph.md'");

        Assert.NotNull(quoted);
        Assert.NotNull(single);
        Assert.Equal(".tech/technology-graph.md", quoted.Raw);
        Assert.Equal(".tech/technology-graph.md", quoted.Path);
        Assert.Equal(".tech/technology-graph.md", single.Raw);
    }

    [Fact]
    public void The_file_name_is_the_last_segment_of_the_path()
    {
        var reference = DevbookReference.Parse(".domain/order-management/domain.md#aggregate-order");

        Assert.NotNull(reference);
        Assert.Equal("domain.md", reference.FileName);
    }

    [Fact]
    public void A_file_label_is_its_name_and_a_chapter_label_is_its_slug_as_words()
    {
        var file = DevbookReference.Parse(".arc42/01-introduction.md");
        var chapter = DevbookReference.Parse(".arc42/02-constraints.md#technical-constraints");

        Assert.NotNull(file);
        Assert.NotNull(chapter);
        Assert.Equal("01-introduction", file.Label);
        Assert.Equal("technical constraints", chapter.Label);

        // The full form stays available, so shortening the label never loses it.
        Assert.Equal(".arc42/02-constraints.md#technical-constraints", chapter.Raw);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#only-a-slug")]
    [InlineData("  #  ")]
    public void Nothing_addressable_parses_to_nothing(string? raw)
    {
        Assert.Null(DevbookReference.Parse(raw));
        Assert.False(DevbookReference.TryParse(raw, out var reference));
        Assert.Null(reference);
    }

    [Fact]
    public void TryParse_hands_back_the_reference_it_read()
    {
        Assert.True(DevbookReference.TryParse(".tech/shared.md#markdown", out var reference));

        Assert.NotNull(reference);
        Assert.Equal("markdown", reference.Slug);
    }
}
