using System.Text.RegularExpressions;

using Backlog.SharedKernel.Markdown;
using Backlog.SharedKernel.Metadata;

namespace Backlog.Infrastructure.Devbook.Documents;

/// <summary>
/// What the devbook panels' block readers take from
/// <see cref="MarkdownPreview.ParseDocument"/> beyond its blocks: a heading's text as
/// written, the <c>meta</c> fence under it, a list's items flat, and the source lines
/// a block was read from.
/// <para>
/// The arc42 and the design and AI readers used to walk the lines themselves, each
/// with its own idea of a list, a table and a fence. They now read the shared block
/// tree and keep only their own view models; these are the few pieces of that
/// mapping both make.
/// </para>
/// </summary>
internal static partial class DevbookMarkdownBlocks
{
    /// <summary>The body split the way <see cref="MarkdownPreview"/> splits it, with
    /// a lone <c>\r</c> read as a break too — the panels always have — so a block's
    /// <see cref="MdBlock.Source"/> indexes these lines.</summary>
    public static string Normalize(string markdown) => markdown.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>The heading's text as the author wrote it — markers off, trimmed,
    /// inline syntax kept — read from its own line.</summary>
    public static string HeadingText(IReadOnlyList<string> lines, MdHeading heading)
    {
        var line = heading.Source.IsKnown && heading.Source.StartLine < lines.Count ? lines[heading.Source.StartLine].Trim() : string.Empty;
        var match = HeadingLine().Match(line);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    /// <summary>
    /// The <c>meta</c> fence at <paramref name="index"/>, read, with the index moved
    /// past it; null, and the index left alone, when the block there is not one. Only
    /// the block directly after a heading is its metadata: blank lines make no
    /// block, so that is the first thing written under it.
    /// </summary>
    public static MetadataRecord? TakeMeta(IReadOnlyList<MdBlock> blocks, ref int index)
    {
        if (index >= blocks.Count || blocks[index] is not MdCode code || !MetadataReader.IsMetaBlock(code.Language)) return null;

        index++;
        return MetadataReader.Parse(code.Text);
    }

    /// <summary>The lines a block was read from.</summary>
    public static IReadOnlyList<string> SourceLines(IReadOnlyList<string> lines, MdSourceSpan span) =>
        span.IsKnown
            ? [.. lines.Skip(span.StartLine).Take(Math.Min(span.EndLineExclusive, lines.Count) - span.StartLine)]
            : [];

    /// <summary>
    /// A list's items in reading order, the items nested under one following it: the
    /// panels draw one level. A checklist item keeps its box as the text it was, as
    /// it read before the shared parse took the box off.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<MdInline>> FlatItems(MdList list)
    {
        var items = new List<IReadOnlyList<MdInline>>();
        Collect(list, items);
        return items;

        static void Collect(MdList list, List<IReadOnlyList<MdInline>> items)
        {
            foreach (var item in list.Items)
            {
                items.Add(item.Done is { } done ? [new MdText(done ? "[x] " : "[ ] "), .. item.Content] : item.Content);
                foreach (var nested in item.Nested) Collect(nested, items);
            }
        }
    }

    /// <summary>The notes of a footnote block, each led by its number.</summary>
    public static IReadOnlyList<IReadOnlyList<MdInline>> NoteItems(MdFootnotes footnotes) =>
        [.. footnotes.Notes.Select(note => (IReadOnlyList<MdInline>)[new MdText($"[{note.Number}] "), .. note.Content])];

    [GeneratedRegex(@"^#{1,6}[ \t]+(.*)$")]
    private static partial Regex HeadingLine();
}
