using System.Globalization;
using System.Text;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// Derives a tag slug from a title, for the editor to pre-fill the Tag field with.
/// <para>
/// A deliberate restatement of the module's own <c>PlanningTag.From</c> rather than a
/// call to it: the UI references the plan's Abstractions, not its domain, so it cannot
/// reach the value object — the same boundary the DTO layer keeps. The module remains
/// the authority: whatever this pre-fills, the save re-normalizes through
/// <c>PlanningTag.Of</c>, so the two only ever differ for the instant before a save.
/// The rule is kept identical here so that instant shows the person the value they are
/// about to store rather than one that will change under them.
/// </para>
/// </summary>
internal static class RoadmapTagSlug
{
    /// <summary>What a title of only punctuation or diacritics falls back to — the
    /// same stable value the module uses, so a pre-fill never shows an empty field for
    /// a title that will still produce a tag.</summary>
    private const string Fallback = "item";

    public static string From(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return Fallback;

        var folded = title.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(folded.Length);
        var pendingHyphen = false;

        foreach (var ch in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsLetterOrDigit(ch))
            {
                if (pendingHyphen && builder.Length > 0) builder.Append('-');
                pendingHyphen = false;
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var slug = builder.ToString();
        return slug.Length == 0 ? Fallback : slug;
    }
}
