namespace Backlog.Infrastructure.Knowledge.UnitTests;

/// <summary>
/// The byte layout of <c>chapter_embedding.vector</c>, pinned from both sides.
///
/// <para>Every other column of this schema declares its own type, so SQLite keeps
/// the two languages honest about it. A <c>BLOB</c> declares nothing: whoever
/// writes it and whoever reads it have to agree on a layout that appears in
/// neither the DDL nor the type system, and a disagreement produces vectors that
/// decode to plausible-looking numbers and rank the wrong chapters. That is
/// precisely the silent cross-language drift local ADR 0004 names as this
/// change's main risk, in its purest form.</para>
///
/// <para>So the layout is written down beside the table in
/// <c>tools/knowledge/knowledge-schema.mjs</c> — <c>dimensions</c> IEEE-754
/// singles, little-endian, packed, no header — this class names that file, and
/// that file names this class. The same pairing <c>normalizeDiagramSource</c> and
/// <c>DiagramSourceHash</c> already have, for the same reason: the failure is
/// invisible from inside either language.</para>
/// </summary>
public sealed class KnowledgeVectorEncodingTests
{
    /// <summary>
    /// A vector written the way the schema describes comes back as the same
    /// numbers. The values are chosen to fail on the mistakes that matter: a
    /// negative and a fraction catch a sign or exponent read the wrong way round,
    /// and a byte-reversed decode of any of them is a different number rather than
    /// a near miss.
    /// </summary>
    [Fact]
    public void A_vector_round_trips_through_the_blob()
    {
        float[] written = [1f, -0.5f, 0.25f, 1234.5f];

        using var temporary = new TemporaryDatabase();
        KnowledgeRetrievalCorpus.Seed(temporary.DatabaseFile);
        KnowledgeRetrievalCorpus.Embed(
            temporary.DatabaseFile,
            KnowledgeRetrievalCorpus.LadderPath,
            KnowledgeRetrievalCorpus.Model,
            written);

        using var database = KnowledgeDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        var row = Assert.Single(database.Embeddings(KnowledgeRetrievalCorpus.Model, scope: null));

        Assert.Equal(written.Length, row.Dimensions);
        Assert.Equal(written, row.Vector);
    }

    /// <summary>The layout is little-endian whatever the machine is, so the bytes
    /// a database carries are the same bytes on every head that reads it.</summary>
    [Fact]
    public void The_layout_is_four_little_endian_bytes_per_value()
    {
        Assert.Equal(new byte[] { 0x00, 0x00, 0x80, 0x3F }, KnowledgeRetrievalCorpus.Encode(1f));
        Assert.Equal(8, KnowledgeRetrievalCorpus.Encode(1f, 2f).Length);
    }

    /// <summary>
    /// A comparison that cannot be made answers zero rather than throwing or
    /// guessing. Zero is what the reader drops, so this is what keeps one
    /// malformed row from either taking the query with it or ranking above real
    /// answers.
    /// </summary>
    [Theory]
    [InlineData(new float[] { 1f, 0f }, new float[] { 1f, 0f, 0f })]
    [InlineData(new float[] { }, new float[] { })]
    [InlineData(new float[] { 0f, 0f }, new float[] { 1f, 0f })]
    public void An_incomparable_pair_scores_zero(float[] left, float[] right) =>
        Assert.Equal(0d, KnowledgeVectorMath.Cosine(left, right));

    /// <summary>Length does not count, only direction — which is why the reader
    /// divides by both norms rather than taking a dot product and trusting the
    /// writer to have normalised.</summary>
    [Fact]
    public void Cosine_ignores_length()
    {
        Assert.Equal(1d, KnowledgeVectorMath.Cosine([1f, 0f], [1000f, 0f]), 6);
        Assert.Equal(-1d, KnowledgeVectorMath.Cosine([1f, 0f], [-3f, 0f]), 6);
    }
}
