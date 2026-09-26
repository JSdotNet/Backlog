using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Backs the backlog up to its GitHub repository on the schedule the workspace
/// settings hold, and on demand.
/// <para>
/// A backup is the database and only the database: one consistent copy of
/// <c>backlog.db</c> taken through SQLite's backup API, so writes still sitting
/// in the write-ahead log come along, committed to
/// <see cref="RepositoryPath"/> on the repository's default branch. Every
/// backup replaces the last one at that path and the repository's history holds
/// the rest, which is what a repository is for; a database that has not changed
/// since the last backup makes no commit at all. Nothing beside the database is
/// taken — not the inbox folder, not the settings, not any cache — because the
/// database is what local ADR 0003 makes the whole of the backlog, and a
/// restore is one file put back. Local ADR 0010 is the decision.
/// </para>
/// <para>
/// One-way, and deliberately so. Nothing here ever reads the repository back
/// into the folder: a backup that came home on its own would be file sync by
/// another name, and R9 in <c>.devbook/arc42/11-risks-and-technical-debt.md</c> is what
/// that did to this database. Getting a backup back is a person's decision,
/// made by hand.
/// </para>
/// <para>
/// Shaped on <c>TaskSyncWorker</c>: a singleton with a timer of its own rather
/// than a hosted service, because MAUI has no generic host to start one, and
/// driven through a <see cref="TimeProvider"/> so a test can move the clock
/// rather than wait for it. Unlike that worker the timer is a one-shot re-armed
/// after every run, because a schedule is a time of day rather than a period.
/// </para>
/// </summary>
public sealed class BackupWorker : IDisposable
{
    /// <summary>Where the backup lands in the repository. A folder of its own
    /// rather than the root, so a repository somebody also keeps notes in gets
    /// one folder from this app and not a database file beside their README.</summary>
    public const string RepositoryPath = "backlog/backlog.db";

    /// <summary>
    /// How long after arming a backup the schedule already owes is taken.
    /// <para>
    /// A slot that went by while the app was closed is not skipped until the
    /// next one — it is taken shortly after start. Shortly rather than at once,
    /// because the first thing a start does is open the database, and a backup
    /// that raced it would copy a file the app was still settling into; and
    /// because a person who just changed the schedule to a time that has passed
    /// today deserves a moment to change it again before it runs.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan CatchUpDelay = TimeSpan.FromSeconds(20);

    /// <summary>A margin on every timer, so a tick that lands a few milliseconds
    /// before its slot — which timers do — still finds the slot behind it when
    /// it re-arms, rather than in front of it and due again at once.</summary>
    private static readonly TimeSpan TimerMargin = TimeSpan.FromSeconds(1);

    /// <summary>What a person is told when a run failed in a way nobody planned
    /// for. The exception's own message goes to the log, for the reason
    /// <c>TaskSyncWorker.UnexpectedFailure</c> gives.</summary>
    public const string UnexpectedFailure = "The backup could not be made. Try again in a moment.";

    /// <summary>Guards <see cref="_timer"/> and <see cref="_disposed"/> together,
    /// so a settings change on one thread cannot arm a timer after the screen
    /// disposed the worker on another.</summary>
    private readonly Lock _gate = new();

    private readonly WorkspaceSettingsStore _settings;
    private readonly IGitHubClient _gitHub;
    private readonly IBackupStateStore _state;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    private readonly CancellationTokenSource _lifetime = new();

    private ITimer? _timer;
    private bool _disposed;

    /// <summary>Zero or one; also what <see cref="IsRunning"/> reports.</summary>
    private int _runInFlight;

    public BackupWorker(
        WorkspaceSettingsStore settings,
        IGitHubClient gitHub,
        IBackupStateStore state,
        TimeProvider time,
        ILogger<BackupWorker>? log = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(gitHub);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(time);

        _settings = settings;
        _gitHub = gitHub;
        _state = state;
        _time = time;
        _log = log ?? NullLogger<BackupWorker>.Instance;

        // The repository and the schedule can both move while the app is
        // running, and each moving is somebody watching to see whether it took.
        _settings.BackupChanged += OnScheduleChanged;

        Arm();
    }

    /// <summary>Raised at both ends of a run, whether it succeeded or failed,
    /// and whenever the next due time moves, so a screen showing
    /// <see cref="Last"/> or <see cref="NextDue"/> can redraw. Arrives on a
    /// thread-pool thread: a renderer subscribing to it has to marshal.</summary>
    public event Action? Changed;

    /// <summary>What the last attempt did.</summary>
    public BackupState Last => _state.Current;

    /// <summary>When the next scheduled backup is due, in local time, or null
    /// while nothing is scheduled or nothing is configured to back up to.</summary>
    public DateTimeOffset? NextDue { get; private set; }

    /// <summary>True while a run is in flight, for a button to show busy.</summary>
    public bool IsRunning => Volatile.Read(ref _runInFlight) == 1;

    /// <summary>Where the last result is kept, for a settings screen to show.</summary>
    public string StorePath => _state.StorePath;

    /// <summary>Whether there is a repository to back up to. The button is
    /// offered only when there is; the schedule waits for one.</summary>
    public bool IsConfigured => _settings.RootRepository is not null;

    /// <summary>
    /// Runs a backup now, and returns before it has done anything.
    /// <para>
    /// What a button calls, so it never blocks and never throws: copying the
    /// database is long enough to be felt on the thread that draws the window.
    /// What happened arrives through <see cref="Changed"/>, the same way a
    /// scheduled run's does. A request that lands while a run is in flight does
    /// nothing; pressing the button twice is one backup.
    /// </para>
    /// </summary>
    public void RequestBackup() => _ = RequestBackupAsync();

    /// <summary>What <see cref="RequestBackup"/> discards. Internal so a test
    /// can wait for the dispatch the button deliberately does not.</summary>
    internal Task RequestBackupAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
        }

        return Task.Run(RunGuardedAsync, CancellationToken.None);
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

        _settings.BackupChanged -= OnScheduleChanged;

        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private void OnScheduleChanged()
    {
        Arm();
        Changed?.Invoke();
    }

    /// <summary>
    /// Brings the timer into line with the settings: one shot at the next slot,
    /// or at <see cref="CatchUpDelay"/> from now when a slot has gone by since
    /// the last attempt, or nothing while there is no repository or no
    /// schedule. Re-armed after every run and after every settings change, so
    /// a schedule moved to an earlier time today fires today.
    /// </summary>
    private void Arm()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _timer?.Dispose();
            _timer = null;
            NextDue = null;

            var schedule = _settings.BackupSchedule;
            if (_settings.RootRepository is null || !schedule.IsScheduled) return;

            // Owed when a slot went by after the last attempt. A machine that
            // has never attempted owes nothing: every slot there has ever been
            // is behind it, and taking one twenty seconds after the schedule is
            // first set would be a backup nobody asked for — the button is what
            // asks for one now.
            var now = _time.GetLocalNow();
            var lastSlot = schedule.LastSlotAtOrBefore(now);
            var owed = lastSlot is { } slot && _state.Current.AttemptedAt is { } attempted && attempted < slot;

            var due = owed ? now + CatchUpDelay : schedule.NextSlotAfter(now);
            if (due is not { } at) return;

            NextDue = at;

            // TimeProvider.CreateTimer rather than System.Threading.Timer, for
            // the reason TaskSyncWorker gives: a real timer is driven by the
            // machine's clock and nothing in a test can move it.
            _timer = _time.CreateTimer(
                _ => OnDue(),
                state: null,
                at - now + TimerMargin,
                Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>A tick, which must not throw: it runs on a thread-pool thread
    /// with nobody to hand a failure to. The run itself catches everything it
    /// can name; this is for the rest.</summary>
    private async void OnDue()
    {
        try
        {
            await RunGuardedAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A scheduled backup threw outside its run.");
        }
        finally
        {
            Arm();
        }
    }

    private async Task RunGuardedAsync()
    {
        var repository = _settings.RootRepository;
        if (repository is null) return;

        if (Interlocked.CompareExchange(ref _runInFlight, 1, 0) != 0) return;

        // After the guard, never before it: a raise from a run that turned out
        // not to be starting would put a screen into a busy state nothing was
        // going to bring it out of.
        Changed?.Invoke();

        var previous = _state.Current;
        var now = _time.GetUtcNow();

        try
        {
            var bytes = CopyDatabase();
            if (bytes is null)
            {
                _state.Save(previous with { AttemptedAt = now, Error = "There is no backlog database to back up yet." });
                return;
            }

            var message = $"Back up the backlog from {Environment.MachineName} ({_time.GetLocalNow():yyyy-MM-dd HH:mm})";
            var committed = await _gitHub.CommitFileAsync(repository, RepositoryPath, bytes, message, _lifetime.Token)
                .ConfigureAwait(false);

            _state.Save(new BackupState(now, now, committed.Committed, Error: null));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed mid-upload. Nothing failed, and there is nobody
            // left to tell about it.
        }
        catch (Exception ex) when (ex is GitHubException or GitHubNotConfiguredException or IOException
                                   or UnauthorizedAccessException or SqliteException)
        {
            // Every failure the run planned for answers with its own sentence:
            // GitHub's words for a refused token or a missing repository, the
            // disk's for a folder that would not take the copy. Logged at
            // information, for the reason TaskSyncWorker gives: a laptop with
            // no network is the ordinary failure, and an error per slot for it
            // would drown the log.
            _log.LogInformation("A backup did not complete: {Message}", ex.Message);
            _state.Save(previous with { AttemptedAt = now, Error = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "A backup threw.");
            _state.Save(previous with { AttemptedAt = now, Error = UnexpectedFailure });
        }
        finally
        {
            Volatile.Write(ref _runInFlight, 0);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// One consistent copy of the database, as bytes, or null when there is no
    /// database yet.
    /// <para>
    /// Through the backup API into a temporary file and read back, rather than
    /// reading <c>backlog.db</c> itself: in WAL mode the file on disk is behind
    /// whatever the log still holds, and a raw read of it is a database missing
    /// its newest writes — the same reason <see cref="WorkspaceSettingsStore.TryMoveRoot"/>
    /// copies the way it does. The temporary file is deleted whether or not the
    /// read succeeded; a copy that could not be removed is left for the
    /// operating system to clean up and reported by nothing, because it holds
    /// nothing the backlog does not.
    /// </para>
    /// </summary>
    private byte[]? CopyDatabase()
    {
        var source = _settings.DatabasePath;
        if (!File.Exists(source)) return null;

        var copy = Path.Combine(Path.GetTempPath(), $"backlog-backup-{Guid.NewGuid():N}.db");

        try
        {
            SqliteDatabaseFile.CopyTo(source, copy);
            return File.ReadAllBytes(copy);
        }
        finally
        {
            try
            {
                File.Delete(copy);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // See the summary.
            }
        }
    }
}
