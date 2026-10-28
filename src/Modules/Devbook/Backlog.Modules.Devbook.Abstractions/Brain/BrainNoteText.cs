using System.Text;
using System.Text.RegularExpressions;

namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The text of a new note in the Brain (local ADR 0026), and where it goes: the
/// one composer behind every writer of one — the Devbook pane's <b>Capture</b>,
/// Inbox triage's <b>To Brain</b>, and the capture tool an assistant calls
/// (<c>.devbook/domain/devbook/features.md#capture-into-the-brain</c>).
///
/// <para>A note is a devbook chapter: its title as the <c>#</c> heading, the
/// <c>meta</c> block under it, then the body. Whatever the person typed is text
/// and stays text: a line of the body the devbook parse would read as a heading or
/// a fence — and so as a chapter, or as a <c>meta</c> or <c>annotation</c> block —
/// is written with <see cref="LineShield"/>, a zero-width space, just before its
/// marker. No reader of the file sees it, and none of them takes the line for
/// structure after it: not the devbook parse, whose whitespace is JavaScript's and
/// leaves U+200B out; not the app's own Markdown view (<c>MarkdownPreview</c>),
/// which trims .NET whitespace and leaves it out too; and not CommonMark, where it
/// is an ordinary character that starts a paragraph. A backslash, CommonMark's own
/// escape, was the first choice and is not used because the app's Markdown view
/// does not read escapes and showed the backslashes.</para>
///
/// <para>Shielded rather than refused, because a capture is the person's own words
/// in their own folder and the requirement is that they read back as text;
/// refused would lose the thought the capture was for. The rules follow the
/// builder's parse (<c>DevbookMarkdown</c>): a fence at any indentation, a heading
/// at up to three spaces.</para>
/// </summary>
public static partial class BrainNoteText
{
    /// <summary>What goes before the marker of a line that would otherwise become
    /// structure: U+200B, zero-width space.</summary>
    public const string LineShield = "​";

    /// <summary>The <c>type</c> every note's <c>meta</c> block carries.</summary>
    public const string NoteType = "note";

    /// <summary>The longest file name a title makes, before <c>.md</c>.</summary>
    public const int MaxFileStemLength = 60;

    /// <summary>The file name a title with nothing usable in it makes.</summary>
    public const string FallbackStem = "note";

    /// <summary>File names Windows keeps for devices, which no note may take.</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    /// <summary>The note's source as its <c>meta</c> block spells it
    /// (<c>.devbook/domain/devbook/domain.md#note-source</c>).</summary>
    public static string SourceSlug(BrainNoteSource source) => source switch
    {
        BrainNoteSource.Inbox => "inbox",
        BrainNoteSource.Import => "import",
        _ => "manual",
    };

    /// <summary>A title as one line — line breaks and tabs become spaces, runs of
    /// spaces one — or null when nothing is left.</summary>
    public static string? TitleOf(string? title)
    {
        if (title is null) return null;

        var line = Whitespace().Replace(title, " ").Trim();
        return line.Length == 0 ? null : line;
    }

    /// <summary>
    /// The whole note: <paramref name="title"/> as its heading, the <c>meta</c>
    /// block, and <paramref name="bodyMd"/> guarded by <see cref="GuardBody"/>.
    /// Line endings are <c>\n</c>, and the text ends with one.
    /// </summary>
    /// <exception cref="ArgumentException">The title is blank.</exception>
    public static string Compose(string title, string? bodyMd, BrainNoteSource source)
    {
        var heading = TitleOf(title) ?? throw new ArgumentException("A note needs a title.", nameof(title));

        var text = new StringBuilder()
            .Append("# ").Append(GuardHeading(heading)).Append('\n')
            .Append('\n')
            .Append("```meta\n")
            .Append("type: ").Append(NoteType).Append('\n')
            .Append("status: draft\n")
            .Append("source: ").Append(SourceSlug(source)).Append('\n')
            .Append("```\n");

        var body = GuardBody(bodyMd);
        if (body.Length > 0) text.Append('\n').Append(body).Append('\n');

        return text.ToString();
    }

    /// <summary>
    /// <paramref name="bodyMd"/> with every line that would become structure
    /// shielded: a fence line, a heading line, and a setext underline under a line
    /// of text. Line endings become <c>\n</c>; blank lines at either end go.
    /// Empty for a null or blank body.
    /// </summary>
    public static string GuardBody(string? bodyMd)
    {
        if (string.IsNullOrWhiteSpace(bodyMd)) return string.Empty;

        var lines = bodyMd.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var start = 0;
        var end = lines.Length;
        while (start < end && lines[start].Trim().Length == 0) start++;
        while (end > start && lines[end - 1].Trim().Length == 0) end--;

        var guarded = new List<string>(end - start);
        for (var i = start; i < end; i++)
        {
            var line = lines[i].TrimEnd();
            var previous = guarded.Count > 0 ? guarded[^1] : string.Empty;
            guarded.Add(Guard(line, previous));
        }

        return string.Join('\n', guarded);
    }

    private static string Guard(string line, string previous)
    {
        if (Fence().Match(line) is { Success: true } fence) return Shield(line, fence.Groups[2].Index);
        if (Heading().Match(line) is { Success: true } heading) return Shield(line, heading.Groups[2].Index);

        // "Title\n===" draws a heading as surely as "# Title" does. A rule after
        // a blank line is a rule, and is left as one.
        if (previous.Trim().Length > 0 && SetextUnderline().Match(line) is { Success: true } underline)
        {
            return Shield(line, underline.Groups[2].Index);
        }

        return line;
    }

    private static string Shield(string line, int markerAt) => line.Insert(markerAt, LineShield);

    /// <summary>
    /// A one-line title as the text of the note's <c>#</c> heading: a run of
    /// <c>#</c> that ends it after a space — <c>Ideas for C #</c> — or that is the
    /// whole of it is shielded, because CommonMark reads that run as the heading's
    /// optional closing sequence and drops it, so the note would render as
    /// <c>Ideas for C</c> in any Markdown reader that follows the spec. A run
    /// joined to a word, <c>C#</c>, is text there already and left alone.
    /// </summary>
    public static string GuardHeading(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        return ClosingSequence().Match(title) is { Success: true } closing
            ? Shield(title, closing.Groups[2].Index)
            : title;
    }

    /// <summary>
    /// The file name <paramref name="title"/> makes, without <c>.md</c>: its
    /// letters and digits, lower case, words joined by hyphens, at most
    /// <see cref="MaxFileStemLength"/> long — <c>Ideas for the sync</c> is
    /// <c>ideas-for-the-sync</c>. Never a path, never a name Windows keeps for a
    /// device, and <see cref="FallbackStem"/> when the title has nothing usable.
    /// </summary>
    public static string FileStemFor(string? title)
    {
        var words = new StringBuilder();
        var pendingHyphen = false;

        foreach (var c in (title ?? string.Empty).Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingHyphen && words.Length > 0) words.Append('-');
                pendingHyphen = false;
                words.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var stem = words.ToString();
        if (stem.Length > MaxFileStemLength) stem = stem[..MaxFileStemLength].TrimEnd('-');
        if (stem.Length == 0) return FallbackStem;

        return ReservedNames.Contains(stem) ? stem + "-" + FallbackStem : stem;
    }

    /// <summary>
    /// The area a new note lands in: the first area the Brain's conventions name
    /// the inbox — named <c>Inbox</c>, or with the folder <c>_inbox</c> or
    /// <c>inbox</c> — or the PARA inbox, <c>_inbox</c>, when they name none.
    /// <para>
    /// The PARA inbox is no fallback while <paramref name="ignored"/> lists it
    /// (<see cref="BrainConventions.Ignored"/>): an ignored folder has no tab and is
    /// not indexed, so a capture there would vanish from view. The first area takes
    /// it then, and with no area at all the PARA inbox still does.
    /// </para>
    /// </summary>
    public static BrainArea InboxArea(IReadOnlyList<BrainArea> areas, IReadOnlyCollection<string>? ignored = null)
    {
        ArgumentNullException.ThrowIfNull(areas);

        if (areas.FirstOrDefault(IsInbox) is { } inbox) return inbox;

        var fallback = BrainConventions.Para[0];
        var fallbackIgnored = ignored?.Contains(fallback.Folder, StringComparer.OrdinalIgnoreCase) == true;
        return fallbackIgnored && areas.Count > 0 ? areas[0] : fallback;
    }

    private static bool IsInbox(BrainArea area) =>
        string.Equals(area.Name.Trim(), "Inbox", StringComparison.OrdinalIgnoreCase)
        || string.Equals(area.Folder.TrimStart('_'), "inbox", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>JavaScript's <c>\s</c>, which the devbook parse uses — the
    /// generator's and its C# port's (<c>DevbookMarkdown.JsWhitespaceClass</c>) —
    /// and which is not .NET's: U+FEFF is in it, U+0085 is not. Spelt out here
    /// because this project references nothing; <c>BrainNoteTextTests</c> reads
    /// the guard back through that parse, so the two cannot drift apart.</summary>
    private const string JsWhitespace = "\\t\\n\\v\\f\\r \\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000\\ufeff";

    /// <summary>The parse's fence: any indentation, then three or more backticks
    /// or tildes.</summary>
    [GeneratedRegex("^([" + JsWhitespace + "]*)(`{3,}|~{3,})")]
    private static partial Regex Fence();

    /// <summary>An ATX heading: up to three spaces, one to six hashes, then
    /// whitespace or the end of the line. <c>#deploy</c> is a tag and not one.</summary>
    [GeneratedRegex("^( {0,3})(#{1,6})(?=[" + JsWhitespace + "]|$)")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^( {0,3})(=+|-+)[ \t]*$")]
    private static partial Regex SetextUnderline();

    /// <summary>CommonMark's closing sequence of an ATX heading: a run of
    /// <c>#</c> at the end, after a space or a tab or as the whole text.</summary>
    [GeneratedRegex(@"(^|[ \t])(#+)[ \t]*$")]
    private static partial Regex ClosingSequence();
}

/// <summary>Where a note came from (<c>.devbook/domain/devbook/domain.md#note-source</c>):
/// written in the Brain — the pane's Capture, or an assistant's capture tool —
/// routed To Brain from Inbox triage, or imported.</summary>
public enum BrainNoteSource
{
    Manual,
    Inbox,
    Import,
}
