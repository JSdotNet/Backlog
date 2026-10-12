using System.Globalization;
using System.Text;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.Abstractions;

/// <summary>
/// The shelf of plans whose tasks arrived first (ADR 0013, ruling 3): which imported plans
/// no item carries yet, and the title an item made from one is given.
/// <para>
/// Here, beside the abstractions, because two readers offer the same shelf — the roadmap
/// band and the Tasks Calendar — and a shelf decided twice is a shelf that disagrees with
/// itself about which plans are waiting.
/// </para>
/// </summary>
public static class RoadmapShelf
{
    /// <summary>What a tag of only punctuation or diacritics folds to — the same value the
    /// module gives such a tag.</summary>
    private const string Fallback = "item";

    /// <summary>
    /// The imported plans no item on <paramref name="plan"/> carries the tag of, in the order
    /// given. Compared as slugs (<see cref="SlugOf"/>), the way the module stores an item's
    /// tag, so a plan tag written with a capital or an underscore is still recognised as
    /// planned once its item exists.
    /// </summary>
    public static IReadOnlyList<ImportedPlanDto> Unplanned(IReadOnlyList<ImportedPlanDto> imported, RoadmapPlanDto plan)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(plan);

        var planned = plan.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Tag))
            .Select(item => SlugOf(item.Tag))
            .ToHashSet(StringComparer.Ordinal);

        return [.. imported.Where(candidate => !planned.Contains(SlugOf(candidate.Tag)))];
    }

    /// <summary>Whether <paramref name="tag"/> names the same plan as
    /// <paramref name="other"/> once both are slugs.</summary>
    public static bool SameTag(string? tag, string? other) =>
        string.Equals(SlugOf(tag), SlugOf(other), StringComparison.Ordinal);

    /// <summary>An item's title read off its tag — the only name a tasks-first plan has.
    /// <c>release-q4</c> becomes <c>Release q4</c>; the editor is where a person gives it a
    /// better one.</summary>
    public static string TitleOf(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        var words = tag.Replace('-', ' ').Replace('_', ' ').Trim();
        return words.Length == 0
            ? tag
            : char.ToUpper(words[0], CultureInfo.InvariantCulture) + words[1..];
    }

    /// <summary>
    /// The slug the module stores a tag or title as: diacritics folded, lower case, every run
    /// of anything but letters and digits one hyphen, none at either end. A restatement of
    /// the module's own <c>PlanningTag</c> rule, which the abstractions cannot reach; the
    /// module stays the authority, since every save re-normalizes through it.
    /// </summary>
    public static string SlugOf(string? title)
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
