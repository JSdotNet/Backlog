using Backlog.Infrastructure.Sqlite.Changes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps the change journal in <c>backlog.db</c> bounded (local ADR 0061 §6): once a
/// day it deletes the revisions and change sets older than
/// <see cref="WorkspaceSettingsStore.ChangeHistoryDays"/>, and on the person's word
/// it deletes all of them.
/// <para>
/// Shaped on <see cref="BackupWorker"/>: a singleton with a timer of its own rather
/// than a hosted service, because MAUI has no generic host to start one, and driven
/// through a <see cref="TimeProvider"/> so a test can move the clock. The first prune
/// runs <see cref="StartDelay"/> after start, so it does not race the app opening
/// the database; a shorter period chosen in Settings prunes after the same delay.
/// </para>
/// </summary>
public sealed class ChangeHistoryWorker : IDisposable
{
    /// <summary>How long after start, or after the period changes, the next prune runs.</summary>
    internal static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);

    /// <summary>How often the journal is pruned.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>What a person is told when clearing failed in a way nobody planned for.</summary>
    public const string ClearFailure = "The change history could not be cleared. Try again in a moment.";

    private readonly Lock _gate = new();
    private readonly WorkspaceSettingsStore _settings;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _runs = new(1, 1);

    private ITimer? _timer;
    private bool _disposed;

    public ChangeHistoryWorker(
        WorkspaceSettingsStore settings,
        TimeProvider time,
        ILogger<ChangeHistoryWorker>? log = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(time);

        _settings = settings;
        _time = time;
        _log = log ?? NullLogger<ChangeHistoryWorker>.Instance;

        _settings.ChangeHistoryChanged += Arm;

        Arm();
    }

    /// <summary>Raised after a prune or a clear deleted something, so a screen showing
    /// the database's size can redraw. Arrives on a thread-pool thread.</summary>
    public event Action? Changed;

    /// <summary>Deletes what is older than the period now. Internal so a test can
    /// run the prune the timer runs and wait for it.</summary>
    internal async Task<ChangeHistoryPurge> PruneAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = _time.GetUtcNow() - TimeSpan.FromDays(_settings.ChangeHistoryDays);

        await _runs.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var purge = await SqliteChangeHistory
                .PruneAsync(_settings.DatabasePath, cutoff, cancellationToken).ConfigureAwait(false);
            if (purge != ChangeHistoryPurge.None)
            {
                _log.LogInformation(
                    "Pruned the change history older than {Days} days: {Revisions} revisions, {ChangeSets} change sets.",
                    _settings.ChangeHistoryDays, purge.Revisions, purge.ChangeSets);
                Changed?.Invoke();
            }

            return purge;
        }
        finally
        {
            _runs.Release();
        }
    }

    /// <summary>Deletes every revision and change set. Answers null when it worked,
    /// else the sentence to show; it cannot be undone.</summary>
    public async Task<string?> ClearAsync(CancellationToken cancellationToken = default)
    {
        await _runs.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var purge = await SqliteChangeHistory
                .ClearAsync(_settings.DatabasePath, cancellationToken).ConfigureAwait(false);
            _log.LogInformation(
                "Cleared the change history: {Revisions} revisions, {ChangeSets} change sets.",
                purge.Revisions, purge.ChangeSets);
            Changed?.Invoke();
            return null;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Clearing the change history failed.");
            return ClearFailure;
        }
        finally
        {
            _runs.Release();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }

        // _runs is left undisposed: a clear still in flight releases it on its way
        // out, and a disposed semaphore would throw there.
        _settings.ChangeHistoryChanged -= Arm;
    }

    /// <summary>One prune <see cref="StartDelay"/> from now, then one every
    /// <see cref="Interval"/>.</summary>
    private void Arm()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _timer?.Dispose();
            _timer = _time.CreateTimer(_ => OnDue(), state: null, StartDelay, Interval);
        }
    }

    /// <summary>A tick, which must not throw: it runs on a thread-pool thread with
    /// nobody to hand a failure to.</summary>
    private async void OnDue()
    {
        try
        {
            await PruneAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The app closed between the tick and the prune.
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Pruning the change history failed; the next tick tries again.");
        }
    }
}
