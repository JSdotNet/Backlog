using System.Text.RegularExpressions;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The remembered parse of a transcript: what comes back, what does not, and what
/// happens when the disk will not cooperate.
/// <para>
/// Real temp directories rather than a filesystem abstraction, the way
/// <see cref="PullRequestDetailCacheTests"/> does it and for the same reason — the
/// behaviour under test <em>is</em> the disk, and a fake would be asserting the fake.
/// </para>
/// </summary>
public class AgentActivityCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "agent-activity-cache-tests-" + Guid.NewGuid().ToString("N"));

    private static readonly DateTimeOffset Written = new(2026, 8, 19, 11, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    private const string Transcript = @"C:\Users\dev\.claude\projects\D--Repos-Backlog\7f1a.jsonl";

    public AgentActivityCacheTests() => Directory.CreateDirectory(_root);

    private AgentActivityCache Cache() => new(() => _root);

    [Fact]
    public void An_entry_written_is_an_entry_read_back()
    {
        var cache = Cache();

        cache.Write(Transcript, Written, Entry(
            runs: [(9, 10), (12, 13)],
            waits: [(10, 12)]));

        var read = cache.TryRead(Transcript, Written, Threshold);

        Assert.NotNull(read);
        Assert.Equal(Threshold, read.IdleAfter);

        Assert.Equal(
            [(At(9), At(10)), (At(12), At(13))],
            read.Runs.Select(run => (run.StartedAt, run.EndedAt)));

        // The waits come back as waits and not as more runs. They are the same two
        // instants and different meanings, and a round trip that merged them would put
        // idle time into the agent-active figure.
        Assert.Equal(
            [(At(10), At(12))],
            read.Waits.Select(wait => (wait.StartedAt, wait.EndedAt)));
    }

    [Fact]
    public void Nothing_stored_is_a_miss_rather_than_a_throw()
    {
        Assert.Null(Cache().TryRead(Transcript, Written, Threshold));
    }

    /// <summary>
    /// The property the whole cache turns on. The one transcript being appended to
    /// right now is exactly the file whose answer must not be stale, and it is the file
    /// whose write time moves — so keying on the pair rather than on the path is what
    /// makes a finished transcript free and a live one always fresh.
    /// </summary>
    [Fact]
    public void An_entry_stored_against_a_different_write_of_the_same_file_is_a_miss()
    {
        var cache = Cache();

        cache.Write(Transcript, Written, Entry(runs: [(9, 10)], waits: []));

        Assert.Null(cache.TryRead(Transcript, Written.AddSeconds(1), Threshold));
    }

    [Fact]
    public void An_entry_from_another_version_is_a_miss_rather_than_a_guess()
    {
        var cache = Cache();
        cache.Write(Transcript, Written, Entry(runs: [(9, 10)], waits: []));

        var path = OnlyEntry();

        // Rewrites whatever version was written rather than the one that was current the
        // day this was written. The version is bumped whenever the parse changes, so a
        // test that spelled the number out would fail on the next honest bump and read as
        // the bump being the mistake.
        File.WriteAllText(
            path,
            Regex.Replace(File.ReadAllText(path), "\"version\":\\s*\\d+", "\"version\": 0"));

        Assert.Null(cache.TryRead(Transcript, Written, Threshold));
    }

    /// <summary>A half-written or unreadable entry costs one parse to replace.
    /// Throwing would fail a whole dashboard read over one corrupt file.</summary>
    [Fact]
    public void A_corrupt_entry_is_a_miss_rather_than_a_throw()
    {
        var cache = Cache();
        cache.Write(Transcript, Written, Entry(runs: [(9, 10)], waits: []));

        File.WriteAllText(OnlyEntry(), "{ not json at all");

        Assert.Null(cache.TryRead(Transcript, Written, Threshold));
    }

    /// <summary>
    /// The threshold is part of the key, so moving it invalidates every entry by
    /// comparison. The alternative — remembering to bump <c>Version</c> when the
    /// constant moves — is a rule that holds until the first person who changes one and
    /// not the other, and the symptom would be a figure that kept the old answer with
    /// nothing on screen to say so.
    /// </summary>
    [Fact]
    public void An_entry_folded_at_a_different_threshold_is_a_miss()
    {
        var cache = Cache();

        cache.Write(Transcript, Written, Entry(runs: [(9, 10)], waits: []));

        Assert.Null(cache.TryRead(Transcript, Written, TimeSpan.FromMinutes(15)));
        Assert.NotNull(cache.TryRead(Transcript, Written, Threshold));
    }

    /// <summary>An entry that could not be written is parsed again next time.
    /// Wasteful, not wrong — and far better than failing a read that succeeded.</summary>
    [Fact]
    public void A_root_that_cannot_be_written_costs_the_entry_and_not_the_read()
    {
        // A file where the cache expects a folder: nothing can be created under it.
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "not a directory");

        var cache = new AgentActivityCache(() => blocked);

        cache.Write(Transcript, Written, Entry(runs: [(9, 10)], waits: []));

        Assert.Null(cache.TryRead(Transcript, Written, Threshold));
    }

    /// <summary>
    /// A session resumed in a worktree it did not start in is filed under two project
    /// slugs while keeping its id, so the two files share a leaf and differ only in
    /// their path. One entry for both would answer for whichever was parsed last.
    /// </summary>
    [Fact]
    public void Two_transcripts_of_one_session_id_under_two_slugs_get_two_entries()
    {
        var cache = Cache();

        const string started = @"C:\Users\dev\.claude\projects\D--Repos-Backlog--worktrees-a\7f1a.jsonl";
        const string carriedOn = @"C:\Users\dev\.claude\projects\D--Repos-Backlog--worktrees-b\7f1a.jsonl";

        cache.Write(started, Written, Entry(runs: [(9, 10)], waits: []));
        cache.Write(carriedOn, Written, Entry(runs: [(14, 15)], waits: []));

        Assert.Equal(At(9), cache.TryRead(started, Written, Threshold)!.Runs[0].StartedAt);
        Assert.Equal(At(14), cache.TryRead(carriedOn, Written, Threshold)!.Runs[0].StartedAt);
    }

    /// <summary>
    /// The root is a workspace location somebody can move, so it is resolved on every
    /// call. One captured at construction would keep writing to where the folder used
    /// to be, and the only symptom would be a read that got slower.
    /// </summary>
    [Fact]
    public void The_root_is_resolved_on_every_call_rather_than_captured()
    {
        var here = _root;
        var cache = new AgentActivityCache(() => here);

        cache.Write(Transcript, Written, Entry(runs: [(9, 10)], waits: []));

        here = Path.Combine(_root, "moved");

        Assert.Null(cache.TryRead(Transcript, Written, Threshold));

        cache.Write(Transcript, Written, Entry(runs: [(14, 15)], waits: []));

        Assert.Equal(At(14), cache.TryRead(Transcript, Written, Threshold)!.Runs[0].StartedAt);
    }

    [Fact]
    public void Forgetting_sends_the_next_read_back_to_the_transcripts()
    {
        var cache = Cache();
        cache.Write(Transcript, Written, Entry(runs: [(9, 10)], waits: []));

        cache.Forget();

        Assert.Null(cache.TryRead(Transcript, Written, Threshold));

        // And it is still usable afterwards: forgetting empties the folder rather than
        // taking the cache out of service.
        cache.Write(Transcript, Written, Entry(runs: [(14, 15)], waits: []));
        Assert.NotNull(cache.TryRead(Transcript, Written, Threshold));
    }

    [Fact]
    public void Forgetting_when_nothing_was_ever_stored_is_not_an_error()
    {
        Cache().Forget();
    }

    private static AgentActivityEntry Entry((int From, int To)[] runs, (int From, int To)[] waits) =>
        new(
            [.. runs.Select(run => new AgentActivityRun(At(run.From), At(run.To)))],
            [.. waits.Select(wait => new AgentActivityWait(At(wait.From), At(wait.To)))],
            Threshold);

    private static DateTimeOffset At(int hour) => new(2026, 8, 19, hour, 0, 0, TimeSpan.Zero);

    /// <summary>The one entry on disk, found rather than spelled out — where the cache
    /// puts it is the cache's business, not this test's.</summary>
    private string OnlyEntry() => Assert.Single(Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories));

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
