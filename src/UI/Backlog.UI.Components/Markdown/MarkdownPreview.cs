using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Markdown;

/// <summary>
/// Turns the body of an entry into a small block/inline tree for the read view —
/// what you see when an entry is not focused. Deliberately hand-written and
/// deliberately partial: it covers exactly the markdown this editor teaches
/// (headings, checklists, lists, quotes, code, emphasis, links, #tags) and
/// renders anything else as plain text, so an unfinished line in a half-typed
/// entry degrades into readable prose instead of disappearing or erroring.
/// </summary>
public static class MarkdownPreview
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})[ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex CheckboxPrefixRegex = new(@"^\[( |x|X)\][ \t]+", RegexOptions.Compiled);
    private static readonly Regex TaskItemRegex = new(@"^[ \t]*[-*][ \t]+\[( |x|X)\][ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new(@"^[ \t]*[-*][ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex OrderedRegex = new(@"^[ \t]*\d+[.)][ \t]+(.*)$", RegexOptions.Compiled);

    private static readonly Regex InlineRegex = new(
        @"(?<code>`[^`\n]+`)" +
        @"|(?<bold>\*\*[^*\n]+\*\*)" +
        @"|(?<em>(?<!\*)\*[^*\n]+\*(?!\*))" +
        @"|(?<link>\[[^\]\n]+\]\([^)\s]+\))" +
        @"|(?<tag>(?<!\S)#[A-Za-z][\w-]*)",
        RegexOptions.Compiled);

    public static IReadOnlyList<MdBlock> Parse(
        string? body,
        string? inheritedArea = null,
        IMarkdownMetadataReader? metadataReader = null)
    {
        var lines = (body ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        var blocks = new List<MdBlock>();
        var paragraph = new List<string>();
        var items = new List<MdListItem>();
        bool? ordered = null;
        var taskIndex = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new MdParagraph(ParseInlines(string.Join(" ", paragraph))));
            paragraph.Clear();
        }

        void FlushList()
        {
            if (items.Count == 0) return;
            blocks.Add(new MdList(ordered ?? false, [.. items]));
            items.Clear();
            ordered = null;
        }

        void FlushAll()
        {
            FlushParagraph();
            FlushList();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushAll();
                var language = trimmed[3..].Trim();
                var code = new List<string>();
                i++;
                while (i < lines.Length && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    code.Add(lines[i]);
                    i++;
                }

                blocks.Add(new MdCode(string.Join('\n', code), language));
                continue;
            }

            if (trimmed.Length == 0)
            {
                FlushAll();
                continue;
            }

            if (trimmed is "---" or "***" or "___")
            {
                FlushAll();
                blocks.Add(new MdDivider());
                continue;
            }

            var heading = HeadingRegex.Match(trimmed);
            if (heading.Success)
            {
                FlushAll();
                var level = heading.Groups[1].Value.Length;
                var text = heading.Groups[2].Value.Trim();
                bool? done = null;
                MarkdownMetadata? metadata = null;

                if (level is 2 or 3)
                {
                    var box = CheckboxPrefixRegex.Match(text);
                    if (box.Success)
                    {
                        done = box.Groups[1].Value is "x" or "X";
                        text = text[box.Length..].Trim();
                    }
                }

                if (level is 2 or 3
                    && metadataReader is not null
                    && i + 1 < lines.Length
                    && metadataReader.IsMetadataLine(lines[i + 1]))
                {
                    metadata = metadataReader.Read(lines[i + 1]);
                    i++;
                }

                blocks.Add(new MdHeading(level, ParseInlines(text), done, metadata, inheritedArea));
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushAll();
                blocks.Add(new MdQuote(ParseInlines(trimmed[2..])));
                continue;
            }

            var task = TaskItemRegex.Match(line);
            if (task.Success)
            {
                FlushParagraph();
                if (ordered is true) FlushList();
                ordered = false;
                items.Add(new MdListItem(task.Groups[1].Value is "x" or "X", ParseInlines(task.Groups[2].Value), taskIndex++));
                continue;
            }

            var bullet = BulletRegex.Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                if (ordered is true) FlushList();
                ordered = false;
                items.Add(new MdListItem(null, ParseInlines(bullet.Groups[1].Value), null));
                continue;
            }

            var numbered = OrderedRegex.Match(line);
            if (numbered.Success)
            {
                FlushParagraph();
                if (ordered is false) FlushList();
                ordered = true;
                items.Add(new MdListItem(null, ParseInlines(numbered.Groups[1].Value), null));
                continue;
            }

            FlushList();
            paragraph.Add(trimmed);
        }

        FlushAll();
        return GroupSubItems(blocks);
    }

    /// <summary>
    /// Folds each level-2 heading and everything under it into a single
    /// <see cref="MdSubItem"/>. A <c>##</c> heading is not really a heading in
    /// this editor — it is a sub-item — so the read view should show it as one
    /// thing with its notes attached, not as a heading followed by some
    /// unrelated paragraphs that happen to sit below it.
    /// </summary>
    private static IReadOnlyList<MdBlock> GroupSubItems(List<MdBlock> blocks)
    {
        if (!blocks.Any(b => b is MdHeading { Level: 2 or 3 })) return blocks;

        var grouped = new List<MdBlock>();
        var index = 0;

        while (index < blocks.Count)
        {
            if (blocks[index] is not MdHeading { Level: 2 or 3 } heading)
            {
                grouped.Add(blocks[index++]);
                continue;
            }

            index++;
            var children = new List<MdBlock>();
            while (index < blocks.Count && blocks[index] is not MdHeading { Level: <= 3 })
            {
                children.Add(blocks[index++]);
            }

            grouped.Add(new MdSubItem(
                heading.Content,
                heading.Metadata?.Done is true || heading.Done is true,
                heading.Done is not null,
                children,
                heading.Level,
                heading.Metadata,
                heading.Area));
        }

        return grouped;
    }

    public static IReadOnlyList<MdInline> ParseInlines(string text)
    {
        var parts = new List<MdInline>();
        var cursor = 0;

        foreach (Match match in InlineRegex.Matches(text))
        {
            if (match.Index > cursor)
            {
                parts.Add(new MdText(text[cursor..match.Index]));
            }

            var value = match.Value;
            if (match.Groups["code"].Success) parts.Add(new MdCodeSpan(value[1..^1]));
            else if (match.Groups["bold"].Success) parts.Add(new MdStrong(value[2..^2]));
            else if (match.Groups["em"].Success) parts.Add(new MdEm(value[1..^1]));
            else if (match.Groups["tag"].Success) parts.Add(new MdTag(value[1..]));
            else if (match.Groups["link"].Success)
            {
                var split = value.IndexOf("](", StringComparison.Ordinal);
                parts.Add(new MdLink(value[1..split], value[(split + 2)..^1]));
            }

            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length) parts.Add(new MdText(text[cursor..]));
        return parts;
    }
}

/// <summary>
/// Reads the metadata line that may follow a sub-item heading. The shape of that
/// metadata belongs to whoever is editing — this library only knows that a line
/// can carry some, whether it means "done", and which tags it names — so the
/// caller supplies the reader and gets its own value back in
/// <see cref="MarkdownMetadata.Value"/>.
/// </summary>
public interface IMarkdownMetadataReader
{
    bool IsMetadataLine(string line);

    MarkdownMetadata Read(string line);
}

/// <summary>What the parser keeps from a metadata line: an opaque caller-owned
/// value, whether the sub-item counts as done, and the tags it named.</summary>
public sealed record MarkdownMetadata(object? Value, bool Done, IReadOnlyList<string> Tags)
{
    public static MarkdownMetadata None { get; } = new(null, false, []);
}

public abstract record MdBlock;

/// <summary>A heading. <see cref="Done"/> is non-null only for the level-2
/// headings that carry sub-item state.</summary>
public sealed record MdHeading(
    int Level,
    IReadOnlyList<MdInline> Content,
    bool? Done,
    MarkdownMetadata? Metadata = null,
    string? Area = null) : MdBlock
{
    public IReadOnlyList<string> MetadataTags => Metadata?.Tags ?? [];
}

/// <summary>A level-2 heading and everything written beneath it — the read
/// view's rendering of a sub-item.</summary>
public sealed record MdSubItem(
    IReadOnlyList<MdInline> Title,
    bool Done,
    bool HasCheckbox,
    IReadOnlyList<MdBlock> Children,
    int Level = 2,
    MarkdownMetadata? Metadata = null,
    string? Area = null) : MdBlock
{
    public IReadOnlyList<string> MetadataTags => Metadata?.Tags ?? [];
}

public sealed record MdParagraph(IReadOnlyList<MdInline> Content) : MdBlock;

public sealed record MdList(bool Ordered, IReadOnlyList<MdListItem> Items) : MdBlock;

/// <summary><see cref="Done"/> is null for a plain bullet, non-null for a
/// checklist item.</summary>
public sealed record MdListItem(bool? Done, IReadOnlyList<MdInline> Content, int? TaskIndex);

public sealed record MdQuote(IReadOnlyList<MdInline> Content) : MdBlock;

public sealed record MdCode(string Text, string Language = "") : MdBlock;

public sealed record MdDivider : MdBlock;

public abstract record MdInline;

public sealed record MdText(string Text) : MdInline;

public sealed record MdStrong(string Text) : MdInline;

public sealed record MdEm(string Text) : MdInline;

public sealed record MdCodeSpan(string Text) : MdInline;

public sealed record MdTag(string Tag) : MdInline;

public sealed record MdLink(string Text, string Url) : MdInline;
