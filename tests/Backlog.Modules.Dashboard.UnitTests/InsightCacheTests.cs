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
            .Select(_ => cache.GetOrAddAsync("activity", async () =>
            {
                Interlocked.Increment(ref calls);
                await gate.Task;
                return "answer";
            }))
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
            cache.GetOrAddAsync<string>("activity", () =>
            {
                calls++;
                throw new InvalidOperationException("GitHub answered 502.");
            }));

        var second = await cache.GetOrAddAsync("activity", () =>
        {
            calls++;
            return Task.FromResult("answer");
        });

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
            _ = await cache.GetOrAddAsync("activity", () =>
            {
                calls++;
                return Task.FromResult("answer");
            });
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

        _ = await cache.GetOrAddAsync("activity|*|12", () => Task.FromResult(++activity));
        _ = await cache.GetOrAddAsync("month|2026-08", () => Task.FromResult(++spend));

        cache.Invalidate("activity");

        _ = await cache.GetOrAddAsync("activity|*|12", () => Task.FromResult(++activity));
        _ = await cache.GetOrAddAsync("month|2026-08", () => Task.FromResult(++spend));

        Assert.Equal(2, activity);
        Assert.Equal(1, spend);
    }
}
