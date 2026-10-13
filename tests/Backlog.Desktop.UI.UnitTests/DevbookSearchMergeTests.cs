namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// How one search across the Brain and every repository's devbook puts its answer
/// together (<c>.devbook/domain/devbook/features.md#unified-search</c>): the words
/// and the meaning answers of one source merged into one list, and the sources'
/// lists grouped with the Brain's first.
/// </summary>
public sealed class DevbookSearchMergeTests
{
    private static readonly DevbookSearchSource Backlog = DevbookSearchSource.Repository("backlog");
    private static readonly DevbookSearchSource ClientApp = DevbookSearchSource.Repository("client-app");

    [Fact]
    public void Words_alone_keep_their_order()
    {
        var words = new[] { Hit("a.md"), Hit("b.md"), Hit("c.md") };

        var merged = DevbookSearchMerge.Merge(words, [], limit: 30);

        Assert.Equal(["a.md", "b.md", "c.md"], merged.Select(hit => hit.Chapter.Reference));
    }

    [Fact]
    public void Meaning_alone_keeps_its_order()
    {
        var meaning = new[] { Hit("x.md"), Hit("y.md") };

        var merged = DevbookSearchMerge.Merge([], meaning, limit: 30);

        Assert.Equal(["x.md", "y.md"], merged.Select(hit => hit.Chapter.Reference));
    }

    /// <summary>A chapter both tiers found is one row, not two, and it outranks a
    /// chapter only one of them found — even one that tier put first.</summary>
    [Fact]
    public void A_chapter_both_tiers_found_is_listed_once_and_ranks_first()
    {
        var words = new[] { Hit("only-words.md"), Hit("both.md#slug") };
        var meaning = new[] { Hit("only-meaning.md"), Hit("both.md#slug") };

        var merged = DevbookSearchMerge.Merge(words, meaning, limit: 30);

        Assert.Equal("both.md#slug", merged[0].Chapter.Reference);
        Assert.Single(merged, hit => hit.Chapter.Reference == "both.md#slug");
        Assert.Equal(3, merged.Count);
    }

    /// <summary>The scores of the two tiers are on different scales — a full-text
    /// rank against a cosine — so the merge goes by place in each list and never
    /// compares the numbers across them.</summary>
    [Fact]
    public void The_merge_goes_by_rank_and_not_by_raw_score()
    {
        var words = new[] { Hit("first-by-words.md", score: 0.001) };
        var meaning = new[] { Hit("first-by-meaning.md", score: 0.99), Hit("second-by-meaning.md", score: 0.98) };

        var merged = DevbookSearchMerge.Merge(words, meaning, limit: 30);

        // Equal first places tie, and the tie goes to the words, which is the tier
        // whose excerpt shows the reader why the row is there.
        Assert.Equal(
            ["first-by-words.md", "first-by-meaning.md", "second-by-meaning.md"],
            merged.Select(hit => hit.Chapter.Reference));
    }

    /// <summary>The excerpt is the words tier's: a vector match has no words to
    /// show, and the row that both found should still say why it matched.</summary>
    [Fact]
    public void A_chapter_both_tiers_found_keeps_the_words_excerpt()
    {
        var words = new[] { Hit("both.md", excerpt: "the matched words") };
        var meaning = new[] { Hit("both.md", excerpt: string.Empty) };

        var merged = DevbookSearchMerge.Merge(words, meaning, limit: 30);

        Assert.Equal("the matched words", Assert.Single(merged).Excerpt);
    }

    [Fact]
    public void The_merge_stops_at_the_limit()
    {
        var words = Enumerable.Range(0, 10).Select(index => Hit($"w{index}.md")).ToArray();
        var meaning = Enumerable.Range(0, 10).Select(index => Hit($"m{index}.md")).ToArray();

        Assert.Equal(5, DevbookSearchMerge.Merge(words, meaning, limit: 5).Count);
    }

    [Fact]
    public void The_brain_group_comes_first_whatever_order_the_sources_answered_in()
    {
        var groups = DevbookSearchMerge.Group(
        [
            DevbookSearchGroup.For(Backlog, [Hit("backlog.md")]),
            DevbookSearchGroup.For(ClientApp, [Hit("client.md")]),
            DevbookSearchGroup.For(DevbookSearchSource.Brain, [Hit("projects/note.md")])
        ]);

        Assert.Equal(["Brain", "backlog", "client-app"], groups.Select(group => group.Source.Label));
    }

    /// <summary>After the Brain, the repositories keep the order they were asked
    /// in, which is the order they are configured in.</summary>
    [Fact]
    public void Repositories_keep_their_configured_order_after_the_brain()
    {
        var groups = DevbookSearchMerge.Group(
        [
            DevbookSearchGroup.For(ClientApp, [Hit("client.md")]),
            DevbookSearchGroup.For(Backlog, [Hit("backlog.md")])
        ]);

        Assert.Equal(["client-app", "backlog"], groups.Select(group => group.Source.Label));
    }

    /// <summary>A source that answered and found nothing has no group: an empty
    /// heading in a list of results is noise.</summary>
    [Fact]
    public void A_source_that_found_nothing_has_no_group()
    {
        var groups = DevbookSearchMerge.Group(
        [
            DevbookSearchGroup.For(DevbookSearchSource.Brain, []),
            DevbookSearchGroup.For(Backlog, [Hit("backlog.md")])
        ]);

        Assert.Equal(["backlog"], groups.Select(group => group.Source.Label));
    }

    /// <summary>A source with no database is named as not searchable rather than
    /// left out — leaving it out would claim it had nothing to say.</summary>
    [Fact]
    public void A_source_with_no_database_keeps_its_group_as_not_searchable()
    {
        var groups = DevbookSearchMerge.Group(
        [
            DevbookSearchGroup.For(Backlog, [Hit("backlog.md")]),
            DevbookSearchGroup.NotSearchable(ClientApp, "No index yet.")
        ]);

        Assert.Equal(["backlog", "client-app"], groups.Select(group => group.Source.Label));
        Assert.True(groups[1].IsUnavailable);
        Assert.Equal("No index yet.", groups[1].UnavailableMessage);
    }

    /// <summary>Within the Brain's place at the top, a Brain that cannot be searched
    /// still comes first: where the Brain is does not depend on whether it answered.</summary>
    [Fact]
    public void A_brain_that_is_not_searchable_still_comes_first()
    {
        var groups = DevbookSearchMerge.Group(
        [
            DevbookSearchGroup.For(Backlog, [Hit("backlog.md")]),
            DevbookSearchGroup.NotSearchable(DevbookSearchSource.Brain, "Pick a folder.")
        ]);

        Assert.Equal(["Brain", "backlog"], groups.Select(group => group.Source.Label));
    }

    private static DevbookSearchHit Hit(string reference, double score = 1, string excerpt = "words")
    {
        var hash = reference.IndexOf('#');
        var path = hash < 0 ? reference : reference[..hash];
        var slug = hash < 0 ? string.Empty : reference[(hash + 1)..];
        return new DevbookSearchHit(new DevbookChapterAddress(path, slug, null, null), excerpt, score);
    }
}
