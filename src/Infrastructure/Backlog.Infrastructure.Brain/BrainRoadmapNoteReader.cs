using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Brain;

/// <summary>
/// Roadmap's <see cref="IRoadmapBrainNotes"/> over the Brain's own database in the
/// app's storage (local ADR 0053 §3 and §5): every note and every heading of a note,
/// with the title, the <c>effort</c> and the <c>roadmap</c> list its <c>meta</c> block
/// registered, as the builder already stored them.
/// <para>
/// Opened per question, as <see cref="BrainTaskLinkReader"/> is, so a rebuild landing
/// underneath is never blocked, and read-only. No Brain set, no database yet, or one
/// that will not answer all read as null — no Brain on this device: the reader is then
/// not the Brain's owner as far as a roadmap can tell, and the item's <c>#brain:</c>
/// refs read as unresolved for want of a Brain.
/// </para>
/// </summary>
public sealed class BrainRoadmapNoteReader : IRoadmapBrainNotes
{
    /// <summary>The list-valued <c>meta</c> field a note names its Roadmap Tags in.</summary>
    private const string RoadmapAttribute = "roadmap";

    private readonly Func<string?> _brainRoot;
    private readonly Func<string, string?> _databaseFor;

    public BrainRoadmapNoteReader(IBrainFolderSource brain)
        : this(() => brain.BrainRoot)
    {
    }

    /// <param name="brainRoot">The Brain's folder as it stands, asked on every
    /// question so a moved setting is followed at once.</param>
    public BrainRoadmapNoteReader(Func<string?> brainRoot)
        : this(brainRoot, BrainDatabaseLocation.ForBrainFolder)
    {
    }

    /// <param name="brainRoot">The Brain's folder as it stands.</param>
    /// <param name="databaseFor">Where that folder's database is.</param>
    internal BrainRoadmapNoteReader(Func<string?> brainRoot, Func<string, string?> databaseFor)
    {
        _brainRoot = brainRoot ?? throw new ArgumentNullException(nameof(brainRoot));
        _databaseFor = databaseFor ?? throw new ArgumentNullException(nameof(databaseFor));
    }

    public Task<IReadOnlyList<RoadmapBrainNote>?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_brainRoot() is not { Length: > 0 } root) return Task.FromResult<IReadOnlyList<RoadmapBrainNote>?>(null);

        return Task.Run(() => Read(root), cancellationToken);
    }

    private IReadOnlyList<RoadmapBrainNote>? Read(string root)
    {
        try
        {
            using var database = DevbookDatabase.TryOpen(_databaseFor(root));
            return database is null ? null : Notes(database);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Busy, replaced under the read, or not a database any more: a roadmap
            // gathering is not worth failing over the Brain's index.
            return null;
        }
    }

    /// <summary>
    /// The notes over a database already open: per file, the note as a whole — titled
    /// by its top heading, which its <c>meta</c> block sits under — then each deeper
    /// heading once, in line order. The outline comes from the chapter table, so a
    /// heading with no <c>meta</c> block still resolves; effort and Roadmap Tags come
    /// from the node the builder wrote for a block.
    /// </summary>
    internal static IReadOnlyList<RoadmapBrainNote> Notes(DevbookDatabase database)
    {
        var headings = database.ChapterHeadings();
        var roadmap = database.Attributes(RoadmapAttribute);
        var nodes = new Dictionary<string, DevbookNodeRow>(StringComparer.Ordinal);
        foreach (var node in database.Nodes()) nodes.TryAdd(node.Id, node);

        var notes = new List<RoadmapBrainNote>();

        foreach (var file in nodes.Values.Where(node => node is { Type: "file", Path.Length: > 0 }))
        {
            var path = file.Path!;
            var outline = headings[path].ToList();
            var top = outline.FirstOrDefault(heading => heading.Level == 1);

            notes.Add(new RoadmapBrainNote(
                path,
                null,
                Title(top?.Title, path),
                Effort(file),
                [.. roadmap[file.Id]],
                top?.Slug));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var heading in outline)
            {
                if (heading.Level <= 1 || string.IsNullOrEmpty(heading.Slug) || !seen.Add(heading.Slug)) continue;

                var id = $"{path}#{heading.Slug}";
                nodes.TryGetValue(id, out var chapter);

                notes.Add(new RoadmapBrainNote(
                    path,
                    heading.Slug,
                    string.IsNullOrWhiteSpace(heading.Title) ? heading.Slug : heading.Title,
                    chapter is null ? null : Effort(chapter),
                    [.. roadmap[id]]));
            }
        }

        return notes;
    }

    private static string Title(string? title, string path) =>
        string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title;

    private static int? Effort(DevbookNodeRow node) => node.Effort is { } effort && effort >= 0 ? effort : null;
}
