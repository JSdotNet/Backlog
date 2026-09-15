using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Badges;

/// <summary>
/// How a tag is drawn, in one place, because three copies of <c>"#" + tag</c> is
/// three chances to draw <c>#@bob</c>.
///
/// <para>
/// There are three kinds of tag and each carries its own sigil: <c>#deploy</c> is
/// a general tag, <c>@bob</c> is a person and <c>+release-q4</c> is a plan tag —
/// the roadmap item an entry is filed under. The difference in how they are
/// <em>stored</em> is the thing this type exists to absorb — a person or plan tag
/// keeps its sigil in the stored value, while a general tag is stored bare and
/// only ever wears its hash on screen. That asymmetry is deliberate: it is what
/// lets every tag written before people and plans existed keep meaning "general"
/// with no migration behind it.
/// </para>
///
/// <para>
/// The grammar is a restatement of the parser's, not a reference to it: the
/// component library deliberately depends on no module, so <c>EntryTextParser</c>
/// is out of reach here. The two have to agree, and the tests on each side name
/// the same examples — <c>bob@example.com</c> is nobody, <c>c++ compiler</c> is
/// no plan — so a change to one shows up as a failure on the other.
/// </para>
/// </summary>
public static class TagText
{
    /// <summary>A tag typed into a title, any kind. The <c>(?&lt;!\S)</c> guard
    /// is what stops <c>bob@example.com</c> naming a person and <c>c++</c> naming
    /// a plan: a sigil only opens a tag when nothing is welded to its left.</summary>
    private static readonly Regex TitleTagRegex =
        new(@"(?<!\S)([@#+])([A-Za-z][\w-]*)", RegexOptions.Compiled);

    /// <summary>Whether a stored tag names a person. The leading <c>@</c> is the
    /// whole of the test, because it is the whole of the difference.</summary>
    public static bool IsPerson(string? tag) =>
        !string.IsNullOrWhiteSpace(tag) && tag.TrimStart().StartsWith('@');

    /// <summary>Whether a stored tag names a roadmap item. The same test as
    /// <see cref="IsPerson"/> with the other sigil.</summary>
    public static bool IsPlan(string? tag) =>
        !string.IsNullOrWhiteSpace(tag) && tag.TrimStart().StartsWith('+');

    /// <summary>Which of the three a stored tag is, for a caller choosing a class
    /// per kind. Read off the sigil, the way <see cref="IsPerson"/> and
    /// <see cref="IsPlan"/> are, so there is one reading and not three.</summary>
    public static TagKind Kind(string? tag) =>
        IsPerson(tag) ? TagKind.Person : IsPlan(tag) ? TagKind.Plan : TagKind.General;

    /// <summary>What a stored tag reads as. A person or plan tag already carries
    /// its sigil; a general tag is given the hash it is always drawn with.</summary>
    public static string Display(string? tag)
    {
        var trimmed = (tag ?? string.Empty).Trim();

        if (trimmed.Length == 0) return string.Empty;

        return IsPerson(trimmed) || IsPlan(trimmed) ? trimmed : "#" + trimmed.TrimStart('#');
    }

    /// <summary>What pressing a chip does, for the accessible name — "Filter by
    /// @bob" rather than "@bob".</summary>
    public static string FilterLabel(string? tag) => "Filter by " + Display(tag);

    /// <summary>
    /// A title split into the runs of plain text and the tags between them, in
    /// document order, so a caller can draw the tags as chips <em>in place</em>
    /// without altering a character of the title around them.
    /// <para>
    /// Concatenating every segment's <see cref="TitleSegment.Text"/> reproduces the
    /// title exactly. That is the contract: the title is preserved verbatim, and
    /// this only says which parts of it are tags.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TitleSegment> Segments(string? title)
    {
        var text = title ?? string.Empty;

        if (text.Length == 0) return [];

        var segments = new List<TitleSegment>();
        var cursor = 0;

        foreach (Match match in TitleTagRegex.Matches(text))
        {
            if (match.Index > cursor)
            {
                segments.Add(new TitleSegment(text[cursor..match.Index], IsTag: false));
            }

            segments.Add(new TitleSegment(match.Value, IsTag: true));
            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length) segments.Add(new TitleSegment(text[cursor..], IsTag: false));

        return segments;
    }

    /// <summary>One run of a title: either prose or a tag, never both. The text is
    /// exactly as it appeared, sigil included, so a tag segment is already what
    /// the chip should draw.</summary>
    public sealed record TitleSegment(string Text, bool IsTag);
}

/// <summary>The three kinds of tag, by the sigil the stored value carries. A
/// general tag carries none — which is why it is the default a reader of an
/// unsigilled value lands on.</summary>
public enum TagKind
{
    General,
    Person,
    Plan
}
