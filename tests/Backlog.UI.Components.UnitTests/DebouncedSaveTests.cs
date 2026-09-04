namespace Backlog.UI.Components.UnitTests;

public sealed class DebouncedSaveTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public void Idle_until_something_is_touched()
    {
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(10), () => Task.CompletedTask);

        Assert.Equal(SaveState.Idle, save.State);
    }

    [Fact]
    public async Task A_touch_says_saving_at_once_and_saved_once_the_delay_has_run()
    {
        var saves = 0;
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(20), () => { saves++; return Task.CompletedTask; });

        save.Touch();

        Assert.Equal(SaveState.Saving, save.State);
        Assert.Equal(0, saves);

        await WaitForAsync(save, SaveState.Saved);

        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task Touching_again_inside_the_delay_restarts_it_and_saves_once()
    {
        // Two keystrokes 100ms apart with a 200ms debounce: the save lands once,
        // and no earlier than 200ms after the *second* touch. A first timer that
        // survived the second touch would land at ~200ms after the first.
        var saves = 0;
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(200), () => { saves++; return Task.CompletedTask; });
        var clock = System.Diagnostics.Stopwatch.StartNew();

        save.Touch();
        await Task.Delay(100);
        save.Touch();

        await WaitForAsync(save, SaveState.Saved);

        Assert.Equal(1, saves);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(250), $"Saved after {clock.ElapsedMilliseconds}ms — the first timer was not cancelled.");
    }

    [Fact]
    public async Task Saving_now_skips_the_delay_and_drops_whatever_was_pending()
    {
        var saves = 0;
        using var save = new DebouncedSave(TimeSpan.FromSeconds(30), () => { saves++; return Task.CompletedTask; });

        save.Touch();
        await save.SaveNowAsync();

        Assert.Equal(SaveState.Saved, save.State);
        Assert.Equal(1, saves);

        // The debounced save that was in flight must not land on top of it.
        await Task.Delay(50);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task Every_transition_is_reported_and_none_is_reported_twice()
    {
        var seen = new List<SaveState>();
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(10), () => Task.CompletedTask);
        save.StateChanged += () => seen.Add(save.State);

        save.Touch();
        await WaitForAsync(save, SaveState.Saved);

        Assert.Equal([SaveState.Saving, SaveState.Saved], seen);
    }

    [Fact]
    public async Task A_save_that_throws_is_reported_as_failed()
    {
        using var save = new DebouncedSave(TimeSpan.FromMilliseconds(10), () => throw new InvalidOperationException("disk full"));

        save.Touch();
        await WaitForAsync(save, SaveState.Failed);

        // The immediate form has a caller to tell, so it tells them too.
        await Assert.ThrowsAsync<InvalidOperationException>(save.SaveNowAsync);
        Assert.Equal(SaveState.Failed, save.State);
    }

    [Fact]
    public async Task Disposing_cancels_a_pending_save()
    {
        var saves = 0;
        var save = new DebouncedSave(TimeSpan.FromMilliseconds(30), () => { saves++; return Task.CompletedTask; });

        save.Touch();
        save.Dispose();
        await Task.Delay(100);

        Assert.Equal(0, saves);
        Assert.Throws<ObjectDisposedException>(save.Touch);
    }

    [Fact]
    public void The_delay_cannot_be_negative_and_the_save_cannot_be_missing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DebouncedSave(TimeSpan.FromMilliseconds(-1), () => Task.CompletedTask));
        Assert.Throws<ArgumentNullException>(() => new DebouncedSave(TimeSpan.Zero, (Func<Task>)null!));
    }

    /// <summary>Waits on <see cref="DebouncedSave.StateChanged"/> rather than
    /// sleeping, so the tests are as fast as the delays they set and fail on a
    /// timeout rather than a race.</summary>
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
