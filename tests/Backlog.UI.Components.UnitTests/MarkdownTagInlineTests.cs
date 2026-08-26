namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The <c>#tag</c> inline: what makes one, and what it renders as.
///
/// <para>Both halves are claims the storybook's Tags page makes in prose, and
/// neither was asserted anywhere before it did. The golden markup samples pin the
/// rendering as part of a whole document; these pin it as the fact it is, and pin
/// the parser rule the page states beside it.</para>
/// </summary>
public sealed class MarkdownTagInlineTests
{
    /// <summary>The rule, as the page states it: whitespace in front of the hash,
    /// a letter straight after it, then word characters and hyphens.</summary>
    [Theory]
    // A tag, and the forms that continue one.
    [InlineData("Filed under #sync for now.", "sync")]
    [InlineData("A #two-word tag.", "two-word")]
    [InlineData("A #snake_case tag.", "snake_case")]
    [InlineData("The #Sync tag keeps its case.", "Sync")]
    [InlineData("That is all for #sync, and more.", "sync")]
    // At the start of a line, where a heading needs the whitespace this has not
    // got — so the line is a paragraph and the hash is a sigil.
    [InlineData("#sync is where this belongs.", "sync")]
    public void A_hash_that_opens_a_word_and_is_followed_by_a_letter_is_a_tag(string line, string expected)
    {
        var tags = MarkdownPreview.ParseInlines(line).OfType<MdTag>().Select(tag => tag.Tag).ToList();

        Assert.Contains(expected, tags);
    }

    /// <summary>And what the same rule refuses. Each of these is a row on the Tags
    /// page, so a change to the pattern fails here rather than making the page
    /// quietly wrong.</summary>
    [Theory]
    // The hash has to open a word.
    [InlineData("See issue#42 and PR#7 for the rest.")]
    [InlineData("The tag (#sync) is in brackets.")]
    // The character after it has to be a letter.
    [InlineData("Ranked #1 this week and #2 last week.")]
    [InlineData("##heading without a space is not a heading either.")]
    // And nothing inside a code span is inline-parsed at all.
    [InlineData("Write `#sync` to tag something.")]
    public void Everything_else_stays_prose(string line)
    {
        Assert.Empty(MarkdownPreview.ParseInlines(line).OfType<MdTag>());
    }

    /// <summary>The full stop and the comma stay in the prose: the first character
    /// that is neither a word character nor a hyphen ends the tag.</summary>
    [Fact]
    public void Punctuation_after_a_tag_is_not_part_of_it()
    {
        var tags = MarkdownPreview.ParseInlines("That is all for #sync, and for #docs.")
            .OfType<MdTag>()
            .Select(tag => tag.Tag)
            .ToList();

        Assert.Equal(["sync", "docs"], tags);
    }

    /// <summary>
    /// What a tag in a body renders as — and, said as an assertion, what it does
    /// not.
    ///
    /// <para>It shares the class <c>tag-chip</c> with <see cref="TagChip"/> and
    /// nothing else: <c>MarkdownRender</c> builds the span by hand and never renders
    /// the component, so the component's inner <c>.tag-chip__label</c> and the click
    /// and remove controls that hang off it exist in no body. That is the state
    /// today rather than an oversight — adopting the component in the renderer would
    /// change the markup of every tag this product draws — and it is pinned here so
    /// the two cannot converge or diverge without somebody deciding to.</para>
    /// </summary>
    [Fact]
    public void A_tag_in_a_body_is_a_bare_span_with_the_hash_in_its_text()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownView>(parameters => parameters
            .Add(v => v.Blocks, MarkdownPreview.ParseDocument("A body with a #sync tag in it.")));

        var chip = view.Find(".tag-chip");

        Assert.Equal("SPAN", chip.TagName);
        Assert.Equal("#sync", chip.TextContent);
        Assert.Equal("tag-chip", chip.ClassName);

        // The three things TagChip has and this has not.
        Assert.Empty(view.FindAll(".tag-chip__label"));
        Assert.Empty(view.FindAll(".tag-chip__remove"));
        Assert.Empty(view.FindAll(".tag-chip button"));
    }

    /// <summary>The other side of the same comparison, so neither half can drift
    /// alone: the component wraps its text, and the wrapper is what its click and
    /// remove controls attach to.</summary>
    [Fact]
    public void The_component_wraps_its_text_in_a_label_the_markdown_path_has_no_equivalent_of()
    {
        using var context = new BunitContext();

        var chip = context.Render<TagChip>(parameters => parameters
            .Add(c => c.Text, "#sync")
            .Add(c => c.Removable, true));

        Assert.Equal("#sync", chip.Find(".tag-chip__label").TextContent);
        Assert.NotNull(chip.Find(".tag-chip__remove"));

        // The hash is the caller's. TagChip prints Text and adds nothing to it,
        // which is the other half of why the two are not interchangeable: the
        // renderer puts the sigil back on, and this does not.
        var plain = context.Render<TagChip>(parameters => parameters.Add(c => c.Text, "sync"));

        Assert.Equal("sync", plain.Find(".tag-chip__label").TextContent);
    }
}
