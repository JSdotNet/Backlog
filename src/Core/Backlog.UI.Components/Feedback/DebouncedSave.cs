namespace Backlog.UI.Components.Feedback;

/// <summary>
/// The state behind a <see cref="SaveIndicator"/> on a surface that auto-saves:
/// <see cref="SaveState.Saving"/> while edits are still arriving, and
/// <see cref="SaveState.Saved"/> once they have stopped for long enough to be
/// written.
/// </summary>
/// <remarks>
/// <para>
/// One instance per thing being saved. <see cref="Touch"/> is what a keystroke
/// calls: it restarts the delay, and the save runs once the delay has passed with
/// no further touch. <see cref="SaveNowAsync"/> is what a discrete change calls —
/// a checkbox, a pick from a list — because there is nothing to wait for.
/// </para>
/// <para>
/// A host binds <see cref="State"/> to the indicator and re-renders on
/// <see cref="StateChanged"/>; in a component that is
/// <c>_save.StateChanged += () => InvokeAsync(StateHasChanged)</c>. Not
/// thread-safe: it is meant to be driven from one renderer's synchronisation
/// context, which is where every event handler already runs.
/// </para>
/// <para>
/// A save that throws leaves the state at <see cref="SaveState.Failed"/>. The
/// debounced path has nobody awaiting it, so the state is the only place that
/// failure can be reported; the immediate path reports it there too and then
/// rethrows to its caller.
/// </para>
/// </remarks>
public sealed class DebouncedSave : IDisposable
{
    private readonly TimeSpan _delay;
    private readonly Func<Task> _save;
    private readonly TimeProvider _timeProvider;
    private CancellationTokenSource? _pending;

    /// <summary>The save that was started last, debounced or immediate, which
    /// may still be running. Every save waits for it before it writes, because two
    /// writes to one document at once land in whatever order they finish, and the
    /// loser may be the newer text. Each save is published here before it is
    /// started, so a save asked for while another is only just beginning still
    /// finds it.</summary>
    private Task _lastSave = Task.CompletedTask;

    private bool _disposed;

    /// <param name="delay">How long the edits have to stop for before the save runs.</param>
    /// <param name="save">The save itself. Runs on whichever context touched last.</param>
    /// <param name="timeProvider">What the delay is measured on; the system clock
    /// unless a test wants to move time by hand.</param>
    public DebouncedSave(TimeSpan delay, Func<Task> save, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "A debounce cannot wait a negative time.");
        }

        _delay = delay;
        _save = save;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The same, for a save with nothing to await.</summary>
    public DebouncedSave(TimeSpan delay, Action save, TimeProvider? timeProvider = null)
        : this(delay, Wrap(save), timeProvider)
    {
    }

    public SaveState State { get; private set; } = SaveState.Idle;

    /// <summary>Raised once per transition, after <see cref="State"/> has moved.
    /// Never raised for a save that ends where it started.</summary>
    public event Action? StateChanged;

    /// <summary>An edit arrived. Saving from now, and the save runs once the delay
    /// has passed without another touch.</summary>
    public void Touch()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var token = Restart();
        Set(SaveState.Saving);
        _ = RunAfterDelayAsync(token);
    }

    /// <summary>Saves without waiting for the delay, and drops any debounced save
    /// still pending — the change it was waiting to write is written by this one.
    /// A save that has already started writing, of either kind, is let finish
    /// first, so no two ever write at once.</summary>
    public Task SaveNowAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return StartSave(Restart(), propagate: true);
    }

    /// <summary>Cancels a save still waiting on its delay, or queued behind one
    /// that is writing. One already writing is not stopped — its write is in the
    /// store's hands — but it no longer reports. Nothing here waits on a save, so
    /// disposing from inside one cannot deadlock.</summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
    }

    private static Func<Task> Wrap(Action save)
    {
        ArgumentNullException.ThrowIfNull(save);

        return () =>
        {
            save();
            return Task.CompletedTask;
        };
    }

    private CancellationToken Restart()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = new CancellationTokenSource();

        return _pending.Token;
    }

    private async Task RunAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_delay, _timeProvider, token);
        }
        catch (OperationCanceledException)
        {
            // A newer touch, an immediate save or disposal took this one's place.
            // That is what a debounce is for, not a failure to report.
            return;
        }

        await StartSave(token, propagate: false);
    }

    /// <summary>
    /// Queues a save behind the one started last and publishes it as the new
    /// last, before any of it runs. Built unstarted and then run inline, so the
    /// write begins on the caller's context exactly as a direct call would — but
    /// only once <see cref="_lastSave"/> already names it.
    /// </summary>
    private Task StartSave(CancellationToken token, bool propagate)
    {
        var previous = _lastSave;
        var start = new Task<Task>(() => SaveAfterAsync(previous, token, propagate));
        var saving = start.Unwrap();
        _lastSave = saving;
        start.RunSynchronously(TaskScheduler.Default);
        return saving;
    }

    private async Task SaveAfterAsync(Task previous, CancellationToken token, bool propagate)
    {
        // Its failure is its own caller's, or its own state's, to report; the
        // save behind it only needs it to be over.
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);

        // A debounced save that was superseded while it queued — by a touch, a
        // save now, or disposal — has nothing to write: whatever replaced it
        // writes the same document, later. An immediate save has a caller
        // awaiting it, so it always writes.
        if (!propagate && token.IsCancellationRequested) return;

        await SaveAsync(token, propagate);
    }

    private async Task SaveAsync(CancellationToken token, bool propagate)
    {
        Set(SaveState.Saving);

        try
        {
            await _save();
        }
        catch (Exception) when (!propagate)
        {
            // Nothing awaits a debounced save, so the state is its only channel.
            if (!token.IsCancellationRequested) Set(SaveState.Failed);
            return;
        }
        catch (Exception)
        {
            if (!token.IsCancellationRequested) Set(SaveState.Failed);
            throw;
        }

        // A save superseded while it ran must not report on top of its successor.
        if (!token.IsCancellationRequested) Set(SaveState.Saved);
    }

    private void Set(SaveState state)
    {
        if (_disposed || State == state) return;

        State = state;
        StateChanged?.Invoke();
    }
}
