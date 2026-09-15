using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// How often the auto-fetch is allowed to ask GitHub, and what it says while it
/// does. Resolution calls <see cref="DevbookSnapshotAutoFetch.Ensure"/> on every
/// panel load and every Settings render, so the whole point is that most of
/// those calls cost nothing.
/// </summary>
public class DevbookSnapshotAutoFetchTests
{
    private static readonly GitHubRepositoryRef Repository = new("backlog", "JSdotNet", "Backlog");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero));

    private int _announced;

    private DevbookSnapshotAutoFetch Fetcher(GatedCache cache) =>
        new(cache, () => Interlocked.Increment(ref _announced), _time, TimeSpan.FromMinutes(10));

    /// <summary>Lets the fetch in flight for <c>main</c> finish with this result
    /// and waits for it. The pending task is taken hold of first: once the result
    /// is in, the fetch clears its own slot and there is nothing left to await.</summary>
    private static async Task Land(DevbookSnapshotAutoFetch fetcher, GatedCache cache, DevbookSnapshotResult result)
    {
        var pending = fetcher.InFlight(Repository, "main")!;
        cache.Complete(result);
        await pending;
    }

    [Fact]
    public async Task The_first_ask_starts_one_fetch_and_every_ask_meanwhile_joins_it()
    {
        var cache = new GatedCache();
        var fetcher = Fetcher(cache);

        var first = fetcher.Ensure(Repository, "main", hasSnapshot: false);
        var second = fetcher.Ensure(Repository, "main", hasSnapshot: false);
        var third = fetcher.Ensure(Repository, "main", hasSnapshot: false);

        Assert.True(first.InFlight);
        Assert.True(second.InFlight);
        Assert.True(third.InFlight);
        Assert.Equal(1, cache.WaitForFetches(1));

        await Land(fetcher, cache, new DevbookSnapshotResult(new DevbookSnapshot("main", "sha-1", _time.GetUtcNow()), true, false, null));

        Assert.False(fetcher.Ensure(Repository, "main", hasSnapshot: true).InFlight);
        Assert.Equal(1, _announced);
    }

    /// <summary>One head check per interval. Inside it a snapshot is trusted;
    /// past it the branch is asked again, once, and a download that finds the
    /// commit unchanged announces nothing.</summary>
    [Fact]
    public async Task A_snapshot_is_re_checked_once_per_interval_and_an_unchanged_branch_is_quiet()
    {
        var cache = new GatedCache();
        var fetcher = Fetcher(cache);
        var upToDate = new DevbookSnapshotResult(new DevbookSnapshot("main", "sha-1", _time.GetUtcNow()), false, false, "Up to date with main.");

        fetcher.Ensure(Repository, "main", hasSnapshot: true);
        await Land(fetcher, cache, upToDate);

        _time.Advance(TimeSpan.FromMinutes(9));
        for (var i = 0; i < 20; i++) fetcher.Ensure(Repository, "main", hasSnapshot: true);
        Assert.Equal(1, cache.Fetches);

        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.True(fetcher.Ensure(Repository, "main", hasSnapshot: true).InFlight);
        Assert.Equal(2, cache.WaitForFetches(2));

        await Land(fetcher, cache, upToDate);

        Assert.Equal(0, _announced);
    }

    [Fact]
    public async Task A_branch_that_moved_is_downloaded_and_announced()
    {
        var cache = new GatedCache();
        var fetcher = Fetcher(cache);

        fetcher.Ensure(Repository, "main", hasSnapshot: true);
        await Land(fetcher, cache, new DevbookSnapshotResult(new DevbookSnapshot("main", "sha-2", _time.GetUtcNow()), true, false, "Updated to the latest main."));

        Assert.Equal(1, _announced);
    }

    /// <summary>The failure is the adapter's words, remembered for the interval
    /// so a repository this machine cannot reach costs one request per interval
    /// rather than one per render; and with nothing on disk it is announced,
    /// because a panel showing "fetching" needs to hear it can stop.</summary>
    [Fact]
    public async Task A_failure_with_no_snapshot_is_reported_announced_and_not_retried_until_the_interval_passes()
    {
        var cache = new GatedCache();
        var fetcher = Fetcher(cache);

        fetcher.Ensure(Repository, "main", hasSnapshot: false);
        await Land(fetcher, cache, DevbookSnapshotResult.Failed(null, "Not Found"));

        var status = fetcher.Ensure(Repository, "main", hasSnapshot: false);

        Assert.False(status.InFlight);
        Assert.Equal("Not Found", status.Failure);
        Assert.Equal(1, cache.Fetches);
        Assert.Equal(1, _announced);

        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(fetcher.Ensure(Repository, "main", hasSnapshot: false).InFlight);
        Assert.Equal(2, cache.WaitForFetches(2));
    }

    /// <summary>A message beside an existing snapshot — the branch could not be
    /// reached just now — is not a failure the reader has to see: the copy that
    /// was there is still what they are reading.</summary>
    [Fact]
    public async Task A_failure_beside_a_snapshot_is_not_a_failure_to_the_reader()
    {
        var cache = new GatedCache();
        var fetcher = Fetcher(cache);
        var existing = new DevbookSnapshot("main", "sha-1", _time.GetUtcNow());

        fetcher.Ensure(Repository, "main", hasSnapshot: true);
        await Land(fetcher, cache, DevbookSnapshotResult.Failed(existing, "Could not reach GitHub."));

        var status = fetcher.Ensure(Repository, "main", hasSnapshot: true);

        Assert.False(status.InFlight);
        Assert.Null(status.Failure);
        Assert.Equal(0, _announced);
    }

    [Fact]
    public async Task Forgetting_makes_the_next_ask_go_to_the_network_again()
    {
        var cache = new GatedCache();
        var fetcher = Fetcher(cache);

        fetcher.Ensure(Repository, "main", hasSnapshot: false);
        await Land(fetcher, cache, DevbookSnapshotResult.Failed(null, "Not Found"));

        fetcher.Forget();
        var status = fetcher.Ensure(Repository, "main", hasSnapshot: false);

        Assert.True(status.InFlight);
        Assert.Equal(2, cache.WaitForFetches(2));
    }

    /// <summary>An exception the cache did not turn into a message is still a
    /// message here, because an unobserved fault on a background task is a
    /// crash nobody asked for.</summary>
    [Fact]
    public async Task An_unexpected_exception_becomes_the_failure_rather_than_a_fault()
    {
        var cache = new GatedCache();
        var fetcher = Fetcher(cache);

        fetcher.Ensure(Repository, "main", hasSnapshot: false);
        var pending = fetcher.InFlight(Repository, "main")!;
        cache.Fail(new InvalidOperationException("boom"));
        await pending;

        Assert.Equal("boom", fetcher.Ensure(Repository, "main", hasSnapshot: false).Failure);
    }

    [Fact]
    public void Branches_of_the_same_repository_are_tracked_apart()
    {
        var cache = new GatedCache();
        var fetcher = Fetcher(cache);

        fetcher.Ensure(Repository, "main", hasSnapshot: false);
        fetcher.Ensure(Repository, "release/2.0", hasSnapshot: false);
        fetcher.Ensure(Repository, null, hasSnapshot: false);

        Assert.Equal(3, cache.WaitForFetches(3));
    }

    /// <summary>A cache whose fetch completes only when the test says so, with
    /// whatever result the test says. <see cref="Complete"/> and <see cref="Fail"/>
    /// wait for a fetch to have arrived, because the auto-fetch starts it on the
    /// pool and the test's next line may well run first.</summary>
    private sealed class GatedCache : IDevbookSnapshotCache
    {
        private readonly object _gate = new();
        private readonly Queue<TaskCompletionSource<DevbookSnapshotResult>> _pending = new();
        private readonly SemaphoreSlim _arrived = new(0);

        private int _fetches;

        public int Fetches => Volatile.Read(ref _fetches);

        /// <summary>How many fetches have started, once at least
        /// <paramref name="expected"/> have — the ask returns before the fetch it
        /// queued on the pool has begun, so a count read straight after it races.</summary>
        public int WaitForFetches(int expected)
        {
            SpinWait.SpinUntil(() => Volatile.Read(ref _fetches) >= expected, TimeSpan.FromSeconds(10));
            return Fetches;
        }

        public void Complete(DevbookSnapshotResult result) => Next().SetResult(result);

        public void Fail(Exception exception) => Next().SetException(exception);

        public string SnapshotPath(GitHubRepositoryRef repository, string? branch) => "unused";

        public DevbookSnapshot? TryRead(GitHubRepositoryRef repository, string? branch) => null;

        public Task<DevbookSnapshotResult> FetchAsync(GitHubRepositoryRef repository, string? branch, CancellationToken cancellationToken = default)
        {
            var source = new TaskCompletionSource<DevbookSnapshotResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_gate)
            {
                _fetches++;
                _pending.Enqueue(source);
            }

            _arrived.Release();

            return source.Task;
        }

        public Task<DevbookSnapshotResult> CheckAsync(GitHubRepositoryRef repository, string? branch, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The auto-fetch goes through FetchAsync, which checks first.");

        private TaskCompletionSource<DevbookSnapshotResult> Next()
        {
            Assert.True(_arrived.Wait(TimeSpan.FromSeconds(10)), "No fetch arrived to complete.");

            lock (_gate)
            {
                return _pending.Dequeue();
            }
        }
    }
}
