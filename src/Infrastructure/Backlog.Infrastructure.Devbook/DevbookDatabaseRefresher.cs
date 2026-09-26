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
/// </summary>
public sealed class DevbookDatabaseRefresher : IDisposable
{
    /// <summary>The default gap between two checks of one repository.</summary>
    public static readonly TimeSpan DefaultQuietInterval = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Func<string?> _devbookCacheDirectory;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    public DevbookDatabaseRefresher(
        Func<string?> devbookCacheDirectory,
        ILogger<DevbookDatabaseRefresher>? logger = null,
        TimeSpan? quietInterval = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(devbookCacheDirectory);

        _devbookCacheDirectory = devbookCacheDirectory;
        _logger = logger ?? NullLogger<DevbookDatabaseRefresher>.Instance;
        _time = time ?? TimeProvider.System;
        QuietInterval = quietInterval ?? DefaultQuietInterval;
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
    /// Checks <paramref name="repositoryRoot"/> now and rebuilds its database if
    /// it is not current, waiting for the answer. Returns whether a build ran.
    /// For a caller that must have the database — a test, a command.
    /// </summary>
    public Task<bool> RefreshAsync(string repositoryRoot, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, cancellationToken);
        return Task.FromResult(Refresh(repositoryRoot, linked.Token));
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
        }
    }

    private bool Refresh(string repositoryRoot, CancellationToken cancellationToken)
    {
        if (_devbookCacheDirectory() is not { Length: > 0 } cache) return false;
        if (DevbookDatabaseLocation.PathFor(Path.Combine(cache, DevbookDatabaseLocation.DatabasesFolderName), repositoryRoot) is not { } target) return false;
        if (!Directory.Exists(repositoryRoot) || !DevbookDatabaseBuilder.HasDevbook(repositoryRoot)) return false;
        if (DevbookDatabaseBuilder.IsCurrent(repositoryRoot, target)) return false;

        var started = _time.GetTimestamp();
        if (!DevbookDatabaseBuilder.Build(repositoryRoot, target, cancellationToken)) return false;

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
    public void Dispose() => _stopping.Cancel();

    private sealed class Entry
    {
        public Task? Running { get; set; }

        public DateTimeOffset? LastChecked { get; set; }
    }
}
