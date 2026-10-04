using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Writes the <c>sync</c> field of the <c>meta</c> fence belonging to one devbook
/// heading — a chapter's, addressed by <c>&lt;path&gt;#&lt;slug&gt;</c>, or a file's
/// own, addressed by the bare path. The address, the file and its newline are
/// <see cref="DevbookMarkdownStatusWriter"/>'s, so a direction and a status are
/// found in a file the same way.
///
/// <para>Two verbs, for the reason the status writer gives: setting a direction and
/// stating none are different operations, and stating none is how a block inherits.
/// <see cref="UpdateSync"/> refuses a value outside <see cref="DevbookSync.Directions"/>
/// and a block that is no sync level — a chapter a unit owns, a page holding only
/// such chapters, any block in <c>tech/</c> or <c>ai/</c> — before the file is
/// written, so the product never puts into a file what the checker would fail.
/// <see cref="RemoveSync"/> takes the line off wherever it is, refused place or
/// not: removing a misplaced value is the fix.</para>
/// </summary>
internal static class DevbookMarkdownSyncWriter
{
    private const string Field = "sync";

    /// <summary>Set the heading's direction, inserting the field — or the whole
    /// fence — when it is not there yet.</summary>
    public static void UpdateSync(string folderRoot, string itemPath, string folderPrefix, string direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(direction);

        var value = direction.Trim().ToLowerInvariant();
        if (!DevbookSync.IsDirection(value))
        {
            throw new ArgumentException(
                $"'{value}' is not a sync direction: {string.Join(", ", DevbookSync.Directions)}.", nameof(direction));
        }

        var folder = DevbookFolders.FromPath(folderPrefix);
        var document = DevbookMarkdownStatusWriter.Open(folderRoot, itemPath, folderPrefix);
        var block = DevbookSyncReading.BlockAt(document.Lines, document.HeadingIndex);

        var place = DevbookSync.Place(
            folder,
            itemPath,
            itemPath.Contains('#', StringComparison.Ordinal) ? DevbookMetadataLevel.Chapter : DevbookMetadataLevel.File,
            block.Type,
            block.Level,
            block.IsIndexRoot);
        if (!place.IsAllowed)
        {
            throw new InvalidOperationException($"This block cannot state a sync direction ({place.Refusal}): {itemPath}");
        }

        Upsert(document.Lines, document.HeadingIndex, value);
        document.Save();
    }

    /// <summary>
    /// Delete the heading's <c>sync</c> line, so the block inherits its direction.
    /// The fence stays even when it empties, for the reason the status writer
    /// keeps it: a heading with a fence is a chapter, one without is not. A no-op
    /// where nothing states a direction.
    /// </summary>
    public static void RemoveSync(string folderRoot, string itemPath, string folderPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPrefix);

        var document = DevbookMarkdownStatusWriter.Open(folderRoot, itemPath, folderPrefix);
        var fence = DevbookMarkdownStatusWriter.FindFence(document.Lines, document.HeadingIndex);
        if (DevbookMarkdownStatusWriter.RemoveFields(document.Lines, fence, [Field]) == 0) return;

        document.Save();
    }

    /// <summary>Replace the field's line in the heading's fence, or add it as the
    /// fence's last line — after the status and the type a reader looks for
    /// first — or open a fence holding only it.</summary>
    private static void Upsert(List<string> lines, int headingIndex, string value)
    {
        var fence = DevbookMarkdownStatusWriter.FindFence(lines, headingIndex);
        if (fence < 0)
        {
            lines.InsertRange(headingIndex + 1, [string.Empty, "```meta", $"{Field}: {value}", "```"]);
            return;
        }

        var close = fence + 1;
        while (close < lines.Count && !lines[close].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            if (DevbookSyncReading.FieldName(lines[close]) == Field)
            {
                var indent = lines[close][..(lines[close].Length - lines[close].TrimStart().Length)];
                lines[close] = $"{indent}{Field}: {value}";
                return;
            }

            close++;
        }

        lines.Insert(close, $"{Field}: {value}");
    }
}

/// <summary>
/// The <c>sync</c> state of every block in one devbook file, read once: where each
/// block sits, what it states, and the direction in force there, resolved
/// nearest-wins through its page, its context and its folder overview.
///
/// <para>A devbook pane builds one per open document and cascades it as a
/// <see cref="DevbookSyncScope"/>. The levels above a unit live in other files — a
/// unit's page is its own file, but its <c>context.md</c> and the folder overview
/// are not — so those file-level blocks are read here too, once, rather than every
/// time a record asks.</para>
/// </summary>
public sealed class DevbookSyncReading
{
    private readonly DevbookFolder _folder;
    private readonly string _documentPath;
    private readonly IReadOnlyList<MetaBlock> _blocks;
    private readonly IReadOnlyList<(DevbookSyncLevel Level, string? Value)> _above;

    private DevbookSyncReading(DevbookFolder folder, string documentPath, IReadOnlyList<MetaBlock> blocks, IReadOnlyList<(DevbookSyncLevel, string?)> above)
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
    /// Read the document and the blocks above it.
    /// </summary>
    /// <param name="folderRoot">The folder's root on disk.</param>
    /// <param name="folder">Which devbook folder it is.</param>
    /// <param name="documentPath">The open document, repository-relative or
    /// relative to the folder root.</param>
    public static DevbookSyncReading Read(string? folderRoot, DevbookFolder folder, string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(folderRoot) || string.IsNullOrWhiteSpace(documentPath)) return None;
        if (DevbookSync.FolderOverview(folder) is not { } overview) return None;

        var subject = DevbookSync.FolderRelative(folder, documentPath);
        var blocks = ReadBlocks(folderRoot, subject);
        if (blocks is null) return None;

        // Above the document's own blocks: in domain/, the context and then the
        // folder; elsewhere, the folder alone. A level that is the document itself
        // — the overview, or context.md — is not read twice.
        var above = new List<(DevbookSyncLevel, string?)>();
        if (folder == DevbookFolder.Domain && subject.Split('/') is [var context, var name] && name != "context.md")
        {
            above.Add((DevbookSyncLevel.Context, FileSync(folderRoot, $"{context}/context.md")));
        }

        if (subject != overview) above.Add((DevbookSyncLevel.Folder, FileSync(folderRoot, overview)));

        return new DevbookSyncReading(folder, subject, blocks, above);
    }

    /// <summary>
    /// The scope a pane cascades over the open document: this reading answers what
    /// each record asks, and <paramref name="write"/> — null where the folder cannot
    /// be written — is handed the item path a status write would use, the bare
    /// document path for its own block and <c>path#slug</c> for a chapter, with the
    /// direction or null to inherit.
    /// </summary>
    public DevbookSyncScope Scope(string documentPath, Func<string, string?, Task>? write) =>
        new(Describe, write is null
            ? null
            : (level, heading, direction) => write(
                Find(level, heading) is { Level: > 1 } chapter
                    ? $"{documentPath}#{DevbookMarkdownStatusWriter.Slug(chapter.Title)}"
                    : documentPath,
                direction));

    /// <summary>The state of the block at <paramref name="level"/> with heading
    /// text <paramref name="heading"/>, or null for one that is no sync level or
    /// is not in this document.</summary>
    public DevbookSyncState? Describe(DevbookMetadataLevel level, string? heading)
    {
        if (Find(level, heading) is not { } block) return null;

        // The file's `#` title reaches here either way: as the file's own block
        // from a header, or as a chapter heading from a document view that draws
        // the title in the body. It is the file's block both times.
        var blockLevel = block.Level == 1 ? DevbookMetadataLevel.File : DevbookMetadataLevel.Chapter;
        var place = DevbookSync.Place(_folder, _documentPath, blockLevel, block.Type, block.Level, block.IsIndexRoot);
        if (place.Level is not { } at) return null;

        return new DevbookSyncState(at, block.Sync, DevbookSync.Resolve(Chain(at, block)));
    }

    /// <summary>The block a record reported: the file's own for its level, else
    /// the first heading — the title included — whose slug matches.</summary>
    private MetaBlock? Find(DevbookMetadataLevel level, string? heading) =>
        level == DevbookMetadataLevel.File
            ? _blocks.FirstOrDefault(candidate => candidate.Level == 1)
            : _blocks.FirstOrDefault(candidate => SameHeading(candidate.Title, heading));

    /// <summary>Whether a heading in the file is the one a record reported. By
    /// slug, the way a status change is addressed, so a heading the view drew
    /// without its inline marks still finds its block.</summary>
    private static bool SameHeading(string title, string? heading) =>
        !string.IsNullOrWhiteSpace(heading)
        && string.Equals(DevbookMarkdownStatusWriter.Slug(title), DevbookMarkdownStatusWriter.Slug(heading.Trim()), StringComparison.OrdinalIgnoreCase);

    /// <summary>The statements a block's direction is read from, nearest first:
    /// itself, then — for a unit chapter — its page, the file it sits in, then
    /// the levels above the document.</summary>
    private IEnumerable<(DevbookSyncLevel Level, string? Value)> Chain(DevbookSyncLevel at, MetaBlock block)
    {
        yield return (at, block.Sync);

        if (block.Level > 1 && _blocks.FirstOrDefault(candidate => candidate.Level == 1) is { } file)
        {
            var filePlace = DevbookSync.Place(_folder, _documentPath, DevbookMetadataLevel.File, file.Type, 1, file.IsIndexRoot);
            if (filePlace.Level is { } fileLevel) yield return (fileLevel, file.Sync);
        }

        foreach (var level in _above) yield return level;
    }

    /// <summary>The heading at <paramref name="headingIndex"/> and its fence, as
    /// the writer needs it to place the block.</summary>
    internal static MetaBlock BlockAt(IReadOnlyList<string> lines, int headingIndex)
    {
        var match = DevbookMarkdownStatusWriter.Heading.Match(lines[headingIndex]);
        var fields = Fields(lines, DevbookMarkdownStatusWriter.FindFence(lines, headingIndex));
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

    private static string? FileSync(string folderRoot, string subject) =>
        ReadBlocks(folderRoot, subject)?.FirstOrDefault(block => block.Level == 1)?.Sync;

    /// <summary>Every heading in the file outside a code fence, with the fields of
    /// the <c>meta</c> fence under it. Null for a file that is not there or not
    /// inside the root.</summary>
    private static IReadOnlyList<MetaBlock>? ReadBlocks(string folderRoot, string subject)
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

            if (inFence || !DevbookMarkdownStatusWriter.Heading.IsMatch(lines[i])) continue;

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

        public string? Sync => Fields.GetValueOrDefault("sync");

        public bool IsIndexRoot => string.Equals(Fields.GetValueOrDefault("index"), "root", StringComparison.OrdinalIgnoreCase);
    }
}
