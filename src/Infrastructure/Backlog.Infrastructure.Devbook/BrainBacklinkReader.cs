using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// <see cref="IBrainBacklinks"/> over the Brain's own database in the app's
/// storage (local ADR 0026 §4, ADR 0027 §6): the <c>repo:</c> edges the builder
/// stored with no node behind them, read in reverse.
/// <para>
/// Opened per question, as every reader of the database is, so a rebuild landing
/// underneath is never blocked. No Brain set, no database built yet, or one that
/// will not open or answer — a rebuild replacing it mid-read — all answer empty:
/// a repository chapter then shows no box, which is what it shows for a Brain
/// that links to nothing.
/// </para>
/// </summary>
public sealed class BrainBacklinkReader : IBrainBacklinks
{
    private readonly Func<string?> _brainRoot;
    private readonly Func<string, string?> _databaseFor;

    public BrainBacklinkReader(IBrainFolderSource brain)
        : this(() => brain.BrainRoot)
    {
    }

    /// <param name="brainRoot">The Brain's folder as it stands, asked on every
    /// question so a moved setting is followed at once.</param>
    public BrainBacklinkReader(Func<string?> brainRoot)
        : this(brainRoot, DevbookDatabaseLocation.ForBrainFolder)
    {
    }

    /// <param name="brainRoot">The Brain's folder as it stands.</param>
    /// <param name="databaseFor">Where that folder's database is.</param>
    internal BrainBacklinkReader(Func<string?> brainRoot, Func<string, string?> databaseFor)
    {
        _brainRoot = brainRoot ?? throw new ArgumentNullException(nameof(brainRoot));
        _databaseFor = databaseFor ?? throw new ArgumentNullException(nameof(databaseFor));
    }

    public Task<IReadOnlyList<BrainBacklink>> LinksToAsync(string repositoryAlias, string chapterPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryAlias) || string.IsNullOrWhiteSpace(chapterPath) || _brainRoot() is not { Length: > 0 } root)
        {
            return Task.FromResult<IReadOnlyList<BrainBacklink>>([]);
        }

        return Task.Run(() => Read(root, repositoryAlias, chapterPath), cancellationToken);
    }

    public Task<IReadOnlyList<BrainChapterBacklink>> ChapterLinksAsync(string repositoryAlias, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryAlias) || _brainRoot() is not { Length: > 0 } root)
        {
            return Task.FromResult<IReadOnlyList<BrainChapterBacklink>>([]);
        }

        return Task.Run(() => Read(root, database => ChapterLinks(database, repositoryAlias)), cancellationToken);
    }

    private IReadOnlyList<BrainBacklink> Read(string root, string repositoryAlias, string chapterPath) =>
        Read(root, database => LinksTo(database, repositoryAlias, chapterPath));

    private IReadOnlyList<T> Read<T>(string root, Func<DevbookDatabase, IReadOnlyList<T>> query)
    {
        try
        {
            using var database = DevbookDatabase.TryOpen(_databaseFor(root));
            return database is null ? [] : query(database);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Busy, replaced under the read, or not a database any more. A chapter
            // is not worth failing over the Brain's index; the next read retries.
            return [];
        }
    }

    /// <summary>
    /// Every link into the repository, over a database already open: one entry per
    /// note, chapter file and heading, in note then line order, each naming the
    /// chapter of the note it is first written in — none when that is the note's
    /// own <c>related</c> or prose outside every chapter, as <see cref="LinksTo"/>
    /// decides.
    /// </summary>
    internal static IReadOnlyList<BrainChapterBacklink> ChapterLinks(DevbookDatabase database, string repositoryAlias)
    {
        var links = new List<BrainChapterBacklink>();
        var seen = new Dictionary<(string Note, string Path, string Slug), int>();

        foreach (var row in database.RepoLinks())
        {
            if (BrainRepoLink.Parse(row.Target) is not { } link || !string.Equals(link.Alias, repositoryAlias, StringComparison.OrdinalIgnoreCase)) continue;

            var title = string.IsNullOrWhiteSpace(row.NoteTitle) ? Path.GetFileNameWithoutExtension(row.NotePath) : row.NoteTitle;
            var heading = row.SourceType == "file" ? null : row.SourceLabel;
            var key = (row.NotePath.ToLowerInvariant(), DevbookLayout.ConventionalPath(link.Path).ToLowerInvariant(), link.Slug?.ToLowerInvariant() ?? string.Empty);

            if (!seen.TryGetValue(key, out var at))
            {
                seen[key] = links.Count;
                links.Add(new BrainChapterBacklink(link.Path, link.Slug, new BrainBacklink(row.NotePath, title, heading)));
            }
            else if (links[at].Note.Heading is not null && heading is null)
            {
                links[at] = links[at] with { Note = links[at].Note with { Heading = null } };
            }
        }

        return links;
    }

    /// <summary>
    /// The backlink query itself, over a database already open. One entry per
    /// note: with no heading when the note links from its own <c>related</c> or
    /// from prose outside every chapter, else naming the first chapter — a heading
    /// with a <c>meta</c> block — it links from, by line.
    /// </summary>
    internal static IReadOnlyList<BrainBacklink> LinksTo(DevbookDatabase database, string repositoryAlias, string chapterPath)
    {
        var notes = new Dictionary<string, BrainBacklink>(StringComparer.OrdinalIgnoreCase);

        // Rows arrive in note, then line order, so the first chapter seen is the
        // first in the note.
        foreach (var row in database.RepoLinks())
        {
            if (BrainRepoLink.Parse(row.Target) is not { } link || !link.Names(repositoryAlias, chapterPath)) continue;

            var title = string.IsNullOrWhiteSpace(row.NoteTitle) ? Path.GetFileNameWithoutExtension(row.NotePath) : row.NoteTitle;
            var heading = row.SourceType == "file" ? null : row.SourceLabel;

            if (!notes.TryGetValue(row.NotePath, out var known))
            {
                notes[row.NotePath] = new BrainBacklink(row.NotePath, title, heading);
            }
            else if (known.Heading is not null && heading is null)
            {
                notes[row.NotePath] = known with { Heading = null };
            }
        }

        return [.. notes.Values
            .OrderBy(note => note.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(note => note.NotePath, StringComparer.OrdinalIgnoreCase)];
    }
}
