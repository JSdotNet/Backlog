using System.Text.Json;

using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps the workflow state of devbook subjects — proposals, decision records,
/// debt records — as one JSON file per repository, in <c>devbook-subjects</c>
/// under the storage folder.
/// <para>
/// The same home and the same reasons as <see cref="DevbookAnnotationStore"/>:
/// the state is the person's workspace data, so it lives under the storage folder,
/// moves with it (the folder is one of
/// <see cref="WorkspaceSettingsStore.OwnedRootFolders"/>) and replicates to their
/// other devices. It is not a table in <c>backlog.db</c>, which is Tasks', and it
/// is never the devbook database, which local ADRs 0004 and 0015 make a build
/// output rebuilt whole from the Markdown — state kept there would be lost on the
/// next build, and state is exactly what devbook's branch-true decision took out
/// of the Markdown.
/// </para>
/// <para>
/// One file per repository, named by the alias made safe for a path with a digest
/// beside it, camelCase and indented so a person can read it. Everything is held
/// in memory once read, under one lock, and every write rewrites the one file it
/// touched. A file that cannot be read counts as empty rather than fatal, as every
/// store here does: a corrupt state file must never stop a chapter from opening.
/// </para>
/// <para>
/// The rules are <see cref="DevbookSubjectRules"/>'; this class only keeps their
/// answer. A refusal comes back with no state, so nothing is written, nothing is
/// raised, and the file is byte-for-byte what it was.
/// </para>
/// </summary>
public sealed class DevbookSubjectStateStore : IDevbookSubjectStateStore
{
    /// <summary>The folder under the storage root the files go in.</summary>
    public const string FolderName = "devbook-subjects";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Func<string> _rootDirectory;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    /// <summary>The root the files below were read from. A different answer from
    /// <see cref="_rootDirectory"/> means the backlog moved, and everything is read
    /// again from the new place.</summary>
    private string? _loadedRoot;

    /// <summary>Keyed by <see cref="DevbookSubject.AliasKey"/>, which already
    /// folds case, so the map compares ordinally.</summary>
    private Dictionary<string, RepositoryFile> _files = new(StringComparer.Ordinal);

    /// <param name="rootDirectory">Where the storage folder is right now — asked
    /// on every call, so a backlog moved from Settings takes its states along.</param>
    /// <param name="time">Stamps the operations. Replication's writes carry their
    /// own stamps and never touch it.</param>
    public DevbookSubjectStateStore(Func<string> rootDirectory, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);

        _rootDirectory = rootDirectory;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>A store over one fixed folder — what a test or a harness that
    /// scopes its state to a content root composes.</summary>
    public DevbookSubjectStateStore(string rootDirectory, TimeProvider? time = null)
        : this(() => rootDirectory, time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
    }

    public event Action? Changed;

    public DevbookSubjectState? Get(string? repositoryAlias, DevbookSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        lock (_gate)
        {
            Load();

            return _files.TryGetValue(DevbookSubject.AliasKey(repositoryAlias), out var file)
                ? file.States.GetValueOrDefault(DevbookSubject.IdFor(repositoryAlias, subject))
                : null;
        }
    }

    public IReadOnlyList<DevbookSubjectState> List(string? repositoryAlias)
    {
        lock (_gate)
        {
            Load();

            return _files.TryGetValue(DevbookSubject.AliasKey(repositoryAlias), out var file)
                ? [.. file.States.Values.OrderBy(state => state.Subject.CanonicalKey, StringComparer.Ordinal)]
                : [];
        }
    }

    public DevbookSubjectState? Find(Guid id)
    {
        lock (_gate)
        {
            Load();

            return FindLoaded(id);
        }
    }

    public DevbookSubjectOutcome SetState(string? repositoryAlias, DevbookSubject subject, string state, string by) =>
        Operate(repositoryAlias, subject, (current, at) => DevbookSubjectRules.SetState(current, repositoryAlias, subject, state, by, at));

    public DevbookSubjectOutcome Approve(string? repositoryAlias, DevbookSubject subject, string fingerprint, string by) =>
        Operate(repositoryAlias, subject, (current, at) => DevbookSubjectRules.Approve(current, repositoryAlias, subject, fingerprint, by, at));

    public DevbookSubjectOutcome Accept(string? repositoryAlias, DevbookSubject subject, string fingerprint, string by) =>
        Operate(repositoryAlias, subject, (current, at) => DevbookSubjectRules.Accept(current, repositoryAlias, subject, fingerprint, by, at));

    public bool Apply(DevbookSubjectState replicated)
    {
        ArgumentNullException.ThrowIfNull(replicated);

        lock (_gate)
        {
            Load();

            if (!DevbookSubjectRules.Supersedes(replicated, FindLoaded(replicated.Id))) return false;

            Put(replicated);
        }

        Changed?.Invoke();

        return true;
    }

    public IReadOnlyList<DevbookSubjectState> ListChangedSince(DateTimeOffset watermark)
    {
        lock (_gate)
        {
            Load();

            return
            [
                .. _files.Values
                    .SelectMany(file => file.States.Values)
                    .Where(state => state.UpdatedAt > watermark)
                    .OrderBy(state => state.UpdatedAt)
                    .ThenBy(state => state.Id),
            ];
        }
    }

    /// <summary>One operation under the lock: the current state read, the rules
    /// asked, and only an answer that is not a refusal written and raised.</summary>
    private DevbookSubjectOutcome Operate(
        string? repositoryAlias,
        DevbookSubject subject,
        Func<DevbookSubjectState?, DateTimeOffset, DevbookSubjectOutcome> operation)
    {
        ArgumentNullException.ThrowIfNull(subject);

        DevbookSubjectOutcome outcome;

        lock (_gate)
        {
            Load();

            var current = FindLoaded(DevbookSubject.IdFor(repositoryAlias, subject));
            outcome = operation(current, Stamp(current));

            if (outcome.State is not { } state) return outcome;

            Put(state);
        }

        Changed?.Invoke();

        return outcome;
    }

    /// <summary>
    /// The stamp a local operation lands with: now, unless the state being changed
    /// already carries a later one — then one tick past it. The replica keeps the
    /// later copy, and a copy that arrived from a machine whose clock runs ahead is
    /// stamped in this machine's future; an operation here is by definition later
    /// than the state it was made to, so its stamp says so whatever the clocks
    /// think. The same rule <see cref="DevbookAnnotationStore"/> keeps.
    /// </summary>
    private DateTimeOffset Stamp(DevbookSubjectState? current)
    {
        var now = _time.GetUtcNow();
        return current is null || now > current.UpdatedAt ? now : current.UpdatedAt.AddTicks(1);
    }

    /// <summary>One document into its repository's file, on disk and in memory.
    /// Must be called under the lock, after <see cref="Load"/>.</summary>
    private void Put(DevbookSubjectState state)
    {
        var key = DevbookSubject.AliasKey(state.RepositoryAlias);

        if (!_files.TryGetValue(key, out var file))
        {
            file = new RepositoryFile(key, PathFor(key));
            _files[key] = file;
        }

        file.States[state.Id] = state with { RepositoryAlias = key };
        Save(file);
    }

    /// <summary>Must be called under the lock, after <see cref="Load"/>.</summary>
    private DevbookSubjectState? FindLoaded(Guid id) =>
        _files.Values
            .Select(file => file.States.GetValueOrDefault(id))
            .FirstOrDefault(state => state is not null);

    private string PathFor(string key) => Path.Combine(_rootDirectory(), FolderName, CachePaths.Safe(key) + ".json");

    /// <summary>Reads every repository file under the current root, once, and
    /// again whenever the root has moved since. Must be called under the
    /// lock.</summary>
    private void Load()
    {
        var root = _rootDirectory();

        if (string.Equals(_loadedRoot, root, StringComparison.OrdinalIgnoreCase)) return;

        var files = new Dictionary<string, RepositoryFile>(StringComparer.Ordinal);
        var directory = Path.Combine(root, FolderName);

        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
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

    /// <summary>One repository's file. An entry this version cannot name — an
    /// unknown kind, a blank key, a decision record with no pull request — is
    /// skipped rather than failing the file: it is state nobody here could ask
    /// for, and the rest of the file is still good. So is JSON that parses with no
    /// list (<c>"subjects": null</c>) or a hole in it: the type says neither can
    /// happen, the file on disk says what it likes, and a throw here would escape
    /// the catch below and stop every chapter from opening.</summary>
    private static RepositoryFile? Read(string path)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<RepositoryFileDto>(File.ReadAllText(path), JsonOptions);
            if (dto is null) return null;

            var file = new RepositoryFile(DevbookSubject.AliasKey(dto.RepositoryAlias), path);

            foreach (var entry in dto.Subjects ?? [])
            {
                if (entry is null || entry.Id == Guid.Empty || ToSubject(entry) is not { } subject || string.IsNullOrWhiteSpace(entry.State)) continue;

                file.States[entry.Id] = new DevbookSubjectState(
                    entry.Id,
                    file.Alias,
                    subject,
                    entry.State,
                    entry.By ?? string.Empty,
                    entry.At,
                    ToDecision(entry.Approval),
                    ToDecision(entry.Acceptance),
                    entry.UpdatedAt);
            }

            return file;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A file nobody can read is a file with nothing in it. The next write
            // to that repository rewrites it whole.
            return null;
        }
    }

    private static DevbookSubject? ToSubject(SubjectStateDto entry)
    {
        if (!DevbookSubjects.TryParseKind(entry.Kind, out var kind)) return null;

        try
        {
            return new DevbookSubject(kind, entry.Key ?? string.Empty, entry.PullRequest);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static DevbookSubjectDecision? ToDecision(DecisionDto? dto) =>
        dto is null ? null : new DevbookSubjectDecision(dto.By ?? string.Empty, dto.At, dto.Fingerprint ?? string.Empty);

    private static DecisionDto? FromDecision(DevbookSubjectDecision? decision) =>
        decision is null ? null : new DecisionDto { By = decision.By, At = decision.At, Fingerprint = decision.Fingerprint };

    private static void Save(RepositoryFile file)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);

            File.WriteAllText(file.Path, JsonSerializer.Serialize(new RepositoryFileDto
            {
                RepositoryAlias = file.Alias,
                Subjects =
                [
                    .. file.States.Values
                        .OrderBy(state => state.Subject.CanonicalKey, StringComparer.Ordinal)
                        .Select(state => new SubjectStateDto
                        {
                            Id = state.Id,
                            Kind = DevbookSubjects.KindName(state.Subject.Kind),
                            Key = state.Subject.Key,
                            PullRequest = state.Subject.PullRequest,
                            State = state.State,
                            By = state.By,
                            At = state.At,
                            Approval = FromDecision(state.Approval),
                            Acceptance = FromDecision(state.Acceptance),
                            UpdatedAt = state.UpdatedAt,
                        }),
                ],
            }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The state is in memory and on screen; only the copy for next time is
            // missing. The next write to the file tries again.
        }
    }

    private sealed class RepositoryFile(string alias, string path)
    {
        public string Alias { get; } = alias;

        public string Path { get; } = path;

        public Dictionary<Guid, DevbookSubjectState> States { get; } = [];
    }

    private sealed class RepositoryFileDto
    {
        public string? RepositoryAlias { get; init; }

        /// <summary>Nullable, entries too, because that is what a file on disk can
        /// hold; <see cref="Read"/> takes either as nothing.</summary>
        public List<SubjectStateDto?>? Subjects { get; init; } = [];
    }

    private sealed class SubjectStateDto
    {
        public Guid Id { get; init; }

        /// <summary>The contract's name for the kind: <c>proposal</c>,
        /// <c>decision</c> or <c>debt</c>.</summary>
        public string? Kind { get; init; }

        /// <summary>The change's folder name, or the record's repository
        /// path.</summary>
        public string? Key { get; init; }

        public int? PullRequest { get; init; }

        public string? State { get; init; }

        public string? By { get; init; }

        public DateTimeOffset At { get; init; }

        public DecisionDto? Approval { get; init; }

        public DecisionDto? Acceptance { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class DecisionDto
    {
        public string? By { get; init; }

        public DateTimeOffset At { get; init; }

        public string? Fingerprint { get; init; }
    }
}
