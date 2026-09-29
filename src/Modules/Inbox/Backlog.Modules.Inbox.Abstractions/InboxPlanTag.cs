using System.Text;

namespace Backlog.Modules.Inbox.Abstractions;

/// <summary>
/// The shape of a plan tag the Inbox mints: a slug with eight hex digits of an
/// id on the end. Published so the pane can show a batch's tag before the
/// module has minted it — the slug is known up front, the digits are not.
/// </summary>
public static class InboxPlanTag
{
    /// <summary>The longest tag the slug becomes, suffix included. Long enough
    /// to keep a title recognisable on the metadata line, short enough that the
    /// line stays one.</summary>
    private const int MaxLength = 40;

    /// <summary>How many hex digits of the id go on the end of the tag.
    /// Thirty-two random bits: enough that two items in one inbox cannot share
    /// a tag by accident, short enough that the title still reads.</summary>
    private const int SuffixLength = 8;

    /// <summary>
    /// The title as a tag, with the id on the end: lower-case letters, digits
    /// and hyphens, at most <see cref="MaxLength"/> long, and opening with a
    /// letter so it is a legal <c>#tag</c> on the metadata line (the grammar
    /// reads a tag as <c>[A-Za-z][\w-]*</c>). No sigil: the caller adds the
    /// <c>+</c> where the tag is written as a plan tag.
    /// <para>
    /// The suffix is what makes the tag the <em>item's</em> — or the batch's —
    /// and not the title's. On the Tasks side the tag is the plan's id (ADR
    /// 0007's <c>import_plan_id</c>), and an import clears every not-started
    /// entry under it before writing its own — so two plans with one title, or
    /// two whose titles both slug to nothing, would each tombstone the other's.
    /// The last eight hex digits rather than the first: a version-7 guid opens
    /// with a timestamp whose leading digits are shared by every id minted in
    /// the same minute, and the random bits are at the tail.
    /// </para>
    /// </summary>
    public static string For(string title, Guid id)
    {
        var suffix = id.ToString("N")[^SuffixLength..];
        var stemLength = MaxLength - SuffixLength - 1;

        var slug = new StringBuilder(stemLength);
        var pendingHyphen = false;

        foreach (var character in (title ?? string.Empty).ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                if (pendingHyphen && slug.Length > 0) slug.Append('-');
                pendingHyphen = false;
                slug.Append(character);
                if (slug.Length >= stemLength) break;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var value = slug.ToString().TrimEnd('-');

        if (value.Length == 0) return $"inbox-plan-{suffix}";
        if (char.IsAsciiLetter(value[0])) return $"{value}-{suffix}";

        const string prefix = "plan-";
        return prefix + value[..Math.Min(value.Length, stemLength - prefix.Length)].TrimEnd('-') + "-" + suffix;
    }

    /// <summary>What a plan tag for <paramref name="title"/> will look like
    /// before its id exists: the sigil and the slug, with an ellipsis where the
    /// digits go — <c>+reading-list-…</c>.</summary>
    public static string Preview(string title) => "+" + For(title, Guid.Empty)[..^SuffixLength] + "…";
}
