using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Resolves configured knowledge folders to local directories. Repository-scoped
/// knowledge uses the GitHub repository settings; the global view uses the
/// workspace settings.
/// <para>
/// This is the join, and it lives here on purpose. Answering "where does this
/// folder live?" needs both the repository settings and the workspace root, and
/// neither context may see the other; an adapter may see both, which is what an
/// adapter is for. Two module ports are served from this one engine so the
/// resolution rules exist once: Devbook takes it as
/// <see cref="IDevbookFolderSource"/>, Tasks through
/// <see cref="WorkspaceTaskStore"/> — which asks only for the storage root,
/// because <c>.backlog</c> is not a knowledge folder. Tasks keeps
/// its entries in the workspace, not in a configured section of somebody's
/// repository.
/// </para>
/// </summary>
public sealed class DevbookFolderSource : IDevbookFolderSource
{
    private readonly GitHubSettingsStore _settings;
    private readonly WorkspaceSettingsStore _store;
    private readonly bool _useRepositoryFallbackWhenNoAlias;

    /// <summary>
    /// The branch snapshots, or null where nothing composed one.
    /// <para>
    /// Optional rather than required because a snapshot needs a network client
    /// and several compositions — tests, and anything that only ever reads a
    /// local folder — have no business constructing one. Where it is absent, a
    /// repository with no clone answers exactly what it answered before branch
    /// loading existed.
    /// </para>
    /// </summary>
    private readonly IDevbookSnapshotCache? _snapshots;

    /// <summary>
    /// What keeps a branch snapshot present and current on its own. Present
    /// exactly when <see cref="_snapshots"/> is, because it is the network half
    /// of the same thing: a cache nothing fills is a cache that reports "not
    /// fetched" forever, which is what the Settings rows used to show.
    /// </summary>
    private readonly DevbookSnapshotAutoFetch? _autoFetch;

    public DevbookFolderSource(GitHubSettingsStore settings, WorkspaceSettingsStore store)
        : this(settings, store, useRepositoryFallbackWhenNoAlias: false)
    {
    }

    public DevbookFolderSource(GitHubSettingsStore settings, WorkspaceSettingsStore store, IDevbookSnapshotCache? snapshots)
        : this(settings, store, useRepositoryFallbackWhenNoAlias: false, snapshots)
    {
    }

    /// <summary>The devbook-only composition: nothing has told this source
    /// where the workspace is, so an unscoped question falls back to the first
    /// configured repository rather than to a storage folder nobody chose.</summary>
    public DevbookFolderSource(GitHubSettingsStore settings)
        : this(
            settings,
            new WorkspaceSettingsStore(Path.Combine(Path.GetTempPath(), "backlog-devbook-source")),
            useRepositoryFallbackWhenNoAlias: true)
    {
    }

    public DevbookFolderSource(
        GitHubSettingsStore settings,
        WorkspaceSettingsStore store,
        bool useRepositoryFallbackWhenNoAlias,
        IDevbookSnapshotCache? snapshots = null,
        TimeProvider? time = null,
        TimeSpan? recheckInterval = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);

        _settings = settings;
        _store = store;
        _useRepositoryFallbackWhenNoAlias = useRepositoryFallbackWhenNoAlias;
        _snapshots = snapshots;

        if (snapshots is not null)
        {
            _autoFetch = new DevbookSnapshotAutoFetch(snapshots, NotifyContentChanged, time, recheckInterval);

            // A settings change may be the very thing a remembered failure was
            // about — a token added, a branch re-pointed — so the memory goes
            // with it and the next resolve asks again rather than waiting out
            // the interval to notice.
            _settings.Changed += _autoFetch.Forget;
        }
    }

    /// <summary>The background fetch running for this repository's branch, or
    /// null. Exposed for tests, which otherwise have nothing to await.</summary>
    public Task? PendingFetch(GitHubRepositoryRef repository) =>
        _autoFetch?.InFlight(repository, repository.DevbookBranch);

    /// <summary>
    /// Three sources of the same news, so the accessor forwards to two stores and
    /// keeps a delegate of its own. The own delegate is what
    /// <see cref="NotifyContentChanged"/> raises: content being replaced under a
    /// folder is nothing either settings store can know about, because nothing was
    /// configured differently.
    /// </summary>
    private Action? _contentChanged;

    public event Action? Changed
    {
        add
        {
            _settings.Changed += value;
            _store.RootChanged += value;
            _contentChanged += value;
        }
        remove
        {
            _settings.Changed -= value;
            _store.RootChanged -= value;
            _contentChanged -= value;
        }
    }

    public void NotifyContentChanged() => _contentChanged?.Invoke();

    public string StorageDirectory => _store.RootDirectory;

    public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias)
    {
        if (string.IsNullOrWhiteSpace(repositoryAlias)) return _store.DevbookFolders;

        return _settings.Current.Find(repositoryAlias) is { } repository
            ? repository.DevbookFolders
            : _store.DevbookFolders;
    }

    public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!string.IsNullOrWhiteSpace(repositoryAlias)) return ResolveRepository(key, repositoryAlias);

        if (!_useRepositoryFallbackWhenNoAlias) return ResolveStorage(key);

        return _settings.Current.Repositories.Count > 0
            ? ResolveRepository(key, _settings.Current.Repositories[0].Alias)
            : DevbookFolderLocation.Unavailable(key, "Configure a repository before opening the devbook.");
    }

    private DevbookFolderLocation ResolveStorage(string key)
    {
        var folder = _store.DevbookFolders.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
        if (folder is null)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"Storage has no {key} knowledge-folder setting.");
        }

        if (!folder.Enabled)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge folder is turned off for storage.",
                folder: folder,
                rootPath: _store.RootDirectory);
        }

        return ResolvePath(key, folder, _store.RootDirectory, null, "storage", _store.RootDirectory);
    }

    private DevbookFolderLocation ResolveRepository(string key, string repositoryAlias)
    {
        var repository = _settings.Current.Find(repositoryAlias);
        if (repository is null)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                "Select a configured repository before opening the devbook.");
        }

        var folder = repository.DevbookFolders.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
        if (folder is null)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"{repository.FullName} has no {key} knowledge-folder setting.",
                repository.FullName,
                repositoryAlias: repository.Alias);
        }

        if (!folder.Enabled)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge folder is turned off for {repository.FullName}.",
                repository.FullName,
                folder,
                rootPath: repository.CloneDirectory,
                repositoryAlias: repository.Alias);
        }

        if (repository.DevbookSource is DevbookSourceKind.Branch) return ResolveBranch(key, folder, repository);

        return ResolvePath(key, folder, repository.CloneDirectory!, repository, repository.FullName, repository.CloneDirectory);
    }

    /// <summary>
    /// Resolves against the cached copy of the repository's branch.
    /// <para>
    /// Never waits on the network. This runs during every panel load, and a
    /// resolution that blocked on GitHub would put it in front of opening a tab.
    /// What it does do is make sure the network has been <em>asked</em>: a branch
    /// nobody has indexed starts its index download here, in the background,
    /// and resolves to "fetching" until it lands; a branch with an index is
    /// served from it and re-checked against its head on a cadence
    /// <see cref="DevbookSnapshotAutoFetch"/> owns. A caller that would rather
    /// wait than be told "fetching" — the menu — goes through
    /// <see cref="PrepareListingAsync"/>, which joins that same download. Refresh
    /// is still never a precondition, which is the rule ADR 0004 states for the
    /// devbook database — it is just no longer something a person has to press
    /// for.
    /// </para>
    /// <para>
    /// Whether the folder exists is asked of the index, not the disk: a branch
    /// whose <c>.arc42</c> has not been fetched yet still <em>has</em> one, and
    /// the menu lists it from the index before a chapter is here.
    /// </para>
    /// </summary>
    private DevbookFolderLocation ResolveBranch(string key, DevbookFolderSetting folder, GitHubRepositoryRef repository)
    {
        var branch = repository.DevbookBranch;

        if (_snapshots is null || _autoFetch is null)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"Add a local clone directory for {repository.FullName} in Settings to read the {folder.DisplayName} knowledge folder.",
                repository.FullName,
                folder,
                repositoryAlias: repository.Alias);
        }

        var root = _snapshots.SnapshotPath(repository, branch);
        var label = string.IsNullOrWhiteSpace(branch) ? "its default branch" : branch.Trim();
        var subject = string.IsNullOrWhiteSpace(branch)
            ? $"the default branch of {repository.FullName}"
            : $"{repository.FullName} ({label})";
        var snapshot = _snapshots.TryRead(repository, branch);
        var fetch = _autoFetch.Ensure(repository, branch, hasSnapshot: snapshot is not null);

        if (snapshot is null || SnapshotTree(repository) is not { } tree)
        {
            // Pending rather than failed while the first download runs: the row
            // and the panel both show it, and both hear the announcement when it
            // lands. Only a fetch that came back with nothing is an error, and
            // then the words are GitHub's rather than a paraphrase.
            return fetch.Failure is null
                ? DevbookFolderLocation.Unavailable(
                    key,
                    $"Fetching {subject} from GitHub…",
                    repository.FullName,
                    folder,
                    rootPath: root,
                    repositoryAlias: repository.Alias,
                    source: DevbookSourceKind.Branch,
                    pending: true)
                : DevbookFolderLocation.Unavailable(
                    key,
                    $"Could not fetch {subject}: {fetch.Failure}",
                    repository.FullName,
                    folder,
                    rootPath: root,
                    repositoryAlias: repository.Alias,
                    source: DevbookSourceKind.Branch);
        }

        // The scope label names the branch rather than just the repository,
        // because "which of these am I reading?" is a real question the moment
        // the same repository can be read two ways.
        return ResolvePath(
            key,
            folder,
            root,
            repository,
            $"{repository.FullName} ({label})",
            root,
            DevbookSourceKind.Branch,
            tree);
    }

    public async Task<DevbookFolderLocation> PrepareListingAsync(
        string key,
        string? repositoryAlias = null,
        CancellationToken cancellationToken = default)
    {
        var location = Resolve(key, repositoryAlias);
        if (BranchRepository(location) is not { } repository) return location;

        if (_snapshots!.TryRead(repository, repository.DevbookBranch) is null)
        {
            // Resolve has just asked the auto-fetch to take the index, so the
            // download to wait for is the one already in flight — starting a
            // second would list the commit twice for one menu. When there is
            // none, the auto-fetch is sitting on a remembered failure, and that
            // failure is the answer until the settings change or the interval
            // passes; asking GitHub again from here would spend the very
            // requests it exists to ration.
            if (PendingFetch(repository) is { } inFlight)
            {
                await inFlight.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            location = Resolve(key, repositoryAlias);
            if (!location.Available || _snapshots.TryRead(repository, repository.DevbookBranch) is null) return location;
        }

        // The reading-order files are part of the listing rather than of any
        // one area: the menu is ordered by them, and a menu drawn without them
        // would silently fall back to alphabetical. A failure here is exactly
        // that fallback, so it is not reported as the folder being unavailable.
        await _snapshots.EnsureAsync(repository, repository.DevbookBranch, ListingFiles, cancellationToken).ConfigureAwait(false);

        return Resolve(key, repositoryAlias);
    }

    public async Task<DevbookFolderLocation> PrepareContentAsync(
        string key,
        string? repositoryAlias = null,
        IReadOnlyCollection<string>? relativePaths = null,
        CancellationToken cancellationToken = default)
    {
        var location = await PrepareListingAsync(key, repositoryAlias, cancellationToken).ConfigureAwait(false);
        if (!location.Available || BranchRepository(location) is not { } repository) return location;

        var folder = FolderRelativePath(location.Folder);
        if (folder is null) return location;

        var selection = relativePaths is null
            ? new[] { DevbookSnapshotSelection.Subtree(folder), DevbookSnapshotSelection.Exclude(RenderedArtifactsFolder) }
            : relativePaths.Select(path => WithinFolder(folder, path)).ToArray();

        var result = await _snapshots!.EnsureAsync(repository, repository.DevbookBranch, selection, cancellationToken).ConfigureAwait(false);
        if (result.Message is null || result.Updated) return location;

        // Nothing landed. What was already here is still readable, so the
        // folder stays available when it holds anything at all; only a folder
        // with nothing in it reports the reason, because a reader of an empty
        // folder would otherwise be told the branch has no chapters.
        var index = _snapshots.TryReadIndex(repository, repository.DevbookBranch);
        var holdsSomething = index is not null && index.Fetched.Any(path =>
            folder.Length == 0 || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));

        return holdsSomething
            ? location
            : DevbookFolderLocation.Unavailable(
                key,
                result.Message,
                repository.FullName,
                location.Folder,
                location.FullPath,
                location.RootPath,
                location.ScopeLabel,
                repository.Alias,
                DevbookSourceKind.Branch);
    }

    public IDevbookFileTree FileTree(DevbookFolderLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);

        return BranchRepository(location) is { } repository && SnapshotTree(repository) is { } tree
            ? tree
            : DevbookDiskFileTree.Instance;
    }

    /// <summary>The files every listing needs, wherever they sit: the authored
    /// reading order, and the generated titles where a repository commits them.</summary>
    private static readonly string[] ListingFiles =
    [
        DevbookSnapshotSelection.AnyDepth("_reading-order.json"),
        DevbookSnapshotSelection.AnyDepth("_meta/index.json")
    ];

    /// <summary>The rendered diagram artifacts beside a chapter. Hundreds of
    /// kilobytes each and read only when a rendered diagram is shown, so an area
    /// is fetched without them — they are the bulk of what the archive used to
    /// carry, and the reason it was slow.</summary>
    private const string RenderedArtifactsFolder = "_archify";

    /// <summary>The repository behind a branch location, or null when the
    /// location is a local folder or this composition has no snapshots.</summary>
    private GitHubRepositoryRef? BranchRepository(DevbookFolderLocation location) =>
        _snapshots is not null
        && location.Source is DevbookSourceKind.Branch
        && !string.IsNullOrWhiteSpace(location.RepositoryAlias)
            ? _settings.Current.Find(location.RepositoryAlias)
            : null;

    /// <summary>The last index read, as a tree, so five areas resolving against
    /// one branch on one load parse its index once rather than five times.</summary>
    private (string Root, string Sha, DevbookSnapshotFileTree Tree)? _lastTree;

    private DevbookSnapshotFileTree? SnapshotTree(GitHubRepositoryRef repository)
    {
        if (_snapshots is null) return null;

        var root = _snapshots.SnapshotPath(repository, repository.DevbookBranch);

        if (_snapshots.TryRead(repository, repository.DevbookBranch) is not { } snapshot) return null;

        var last = _lastTree;
        if (last is { } cached
            && string.Equals(cached.Root, root, StringComparison.OrdinalIgnoreCase)
            && string.Equals(cached.Sha, snapshot.Sha, StringComparison.OrdinalIgnoreCase))
        {
            return cached.Tree;
        }

        if (_snapshots.TryReadIndex(repository, repository.DevbookBranch) is not { } index) return null;

        var tree = new DevbookSnapshotFileTree(root, index.Entries);
        _lastTree = (root, snapshot.Sha, tree);
        return tree;
    }

    /// <summary>Where the folder sits in the repository, <c>/</c>-separated and
    /// empty at the root — or null for a folder pointed outside the repository
    /// by an absolute path, which no branch can carry.</summary>
    private static string? FolderRelativePath(DevbookFolderSetting? folder)
    {
        var path = folder?.EffectivePath ?? string.Empty;
        if (Path.IsPathRooted(path)) return null;

        return DevbookSnapshotSelection.Normalize(path).Trim('/');
    }

    /// <summary>A caller's folder-relative path as a repository-relative one;
    /// the any-depth and exclusion forms are already repository-wide and pass
    /// through.</summary>
    private static string WithinFolder(string folder, string path)
    {
        var trimmed = path.Trim();

        if (trimmed.StartsWith(DevbookSnapshotSelection.AnyDepthPrefix, StringComparison.Ordinal)
            || trimmed.StartsWith(DevbookSnapshotSelection.ExcludePrefix))
        {
            return trimmed;
        }

        var subtree = trimmed.EndsWith('/');
        var relative = DevbookSnapshotSelection.Normalize(trimmed).Trim('/');
        var combined = folder.Length == 0 ? relative : relative.Length == 0 ? folder : $"{folder}/{relative}";

        return subtree ? DevbookSnapshotSelection.Subtree(combined) : combined;
    }

    private static DevbookFolderLocation ResolvePath(
        string key,
        DevbookFolderSetting folder,
        string rootDirectory,
        GitHubRepositoryRef? repository,
        string scopeLabel,
        string? rootPath,
        DevbookSourceKind source = DevbookSourceKind.LocalFolder,
        IDevbookFileTree? tree = null)
    {
        tree ??= DevbookDiskFileTree.Instance;

        var path = folder.EffectivePath;
        var fullPath = Path.IsPathRooted(path)
            ? path
            : Path.Combine(rootDirectory, path);

        try
        {
            fullPath = Path.GetFullPath(fullPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return DevbookFolderLocation.Unavailable(
                key,
                $"{folder.DisplayName} knowledge folder path is not valid: {ex.Message}",
                repository?.FullName,
                folder,
                fullPath,
                rootPath,
                repositoryAlias: repository?.Alias,
                source: source);
        }

        if (!tree.DirectoryExists(fullPath))
        {
            // Worth different words when the tree came from a branch: the folder
            // is not missing from somebody's disk, it is missing from the commit,
            // and telling them to check their clone would send them looking in a
            // folder that has nothing to do with it.
            return DevbookFolderLocation.Unavailable(
                key,
                source is DevbookSourceKind.Branch
                    ? $"{scopeLabel} has no {folder.DisplayName} knowledge folder at {folder.EffectivePath}."
                    : $"{folder.DisplayName} knowledge folder was not found at {fullPath}.",
                repository?.FullName,
                folder,
                fullPath,
                rootPath,
                repositoryAlias: repository?.Alias,
                source: source);
        }

        return new DevbookFolderLocation(
            key,
            true,
            null,
            repository?.FullName,
            folder,
            fullPath,
            rootPath,
            scopeLabel,
            repository?.Alias,
            source);
    }
}
