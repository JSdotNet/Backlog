using System.Text.Json;

using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps the one-time devbook import's marks — which repositories it finished for
/// — as one small JSON file in <c>devbook-imports</c> under the storage folder.
/// <para>
/// Under the storage folder, and one of
/// <see cref="WorkspaceSettingsStore.OwnedRootFolders"/>, for the reason the stores
/// it records imports into are: the states and remarks move with a moved backlog,
/// and a mark left behind would have the new place import them a second time. The
/// stores would write nothing — the import never overwrites — but the default
/// branch would be read for nothing.
/// </para>
/// <para>
/// Not replicated, unlike those stores: each device imports for itself. A file
/// that cannot be read counts as no marks, as every store here treats a file it
/// cannot read; the import then runs again and writes nothing new.
/// </para>
/// </summary>
public sealed class DevbookImportLedger : IDevbookImportLedger
{
    /// <summary>The folder under the storage root the file goes in.</summary>
    public const string FolderName = "devbook-imports";

    /// <summary>The one file in it.</summary>
    public const string FileName = "imported.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<string> _rootDirectory;
    private readonly Lock _gate = new();

    /// <summary>The root the marks below were read from; another answer from
    /// <see cref="_rootDirectory"/> means the backlog moved.</summary>
    private string? _loadedRoot;

    /// <summary>Keyed by <see cref="DevbookSubject.AliasKey"/>.</summary>
    private Dictionary<string, DateTimeOffset> _marks = new(StringComparer.Ordinal);

    /// <param name="rootDirectory">Where the storage folder is right now, asked on
    /// every call.</param>
    public DevbookImportLedger(Func<string> rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);

        _rootDirectory = rootDirectory;
    }

    /// <summary>A ledger over one fixed folder.</summary>
    public DevbookImportLedger(string rootDirectory)
        : this(() => rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
    }

    public bool HasImported(string repositoryAlias)
    {
        lock (_gate)
        {
            Load();

            return _marks.ContainsKey(DevbookSubject.AliasKey(repositoryAlias));
        }
    }

    public void MarkImported(string repositoryAlias, DateTimeOffset at)
    {
        lock (_gate)
        {
            Load();

            _marks[DevbookSubject.AliasKey(repositoryAlias)] = at;
            Save();
        }
    }

    private string FilePath(string root) => Path.Combine(root, FolderName, FileName);

    /// <summary>Must be called under the lock.</summary>
    private void Load()
    {
        var root = _rootDirectory();

        if (string.Equals(_loadedRoot, root, StringComparison.OrdinalIgnoreCase)) return;

        var marks = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        try
        {
            var path = FilePath(root);
            if (File.Exists(path)
                && JsonSerializer.Deserialize<LedgerDto>(File.ReadAllText(path), JsonOptions)?.Repositories is { } entries)
            {
                foreach (var entry in entries)
                {
                    if (entry is null || string.IsNullOrWhiteSpace(entry.RepositoryAlias)) continue;

                    marks[DevbookSubject.AliasKey(entry.RepositoryAlias)] = entry.ImportedAt;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // No marks: the import runs again and the stores write nothing new.
        }

        _marks = marks;
        _loadedRoot = root;
    }

    /// <summary>Must be called under the lock, after <see cref="Load"/>.</summary>
    private void Save()
    {
        try
        {
            var path = FilePath(_loadedRoot!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            File.WriteAllText(path, JsonSerializer.Serialize(new LedgerDto
            {
                Repositories =
                [
                    .. _marks
                        .OrderBy(mark => mark.Key, StringComparer.Ordinal)
                        .Select(mark => new MarkDto { RepositoryAlias = mark.Key, ImportedAt = mark.Value }),
                ],
            }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The mark holds in memory for this session; without the file the next
            // start imports again and writes nothing new.
        }
    }

    private sealed class LedgerDto
    {
        public List<MarkDto?>? Repositories { get; init; } = [];
    }

    private sealed class MarkDto
    {
        public string? RepositoryAlias { get; init; }

        public DateTimeOffset ImportedAt { get; init; }
    }
}
