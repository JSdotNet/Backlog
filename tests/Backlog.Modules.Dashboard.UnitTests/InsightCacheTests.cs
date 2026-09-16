using Backlog.Modules.Dashboard.Services;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The session cache in front of the providers. Small, and worth testing directly
/// for two properties that are easy to get subtly wrong.
/// </summary>
public class InsightCacheTests
{
    /// <summary>
    /// The reason entries hold the task rather than its result. When the pane
    /// renders, seven parts start their fetches at once and four of them ask the
    /// same question; storing results would let all four miss and make four calls.
    /// </summary>
    [Fact]
    public async Task Callers_arriving_together_join_one_call_rather_than_racing_several()
    {
        var cache = new InsightCache();
        var gate = new TaskCompletionSource();
        var calls = 0;

        var waiters = Enumerable.Range(0, 5)
            .Select(_ => cache.GetOrAddAsync("activity", async _ =>
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return "answer";
            }, TestContext.Current.CancellationToken))
            .ToList();

        gate.SetResult();

        var answers = await Task.WhenAll(waiters);

        Assert.Equal(1, calls);
        Assert.All(answers, answer => Assert.Equal("answer", answer));
    }

    /// <summary>
    /// A failure must not be cached. Otherwise one dropped connection makes a part
    /// unavailable for the rest of the session with no way back but closing the
    /// dashboard — and the refresh control would be a button that does nothing.
    /// </summary>
    [Fact]
    public async Task A_failure_is_forgotten_so_the_next_attempt_actually_tries_again()
    {
        var cache = new InsightCache();
        var calls = 0;

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrAddAsync<string>("activity", _ =>
            {
                calls++;
                throw new InvalidOperationException("GitHub answered 502.");
            }, TestContext.Current.CancellationToken));

        var second = await cache.GetOrAddAsync("activity", _ =>
        {
            calls++;
            return Task.FromResult("answer");
        }, TestContext.Current.CancellationToken);

        Assert.Equal(2, calls);
        Assert.Equal("answer", second);
    }

    [Fact]
    public async Task A_success_is_answered_from_the_cache_the_second_time()
    {
        var cache = new InsightCache();
        var calls = 0;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            _ = await cache.GetOrAddAsync("activity", _ =>
            {
                calls++;
                return Task.FromResult("answer");
            }, TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, calls);
    }

    /// <summary>Invalidating by prefix is how one part refreshes without throwing
    /// away what the others already fetched.</summary>
    [Fact]
    public async Task Invalidating_a_prefix_leaves_the_other_entries_alone()
    {
        var cache = new InsightCache();
        var activity = 0;
        var spend = 0;

        _ = await cache.GetOrAddAsync("activity|*|12", _ => Task.FromResult(++activity), TestContext.Current.CancellationToken);
        _ = await cache.GetOrAddAsync("month|2026-08", _ => Task.FromResult(++spend), TestContext.Current.CancellationToken);

        cache.Invalidate("activity");

        _ = await cache.GetOrAddAsync("activity|*|12", _ => Task.FromResult(++activity), TestContext.Current.CancellationToken);
        _ = await cache.GetOrAddAsync("month|2026-08", _ => Task.FromResult(++spend), TestContext.Current.CancellationToken);

        Assert.Equal(2, activity);
        Assert.Equal(1, spend);
    }
    /// <summary>
    /// The stuck-loading race. A part moving its filter cancels its previous fetch
    /// and starts another against the same entry; if the shared call ran under the
    /// first caller's token, the second caller would inherit a cancellation that was
    /// never its own, catch it as "I was cancelled", and stay on Loading for good.
    /// So one caller giving up must not cancel the call the others are still waiting
    /// on.
    /// </summary>
    [Fact]
    public async Task A_caller_that_gives_up_leaves_the_shared_call_running_for_the_others()
    {
        var cache = new InsightCache();
        var gate = new TaskCompletionSource();
        var calls = 0;
        CancellationToken seen = default;

        Task<string> Ask(CancellationToken cancellationToken) =>
            cache.GetOrAddAsync("sessions", async token =>
            {
                Interlocked.Increment(ref calls);
                seen = token;
                await gate.Task.WaitAsync(token);
                return "answer";
            }, cancellationToken);

        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();

        var firstAsk = Ask(first.Token);
        var secondAsk = Ask(second.Token);

        await first.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstAsk);
        Assert.False(seen.IsCancellationRequested);

        gate.SetResult();

        Assert.Equal("answer", await secondAsk);
        Assert.Equal(1, calls);
    }

    /// <summary>
    /// The other half: cancellation still means something. When the reader closes the
    /// dashboard nobody is waiting any more, and a read of hundreds of megabytes must
    /// stop rather than run to an answer nobody will see — and the entry it would have
    /// filled must not be left holding a cancelled task for the next opener to trip on.
    /// </summary>
    [Fact]
    public async Task The_last_caller_giving_up_stops_the_call_and_the_next_one_starts_afresh()
    {
        var cache = new InsightCache();
        var gate = new TaskCompletionSource();
        var calls = 0;
        CancellationToken seen = default;

        Task<string> Ask(CancellationToken cancellationToken) =>
            cache.GetOrAddAsync("sessions", async token =>
            {
                Interlocked.Increment(ref calls);
                seen = token;
                await gate.Task.WaitAsync(token);
                return "answer";
            }, cancellationToken);

        using var only = new CancellationTokenSource();

        var onlyAsk = Ask(only.Token);

        await only.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => onlyAsk);
        Assert.True(seen.IsCancellationRequested);

        gate.SetResult();

        Assert.Equal("answer", await Ask(TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }
}
