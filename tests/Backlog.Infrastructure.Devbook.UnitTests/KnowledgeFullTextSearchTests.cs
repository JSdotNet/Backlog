using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Infrastructure.Knowledge.UnitTests;

/// <summary>
/// Retrieval by words, over <c>chapter_fts</c>.
///
/// <para>Two properties are load-bearing here and the rest is detail.</para>
///
/// <para><b>A result is an address.</b>
/// <c>.domain/second-brain/features.md#knowledge-retrieval</c> says results name
/// the chapter they came from rather than returning loose text, and gives the
/// reason: a chapter address is what every other part of this context already
/// links by. A hit that carried only matched text would be the one thing in
/// Second Brain nothing could link to, follow, or cite.</para>
///
/// <para><b>No index is a sentence, not an empty list.</b> This is the last rung
/// of local ADR 0004's ladder and the only one a user ever sees. Every other
/// consumer degrades to Markdown because it is showing a handful of files;
/// search cannot, so it says so — and names the command that fixes it, because
/// "unavailable" without a next step is a dead end.</para>
/// </summary>
public sealed class KnowledgeFullTextSearchTests
{
    [Fact]
    public void A_hit_names_the_chapter_it_came_from()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);

        var answer = Search(temporary).Search("degradation ladder");

        Assert.False(answer.IsUnavailable);
        var hit = answer.Hits[0];

        // The address, in every form a caller asks for it.
        Assert.Equal(KnowledgeRetrievalCorpus.LadderPath, hit.Chapter.Path);
        Assert.Equal("the-degradation-ladder", hit.Chapter.Slug);
        Assert.Equal("The degradation ladder", hit.Chapter.Title);
        Assert.Equal("arc42", hit.Chapter.Folder);
        Assert.Equal(KnowledgeRetrievalCorpus.LadderPath + "#the-degradation-ladder", hit.Chapter.Reference);

        // Every hit, not just the first: an answer with one addressable row and
        // one loose one would pass an assertion about the first and still be the
        // thing this rule forbids.
        Assert.All(answer.Hits, candidate =>
        {
            Assert.NotEmpty(candidate.Chapter.Path);
            Assert.NotEmpty(candidate.Chapter.Slug);
            Assert.Contains("#", candidate.Chapter.Reference, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The excerpt is context for the address and never a replacement for it.
    /// Asserted rather than assumed, because "returning loose text" is exactly
    /// what a snippet becomes the moment nothing else is carried with it.
    /// </summary>
    [Fact]
    public void An_excerpt_travels_with_its_address_and_is_one_line()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);

        var hit = Assert.Single(Search(temporary).Search("loose text").Hits);

        Assert.Equal(KnowledgeRetrievalCorpus.MentionPath + "#knowledge-retrieval", hit.Chapter.Reference);
        Assert.Contains("loose text", hit.Excerpt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\n', hit.Excerpt);
    }

    /// <summary>
    /// An excerpt is prose, and the reason it is prose is that the index is.
    ///
    /// <para>Every authored chapter in this repository opens with a <c>meta</c>
    /// fence, so before <c>chapter.search_text</c> existed, a hit on a chapter's
    /// first sentence showed a reader the tail of its metadata block —
    /// "draft aliases: [KnowledgeNote, Note] related: [...] ``` The durable". An
    /// excerpt's whole job is to say why a chapter is in the list, and fence
    /// debris does not do that job.</para>
    ///
    /// <para>Asserted here rather than on the writer alone because
    /// <c>snippet()</c> reads the indexed column back out of <c>chapter</c>: if
    /// this side ever pointed the query at <c>text</c> instead, the writer's own
    /// tests would still pass and the debris would be back.</para>
    /// </summary>
    [Fact]
    public void An_excerpt_of_a_chapter_with_a_meta_fence_carries_no_fence()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);

        var hit = Assert.Single(Search(temporary).Search("durable unit").Hits);

        Assert.Equal(KnowledgeRetrievalCorpus.FencedPath, hit.Chapter.Path);
        Assert.Contains("The durable unit of captured knowledge", hit.Excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain('`', hit.Excerpt);
        Assert.DoesNotContain("status:", hit.Excerpt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aliases:", hit.Excerpt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The quieter half of the same defect: a word that only ever appears in a
    /// metadata block is not something the chapter is about, and a reader typing
    /// it meant the word rather than the bookkeeping.
    /// </summary>
    [Fact]
    public void A_word_only_in_a_meta_fence_matches_nothing()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);

        Assert.Empty(Search(temporary).Search("draft ").Hits);
        Assert.Empty(Search(temporary).Search("aliases ").Hits);
    }

    /// <summary>
    /// The chapter whose own heading is the phrase outranks the one that mentions
    /// it, which is what the lopsided <c>bm25</c> weighting is for: on this corpus
    /// a title is the chapter's name for itself.
    /// </summary>
    [Fact]
    public void A_chapter_named_for_the_words_outranks_one_that_mentions_them()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);

        var hits = Search(temporary).Search("ladder").Hits;

        Assert.Equal(2, hits.Count);
        Assert.Equal(KnowledgeRetrievalCorpus.LadderPath, hits[0].Chapter.Path);
        Assert.True(hits[0].Score > hits[1].Score, "A title match is expected to score above a body match.");
    }

    [Fact]
    public void A_scope_narrows_the_answer_to_one_area()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);

        var scoped = Search(temporary).Search("ladder", scope: KnowledgeRetrievalCorpus.DomainScope);

        var hit = Assert.Single(scoped.Hits);
        Assert.Equal(KnowledgeRetrievalCorpus.MentionPath, hit.Chapter.Path);
    }

    [Fact]
    public void Nothing_matching_is_an_empty_answer_and_not_an_unavailable_one()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);

        var answer = Search(temporary).Search("dendrochronology");

        Assert.Empty(answer.Hits);
        Assert.False(answer.IsUnavailable);
        Assert.Null(answer.UnavailableMessage);
    }

    /// <summary>
    /// The last rung of the ladder, and the only one with words in it. A
    /// repository nobody has generated an index for has no search, and the answer
    /// names both the fact and the command — an empty list here would claim
    /// nothing matched, which is a different statement and a false one.
    /// </summary>
    [Fact]
    public void With_no_database_search_is_unavailable_and_names_the_command()
    {
        using var temporary = new TemporaryDatabase();

        // Deliberately not seeded: this is a fresh clone.
        Assert.False(File.Exists(temporary.DatabaseFile));

        var answer = Search(temporary).Search("ladder");

        Assert.True(answer.IsUnavailable);
        Assert.Empty(answer.Hits);
        Assert.Contains(KnowledgeRetrieval.BuildCommand, answer.UnavailableMessage!, StringComparison.Ordinal);
        Assert.Equal(KnowledgeRetrieval.UnavailableMessage("Search"), answer.UnavailableMessage);
    }

    /// <summary>
    /// The state is reached before the reader has typed anything, which is the
    /// point of it: finding out there is no index only after asking a question
    /// would be the empty list this rung exists to replace, arriving one keystroke
    /// late.
    /// </summary>
    [Fact]
    public void The_unavailable_state_is_reached_before_anything_is_typed()
    {
        using var temporary = new TemporaryDatabase();

        Assert.True(Search(temporary).Search(string.Empty).IsUnavailable);

        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);

        // And with an index, an empty query is simply no question rather than a
        // report about the index.
        var answer = Search(temporary).Search("   ");
        Assert.False(answer.IsUnavailable);
        Assert.Empty(answer.Hits);
    }

    /// <summary>
    /// A folder pointing somewhere with no repository above it is the same answer
    /// as no database, which is what makes a knowledge folder configured off a
    /// clone safe to search rather than an exception.
    /// </summary>
    [Fact]
    public void A_folder_with_no_repository_above_it_is_unavailable_rather_than_an_error()
    {
        var answer = new KnowledgeFullTextSearch(new StubKnowledgeFolderSource(null)).Search("ladder");

        Assert.True(answer.IsUnavailable);
    }

    /// <summary>
    /// FTS5's MATCH takes a query language, and a reader types prose. Every one of
    /// these is a syntax error if it reaches the parser as written — an
    /// unterminated string, a boolean operator, a column filter, a bare prefix
    /// star — and none of them may throw out of a search box.
    /// </summary>
    [Theory]
    [InlineData("what's stale?")]
    [InlineData("ladder AND OR NOT")]
    [InlineData("chapter: ladder")]
    [InlineData("\"unbalanced")]
    [InlineData("*")]
    [InlineData("(ladder")]
    [InlineData("^ladder NEAR/2 rung")]
    public void A_readers_punctuation_is_never_query_syntax(string query)
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);

        var answer = Search(temporary).Search(query);

        Assert.False(answer.IsUnavailable);
        Assert.All(answer.Hits, hit => Assert.NotEmpty(hit.Chapter.Path));
    }

    /// <summary>A query with nothing in it a tokenizer would keep asks nothing,
    /// rather than asking for everything.</summary>
    [Fact]
    public void A_query_of_pure_punctuation_matches_nothing()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);

        Assert.Empty(Search(temporary).Search("--- ... ???").Hits);
    }

    /// <summary>The reader is still typing the last word, so it matches as a
    /// prefix; the words before it they have finished.</summary>
    [Fact]
    public void The_word_still_being_typed_matches_as_a_prefix()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);

        Assert.NotEmpty(Search(temporary).Search("degrad").Hits);

        // Finished - a trailing space says so - and "degrad" is not a word in the
        // corpus.
        Assert.Empty(Search(temporary).Search("degrad ").Hits);
    }

    [Fact]
    public void The_limit_is_honoured()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);

        Assert.Single(Search(temporary).Search("ladder", limit: 1).Hits);
    }

    private static IKnowledgeSearch Search(TemporaryDatabase temporary) =>
        new KnowledgeFullTextSearch(new StubKnowledgeFolderSource(temporary.RootDirectory));
}
