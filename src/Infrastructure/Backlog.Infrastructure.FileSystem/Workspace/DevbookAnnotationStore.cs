using System.Text.Json;

using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps a person's remarks on Devbook chapters as one JSON file per
/// repository, in <c>devbook-annotations</c> under the storage folder.
/// <para>
/// Under the storage folder rather than beside the per-user settings because a
/// remark is the person's data on the same terms as their backlog: it belongs
/// to the workspace, moves with it (the folder is one of
/// <see cref="WorkspaceSettingsStore.OwnedRootFolders"/>), and is what a second
/// device wants a copy of. It is deliberately not a table in <c>backlog.db</c>:
/// that file has three owners already and a remark on a repository's chapter is
/// Devbook's, not Tasks'. And it is deliberately not <c>_meta/devbook.db</c> in
/// the repository, which .arc42/adr/0004 makes a generated, ignored build output
/// the app never writes — a remark kept there would be lost on the next
/// generator run and could never leave the machine.
/// </para>
/// <para>
/// One file per repository rather than one for everything, so that the file a
/// person opens to see what they wrote about one repository is that
/// repository's and nobody else's; the name is the alias made safe for a path
/// with a digest beside it, the same folding the caches use. The file is meant
/// to be read: camelCase, indented, tombstones kept until replication has
/// carried them and then kept anyway, because the store has no way to know
/// when every device has seen one and the file is small.
/// </para>
/// <para>
/// Everything is held in memory once read, under one lock, and every write
/// rewrites the one repository file it touched. A file that cannot be read is
/// treated as empty rather than fatal: a corrupt remark file must never stop a
/// chapter from opening, and the fallback matches what every other settings
/// store here does.
/// </para>
/// </summary>
public sealed class DevbookAnnotationStore : IDevbookAnnotationStore
{
    /// <summary>The folder under the storage root the files go in.</summary>
    public const string FolderName = "devbook-annotations";

    /// <summary>The alias an unscoped chapter is filed under. A storybook page or
    /// a harness with no repository registry still asks for remarks, and the
    /// answer has to be the same key on every call.</summary>
    private const string UnscopedAlias = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<string> _rootDirectory;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    /// <summary>The root the files below were read from. A different answer
    /// from <see cref="_rootDirectory"/> means the backlog moved, and everything
    /// is read again from the new place.</summary>
    private string? _loadedRoot;

    private Dictionary<string, RepositoryFile> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="rootDirectory">Where the storage folder is right now — asked
    /// on every call rather than fixed, so a backlog moved from Settings takes
    /// its remarks along the way the inbox repository follows the same
    /// pointer.</param>
    /// <param name="time">Stamps the writes a panel makes. Replication's writes
    /// carry their own stamps and never touch it.</param>
    public DevbookAnnotationStore(Func<string> rootDirectory, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);

        _rootDirectory = rootDirectory;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>A store over one fixed folder — what a test or a harness that
    /// scopes its state to a content root composes.</summary>
    public DevbookAnnotationStore(string rootDirectory, TimeProvider? time = null)
        : this(() => rootDirectory, time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
    }

    public event Action? Changed;

    /// <summary>The folder the files are in right now, for a settings screen to
    /// show.</summary>
    public string Directory => Path.Combine(_rootDirectory(), FolderName);

    public IReadOnlyList<DevbookAnnotation> List(string? repositoryAlias, string chapterPath)
    {
        ArgumentNullException.ThrowIfNull(chapterPath);

        lock (_gate)
        {
            Load();

            if (!_files.TryGetValue(Key(repositoryAlias), out var file)) return [];

            return
            [
                .. file.Annotations.Values
                    .Where(annotation => annotation.IsLive)
                    .Where(annotation => string.Equals(annotation.ChapterPath, chapterPath, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(annotation => annotation.CreatedAt)
                    .ThenBy(annotation => annotation.Id),
            ];
        }
    }

    public DevbookAnnotation? Find(Guid id)
    {
        lock (_gate)
        {
            Load();

            return FindLoaded(id);
        }
    }

    public DevbookAnnotation Add(string? repositoryAlias, string chapterPath, int blockIndex, string author)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterPath);
        ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
        ArgumentNullException.ThrowIfNull(author);

        var now = _time.GetUtcNow();
        var annotation = new DevbookAnnotation(
            Guid.NewGuid(),
            Key(repositoryAlias),
            chapterPath,
            blockIndex,
            string.Empty,
            author,
            now,
            now);

        Write(annotation);

        return annotation;
    }

    public void Edit(Guid id, string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        Mutate(id, annotation => annotation with { Body = body, UpdatedAt = _time.GetUtcNow() });
    }

    public void SetResolved(Guid id, bool resolved) =>
        Mutate(id, annotation => annotation with { Resolved = resolved, UpdatedAt = _time.GetUtcNow() });

    public void Delete(Guid id)
    {
        lock (_gate)
        {
            Load();

            if (FindLoaded(id) is not { IsLive: true } annotation) return;

            if (annotation.IsDraft)
            {
                // Nothing to replicate, so nothing to leave behind: a draft
                // never made it past ListChangedSince.
                var file = _files[annotation.RepositoryAlias];
                file.Annotations.Remove(id);
                Save(file);
            }
            else
            {
                var now = _time.GetUtcNow();
                Put(annotation with { DeletedAt = now, UpdatedAt = now });
            }
        }

        Changed?.Invoke();
    }

    public void Apply(DevbookAnnotation replicated)
    {
        ArgumentNullException.ThrowIfNull(replicated);

        Write(replicated);
    }

    public IReadOnlyList<DevbookAnnotation> ListChangedSince(DateTimeOffset watermark)
    {
        lock (_gate)
        {
            Load();

            return
            [
                .. _files.Values
                    .SelectMany(file => file.Annotations.Values)
                    .Where(annotation => !annotation.IsDraft)
                    .Where(annotation => annotation.UpdatedAt > watermark)
                    .OrderBy(annotation => annotation.UpdatedAt)
                    .ThenBy(annotation => annotation.Id),
            ];
        }
    }

    private void Mutate(Guid id, Func<DevbookAnnotation, DevbookAnnotation> change)
    {
        lock (_gate)
        {
            Load();

            if (FindLoaded(id) is not { IsLive: true } annotation) return;

            Put(change(annotation));
        }

        Changed?.Invoke();
    }

    /// <summary>Puts one document in its repository's file and says so.</summary>
    private void Write(DevbookAnnotation annotation)
    {
        lock (_gate)
        {
            Load();
            Put(annotation);
        }

        Changed?.Invoke();
    }

    /// <summary>One document into its repository's file, on disk and in memory.
    /// The alias on the document decides which file — an applied document names
    /// its own. Must be called under the lock, after <see cref="Load"/>.</summary>
    private void Put(DevbookAnnotation annotation)
    {
        var key = Key(annotation.RepositoryAlias);

        if (!_files.TryGetValue(key, out var file))
        {
            file = new RepositoryFile(key, PathFor(key));
            _files[key] = file;
        }

        file.Annotations[annotation.Id] = annotation with { RepositoryAlias = key };
        Save(file);
    }

    /// <summary>Must be called under the lock, after <see cref="Load"/>.</summary>
    private DevbookAnnotation? FindLoaded(Guid id) =>
        _files.Values
            .Select(file => file.Annotations.GetValueOrDefault(id))
            .FirstOrDefault(annotation => annotation is not null);

    /// <summary>The alias as a key: the empty alias for an unscoped chapter,
    /// and otherwise the alias as given. Case is folded on lookup rather than
    /// here, so the file keeps the spelling the registry has.</summary>
    private static string Key(string? repositoryAlias) =>
        string.IsNullOrWhiteSpace(repositoryAlias) ? UnscopedAlias : repositoryAlias.Trim();

    private string PathFor(string key) => Path.Combine(Directory, CachePaths.Safe(key) + ".json");

    /// <summary>Reads every repository file under the current root, once, and
    /// again whenever the root has moved since. Must be called under the lock.</summary>
    private void Load()
    {
        var root = _rootDirectory();

        if (string.Equals(_loadedRoot, root, StringComparison.OrdinalIgnoreCase)) return;

        var files = new Dictionary<string, RepositoryFile>(StringComparer.OrdinalIgnoreCase);
        var directory = Path.Combine(root, FolderName);

        if (System.IO.Directory.Exists(directory))
        {
            foreach (var path in System.IO.Directory.EnumerateFiles(directory, "*.json"))
            {
                if (Read(path) is { } file)
                {
                    files[file.Alias] = file;
                }
            }
        }

        _files = files;
        _loadedRoot = root;
    }

    private static RepositoryFile? Read(string path)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<RepositoryFileDto>(File.ReadAllText(path), JsonOptions);
            if (dto is null) return null;

            var file = new RepositoryFile(Key(dto.RepositoryAlias), path);

            foreach (var entry in dto.Annotations)
            {
                if (entry.Id == Guid.Empty || string.IsNullOrWhiteSpace(entry.ChapterPath)) continue;

                file.Annotations[entry.Id] = new DevbookAnnotation(
                    entry.Id,
                    file.Alias,
                    entry.ChapterPath,
                    Math.Max(0, entry.BlockIndex),
                    entry.Body ?? string.Empty,
                    entry.Author ?? string.Empty,
                    entry.CreatedAt,
                    entry.UpdatedAt,
                    entry.Resolved,
                    entry.DeletedAt);
            }

            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A file nobody can read is a file with nothing in it. The next
            // write to that repository rewrites it whole.
            return null;
        }
    }

    private static void Save(RepositoryFile file)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);

            File.WriteAllText(file.Path, JsonSerializer.Serialize(new RepositoryFileDto
            {
                RepositoryAlias = file.Alias,
                Annotations =
                [
                    .. file.Annotations.Values
                        .OrderBy(annotation => annotation.CreatedAt)
                        .ThenBy(annotation => annotation.Id)
                        .Select(annotation => new AnnotationDto
                        {
                            Id = annotation.Id,
                            ChapterPath = annotation.ChapterPath,
                            BlockIndex = annotation.BlockIndex,
                            Body = annotation.Body,
                            Author = annotation.Author,
                            CreatedAt = annotation.CreatedAt,
                            UpdatedAt = annotation.UpdatedAt,
                            Resolved = annotation.Resolved,
                            DeletedAt = annotation.DeletedAt,
                        }),
                ],
            }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The remark is on screen and in memory; only the copy for next
            // time is missing. The next write to the file tries again.
        }
    }

    private sealed class RepositoryFile(string alias, string path)
    {
        public string Alias { get; } = alias;

        public string Path { get; } = path;

        public Dictionary<Guid, DevbookAnnotation> Annotations { get; } = [];
    }

    private sealed class RepositoryFileDto
    {
        public string? RepositoryAlias { get; init; }

        public List<AnnotationDto> Annotations { get; init; } = [];
    }

    private sealed class AnnotationDto
    {
        public Guid Id { get; init; }

        public string? ChapterPath { get; init; }

        public int BlockIndex { get; init; }

        public string? Body { get; init; }

        public string? Author { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public bool Resolved { get; init; }

        public DateTimeOffset? DeletedAt { get; init; }
    }
}
