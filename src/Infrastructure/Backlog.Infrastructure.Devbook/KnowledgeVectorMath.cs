namespace Backlog.Infrastructure.Knowledge;

/// <summary>
/// Cosine similarity, computed in the reader.
///
/// <para>This is the whole of the "vector index" local ADR 0004 declines to add.
/// The corpus is 639 chapters; a scan over a few thousand vectors costs less than
/// the query that fetched them, and the alternative — <c>sqlite-vec</c> or an
/// equivalent — is a native loadable extension, which is a deployment problem
/// inside an MSIX package and on Android. So the arithmetic lives here, behind
/// <see cref="IKnowledgeVectorSearch"/>, where an index can replace it later
/// without a caller moving.</para>
///
/// <para>Cosine rather than a dot product because nothing guarantees the stored
/// vectors are normalised: the model that produced them may change, and a
/// database built by an older run may hold vectors of a different scale. Dividing
/// by the norms costs one pass and removes a silent assumption.</para>
/// </summary>
internal static class KnowledgeVectorMath
{
    /// <summary>
    /// How nearly two vectors point the same way, from -1 to 1.
    ///
    /// <para>Zero for anything that cannot be compared: different widths, or a
    /// vector with no length at all. That is the honest answer rather than a
    /// defensive one — a similarity between vectors of different widths is not a
    /// small number, it is not a number, and a zero-length vector has no
    /// direction to be similar to. Both sort to the bottom and neither throws,
    /// because a single malformed row must not take the query with it.</para>
    /// </summary>
    public static double Cosine(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length == 0 || left.Length != right.Length) return 0;

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;

        for (var index = 0; index < left.Length; index++)
        {
            double a = left[index];
            double b = right[index];

            dot += a * b;
            leftNorm += a * a;
            rightNorm += b * b;
        }

        if (leftNorm <= 0 || rightNorm <= 0) return 0;

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }
}
