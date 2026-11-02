using Backlog.SharedKernel.Devbook;

namespace Backlog.Infrastructure.Devbook.Chapters;

/// <summary>
/// The <c>sync</c> values a devbook folder's Markdown still states, as direction
/// map entries — what the one-time import copies into Backlog's map
/// (<c>IDevbookSyncDirectionStore.Import</c>) before nothing reads the field from
/// a chapter again.
/// <para>
/// Only a value the rule would have honoured is taken: one of
/// <see cref="DevbookSync.Directions"/>, on a block <see cref="DevbookSync.Place"/>
/// allows. A misplaced one — a chapter a unit owns, a page holding only such
/// chapters — or a misspelt one was never in force, and copying it in would put
/// into the map what the checker failed in the file. The blocks are read the way
/// <see cref="DevbookSyncReading"/> reads them, so a block is placed by the same
/// type, depth and index flag either way.
/// </para>
/// </summary>
public static class DevbookSyncMarkdownImport
{
    /// <summary>Every allowed statement in the folder, by address, in file and
    /// heading order. A file that cannot be read is skipped: the rest of the
    /// folder is still good.</summary>
    /// <param name="folderRoot">The folder's root on disk.</param>
    /// <param name="folder">Which devbook folder it is.</param>
    public static IReadOnlyList<(string Address, string Direction)> Read(string folderRoot, DevbookFolder folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderRoot);

        if (DevbookSync.FolderOverview(folder) is null || !Directory.Exists(folderRoot)) return [];

        var statements = new List<(string, string)>();
        var root = Path.GetFullPath(folderRoot);

        foreach (var path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var subject = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

            IReadOnlyList<DevbookSyncReading.MetaBlock>? blocks;
            try
            {
                blocks = DevbookSyncReading.ReadBlocks(root, subject);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var block in blocks ?? [])
            {
                if (!DevbookSync.IsDirection(block.Sync)) continue;

                var level = block.Level == 1 ? DevbookMetadataLevel.File : DevbookMetadataLevel.Chapter;
                if (!DevbookSync.Place(folder, subject, level, block.Type, block.Level, block.IsIndexRoot).IsAllowed) continue;

                var item = block.Level > 1 ? $"{subject}#{DevbookSyncReading.AddressSlug(block.Title)}" : subject;
                statements.Add((DevbookSync.Address(folder, item), block.Sync!.Trim().ToLowerInvariant()));
            }
        }

        return statements;
    }
}
