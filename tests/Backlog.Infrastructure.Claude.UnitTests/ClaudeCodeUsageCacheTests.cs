using Backlog.Infrastructure.Claude;

namespace Backlog.Infrastructure.Claude.UnitTests;

/// <summary>
/// The saved days of one actor's Claude Code spend: what comes back, what does
/// not, and what happens when the disk will not cooperate.
/// </summary>
public class ClaudeCodeUsageCacheTests
{
    private static readonly ClaudeAccount Personal = new() { Id = "a1b2c3", DisplayName = "personal" };

    private static readonly ClaudeAccount Work = new() { Id = "d4e5f6", DisplayName = "work" };

    private static readonly DateOnly Day = new(2026, 9, 1);

    private static ClaudeCodeSettledDay Spend() => new(
    [
        new ClaudeCodeModelUsage("claude-opus-5", new ClaudeTokenUsage(1_000, 200, 50, 5_000), 1.25m, "USD"),
        new ClaudeCodeModelUsage("claude-sonnet-5", new ClaudeTokenUsage(400, 100, 0, 0), 0.10m, "USD")
    ]);

    [Fact]
    public void What_was_written_comes_back()
    {
        using var directory = new TemporaryDirectory();
        var cache = new ClaudeCodeUsageCache(() => directory.Path);

        cache.Write(Personal, "me@example.com", Day, Spend());

        var read = cache.TryRead(Personal, "me@example.com", Day);

        Assert.NotNull(read);
        Assert.Equal(2, read.Models.Count);
        Assert.Equal("claude-opus-5", read.Models[0].Model);
        Assert.Equal(1.25m, read.Models[0].EstimatedCost);
        Assert.Equal("USD", read.Models[0].Currency);
        Assert.Equal(6_250, read.Models[0].Tokens.TotalTokens);
        Assert.Equal(5_000, read.Models[0].Tokens.CacheReadInputTokens);
    }

    /// <summary>A day this actor used nothing is a real answer. It comes back as an
    /// empty day rather than as a miss, or the quiet days would be re-fetched on
    /// every open for ever.</summary>
    [Fact]
    public void An_empty_day_comes_back_empty_rather_than_missing()
    {
        using var directory = new TemporaryDirectory();
        var cache = new ClaudeCodeUsageCache(() => directory.Path);

        cache.Write(Personal, "me@example.com", Day, ClaudeCodeSettledDay.Empty);

        var read = cache.TryRead(Personal, "me@example.com", Day);

        Assert.NotNull(read);
        Assert.Empty(read.Models);
    }

    [Fact]
    public void A_day_nothing_was_written_for_is_a_miss()
    {
        using var directory = new TemporaryDirectory();

        Assert.Null(new ClaudeCodeUsageCache(() => directory.Path).TryRead(Personal, "me@example.com", Day));
    }

    /// <summary>The key is the account, the actor and the day, and each of the three
    /// on its own is a different entry. The actor most of all: the report covers the
    /// whole organization, and a day saved for one address must not answer for
    /// another.</summary>
    [Fact]
    public void Each_account_actor_and_day_is_its_own_entry()
    {
        using var directory = new TemporaryDirectory();
        var cache = new ClaudeCodeUsageCache(() => directory.Path);

        cache.Write(Personal, "me@example.com", Day, Spend());

        Assert.Null(cache.TryRead(Work, "me@example.com", Day));
        Assert.Null(cache.TryRead(Personal, "someone-else@example.com", Day));
        Assert.Null(cache.TryRead(Personal, "me@example.com", Day.AddDays(1)));
        Assert.NotNull(cache.TryRead(Personal, "ME@example.com", Day));
    }

    /// <summary>The address is on disk as a digest, not as itself.</summary>
    [Fact]
    public void The_actor_does_not_appear_in_the_path()
    {
        using var directory = new TemporaryDirectory();
        var cache = new ClaudeCodeUsageCache(() => directory.Path);

        cache.Write(Personal, "me@example.com", Day, Spend());

        var entry = Assert.Single(Directory.GetFiles(directory.Path, "*.json", SearchOption.AllDirectories));
        Assert.DoesNotContain("example", entry, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unreadable_day_reads_as_a_miss_rather_than_throwing()
    {
        using var directory = new TemporaryDirectory();
        var cache = new ClaudeCodeUsageCache(() => directory.Path);
        cache.Write(Personal, "me@example.com", Day, Spend());

        var entry = Assert.Single(Directory.GetFiles(directory.Path, "*.json", SearchOption.AllDirectories));
        File.WriteAllText(entry, "{ not json at all");

        Assert.Null(cache.TryRead(Personal, "me@example.com", Day));
    }

    [Fact]
    public void A_day_written_under_an_older_version_reads_as_a_miss()
    {
        using var directory = new TemporaryDirectory();
        var cache = new ClaudeCodeUsageCache(() => directory.Path);
        cache.Write(Personal, "me@example.com", Day, Spend());

        var entry = Assert.Single(Directory.GetFiles(directory.Path, "*.json", SearchOption.AllDirectories));
        File.WriteAllText(entry, File.ReadAllText(entry).Replace("\"version\": 1", "\"version\": 0", StringComparison.Ordinal));

        Assert.Null(cache.TryRead(Personal, "me@example.com", Day));
    }

    [Fact]
    public void A_write_that_cannot_land_is_not_fatal()
    {
        using var directory = new TemporaryDirectory();
        var blocked = directory.File("blocked");
        File.WriteAllText(blocked, "not a directory");

        var cache = new ClaudeCodeUsageCache(() => blocked);

        cache.Write(Personal, "me@example.com", Day, Spend());

        Assert.Null(cache.TryRead(Personal, "me@example.com", Day));
    }

    [Fact]
    public void Forgetting_drops_every_account_and_every_day()
    {
        using var directory = new TemporaryDirectory();
        var cache = new ClaudeCodeUsageCache(() => directory.Path);
        cache.Write(Personal, "me@example.com", Day, Spend());
        cache.Write(Work, "me@employer.example", Day.AddDays(-1), Spend());

        cache.Forget();

        Assert.Null(cache.TryRead(Personal, "me@example.com", Day));
        Assert.Null(cache.TryRead(Work, "me@employer.example", Day.AddDays(-1)));
    }

    [Fact]
    public void Forgetting_when_nothing_was_stored_is_not_an_error()
    {
        using var directory = new TemporaryDirectory();

        new ClaudeCodeUsageCache(() => directory.Path).Forget();
    }
}
