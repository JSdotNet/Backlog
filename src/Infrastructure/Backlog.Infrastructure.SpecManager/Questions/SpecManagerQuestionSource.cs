using System.Threading.Channels;

using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.SpecManager.Questions;

/// <summary>
/// What brings spec-manager's questions into Backlog on the desktop, while it is signed in
/// to an installation and a spec-manager product is connected: it holds the installation's
/// notification stream and pulls the questions (<see cref="SpecManagerQuestionSync"/>), and
/// after them the person's other notifications as alerts
/// (<see cref="SpecManagerNotificationSync"/>), whenever there may be news. Each of the two
/// pulls fails on its own: one that throws is logged and the other still runs.
/// <list type="bullet">
/// <item>A pull on every <c>changed</c> event, a whole pull each time the stream (re)opens,
/// and one every <see cref="BackupInterval"/> while it is open, in case an event was
/// lost.</item>
/// <item>While the stream cannot be opened, or has dropped, a pull every
/// <see cref="FallbackInterval"/>.</item>
/// <item>The stream is opened again after <see cref="FirstRetryDelay"/>, the wait doubling
/// up to <see cref="MaxRetryDelay"/> while it keeps failing, and back to the first once a
/// stream stayed open for <see cref="StableConnection"/> — one a proxy closes as soon as it
/// opens counts as failing, so it is not reopened, and pulled whole, every five seconds.</item>
/// <item>Changing a product's "Only questions assigned to me" pulls whole at once, so the
/// questions it now leaves out close and the ones it now lets in arrive.</item>
/// </list>
/// <para>
/// After a pull, while an answer waits on a sign-in (<see cref="SpecManagerSignIn.Notice"/>),
/// the waiting answers are handed over again (<see cref="INotificationAnswerReplay"/>), so
/// an answer given while signed out reaches spec-manager once the person signs back in.
/// </para>
/// <para>
/// It owns the listener that marks a notification read in spec-manager once its alert is
/// acknowledged here (<see cref="SpecManagerNotificationReads"/>): started with it, stopped
/// when it is disposed.
/// </para>
/// <para>
/// A plain singleton the heads resolve once, like the sync workers: the MAUI head has no
/// generic host to start a hosted service.
/// </para>
/// </summary>
public sealed class SpecManagerQuestionSource : IDisposable
{
    /// <summary>How often a pull runs while the stream is open, in case an event went
    /// missing.</summary>
    internal static readonly TimeSpan BackupInterval = TimeSpan.FromMinutes(5);

    /// <summary>How often a pull runs while the stream is not open.</summary>
    internal static readonly TimeSpan FallbackInterval = TimeSpan.FromMinutes(2);

    /// <summary>The first wait before the stream is opened again.</summary>
    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>The longest wait before the stream is opened again.</summary>
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(2);

    /// <summary>How long a stream has to stay open to count as having worked.</summary>
    internal static readonly TimeSpan StableConnection = TimeSpan.FromSeconds(30);

    /// <summary>How recent a whole pull may be for the stream opening not to ask for
    /// another: the first pull of a run and the stream's first opening come together.</summary>
    internal static readonly TimeSpan WholePullFreshFor = TimeSpan.FromMinutes(1);

    /// <summary>The event spec-manager sends when the person's notification centre
    /// changed — a question asked of them, answered, reassigned among them.</summary>
    internal const string ChangedEvent = "changed";

    private readonly Lock _gate = new();
    private readonly SpecManagerQuestionSync _sync;
    private readonly SpecManagerNotificationSync? _notifications;
    private readonly SpecManagerNotificationReads? _reads;
    private readonly ISpecManagerEventStreamConnector _connector;
    private readonly SpecManagerSignIn _signIn;
    private readonly IConnectedTargets? _targets;
    private readonly INotificationAnswerReplay? _replay;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    private Channel<bool> _pulls = NewPulls();
    private string _settings = string.Empty;
    private long _lastWholeTicks = long.MinValue;
    private CancellationTokenSource? _running;
    private Task _loops = Task.CompletedTask;
    private int _open;
    private int _whole;
    private bool _disposed;

    internal SpecManagerQuestionSource(
        SpecManagerQuestionSync sync,
        ISpecManagerEventStreamConnector connector,
        SpecManagerSignIn signIn,
        IConnectedTargets? targets,
        INotificationAnswerReplay? replay,
        TimeProvider time,
        ILogger<SpecManagerQuestionSource>? log = null,
        SpecManagerNotificationSync? notifications = null,
        SpecManagerNotificationReads? reads = null)
        : this(sync, connector, signIn, targets, replay, time, log, start: true, notifications, reads)
    {
    }

    /// <param name="start">False for a test that sets <see cref="Delay"/> first and then
    /// calls <see cref="Start"/>.</param>
    /// <param name="notifications">The pull of the other notifications, run after each
    /// question pull; none when null.</param>
    /// <param name="reads">The listener that marks an acknowledged alert's notification read,
    /// started and stopped with this source; none when null.</param>
    internal SpecManagerQuestionSource(
        SpecManagerQuestionSync sync,
        ISpecManagerEventStreamConnector connector,
        SpecManagerSignIn signIn,
        IConnectedTargets? targets,
        INotificationAnswerReplay? replay,
        TimeProvider time,
        ILogger? log,
        bool start,
        SpecManagerNotificationSync? notifications = null,
        SpecManagerNotificationReads? reads = null)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(time);

        _sync = sync;
        _notifications = notifications;
        _reads = reads;
        _connector = connector;
        _signIn = signIn;
        _targets = targets;
        _replay = replay;
        _time = time;
        _log = log ?? NullLogger.Instance;

        Delay = (delay, cancellationToken) => Task.Delay(delay, time, cancellationToken);

        if (start) Start();
    }

    /// <summary>Raised when the stream opens or closes, on a thread pool thread.</summary>
    public event Action? Changed;

    /// <summary>True while the notification stream is open.</summary>
    public bool IsOpen => Volatile.Read(ref _open) == 1;

    /// <summary>Every wait — between pulls and before opening the stream again — on the
    /// injected clock; replaced by a test that decides when each ends.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; }

    /// <summary>The running loops, so a test can wait for them to stop.</summary>
    internal Task Loops
    {
        get
        {
            lock (_gate) return _loops;
        }
    }

    /// <summary>Starts following the sign-in and the connected targets.</summary>
    internal void Start()
    {
        _reads?.Start();
        _signIn.AccountChanged += OnGateChanged;
        if (_targets is not null) _targets.Changed += OnGateChanged;

        ApplyGates();
    }

    /// <summary>Asks for a pull now — what a <c>changed</c> event, and the stream opening,
    /// ask for. Several asked before the pull starts are one pull.</summary>
    internal void RequestPull(bool whole = false)
    {
        if (whole) Interlocked.Exchange(ref _whole, 1);

        lock (_gate) _pulls.Writer.TryWrite(true);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            StopLocked();
        }

        _signIn.AccountChanged -= OnGateChanged;
        if (_targets is not null) _targets.Changed -= OnGateChanged;
        _reads?.Dispose();
    }

    private bool ShouldRun => _signIn.Account is not null && _sync.Products().Count > 0;

    private void OnGateChanged() => ApplyGates();

    private void ApplyGates()
    {
        lock (_gate)
        {
            if (_disposed) return;

            if (!ShouldRun)
            {
                StopLocked();
                return;
            }

            var settings = Settings();
            var settingsChanged = !string.Equals(settings, _settings, StringComparison.Ordinal);
            _settings = settings;

            if (_running is not null)
            {
                // A product added, or its "Only questions assigned to me" flipped: a
                // changed-since pull would not show the questions that now fall in or out.
                if (settingsChanged)
                {
                    Interlocked.Exchange(ref _whole, 1);
                    _pulls.Writer.TryWrite(true);
                }

                return;
            }

            _running = new CancellationTokenSource();
            _pulls = NewPulls();
            var token = _running.Token;

            // The first pull is whole, and it hands over any answer that waited on the sign-in.
            Interlocked.Exchange(ref _whole, 1);
            _pulls.Writer.TryWrite(true);

            _loops = Task.WhenAll(
                Task.Run(() => PullLoopAsync(token), CancellationToken.None),
                Task.Run(() => StreamLoopAsync(token), CancellationToken.None));
        }
    }

    private void StopLocked()
    {
        if (_running is null) return;

        _running.Cancel();
        _running.Dispose();
        _running = null;
        _pulls.Writer.TryComplete();

        // The stopped run's stream no longer counts, whatever its loop is still unwinding.
        if (Interlocked.Exchange(ref _open, 0) == 1) Changed?.Invoke();
    }

    /// <summary>The products and the one setting of theirs a pull reads, as one string, so a
    /// change to either is seen.</summary>
    private string Settings() =>
        string.Join('|', _sync.Products()
            .Select(target => target.Target.Trim().ToUpperInvariant() + (target.OnlyQuestionsAssignedToMe ? ":mine" : ":all"))
            .Order(StringComparer.Ordinal));

    private static Channel<bool> NewPulls() =>
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>Pulls when asked, or when the interval runs out: the backup's while the
    /// stream is open, the fallback's while it is not.</summary>
    private async Task PullLoopAsync(CancellationToken cancellationToken)
    {
        ChannelReader<bool> pulls;
        lock (_gate) pulls = _pulls.Reader;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await WaitForPullAsync(pulls, IsOpen ? BackupInterval : FallbackInterval, cancellationToken).ConfigureAwait(false);
                await PullOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ChannelClosedException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Nothing here may end the loop: the next pull is the recovery.
                _log.LogError(ex, "Pulling the spec-manager questions failed unexpectedly.");
            }
        }
    }

    private async Task WaitForPullAsync(ChannelReader<bool> pulls, TimeSpan interval, CancellationToken cancellationToken)
    {
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var asked = pulls.WaitToReadAsync(waiting.Token).AsTask();
        var due = Delay(interval, waiting.Token);

        await Task.WhenAny(asked, due).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (asked.IsCompletedSuccessfully && !asked.Result) throw new ChannelClosedException();

        await waiting.CancelAsync().ConfigureAwait(false);
        await Quietly(asked).ConfigureAwait(false);
        await Quietly(due).ConfigureAwait(false);

        while (pulls.TryRead(out _))
        {
        }
    }

    private async Task PullOnceAsync(CancellationToken cancellationToken)
    {
        var whole = Interlocked.Exchange(ref _whole, 0) == 1;
        var pulled = false;

        try
        {
            pulled = await _sync.PullAsync(whole, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Never the notifications' pull's business: it runs all the same.
            _log.LogError(ex, "Pulling the spec-manager questions failed unexpectedly.");
        }
        finally
        {
            // A whole pull that did not get through is still owed: only a whole list shows
            // what was deleted.
            if (whole && !pulled) Interlocked.Exchange(ref _whole, 1);
            if (whole && pulled) Interlocked.Exchange(ref _lastWholeTicks, _time.GetUtcNow().UtcTicks);
        }

        await PullNotificationsAsync(cancellationToken).ConfigureAwait(false);

        if (_signIn.Notice is not null && _signIn.Account is not null && _replay is not null)
        {
            await _replay.ReplayAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Pulls the other notifications; a failure is logged and never reaches the
    /// question pull or the loop.</summary>
    private async Task PullNotificationsAsync(CancellationToken cancellationToken)
    {
        if (_notifications is null) return;

        try
        {
            await _notifications.PullAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _log.LogError(ex, "Pulling the spec-manager notifications failed unexpectedly.");
        }
    }

    /// <summary>Holds the stream, asking for a pull on each event and each time it opens,
    /// and opens it again on the growing wait while it keeps failing.</summary>
    private async Task StreamLoopAsync(CancellationToken cancellationToken)
    {
        var retry = FirstRetryDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan wait;

            try
            {
                if (await ListenOnceAsync(cancellationToken).ConfigureAwait(false))
                {
                    wait = FirstRetryDelay;
                    retry = Next(FirstRetryDelay);
                }
                else
                {
                    wait = retry;
                    retry = Next(retry);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "The spec-manager notification stream failed unexpectedly.");
                wait = retry;
                retry = Next(retry);
            }

            try
            {
                await Delay(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal static TimeSpan Next(TimeSpan delay) =>
        delay * 2 > MaxRetryDelay ? MaxRetryDelay : delay * 2;

    /// <summary>Opens the stream once and reads it until it ends; answers whether it was
    /// open at all.</summary>
    private async Task<bool> ListenOnceAsync(CancellationToken cancellationToken)
    {
        ISpecManagerEventStream stream;

        try
        {
            stream = await _connector.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _log.LogInformation(ex, "The spec-manager notification stream did not open; questions are pulled every two minutes meanwhile.");
            return false;
        }

        var openedAt = _time.GetTimestamp();

        await using (stream.ConfigureAwait(false))
        {
            SetOpen(true);
            if (!WholePullIsFresh()) RequestPull(whole: true);

            try
            {
                while (await stream.ReadEventAsync(cancellationToken).ConfigureAwait(false) is { } name)
                {
                    if (string.Equals(name, ChangedEvent, StringComparison.Ordinal)) RequestPull();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _log.LogInformation(ex, "The spec-manager notification stream dropped; opening it again, and pulling every two minutes meanwhile.");
            }
            finally
            {
                // A stopped run leaves the flag to the run after it.
                if (!cancellationToken.IsCancellationRequested)
                {
                    SetOpen(false);
                    RequestPull();
                }
            }
        }

        return _time.GetElapsedTime(openedAt) >= StableConnection;
    }

    private bool WholePullIsFresh()
    {
        var last = Interlocked.Read(ref _lastWholeTicks);
        return last != long.MinValue && _time.GetUtcNow().UtcTicks - last < WholePullFreshFor.Ticks;
    }

    private void SetOpen(bool open)
    {
        Volatile.Write(ref _open, open ? 1 : 0);
        Changed?.Invoke();
    }

    private static async Task Quietly(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
