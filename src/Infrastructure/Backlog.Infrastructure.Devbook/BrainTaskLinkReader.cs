using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// <see cref="IBrainTaskLinks"/> over the Brain's own database in the app's storage
/// (local ADR 0026 §3 and §4): the <c>tasks</c> values the builder stored as node
/// attributes, read forwards for a note and in reverse for a task.
/// <para>
/// Opened per question, as <see cref="BrainBacklinkReader"/> is, so a rebuild
/// landing underneath is never blocked; no Brain, no database, or one that will not
/// answer all read as no link.
/// </para>
/// </summary>
public sealed class BrainTaskLinkReader : IBrainTaskLinks
{
    private readonly Func<string?> _brainRoot;
    private readonly Func<string, string?> _databaseFor;

    public BrainTaskLinkReader(IBrainFolderSource brain)
        : this(() => brain.BrainRoot)
    {
    }

    /// <param name="brainRoot">The Brain's folder as it stands, asked on every
    /// question so a moved setting is followed at once.</param>
    public BrainTaskLinkReader(Func<string?> brainRoot)
        : this(brainRoot, DevbookDatabaseLocation.ForBrainFolder)
    {
    }

    /// <param name="brainRoot">The Brain's folder as it stands.</param>
    /// <param name="databaseFor">Where that folder's database is.</param>
    internal BrainTaskLinkReader(Func<string?> brainRoot, Func<string, string?> databaseFor)
    {
        _brainRoot = brainRoot ?? throw new ArgumentNullException(nameof(brainRoot));
        _databaseFor = databaseFor ?? throw new ArgumentNullException(nameof(databaseFor));
    }

    public Task<IReadOnlyList<BrainTaskLink>> TasksOfAsync(string notePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(notePath) || _brainRoot() is not { Length: > 0 } root)
        {
            return Task.FromResult<IReadOnlyList<BrainTaskLink>>([]);
        }

        return Task.Run(() => Read(root, database => TasksOf(database, notePath), (IReadOnlyList<BrainTaskLink>)[]), cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, IReadOnlyList<BrainTaskNote>>> NotesByTaskAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, IReadOnlyList<BrainTaskNote>> none = new Dictionary<string, IReadOnlyList<BrainTaskNote>>();
        if (_brainRoot() is not { Length: > 0 } root) return Task.FromResult(none);

        return Task.Run(() => Read(root, NotesByTask, none), cancellationToken);
    }

    private T Read<T>(string root, Func<DevbookDatabase, T> query, T none)
    {
        try
        {
            using var database = DevbookDatabase.TryOpen(_databaseFor(root));
            return database is null ? none : query(database);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Busy, replaced under the read, or not a database any more: a task row
            // or a note is not worth failing over the Brain's index.
            return none;
        }
    }

    /// <summary>The note's task links, over a database already open: every block of
    /// the file that carries the field, in line order, each value once.</summary>
    internal static IReadOnlyList<BrainTaskLink> TasksOf(DevbookDatabase database, string notePath)
    {
        var path = notePath.Replace('\\', '/').TrimStart('/');
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var links = new List<BrainTaskLink>();

        foreach (var row in database.TaskLinks())
        {
            if (!string.Equals(row.NotePath, path, StringComparison.OrdinalIgnoreCase)) continue;
            if (BrainTaskLink.Parse(row.Value) is not { } link) continue;
            if (seen.Add(link.TaskId ?? link.Raw)) links.Add(link);
        }

        return links;
    }

    /// <summary>The reverse, over a database already open: one entry per task id,
    /// each note once, in title order.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<BrainTaskNote>> NotesByTask(DevbookDatabase database)
    {
        var byTask = new Dictionary<string, Dictionary<string, BrainTaskNote>>(StringComparer.Ordinal);

        foreach (var row in database.TaskLinks())
        {
            if (BrainTaskLink.Normalize(row.Value) is not { } id) continue;

            if (!byTask.TryGetValue(id, out var notes)) byTask[id] = notes = new(StringComparer.OrdinalIgnoreCase);
            var title = string.IsNullOrWhiteSpace(row.NoteTitle) ? Path.GetFileNameWithoutExtension(row.NotePath) : row.NoteTitle;
            notes.TryAdd(row.NotePath, new BrainTaskNote(row.NotePath, title));
        }

        return byTask.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<BrainTaskNote>)[.. pair.Value.Values
                .OrderBy(note => note.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(note => note.NotePath, StringComparer.OrdinalIgnoreCase)],
            StringComparer.Ordinal);
    }
}
