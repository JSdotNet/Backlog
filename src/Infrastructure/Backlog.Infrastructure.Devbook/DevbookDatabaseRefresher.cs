using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// Keeps each repository's devbook database current, in the background, when
/// the repository is read — the "on open" refresh of local ADR 0015.
///
/// <para><see cref="StartForApp"/> configures <see cref="DevbookDatabaseLocation"/>
/// with the app's devbook cache folder and makes the refresher the observer every
/// database ask reports to. An ask schedules a check for that repository and returns at once;
/// the read that triggered it carries on down ADR 0004's ladder with whatever
/// exists now, so nothing ever waits on a build. A check is one directory walk
/// and one <c>stat</c> per input (<see cref="DevbookDatabaseBuilder.IsCurrent"/>),
/// and a rebuild happens only when the database is absent, in another schema
/// version, or any input changed.</para>
///
/// <para>One check per repository at a time, and none again within
/// <see cref="QuietInterval"/> of the last, so a panel asking a dozen times while
/// it draws costs one walk. Nothing runs at startup: a repository nobody opens is
/// never checked. Disposing cancels a build in flight; the half-built temporary
/// file is removed and the old database, if any, stays.</para>
///
/// <para>The Brain is also watched (<see cref="WatchBrain"/>): its notes change
/// from outside the app — the folder's own sync brings another machine's edits —
/// so a change in the folder is itself an ask. Changes inside the quiet interval
/// collapse into one check at its end rather than being dropped, so the last
/// edit of a burst is always built. A folder recorded as a Brain
/// (<see cref="DevbookDatabaseLocation.ForBrainFolder"/>) is built under the
/// Brain's convention.</para>
/// </summary>
public sealed class DevbookDatabaseRefresher : IDisposable
{
    /// <summary>The default gap between two checks of one repository.</summary>
    public static readonly TimeSpan DefaultQuietInterval = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>The normalized roots <see cref="WatchBrain"/> is watching.</summary>
    private readonly ConcurrentDictionary<string, byte> _watched = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Func<string?> _devbookCacheDirectory;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Func<string, IDisposable?> _watch;

    public DevbookDatabaseRefresher(
        Func<string?> devbookCacheDirectory,
        ILogger<DevbookDatabaseRefresher>? logger = null,
        TimeSpan? quietInterval = null,
        TimeProvider? time = null,
        Func<string, Action, IDisposable?>? watchFolder = null)
    {
        ArgumentNullException.ThrowIfNull(devbookCacheDirectory);

        _devbookCacheDirectory = devbookCacheDirectory;
        _logger = logger ?? NullLogger<DevbookDatabaseRefresher>.Instance;
        _time = time ?? TimeProvider.System;
        QuietInterval = quietInterval ?? DefaultQuietInterval;

        var watch = watchFolder ?? WatchFolder;
        _watch = root => watch(root, () => NotifyChanged(root));
    }

    /// <summary>
    /// The app's refresher: builds databases under
    /// <paramref name="devbookCacheDirectory"/> and hears every ask
    /// <see cref="DevbookDatabaseLocation"/> answers, which it is pointed at the
    /// same folder for. One per process, created by the composition root.
    /// </summary>
    public static DevbookDatabaseRefresher StartForApp(Func<string?> devbookCacheDirectory, ILogger<DevbookDatabaseRefresher>? logger = null)
    {
        var refresher = new DevbookDatabaseRefresher(devbookCacheDirectory, logger);
        DevbookDatabaseLocation.Configure(devbookCacheDirectory, refresher.Request);
        return refresher;
    }

    /// <summary>How long after a check of a repository another ask for it is
    /// ignored.</summary>
    public TimeSpan QuietInterval { get; }

    /// <summary>Raised, off the UI thread, with the repository root whose
    /// database was just rebuilt, so a surface showing "unavailable" can ask
    /// again.</summary>
    public event Action<string>? Rebuilt;

    /// <summary>
    /// Schedules a check of <paramref name="repositoryRoot"/> unless one is
    /// running or ran within <see cref="QuietInterval"/>. Never blocks.
    /// </summary>
    public void Request(string repositoryRoot)
    {
        if (_stopping.IsCancellationRequested) return;
        if (DevbookDatabaseLocation.NormalizedRoot(repositoryRoot) is not { } key) return;

        var now = _time.GetUtcNow();
        var entry = _entries.GetOrAdd(key, _ => new Entry());

        lock (entry)
        {
            if (entry.Running is { IsCompleted: false }) return;
            if (entry.LastChecked is { } last && now - last < QuietInterval) return;

            entry.LastChecked = now;
            var stopping = _stopping.Token;
            entry.Running = Task.Run(() => RefreshCore(repositoryRoot, stopping));
        }
    }

    /// <summary>
    /// Says that something in the folder at <paramref name="root"/> changed:
    /// schedules a check now, or — when one is running or ran within
    /// <see cref="QuietInterval"/> — one check when the interval ends, however
    /// many changes arrive before it. Never blocks.
    /// </summary>
    public void NotifyChanged(string root)
    {
        if (_stopping.IsCancellationRequested) return;
        if (DevbookDatabaseLocation.NormalizedRoot(root) is not { } key) return;

        var now = _time.GetUtcNow();
        var entry = _entries.GetOrAdd(key, _ => new Entry());

        lock (entry)
        {
            // Checked again under the lock Dispose sweeps under, so no timer is
            // made after the sweep.
            if (entry.Deferred is not null || _stopping.IsCancellationRequested) return;

            var due = entry.Running is { IsCompleted: false }
                ? QuietInterval
                : entry.LastChecked is { } last ? last + QuietInterval - now : TimeSpan.Zero;

            if (due > TimeSpan.Zero)
            {
                entry.Deferred = _time.CreateTimer(_ => Deferred(root, entry), null, due, Timeout.InfiniteTimeSpan);
                return;
            }
        }

        Request(root);
    }

    private void Deferred(string root, Entry entry)
    {
        lock (entry)
        {
            entry.Deferred?.Dispose();
            entry.Deferred = null;
        }

        NotifyChanged(root);
    }

    /// <summary>
    /// Watches the Brain folder at <paramref name="brainRoot"/>: records it as a
    /// Brain, checks it now, and checks it again whenever a file or folder in it
    /// is added, removed, renamed, written, or has its attributes changed — which
    /// is how a OneDrive placeholder announces it was hydrated — at most once per
    /// <see cref="QuietInterval"/>. Dispose the result to stop watching; a folder
    /// that does not exist is not watched.
    /// </summary>
    public IDisposable WatchBrain(string brainRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brainRoot);

        DevbookDatabaseLocation.RegisterBrainRoot(brainRoot);
        var key = DevbookDatabaseLocation.NormalizedRoot(brainRoot);
        if (key is not null) _watched.TryAdd(key, 0);

        var watcher = _watch(brainRoot);
        NotifyChanged(brainRoot);

        return new Watch(watcher, () =>
        {
            if (key is not null) _watched.TryRemove(key, out _);
        });
    }

    /// <summary>A recursive watch over every change a build reads, calling
    /// <paramref name="changed"/> on each — and on an overflowed buffer, after
    /// which anything may have changed.</summary>
    private static IDisposable? WatchFolder(string root, Action changed)
    {
        if (!Directory.Exists(root)) return null;

        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                    | NotifyFilters.Size | NotifyFilters.Attributes
            };
            // A dot-folder — .git, .obsidian, .trash — holds no note, and an editor
            // rewriting its own state there every few seconds would keep a walk
            // of the whole folder running once per quiet interval.
            void Changed(string fullPath)
            {
                var relative = Path.GetRelativePath(root, fullPath);
                if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment.StartsWith('.') && segment is not "." and not "..")) return;
                changed();
            }

            watcher.Changed += (_, e) => Changed(e.FullPath);
            watcher.Created += (_, e) => Changed(e.FullPath);
            watcher.Deleted += (_, e) => Changed(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                Changed(e.OldFullPath);
                Changed(e.FullPath);
            };
            watcher.Error += (_, _) => changed();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks <paramref name="repositoryRoot"/> now and rebuilds its database if
    /// it is not current, waiting for the answer. Returns whether a build ran.
    /// For a caller that must have the database — a test, a command. The walk and
    /// the build run on the pool, so a caller on the UI thread waits without
    /// blocking it.
    /// </summary>
    public async Task<bool> RefreshAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        // Disposed only once the check has finished: the build asks this token
        // whether to stop for as long as it runs.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, cancellationToken);
        var token = linked.Token;

        return await Task.Run(() => Refresh(repositoryRoot, token), token).ConfigureAwait(false);
    }

    /// <summary>The check in flight or last run for a repository, for a caller
    /// that wants to wait on the background work it triggered.</summary>
    public Task Pending(string repositoryRoot) =>
        DevbookDatabaseLocation.NormalizedRoot(repositoryRoot) is { } key
        && _entries.TryGetValue(key, out var entry)
        && entry.Running is { } running
            ? running
            : Task.CompletedTask;

    private void RefreshCore(string repositoryRoot, CancellationToken cancellationToken)
    {
        try
        {
            Refresh(repositoryRoot, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping. The old database, if any, stays.
        }
        catch (Exception exception)
        {
            // A failed build costs speed, never correctness: the readers stay on
            // the Markdown path and the next check tries again.
            _logger.LogWarning(exception, "Building the devbook database for {RepositoryRoot} failed; the Devbook reads Markdown until the next check.", repositoryRoot);

            // A watched folder has no next ask to wait for — its changes are the
            // asks — so a build that failed, say on a file its sync client was
            // still writing, is tried again after the quiet interval.
            if (DevbookDatabaseLocation.NormalizedRoot(repositoryRoot) is { } key && _watched.ContainsKey(key)) NotifyChanged(repositoryRoot);
        }
    }

    private bool Refresh(string repositoryRoot, CancellationToken cancellationToken)
    {
        if (_devbookCacheDirectory() is not { Length: > 0 } cache) return false;
        if (DevbookDatabaseLocation.PathFor(Path.Combine(cache, DevbookDatabaseLocation.DatabasesFolderName), repositoryRoot) is not { } target) return false;
        var kind = DevbookDatabaseLocation.KindFor(repositoryRoot);
        if (!Directory.Exists(repositoryRoot) || !DevbookDatabaseBuilder.HasDevbook(repositoryRoot, kind)) return false;
        if (DevbookDatabaseBuilder.IsCurrent(repositoryRoot, target, kind)) return false;

        var started = _time.GetTimestamp();
        if (!DevbookDatabaseBuilder.Build(repositoryRoot, target, kind, cancellationToken)) return false;

        _logger.LogInformation(
            "Built the devbook database for {RepositoryRoot} at {DatabasePath} in {ElapsedMilliseconds} ms.",
            repositoryRoot,
            target,
            (long)_time.GetElapsedTime(started).TotalMilliseconds);

        Rebuilt?.Invoke(repositoryRoot);
        return true;
    }

    /// <summary>Cancels any build in flight. The token source is deliberately not
    /// disposed: a background check still holding its token must be able to ask
    /// it whether to stop.</summary>
    public void Dispose()
    {
        _stopping.Cancel();

        foreach (var entry in _entries.Values)
        {
            lock (entry)
            {
                entry.Deferred?.Dispose();
                entry.Deferred = null;
            }
        }
    }

    private sealed class Entry
    {
        public Task? Running { get; set; }

        public DateTimeOffset? LastChecked { get; set; }

        /// <summary>The one check a change inside the quiet interval scheduled
        /// for its end, or null.</summary>
        public ITimer? Deferred { get; set; }
    }

    private sealed class Watch(IDisposable? watcher, Action stopped) : IDisposable
    {
        public void Dispose()
        {
            watcher?.Dispose();
            stopped();
        }
    }
}
