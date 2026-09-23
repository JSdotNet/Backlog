using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The saved facts of a transcript — folder, branch, turns — and the key that
/// decides whether they still describe the file: what comes back, what does not,
/// and what happens when the disk will not cooperate.
/// </summary>
public class TranscriptFactsCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "transcript-facts-cache-tests-" + Guid.NewGuid().ToString("N"));

    private const string Transcript = @"C:\Users\someone\.claude\projects\D--Repos-Backlog\0b8f2c3e.jsonl";

    private static readonly DateTimeOffset WrittenAt = new(2026, 9, 1, 8, 30, 0, TimeSpan.Zero);

    public TranscriptFactsCacheTests() => Directory.CreateDirectory(_root);

    private TranscriptFactsCache Cache() => new(() => _root);

    [Fact]
    public void What_was_written_comes_back()
    {
        var cache = Cache();

        cache.Write(Transcript, 64_000, WrittenAt, new TranscriptFacts(@"D:\Repos\Backlog", "main", 17));

        var read = cache.TryRead(Transcript, 64_000, WrittenAt);

        Assert.NotNull(read);
        Assert.Equal(@"D:\Repos\Backlog", read.Folder);
        Assert.Equal("main", read.Branch);
        Assert.Equal(17, read.Turns);
    }

    /// <summary>A transcript that stated nothing countable is a real thing, and it
    /// comes back as absent rather than as a miss to be re-read for ever.</summary>
    [Fact]
    public void Absent_facts_survive_the_round_trip_as_absent()
    {
        var cache = Cache();

        cache.Write(Transcript, 900, WrittenAt, new TranscriptFacts(string.Empty, null, null));

        var read = cache.TryRead(Transcript, 900, WrittenAt);

        Assert.NotNull(read);
        Assert.Equal(string.Empty, read.Folder);
        Assert.Null(read.Branch);
        Assert.Null(read.Turns);
    }

    /// <summary>
    /// The key. A transcript appended to since is a different length and a different
    /// write time, and either alone is enough to say the stored count is no longer
    /// the count — the one file whose answer must not be stale is the one being
    /// written to right now.
    /// </summary>
    [Fact]
    public void A_transcript_that_grew_is_a_miss()
    {
        var cache = Cache();
        cache.Write(Transcript, 64_000, WrittenAt, new TranscriptFacts(@"D:\Repos\Backlog", "main", 17));

        Assert.Null(cache.TryRead(Transcript, 64_500, WrittenAt));
    }

    [Fact]
    public void A_transcript_written_to_since_is_a_miss()
    {
        var cache = Cache();
        cache.Write(Transcript, 64_000, WrittenAt, new TranscriptFacts(@"D:\Repos\Backlog", "main", 17));

        Assert.Null(cache.TryRead(Transcript, 64_000, WrittenAt.AddSeconds(1)));
    }

    /// <summary>A transcript that grew replaces its own entry rather than leaving a
    /// stale file per append behind it: one transcript, one file on disk.</summary>
    [Fact]
    public void A_transcript_that_grew_replaces_its_own_entry()
    {
        var cache = Cache();
        cache.Write(Transcript, 64_000, WrittenAt, new TranscriptFacts(@"D:\Repos\Backlog", "main", 17));
        cache.Write(Transcript, 64_500, WrittenAt.AddMinutes(1), new TranscriptFacts(@"D:\Repos\Backlog", "main", 18));

        Assert.Single(Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories));
        Assert.Equal(18, cache.TryRead(Transcript, 64_500, WrittenAt.AddMinutes(1))!.Turns);
    }

    [Fact]
    public void A_transcript_nothing_was_written_for_is_a_miss()
    {
        Assert.Null(Cache().TryRead(Transcript, 1, WrittenAt));
    }

    [Fact]
    public void An_unreadable_entry_reads_as_a_miss_rather_than_throwing()
    {
        var cache = Cache();
        cache.Write(Transcript, 64_000, WrittenAt, new TranscriptFacts(@"D:\Repos\Backlog", "main", 17));

        File.WriteAllText(OnlyEntry(), "{ not json at all");

        Assert.Null(cache.TryRead(Transcript, 64_000, WrittenAt));
    }

    [Fact]
    public void An_entry_written_under_an_older_version_reads_as_a_miss()
    {
        var cache = Cache();
        cache.Write(Transcript, 64_000, WrittenAt, new TranscriptFacts(@"D:\Repos\Backlog", "main", 17));

        var path = OnlyEntry();
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"version\": 2", "\"version\": 0", StringComparison.Ordinal));

        Assert.Null(cache.TryRead(Transcript, 64_000, WrittenAt));
    }

    [Fact]
    public void A_write_that_cannot_land_is_not_fatal()
    {
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "not a directory");

        var cache = new TranscriptFactsCache(() => blocked);

        cache.Write(Transcript, 64_000, WrittenAt, new TranscriptFacts(@"D:\Repos\Backlog", "main", 17));

        Assert.Null(cache.TryRead(Transcript, 64_000, WrittenAt));
    }

    /// <summary>Two transcripts with the same file name in different projects are
    /// two entries, which the digest of the full path guarantees.</summary>
    [Fact]
    public void The_same_file_name_in_two_projects_is_two_entries()
    {
        var cache = Cache();
        const string other = @"C:\Users\someone\.claude\projects\D--Repos-Other\0b8f2c3e.jsonl";

        cache.Write(Transcript, 10, WrittenAt, new TranscriptFacts(@"D:\Repos\Backlog", null, 1));
        cache.Write(other, 10, WrittenAt, new TranscriptFacts(@"D:\Repos\Other", null, 2));

        Assert.Equal(1, cache.TryRead(Transcript, 10, WrittenAt)!.Turns);
        Assert.Equal(2, cache.TryRead(other, 10, WrittenAt)!.Turns);
    }

    /// <summary>The two transcript caches share a root and must not collide: an
    /// activity entry and a facts entry for the same file are both readable, and
    /// forgetting the activity cache takes the facts with it — same files, same
    /// reasons.</summary>
    [Fact]
    public void The_facts_live_beside_the_activity_and_are_forgotten_with_it()
    {
        var facts = Cache();
        var activity = new AgentActivityCache(() => _root);

        facts.Write(Transcript, 10, WrittenAt, new TranscriptFacts(@"D:\Repos\Backlog", null, 1));
        activity.Write(Transcript, WrittenAt, new AgentActivityEntry([], [], TimeSpan.FromMinutes(5)));

        Assert.NotNull(facts.TryRead(Transcript, 10, WrittenAt));
        Assert.NotNull(activity.TryRead(Transcript, WrittenAt, TimeSpan.FromMinutes(5)));

        activity.Forget();

        Assert.Null(facts.TryRead(Transcript, 10, WrittenAt));
    }

    [Fact]
    public void Forgetting_drops_every_entry()
    {
        var cache = Cache();
        cache.Write(Transcript, 10, WrittenAt, new TranscriptFacts(@"D:\Repos\Backlog", null, 1));

        cache.Forget();

        Assert.Null(cache.TryRead(Transcript, 10, WrittenAt));
    }

    [Fact]
    public void Forgetting_when_nothing_was_stored_is_not_an_error()
    {
        Cache().Forget();
    }

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
            // A temp folder left behind is not a failed test.
        }
    }
}
