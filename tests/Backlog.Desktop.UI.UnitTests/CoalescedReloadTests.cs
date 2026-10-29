using Backlog.Desktop.UI.Shell;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A window's reload for writes it did not make, coalesced (local ADR 0041 §5): a burst
/// of signals while a reload runs is one more reload, never one per signal and never
/// two at once, and a failed reload lets go of what it owed so the next signal starts
/// over.
/// </summary>
public sealed class CoalescedReloadTests
{
    private readonly CoalescedReload _reload = new();

    [Fact]
    public async Task A_signal_with_nothing_running_reloads_once()
    {
        var reloads = 0;

        await _reload.RunAsync(() => { reloads++; return Task.CompletedTask; });

        Assert.Equal(1, reloads);
    }

    [Fact]
    public async Task A_burst_while_a_reload_runs_collapses_into_one_more_reload()
    {
        var first = new TaskCompletionSource();
        var reloads = 0;
        var inFlight = 0;
        var mostInFlight = 0;

        async Task Reload()
        {
            reloads++;
            mostInFlight = Math.Max(mostInFlight, ++inFlight);
            if (reloads == 1) await first.Task;
            inFlight--;
        }

        var running = _reload.RunAsync(Reload);
        var burst = Enumerable.Range(0, 5).Select(_ => _reload.RunAsync(Reload)).ToList();

        // Each signal of the burst only marks a reload owed and returns at once.
        Assert.All(burst, signal => Assert.True(signal.IsCompletedSuccessfully));
        Assert.Equal(1, reloads);

        first.SetResult();
        await running;

        Assert.Equal(2, reloads);
        Assert.Equal(1, mostInFlight);
    }

    [Fact]
    public async Task A_failed_reload_is_thrown_to_the_running_call_and_drops_what_it_owed()
    {
        var failing = new TaskCompletionSource();
        var reloads = 0;

        async Task Reload()
        {
            reloads++;
            if (reloads == 1) await failing.Task;
        }

        var running = _reload.RunAsync(Reload);
        await _reload.RunAsync(Reload);

        failing.SetException(new InvalidOperationException("the list could not be read"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => running);
        Assert.Equal(1, reloads);
    }

    [Fact]
    public async Task After_a_failure_the_next_signal_starts_a_reload_of_its_own()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _reload.RunAsync(() => throw new InvalidOperationException("the list could not be read")));

        var reloads = 0;
        await _reload.RunAsync(() => { reloads++; return Task.CompletedTask; });

        Assert.Equal(1, reloads);
    }
}
