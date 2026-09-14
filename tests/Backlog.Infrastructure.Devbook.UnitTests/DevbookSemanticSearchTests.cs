using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// Retrieval by meaning: the brute-force cosine over <c>chapter_embedding</c>.
///
/// <para>Two failures are worth more than the rest of this file put together.</para>
///
/// <para><b>Comparing across models.</b> A database is pinned to whatever
/// produced its vectors. Two models' embeddings share a coordinate space only by
/// coincidence, so a cosine between them is not an approximate answer — it is a
/// meaningless one that still sorts, which is the worst possible failure here
/// because it looks exactly like working. The reader ignores every vector that is
/// not the model it is configured for, and that is asserted rather than
/// documented.</para>
///
/// <para><b>Silence mistaken for absence.</b> A database with no vectors is rung
/// five of local ADR 0004's ladder — full-text search answers and meaning is
/// absent, not broken — while no database at all is rung six, the one with words
/// in it. Reporting either as the other is the mistake, so both are pinned.</para>
/// </summary>
public sealed class DevbookSemanticSearchTests
{
    /// <summary>Along the first axis. The corpus vectors below are placed at known
    /// angles to it, so the expected order is arithmetic rather than a
    /// guess.</summary>
    private static readonly float[] Question = [1f, 0f, 0f];

    [Fact]
    public void Cosine_ranks_the_nearest_direction_first()
    {
        using var temporary = new TemporaryDatabase();
        DevbookRetrievalCorpus.Seed(temporary.DatabaseFile);

        // Angles to the question: 0 degrees, 45 degrees, and 60 degrees. Written
        // as plain coordinates rather than unit vectors on purpose - a cosine
        // divides by both norms, so a longer vector must not outrank a nearer one,
        // and the third of these is ten times the length of the first.
        DevbookRetrievalCorpus.Embed(temporary.DatabaseFile, DevbookRetrievalCorpus.LadderPath, DevbookRetrievalCorpus.Model, 1f, 0f, 0f);
        DevbookRetrievalCorpus.Embed(temporary.DatabaseFile, DevbookRetrievalCorpus.MentionPath, DevbookRetrievalCorpus.Model, 1f, 1f, 0f);
        DevbookRetrievalCorpus.Embed(temporary.DatabaseFile, DevbookRetrievalCorpus.UnrelatedPath, DevbookRetrievalCorpus.Model, 5f, 8.66f, 0f);

        var hits = Nearest(temporary).Nearest(Question).Hits;

        Assert.Equal(
            [DevbookRetrievalCorpus.LadderPath, DevbookRetrievalCorpus.MentionPath, DevbookRetrievalCorpus.UnrelatedPath],
            hits.Select(hit => hit.Chapter.Path));

        Assert.Equal(1d, hits[0].Score, 5);
        Assert.Equal(Math.Sqrt(0.5), hits[1].Score, 5);
        Assert.Equal(0.5d, hits[2].Score, 3);
    }

    /// <summary>Every hit is an address here too. Meaning is a different way in,
    /// not a different kind of answer.</summary>
    [Fact]
    public void A_hit_names_the_chapter_it_came_from()
    {
        using var temporary = new TemporaryDatabase();
        DevbookRetrievalCorpus.Seed(temporary.DatabaseFile);
        DevbookRetrievalCorpus.Embed(temporary.DatabaseFile, DevbookRetrievalCorpus.LadderPath, DevbookRetrievalCorpus.Model, 1f, 0f, 0f);

        var hit = Assert.Single(Nearest(temporary).Nearest(Question).Hits);

        Assert.Equal(DevbookRetrievalCorpus.LadderPath + "#the-degradation-ladder", hit.Chapter.Reference);
        Assert.Equal("The degradation ladder", hit.Chapter.Title);
        Assert.Equal("arc42", hit.Chapter.Folder);
    }

    [Fact]
    public void A_vector_from_another_model_is_ignored()
    {
        using var temporary = new TemporaryDatabase();
        DevbookRetrievalCorpus.Seed(temporary.DatabaseFile);

        // A perfect match on the question, produced by something else. Ranked, it
        // would come first; ignored, it is not in the answer at all.
        DevbookRetrievalCorpus.Embed(temporary.DatabaseFile, DevbookRetrievalCorpus.LadderPath, "text-embedding-3-large", 1f, 0f, 0f);
        DevbookRetrievalCorpus.Embed(temporary.DatabaseFile, DevbookRetrievalCorpus.MentionPath, DevbookRetrievalCorpus.Model, 1f, 1f, 0f);

        var hits = Nearest(temporary).Nearest(Question).Hits;

        var hit = Assert.Single(hits);
        Assert.Equal(DevbookRetrievalCorpus.MentionPath, hit.Chapter.Path);
        Assert.DoesNotContain(hits, candidate => candidate.Chapter.Path == DevbookRetrievalCorpus.LadderPath);
    }

    /// <summary>A database built entirely with another model answers nothing
    /// rather than answering wrongly — and it is still not "unavailable", because
    /// the index exists and full-text search is reading it.</summary>
    [Fact]
    public void A_database_pinned_to_another_model_answers_nothing()
    {
        using var temporary = new TemporaryDatabase();
        DevbookRetrievalCorpus.Seed(temporary.DatabaseFile);
        DevbookRetrievalCorpus.Embed(temporary.DatabaseFile, DevbookRetrievalCorpus.LadderPath, "some-other-model", 1f, 0f, 0f);

        var answer = Nearest(temporary).Nearest(Question);

        Assert.Empty(answer.Hits);
        Assert.False(answer.IsUnavailable);
    }

    /// <summary>The reader can be pointed at whatever a repository was actually
    /// embedded with, which is the other half of the model check: ignoring
    /// mismatches would be useless if the match were hard-coded.</summary>
    [Fact]
    public void A_reader_configured_for_that_other_model_reads_it()
    {
        using var temporary = new TemporaryDatabase();
        DevbookRetrievalCorpus.Seed(temporary.DatabaseFile);
        DevbookRetrievalCorpus.Embed(temporary.DatabaseFile, DevbookRetrievalCorpus.LadderPath, "some-other-model", 1f, 0f, 0f);

        var reader = new DevbookSemanticSearch(new StubDevbookFolderSource(temporary.RootDirectory), "some-other-model");

        Assert.Equal("some-other-model", reader.Model);
        Assert.Single(reader.Nearest(Question).Hits);
    }

    /// <summary>
    /// A vector of a different width cannot be compared with the question, so it
    /// is dropped rather than compared against its first three components. This is
    /// the shape a half-written or truncated row has.
    /// </summary>
    [Fact]
    public void A_vector_of_a_different_width_is_ignored()
    {
        using var temporary = new TemporaryDatabase();
        DevbookRetrievalCorpus.Seed(temporary.DatabaseFile);
        DevbookRetrievalCorpus.Embed(temporary.DatabaseFile, DevbookRetrievalCorpus.LadderPath, DevbookRetrievalCorpus.Model, 1f, 0f, 0f, 0f, 0f);

        Assert.Empty(Nearest(temporary).Nearest(Question).Hits);
    }

    /// <summary>A blob that is not as many floats as the row claims is not a
    /// vector, and padding it would invent a direction nobody embedded.</summary>
    [Fact]
    public void A_blob_shorter_than_its_declared_dimensions_is_ignored()
    {
        using var temporary = new TemporaryDatabase();
        DevbookRetrievalCorpus.Seed(temporary.DatabaseFile);
        DevbookRetrievalCorpus.EmbedMalformed(
            temporary.DatabaseFile,
            DevbookRetrievalCorpus.LadderPath,
            DevbookRetrievalCorpus.Model,
            dimensions: 3,
            bytes: [1, 2, 3]);

        Assert.Empty(Nearest(temporary).Nearest(Question).Hits);
    }

    /// <summary>Rung five: the index is there and has no vectors. Search by
    /// meaning is absent, not broken, so this is an empty answer and not the
    /// sentence about a missing index.</summary>
    [Fact]
    public void A_database_with_no_vectors_is_empty_rather_than_unavailable()
    {
        using var temporary = new TemporaryDatabase();
        DevbookRetrievalCorpus.Seed(temporary.DatabaseFile);

        var answer = Nearest(temporary).Nearest(Question);

        Assert.Empty(answer.Hits);
        Assert.False(answer.IsUnavailable);
    }

    /// <summary>Rung six, for this tier too, and with the same command in it —
    /// because it is the same missing file.</summary>
    [Fact]
    public void With_no_database_search_by_meaning_is_unavailable_and_names_the_command()
    {
        using var temporary = new TemporaryDatabase();

        var answer = Nearest(temporary).Nearest(Question);

        Assert.True(answer.IsUnavailable);
        Assert.Contains(DevbookRetrieval.BuildCommand, answer.UnavailableMessage!, StringComparison.Ordinal);
        Assert.Equal(DevbookRetrieval.UnavailableMessage("Search by meaning"), answer.UnavailableMessage);
    }

    [Fact]
    public void A_scope_narrows_the_answer_to_one_area()
    {
        using var temporary = new TemporaryDatabase();
        DevbookRetrievalCorpus.Seed(temporary.DatabaseFile);
        DevbookRetrievalCorpus.Embed(temporary.DatabaseFile, DevbookRetrievalCorpus.LadderPath, DevbookRetrievalCorpus.Model, 1f, 0f, 0f);
        DevbookRetrievalCorpus.Embed(temporary.DatabaseFile, DevbookRetrievalCorpus.MentionPath, DevbookRetrievalCorpus.Model, 1f, 1f, 0f);

        var hit = Assert.Single(Nearest(temporary).Nearest(Question, scope: DevbookRetrievalCorpus.DomainScope).Hits);

        Assert.Equal(DevbookRetrievalCorpus.MentionPath, hit.Chapter.Path);
    }

    /// <summary>The default is the model the bicep deployment creates, so a reader
    /// registered with no configuration reads the database this repository's
    /// tooling would produce.</summary>
    [Fact]
    public void The_default_model_is_the_one_the_deployment_creates()
    {
        Assert.Equal("text-embedding-3-small", DevbookEmbeddingModel.Default);
        Assert.Equal(DevbookEmbeddingModel.Default, new DevbookSemanticSearch(new StubDevbookFolderSource(null)).Model);
    }

    private static IDevbookVectorSearch Nearest(TemporaryDatabase temporary) =>
        new DevbookSemanticSearch(new StubDevbookFolderSource(temporary.RootDirectory), DevbookRetrievalCorpus.Model);
}
