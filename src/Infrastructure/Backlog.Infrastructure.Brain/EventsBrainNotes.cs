using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Events.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Brain;

/// <summary>
/// Answers Events' <see cref="IEventBrainNotes"/> over the Brain's folder and its database
/// (local ADR 0050 §3, ADR 0026 §4): whether a note is at the path an event links, and what it
/// is called. Read only; nothing is written to the Brain.
/// <para>
/// The folder decides whether a note is there, because a note moved since the last rebuild is
/// still in the database; the database gives its title, and a note written since then reads by
/// its file name. A path that leaves the Brain's folder is never there. No Brain, no database,
/// or one that will not answer all read as fewer notes, never a throw.
/// </para>
/// </summary>
public sealed class EventsBrainNotes : IEventBrainNotes
{
    private readonly IBrainFolderSource? _source;
    private readonly Func<string?> _brainRoot;
    private readonly Func<string, string?> _databaseFor;

    public EventsBrainNotes(IBrainFolderSource brain)
        : this(brain ?? throw new ArgumentNullException(nameof(brain)), () => brain.BrainRoot, BrainDatabaseLocation.ForBrainFolder)
    {
    }

    /// <param name="source">Whose <see cref="IBrainFolderSource.Changed"/> this relays, or null.</param>
    /// <param name="brainRoot">The Brain's folder as it stands, asked on every question.</param>
    /// <param name="databaseFor">Where that folder's database is.</param>
    internal EventsBrainNotes(IBrainFolderSource? source, Func<string?> brainRoot, Func<string, string?> databaseFor)
    {
        _source = source;
        _brainRoot = brainRoot ?? throw new ArgumentNullException(nameof(brainRoot));
        _databaseFor = databaseFor ?? throw new ArgumentNullException(nameof(databaseFor));
    }

    public string? UnavailableReason => _brainRoot() switch
    {
        not { Length: > 0 } => BrainFolderSource.NoBrainFolder,
        var root when !Directory.Exists(root) => $"The Brain folder was not found at {root}.",
        _ => null
    };

    public event Action? Changed
    {
        add
        {
            if (_source is not null) _source.Changed += value;
        }
        remove
        {
            if (_source is not null) _source.Changed -= value;
        }
    }

    public Task<IReadOnlyDictionary<string, EventBrainNoteDto>> ResolveAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        IReadOnlyDictionary<string, EventBrainNoteDto> none = new Dictionary<string, EventBrainNoteDto>();
        if (Root() is not { } root || paths.Count == 0) return Task.FromResult(none);

        return Task.Run(() =>
        {
            var titles = Titles(root);
            var found = new Dictionary<string, EventBrainNoteDto>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in paths)
            {
                if (NoteAt(root, path) is { } relative && !found.ContainsKey(path))
                {
                    found[path] = new EventBrainNoteDto(relative, TitleOf(relative, titles));
                }
            }

            return (IReadOnlyDictionary<string, EventBrainNoteDto>)found;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<EventBrainNoteDto>> CandidatesAsync(CancellationToken cancellationToken = default)
    {
        if (Root() is not { } root) return Task.FromResult<IReadOnlyList<EventBrainNoteDto>>([]);

        return Task.Run(() =>
        {
            var titles = Titles(root);

            return (IReadOnlyList<EventBrainNoteDto>)
            [
                .. titles.Keys
                    .Where(path => !string.Equals(path, BrainConventions.FileName, StringComparison.OrdinalIgnoreCase))
                    .Select(path => NoteAt(root, path))
                    .OfType<string>()
                    .Select(path => new EventBrainNoteDto(path, TitleOf(path, titles)))
                    .OrderBy(note => note.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(note => note.Path, StringComparer.OrdinalIgnoreCase)
            ];
        }, cancellationToken);
    }

    private string? Root() => _brainRoot() is { Length: > 0 } root && Directory.Exists(root) ? root : null;

    /// <summary>The path from the Brain's root, with forward slashes, when a file is there now and
    /// inside the Brain's folder; else null.</summary>
    private static string? NoteAt(string root, string? path)
    {
        var relative = path?.Trim().Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(relative)) return null;

        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));

        return full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && File.Exists(full)
            ? Path.GetRelativePath(folder, full).Replace('\\', '/')
            : null;
    }

    private static string TitleOf(string path, IReadOnlyDictionary<string, string?> titles) =>
        titles.TryGetValue(path, out var title) && !string.IsNullOrWhiteSpace(title)
            ? title
            : Path.GetFileNameWithoutExtension(path);

    /// <summary>Every note the Brain's database holds, by path, with its first top-level
    /// heading; empty when there is no database or it will not answer.</summary>
    private Dictionary<string, string?> Titles(string root)
    {
        var titles = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var database = DevbookDatabase.TryOpen(_databaseFor(root));
            if (database is null) return titles;

            foreach (var file in database.ChapterHeadings())
            {
                titles[file.Key] = file.FirstOrDefault(chapter => chapter.Level == 1)?.Title;
            }
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Busy, replaced under the read, or not a database any more: titles fall back to
            // file names, and the next read retries.
        }

        return titles;
    }
}
