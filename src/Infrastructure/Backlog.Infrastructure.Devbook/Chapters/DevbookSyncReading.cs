using Backlog.Infrastructure.Devbook.Building;
using Backlog.SharedKernel.Devbook;

namespace Backlog.Infrastructure.Devbook.Chapters;

/// <summary>
/// The <c>sync</c> state of every block in one devbook file, read once: where each
/// block sits, what Backlog's direction map states for it, and the direction in
/// force there, resolved nearest-wins through its page, its context and its
/// folder overview.
///
/// <para>Two sources, each for what it knows. Where a block sits — its type, its
/// heading depth, whether it is an index root — is the Markdown's, so the file is
/// read for its headings and fences. What a block states is no longer the file's:
/// devbook's branch-true decision moved <c>sync</c> into the tool, so every level's
/// value comes from the <c>stated</c> lookup the caller hands over (address to
/// direction, <see cref="DevbookSync.Address"/>), and a <c>sync</c> line a chapter
/// still carries is read only by <see cref="DevbookSyncMarkdownImport"/>, once.</para>
///
/// <para>A devbook pane builds one per open document and cascades it as a
/// <c>DevbookSyncScope</c>, which the Devbook UI project's <c>Scope</c> extension
/// builds from <see cref="Describe"/> and <see cref="ItemPath"/>. The statements
/// are looked up when the reading is built, off the dispatcher, so a pane rebuilds
/// it when the map changes rather than asking the store on every render.</para>
/// </summary>
public sealed class DevbookSyncReading
{
    private readonly DevbookFolder _folder;
    private readonly string _documentPath;
    private readonly IReadOnlyList<StatedBlock> _blocks;
    private readonly IReadOnlyList<(DevbookSyncLevel Level, string? Value)> _above;

    private DevbookSyncReading(DevbookFolder folder, string documentPath, IReadOnlyList<StatedBlock> blocks, IReadOnlyList<(DevbookSyncLevel, string?)> above)
    {
        _folder = folder;
        _documentPath = documentPath;
        _blocks = blocks;
        _above = above;
    }

    /// <summary>A reading that places nothing, for a folder that takes no
    /// direction or a document that could not be read.</summary>
    public static DevbookSyncReading None { get; } = new(DevbookFolder.Unknown, string.Empty, [], []);

    /// <summary>
    /// Read the document's blocks and the statements for them and for the levels
    /// above it.
    /// </summary>
    /// <param name="folderRoot">The folder's root on disk.</param>
    /// <param name="folder">Which devbook folder it is.</param>
    /// <param name="documentPath">The open document, repository-relative or
    /// relative to the folder root.</param>
    /// <param name="stated">The direction stated at an address, or null — the
    /// repository's direction map.</param>
    public static DevbookSyncReading Read(string? folderRoot, DevbookFolder folder, string? documentPath, Func<string, string?> stated)
    {
        ArgumentNullException.ThrowIfNull(stated);

        if (string.IsNullOrWhiteSpace(folderRoot) || string.IsNullOrWhiteSpace(documentPath)) return None;
        if (DevbookSync.FolderOverview(folder) is not { } overview) return None;

        var subject = DevbookSync.FolderRelative(folder, documentPath);
        if (ReadBlocks(folderRoot, subject) is not { } blocks) return None;

        var statedBlocks = blocks
            .Select(block => new StatedBlock(block, stated(DevbookSync.Address(folder, block.Level > 1 ? $"{subject}#{AddressSlug(block.Title)}" : subject))))
            .ToList();

        // Above the document's own blocks: in domain/, the context and then the
        // folder; elsewhere, the folder alone. A level that is the document itself
        // — the overview, or context.md — is not asked twice.
        var above = new List<(DevbookSyncLevel, string?)>();
        if (folder == DevbookFolder.Domain && subject.Split('/') is [var context, var name] && name != "context.md")
        {
            above.Add((DevbookSyncLevel.Context, stated(DevbookSync.Address(folder, $"{context}/context.md"))));
        }

        if (subject != overview) above.Add((DevbookSyncLevel.Folder, stated(DevbookSync.Address(folder, overview))));

        return new DevbookSyncReading(folder, subject, statedBlocks, above);
    }

    /// <summary>
    /// The item path a direction for the block at <paramref name="level"/> with
    /// heading text <paramref name="heading"/> is addressed by —
    /// <c>path#slug</c> for a chapter, the bare <paramref name="documentPath"/>
    /// for the document's own block and for a heading this document does not
    /// hold. <see cref="DevbookSync.Address"/> turns it into the map's address.
    /// <para>The screen's <c>DevbookSyncScope</c> is built from this and
    /// <see cref="Describe"/> by a <c>Scope</c> extension in the Devbook UI project,
    /// so this adapter names no UI type (issue #738).</para>
    /// </summary>
    public string ItemPath(string documentPath, DevbookMetadataLevel level, string? heading) =>
        Find(level, heading) is { Block.Level: > 1 } chapter
            ? $"{documentPath}#{AddressSlug(chapter.Block.Title)}"
            : documentPath;

    /// <summary>
    /// The <c>#slug</c> a chapter's direction address carries: <c>units.mjs</c>'s,
    /// the generator's <c>slugify</c> ported as <see cref="DevbookMarkdown.Slugify"/>,
    /// so the map and the sweeps name a unit with one id. Not
    /// <see cref="DevbookHeadingDocument.Slug"/>, which keeps finding the heading a
    /// record names but spells some headings differently — an apostrophe, an
    /// underscore.
    /// </summary>
    internal static string AddressSlug(string title) => DevbookMarkdown.Slugify(DevbookMarkdown.JsTrim(title));

    /// <summary>The state of the block at <paramref name="level"/> with heading
    /// text <paramref name="heading"/>, or null for one that is no sync level or
    /// is not in this document.</summary>
    public DevbookSyncState? Describe(DevbookMetadataLevel level, string? heading)
    {
        if (Find(level, heading) is not { } found) return null;

        // The file's `#` title reaches here either way: as the file's own block
        // from a header, or as a chapter heading from a document view that draws
        // the title in the body. It is the file's block both times.
        var block = found.Block;
        var blockLevel = block.Level == 1 ? DevbookMetadataLevel.File : DevbookMetadataLevel.Chapter;
        var place = DevbookSync.Place(_folder, _documentPath, blockLevel, block.Type, block.Level, block.IsIndexRoot);
        if (place.Level is not { } at) return null;

        return new DevbookSyncState(at, found.Stated, DevbookSync.Resolve(Chain(at, found)));
    }

    /// <summary>The block a record reported: the file's own for its level, else
    /// the first heading — the title included — whose slug matches.</summary>
    private StatedBlock? Find(DevbookMetadataLevel level, string? heading) =>
        level == DevbookMetadataLevel.File
            ? _blocks.FirstOrDefault(candidate => candidate.Block.Level == 1)
            : _blocks.FirstOrDefault(candidate => SameHeading(candidate.Block.Title, heading));

    /// <summary>Whether a heading in the file is the one a record reported. By
    /// slug, the way the block's address is, so a heading the view drew without
    /// its inline marks still finds its block.</summary>
    private static bool SameHeading(string title, string? heading) =>
        !string.IsNullOrWhiteSpace(heading)
        && string.Equals(DevbookHeadingDocument.Slug(title), DevbookHeadingDocument.Slug(heading.Trim()), StringComparison.OrdinalIgnoreCase);

    /// <summary>The statements a block's direction is read from, nearest first:
    /// itself, then — for a unit chapter — its page, the file it sits in, then
    /// the levels above the document.</summary>
    private IEnumerable<(DevbookSyncLevel Level, string? Value)> Chain(DevbookSyncLevel at, StatedBlock found)
    {
        yield return (at, found.Stated);

        if (found.Block.Level > 1 && _blocks.FirstOrDefault(candidate => candidate.Block.Level == 1) is { } file)
        {
            var filePlace = DevbookSync.Place(_folder, _documentPath, DevbookMetadataLevel.File, file.Block.Type, 1, file.Block.IsIndexRoot);
            if (filePlace.Level is { } fileLevel) yield return (fileLevel, file.Stated);
        }

        foreach (var level in _above) yield return level;
    }

    /// <summary>The heading at <paramref name="headingIndex"/> and its fence.</summary>
    internal static MetaBlock BlockAt(IReadOnlyList<string> lines, int headingIndex)
    {
        var match = DevbookHeadingDocument.Heading.Match(lines[headingIndex]);
        var fields = Fields(lines, DevbookHeadingDocument.FindFence(lines, headingIndex));
        return new MetaBlock(match.Groups[1].Value.Length, match.Groups[2].Value.Trim(), fields);
    }

    /// <summary>A fence line's field name, lower-cased, or null for a line that
    /// names none.</summary>
    internal static string? FieldName(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith('-') || trimmed.StartsWith('#')) return null;

        var colon = trimmed.IndexOf(':');
        return colon > 0 ? trimmed[..colon].Trim().ToLowerInvariant() : null;
    }

    /// <summary>Every heading in the file outside a code fence, with the fields of
    /// the <c>meta</c> fence under it. Null for a file that is not there or not
    /// inside the root.</summary>
    internal static IReadOnlyList<MetaBlock>? ReadBlocks(string folderRoot, string subject)
    {
        var root = Path.GetFullPath(folderRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, subject.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;

        var lines = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
        var blocks = new List<MetaBlock>();
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence || !DevbookHeadingDocument.Heading.IsMatch(lines[i])) continue;

            blocks.Add(BlockAt(lines, i));
        }

        return blocks;
    }

    private static IReadOnlyDictionary<string, string> Fields(IReadOnlyList<string> lines, int fence)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (fence < 0) return fields;

        for (var i = fence + 1; i < lines.Count && !lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal); i++)
        {
            if (FieldName(lines[i]) is not { } name) continue;

            var value = lines[i][(lines[i].IndexOf(':') + 1)..].Trim().Trim('"', '\'');
            if (value.Length > 0) fields.TryAdd(name, value);
        }

        return fields;
    }

    /// <summary>One heading and the scalar fields of its fence.</summary>
    internal sealed record MetaBlock(int Level, string Title, IReadOnlyDictionary<string, string> Fields)
    {
        public string? Type => Fields.GetValueOrDefault("type");

        /// <summary>The <c>sync</c> line the chapter still carries — read by the
        /// one-time import, never by a reading.</summary>
        public string? Sync => Fields.GetValueOrDefault("sync");

        public bool IsIndexRoot => string.Equals(Fields.GetValueOrDefault("index"), "root", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A block and what the direction map states for it.</summary>
    private sealed record StatedBlock(MetaBlock Block, string? Stated);
}
