using Backlog.UI.Components.Metadata;

namespace Backlog.UI.Components.Markdown;

/// <summary>
/// Whether a document opens with a title that carries a metadata block, and what
/// that block says.
///
/// <para>The convention writes two kinds of <c>meta</c> fence into one file: one
/// under every <c>##</c> chapter, describing that chapter, and one under the
/// <c>#</c> title, describing the file itself. Only the second is about the file,
/// and only the second is drawn somewhere other than the flow of the body — which
/// is a decision two components have to agree on, because one of them stops
/// drawing it exactly where the other starts. Both ask here, so they cannot
/// answer differently and leave the file's status drawn twice or not at all.</para>
///
/// <para>Asked two ways, because the two callers hold different things. A read
/// view has the parsed blocks and needs nothing more than the shape of the first
/// two; a pane whose host supplies its own body has no blocks at all and still has
/// the file's text. The rule is the same either way: the title first, the fence
/// directly under it, and nothing in between.</para>
/// </summary>
public static class MarkdownFileMetadata
{
    /// <summary>The document's own title and the record written under it. The
    /// title travels with the metadata because a host is told about a status
    /// change by the name it knows the chapter as, and in the file that name is
    /// the heading.</summary>
    /// <param name="Title">The heading's text, as the file spells it.</param>
    /// <param name="Metadata">The block under it, read.</param>
    public sealed record FileRecord(string Title, MetadataRecord Metadata);

    /// <summary>
    /// Whether parsed blocks open with a level-one heading and its metadata fence.
    ///
    /// <para>The shape only — the fence itself is never read. A renderer asks this
    /// once per block per render to work out whose turn it is, and parsing the
    /// record to answer a question about position would be the same block parsed
    /// once for every paragraph in the file.</para>
    /// </summary>
    public static bool OpensWithFileBlock(IReadOnlyList<MdBlock> blocks) =>
        blocks.Count > 1
        && blocks[0] is MdHeading { Level: 1 }
        && blocks[1] is MdCode fence
        && MetadataReader.IsMetaBlock(fence.Language);

    /// <summary>
    /// The same reading, off the markdown itself, for the caller that has no
    /// blocks to look at.
    ///
    /// <para>It gives up the moment the shape does not hold, and it has to: this
    /// runs on every render of a pane whose body is being typed into, so it reads
    /// the top of the file rather than the file.</para>
    ///
    /// <para>Leading YAML frontmatter is <em>not</em> stepped over. A document that
    /// opens with <c>---</c> has no file-level block by this rule, which is
    /// deliberate — the block reading sees whatever the parser made of those
    /// lines, and a text reading that quietly skipped them would find a record the
    /// other one cannot. A caller showing frontmatter as a strip of its own hands
    /// in the body it took the block out of, and the two agree again.</para>
    /// </summary>
    public static FileRecord? Read(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;

        // Split on '\n' with the '\r' left on the end of each line, the way the
        // frontmatter reader does: every test below trims, and normalising the
        // whole text would rewrite a file this only means to read a few lines of.
        var lines = markdown.Split('\n');

        var index = NextContent(lines, 0);
        if (index >= lines.Length || Title(lines[index]) is not { } title) return null;

        index = NextContent(lines, index + 1);
        if (index >= lines.Length || OpeningFence(lines[index]) is not { } fence) return null;

        var body = new List<string>();

        // An unclosed fence runs to the end of the document. That is what the
        // document parser does with one, and it is what a file being typed into
        // looks like between the opening fence and the closing one.
        for (index++; index < lines.Length && !Closes(lines[index], fence); index++)
        {
            body.Add(lines[index]);
        }

        return new FileRecord(title, MetadataReader.Parse(string.Join('\n', body)));
    }

    /// <summary>The next line with something on it. Blank lines are the file
    /// breathing, not the document starting with something else.</summary>
    private static int NextContent(string[] lines, int from)
    {
        var index = from;
        while (index < lines.Length && lines[index].Trim().Length == 0) index++;
        return index;
    }

    /// <summary>The title, if this line is one: exactly one <c>#</c> and then a
    /// space, which is the file's own heading and not a chapter's.</summary>
    private static string? Title(string line)
    {
        var text = line.TrimStart();

        // The second character carries both halves of the test: a space or a tab
        // there means the run of hashes was one long, which is the level, and that
        // the hash opened a heading rather than a tag.
        return text.Length > 1 && text[0] == '#' && (text[1] == ' ' || text[1] == '\t')
            ? text[2..].Trim()
            : null;
    }

    /// <summary>The fence marker this line opens a <c>meta</c> block with, or null
    /// when it opens no such thing. The marker comes back because the closing
    /// fence has to match it.</summary>
    private static string? OpeningFence(string line)
    {
        var text = line.TrimStart();

        // Both markers, because both are markdown, even though this repository
        // writes the backtick one everywhere.
        var marker = text.StartsWith("```", StringComparison.Ordinal)
            ? "```"
            : text.StartsWith("~~~", StringComparison.Ordinal) ? "~~~" : null;

        if (marker is null) return null;

        return MetadataReader.IsMetaBlock(text[marker.Length..]) ? marker : null;
    }

    /// <summary>Whether this line closes the block: the marker it was opened with,
    /// leading whitespace allowed. The same test the document parser makes, which
    /// is the point — a body that ended in a different place here would be a
    /// record read from lines the reader is looking at as prose.</summary>
    private static bool Closes(string line, string marker) =>
        line.Trim().StartsWith(marker, StringComparison.Ordinal);
}
