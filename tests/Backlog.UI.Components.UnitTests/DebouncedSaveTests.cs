using Microsoft.Extensions.Time.Testing;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The debounce runs on a <see cref="FakeTimeProvider"/> the tests move by hand,
/// so each one says which side of the delay it is on instead of sleeping past it.
/// </summary>
public sealed class DebouncedSaveTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public void Idle_until_something_is_touched()
    {
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(10), () => Task.CompletedTask, _clock);

        Assert.Equal(SaveState.Idle, save.State);
    }

    [Fact]
    public async Task A_touch_says_saving_at_once_and_saved_once_the_delay_has_run()
    {
        var saves = 0;
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(750), () => { saves++; return Task.CompletedTask; }, _clock);

        save.Touch();

        Assert.Equal(SaveState.Saving, save.State);
        Assert.Equal(0, saves);

        _clock.Advance(TimeSpan.FromMilliseconds(749));

        Assert.Equal(SaveState.Saving, save.State);
        Assert.Equal(0, saves);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await WaitForAsync(save, SaveState.Saved);

        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task Touching_again_inside_the_delay_restarts_it_and_saves_once()
    {
        // Two keystrokes 100ms apart with a 200ms debounce: the save lands once,
        // and no earlier than 200ms after the *second* touch. A first timer that
        // survived the second touch would land at 200ms after the first.
        var saves = 0;
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(200), () => { saves++; return Task.CompletedTask; }, _clock);

        save.Touch();
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        save.Touch();

        _clock.Advance(TimeSpan.FromMilliseconds(199));

        Assert.Equal(0, saves);
        Assert.Equal(SaveState.Saving, save.State);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await WaitForAsync(save, SaveState.Saved);

        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task Saving_now_skips_the_delay_and_drops_whatever_was_pending()
    {
        var saves = 0;
        using var save = new DebouncedSave(TimeSpan.FromSeconds(30), () => { saves++; return Task.CompletedTask; }, _clock);

        save.Touch();
        await save.SaveNowAsync();

        Assert.Equal(SaveState.Saved, save.State);
        Assert.Equal(1, saves);

        // The debounced save that was pending must not land on top of it.
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, saves);
    }

    /// <summary>
    /// A save now that arrives while the debounced one is still writing waits for
    /// it rather than writing beside it. Two writes to one document at once land
    /// in whatever order the store finishes them, and the loser may be the newer
    /// text.
    /// </summary>
    [Fact]
    public async Task Saving_now_while_a_debounced_save_is_writing_waits_for_it()
    {
        var saves = 0;
        var writing = 0;
        var mostAtOnce = 0;
        var debouncedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDebounced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(750), async () =>
        {
            var atOnce = Interlocked.Increment(ref writing);
            InterlockedMax(ref mostAtOnce, atOnce);
            try
            {
                if (Interlocked.Increment(ref saves) == 1)
                {
                    debouncedStarted.TrySetResult();
                    await releaseDebounced.Task;
                }
            }
            finally
            {
                Interlocked.Decrement(ref writing);
            }
        }, _clock);

        save.Touch();
        _clock.Advance(TimeSpan.FromMilliseconds(750));
        await debouncedStarted.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        var now = save.SaveNowAsync();

        Assert.False(now.IsCompleted);
        Assert.Equal(1, Volatile.Read(ref saves));

        releaseDebounced.SetResult();
        await now.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, saves);
        Assert.Equal(1, mostAtOnce);
        Assert.Equal(SaveState.Saved, save.State);
    }

    /// <summary>
    /// The other order: a debounce that comes due while a save now is still
    /// writing queues behind it rather than writing beside it.
    /// </summary>
    [Fact]
    public async Task A_debounce_due_while_a_save_now_is_writing_waits_for_it()
    {
        var writes = new HeldWrites();
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(750), writes.Save, _clock);

        var now = save.SaveNowAsync();
        await writes.Started(1);

        save.Touch();
        _clock.Advance(TimeSpan.FromMilliseconds(750));

        Assert.Equal(1, writes.Calls);

        writes.Release(1);
        await now.WaitAsync(Patience, TestContext.Current.CancellationToken);
        await writes.Started(2);
        writes.Release(2);
        await WaitForAsync(save, SaveState.Saved);

        Assert.Equal(2, writes.Calls);
        Assert.Equal(1, writes.MostAtOnce);
    }

    /// <summary>
    /// Two saves now in a row — a checkbox and then a pick from a list — write
    /// one after the other, never together.
    /// </summary>
    [Fact]
    public async Task Two_saves_now_in_a_row_never_write_at_once()
    {
        var writes = new HeldWrites();
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(750), writes.Save, _clock);

        var first = save.SaveNowAsync();
        await writes.Started(1);
        var second = save.SaveNowAsync();

        Assert.False(second.IsCompleted);
        Assert.Equal(1, writes.Calls);

        writes.Release(1);
        await first.WaitAsync(Patience, TestContext.Current.CancellationToken);
        await writes.Started(2);
        writes.Release(2);
        await second.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, writes.Calls);
        Assert.Equal(1, writes.MostAtOnce);
        Assert.Equal(SaveState.Saved, save.State);
    }

    /// <summary>
    /// A save now asked for from inside a save that has only just begun — the
    /// moment before its task is handed back — still sees that save as the one
    /// to wait for. Published only after the write began, it would not be.
    /// </summary>
    [Fact]
    public async Task A_save_now_asked_for_as_a_debounced_save_begins_waits_for_it()
    {
        var writes = new HeldWrites();
        DebouncedSave? save = null;
        Task? now = null;
        var asked = false;
        using var owner = new DebouncedSave(TimeSpan.FromMilliseconds(750), () =>
        {
            if (!asked)
            {
                asked = true;
                now = save!.SaveNowAsync();
            }

            return writes.Save();
        }, _clock);
        save = owner;

        owner.Touch();
        _clock.Advance(TimeSpan.FromMilliseconds(750));
        await writes.Started(1);

        Assert.NotNull(now);
        Assert.False(now.IsCompleted);
        Assert.Equal(1, writes.Calls);

        writes.Release(1);
        await writes.Started(2);
        writes.Release(2);
        await now.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(2, writes.Calls);
        Assert.Equal(1, writes.MostAtOnce);
    }

    /// <summary>
    /// Dropping the last edit is the failure serialising must not buy: a touch
    /// that arrives while a save is writing is written by a save of its own
    /// once that one is done.
    /// </summary>
    [Fact]
    public async Task A_touch_while_a_save_now_is_writing_is_still_written()
    {
        var writes = new HeldWrites();
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(750), writes.Save, _clock);

        var now = save.SaveNowAsync();
        await writes.Started(1);
        save.Touch();

        writes.Release(1);
        await now.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(SaveState.Saving, save.State);

        _clock.Advance(TimeSpan.FromMilliseconds(750));
        await writes.Started(2);
        writes.Release(2);
        await WaitForAsync(save, SaveState.Saved);

        Assert.Equal(2, writes.Calls);
    }

    [Fact]
    public async Task Every_transition_is_reported_and_none_is_reported_twice()
    {
        var seen = new List<SaveState>();
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(10), () => Task.CompletedTask, _clock);
        save.StateChanged += () => seen.Add(save.State);

        save.Touch();
        _clock.Advance(TimeSpan.FromMilliseconds(10));
        await WaitForAsync(save, SaveState.Saved);

        Assert.Equal([SaveState.Saving, SaveState.Saved], seen);
    }

    [Fact]
    public async Task A_save_that_throws_is_reported_as_failed()
    {
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(10), () => throw new InvalidOperationException("disk full"), _clock);

        save.Touch();
        _clock.Advance(TimeSpan.FromMilliseconds(10));
        await WaitForAsync(save, SaveState.Failed);

        // The immediate form has a caller to tell, so it tells them too.
        await Assert.ThrowsAsync<InvalidOperationException>(save.SaveNowAsync);
        Assert.Equal(SaveState.Failed, save.State);
    }

    [Fact]
    public void Disposing_cancels_a_pending_save()
    {
        var saves = 0;
        var save = new DebouncedSave(TimeSpan.FromMilliseconds(30), () => { saves++; return Task.CompletedTask; }, _clock);

        save.Touch();
        save.Dispose();
        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(0, saves);
        Assert.Throws<ObjectDisposedException>(save.Touch);
    }

    [Fact]
    public void The_delay_cannot_be_negative_and_the_save_cannot_be_missing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DebouncedSave(TimeSpan.FromMilliseconds(-1), () => Task.CompletedTask));
        Assert.Throws<ArgumentNullException>(() => new DebouncedSave(TimeSpan.Zero, (Func<Task>)null!));
    }

    /// <summary>
    /// A save that holds every write open until the test lets it go, and counts
    /// how many were open at once. Write <c>n</c> is the <c>n</c>th call, from 1.
    /// </summary>
    private sealed class HeldWrites
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<int, TaskCompletionSource> _started = [];
        private readonly Dictionary<int, TaskCompletionSource> _released = [];
        private int _calls;
        private int _writing;
        private int _mostAtOnce;

        public int Calls => Volatile.Read(ref _calls);

        public int MostAtOnce => Volatile.Read(ref _mostAtOnce);

        public async Task Save()
        {
            var call = Interlocked.Increment(ref _calls);
            InterlockedMax(ref _mostAtOnce, Interlocked.Increment(ref _writing));
            try
            {
                Signal(_started, call).TrySetResult();
                await Signal(_released, call).Task;
            }
            finally
            {
                Interlocked.Decrement(ref _writing);
            }
        }

        public Task Started(int call) =>
            Signal(_started, call).Task.WaitAsync(Patience, TestContext.Current.CancellationToken);

        public void Release(int call) => Signal(_released, call).TrySetResult();

        private TaskCompletionSource Signal(Dictionary<int, TaskCompletionSource> signals, int call)
        {
            lock (_gate)
            {
                if (!signals.TryGetValue(call, out var signal))
                {
                    signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    signals[call] = signal;
                }

                return signal;
            }
        }
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref location);
            if (value <= seen) return;
        }
        while (Interlocked.CompareExchange(ref location, value, seen) != seen);
    }

    /// <summary>Waits on <see cref="DebouncedSave.StateChanged"/> rather than
    /// sleeping, so a moved clock's save is awaited for exactly as long as it
    /// takes, and a test fails on a timeout rather than a race.</summary>
    private static async Task WaitForAsync(DebouncedSave save, SaveState wanted)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged()
        {
            if (save.State == wanted) reached.TrySetResult();
        }

        save.StateChanged += OnChanged;
        try
        {
            if (save.State == wanted) return;
            await reached.Task.WaitAsync(Patience);
        }
        finally
        {
            save.StateChanged -= OnChanged;
        }
    }
}
