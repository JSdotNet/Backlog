namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>What <see cref="EntryTextMerge.Merge"/> made of two edits to one
/// entry. <paramref name="Overlapped"/> is true when both edits changed the same
/// lines differently, so the text holds both versions — or, on the title and
/// metadata lines, only the reader's — and somebody has to look.</summary>
public sealed record EntryTextMergeResult(string Text, bool Overlapped);

/// <summary>
/// Carries a write somebody else made to an entry onto the reader's own edit of
/// it: a line-wise three-way merge of the entry text.
/// <para>
/// An entry is saved whole — the text is the entry — so an edit built on the text
/// as it stood before another writer's change would put that older text back and
/// erase the change. Given the text both edits started from, the change each one
/// made is known, and the two can be applied together. Where they touched
/// different lines, both land. Where one only added lines around what the other
/// rewrote — an agent's dated comment appended to the paragraph the reader is
/// still typing at the end of — the addition is carried onto the rewrite. Where
/// both rewrote the same lines differently nothing can be chosen for the reader,
/// so both versions are kept, the reader's first, and the result says so; except
/// on the title and metadata lines, which are one line each by grammar and would
/// turn into body text if doubled, where the reader's version stands.
/// </para>
/// </summary>
public static class EntryTextMerge
{
    /// <summary>The title line and the metadata line under it.</summary>
    private const int HeadLineCount = 2;

    public static EntryTextMergeResult Merge(string baseText, string ours, string theirs)
    {
        ArgumentNullException.ThrowIfNull(baseText);
        ArgumentNullException.ThrowIfNull(ours);
        ArgumentNullException.ThrowIfNull(theirs);

        var b = Lines(baseText);
        var o = Lines(ours);
        var t = Lines(theirs);

        if (o.SequenceEqual(b)) return new(theirs, false);
        if (t.SequenceEqual(b) || t.SequenceEqual(o)) return new(ours, false);

        var oursAt = Match(b, o);
        var theirsAt = Match(b, t);

        var merged = new List<string>();
        var overlapped = false;
        int i = 0, x = 0, y = 0;

        while (true)
        {
            // The next base line both sides kept is an anchor; everything before
            // it on each side is one chunk to resolve.
            var j = i;
            while (j < b.Length && (oursAt[j] < 0 || theirsAt[j] < 0)) j++;

            var xEnd = j < b.Length ? oursAt[j] : o.Length;
            var yEnd = j < b.Length ? theirsAt[j] : t.Length;

            overlapped |= Resolve(b[i..j], o[x..xEnd], t[y..yEnd], atHead: i < HeadLineCount, merged);

            if (j >= b.Length) break;

            merged.Add(b[j]);
            i = j + 1;
            x = xEnd + 1;
            y = yEnd + 1;
        }

        return new(string.Join('\n', merged), overlapped);
    }

    /// <summary>Appends one chunk's resolution and answers whether the two sides
    /// overlapped.</summary>
    private static bool Resolve(string[] b, string[] o, string[] t, bool atHead, List<string> merged)
    {
        var oursChanged = !o.SequenceEqual(b);
        var theirsChanged = !t.SequenceEqual(b);

        if (!theirsChanged || o.SequenceEqual(t))
        {
            merged.AddRange(o);
            return false;
        }

        if (!oursChanged)
        {
            merged.AddRange(t);
            return false;
        }

        // One side only added lines before or after the base lines it kept whole:
        // carry the addition onto the other side's rewrite.
        if (Surrounds(t, b, out var before, out var after))
        {
            merged.AddRange(before);
            merged.AddRange(o);
            merged.AddRange(after);
            return false;
        }

        if (Surrounds(o, b, out before, out after))
        {
            merged.AddRange(before);
            merged.AddRange(t);
            merged.AddRange(after);
            return false;
        }

        merged.AddRange(o);
        if (!atHead) merged.AddRange(t);
        return true;
    }

    /// <summary>Whether <paramref name="side"/> is <paramref name="b"/> with lines
    /// only added around it.</summary>
    private static bool Surrounds(string[] side, string[] b, out string[] before, out string[] after)
    {
        for (var k = 0; k + b.Length <= side.Length; k++)
        {
            if (side.AsSpan(k, b.Length).SequenceEqual(b))
            {
                before = side[..k];
                after = side[(k + b.Length)..];
                return true;
            }
        }

        before = after = [];
        return false;
    }

    /// <summary>For each base line, the index of the line the other text kept it
    /// as, or -1 — a longest common subsequence, so the matches only ever move
    /// forward.</summary>
    private static int[] Match(string[] b, string[] other)
    {
        var lengths = new int[b.Length + 1, other.Length + 1];
        for (var i = b.Length - 1; i >= 0; i--)
        {
            for (var j = other.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = string.Equals(b[i], other[j], StringComparison.Ordinal)
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var at = new int[b.Length];
        Array.Fill(at, -1);

        int bi = 0, oi = 0;
        while (bi < b.Length && oi < other.Length)
        {
            if (string.Equals(b[bi], other[oi], StringComparison.Ordinal))
            {
                at[bi++] = oi++;
            }
            else if (lengths[bi + 1, oi] >= lengths[bi, oi + 1])
            {
                bi++;
            }
            else
            {
                oi++;
            }
        }

        return at;
    }

    private static string[] Lines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
}
