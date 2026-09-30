using System.Text;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// Whether two titles are the same thought captured twice: equal once case,
/// punctuation and spacing are set aside, or within one edit in ten of it.
/// <para>
/// Deterministic, so the reason on screen is the whole reason — no model, no
/// weighting, nothing learned. The minimum length is what keeps it honest:
/// "Fix bug" and "Fix bugs" are a tenth apart by edits but are two short labels,
/// not one capture, and below <see cref="MinLength"/> characters two titles say
/// too little to be told apart from ordinary words.
/// </para>
/// </summary>
internal static class TitleSimilarity
{
    /// <summary>The fewest characters both normalised titles need before they
    /// can be called the same.</summary>
    internal const int MinLength = 8;

    /// <summary>The most edits allowed per <see cref="EditsPer"/> characters of
    /// the longer title: one in ten, a ratio of at least 0.9. Kept as two
    /// integers so the boundary is exact — "fix login" and "fix logins" are one
    /// edit in ten and match, where a ratio held in a double sits a hair under
    /// 0.1 and would refuse them.</summary>
    internal const int MaxEdits = 1;

    /// <summary>The characters <see cref="MaxEdits"/> is counted against.</summary>
    internal const int EditsPer = 10;

    /// <summary>The title lower-cased, with every run of punctuation and
    /// whitespace made one space and the ends trimmed: "Set-up  CI!" reads
    /// "set up ci".</summary>
    public static string Normalise(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;

        var builder = new StringBuilder(title.Length);
        var gap = false;

        foreach (var character in title.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (gap && builder.Length > 0) builder.Append(' ');
                builder.Append(char.ToLowerInvariant(character));
                gap = false;
            }
            else
            {
                gap = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>Whether the two titles are near-identical: both at least
    /// <see cref="MinLength"/> characters once normalised, and equal then or
    /// with at most <see cref="MaxEdits"/> edit per <see cref="EditsPer"/> characters
    /// of the longer one.</summary>
    public static bool IsNearIdentical(string? a, string? b)
    {
        var left = Normalise(a);
        var right = Normalise(b);

        if (left.Length < MinLength || right.Length < MinLength) return false;
        if (string.Equals(left, right, StringComparison.Ordinal)) return true;

        var longer = Math.Max(left.Length, right.Length);

        // Titles more than a tenth apart in length cannot meet the ratio, and
        // this spares the distance for the pairs that plainly differ. Both sides
        // multiplied out, so exactly a tenth is still in.
        if (Math.Abs(left.Length - right.Length) * EditsPer > longer * MaxEdits) return false;

        return Distance(left, right) * EditsPer <= longer * MaxEdits;
    }

    /// <summary>The Levenshtein distance: the fewest single-character inserts,
    /// deletes and substitutions that turn one into the other. Two rows rather
    /// than the whole table, because only the last row is ever read.</summary>
    internal static int Distance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var j = 0; j <= right.Length; j++) previous[j] = j;

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= right.Length; j++)
            {
                var substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
