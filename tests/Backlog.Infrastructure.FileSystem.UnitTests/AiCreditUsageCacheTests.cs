using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The saved months of Copilot AI-credit usage: what comes back, what does not,
/// and what happens when the disk will not cooperate.
/// </summary>
public class AiCreditUsageCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ai-credit-usage-cache-tests-" + Guid.NewGuid().ToString("N"));

    public AiCreditUsageCacheTests() => Directory.CreateDirectory(_root);

    private AiCreditUsageCache Cache() => new(() => _root);

    private static GitHubAiCreditUsage August() => new(
        [
            new GitHubAiCreditUsageItem("copilot", "copilot_ai_credit", "gpt-5", "credit", 0.04m, 300, 12m, 200, 8m, 100, 4m),
            new GitHubAiCreditUsageItem("copilot", "copilot_ai_credit", "claude-sonnet-5", "credit", 0.05m, 40, 2m, 0, 0, 40, 2m)
        ],
        GitHubBillingScope.Organization);

    [Fact]
    public void What_was_written_comes_back()
    {
        var cache = Cache();

        cache.Write("jsdotnet", 2026, 8, August());

        var read = cache.TryRead("jsdotnet", 2026, 8);

        Assert.NotNull(read);
        Assert.Equal(2, read.Items.Count);
        Assert.Equal(6m, read.NetAmount);
        Assert.Equal(8m, read.Items[0].DiscountAmount);
        Assert.Equal("claude-sonnet-5", read.Items[1].Model);
        Assert.Equal(GitHubBillingScope.Organization, read.Scope);
    }

    /// <summary>A quiet month is a real answer, and it comes back as one rather
    /// than as a miss that would be fetched for ever.</summary>
    [Fact]
    public void An_empty_month_comes_back_empty_rather_than_missing()
    {
        var cache = Cache();

        cache.Write("jsdotnet", 2026, 7, new GitHubAiCreditUsage([], GitHubBillingScope.PersonalAccount));

        var read = cache.TryRead("jsdotnet", 2026, 7);

        Assert.NotNull(read);
        Assert.Empty(read.Items);
        Assert.Equal(GitHubBillingScope.PersonalAccount, read.Scope);
    }

    [Fact]
    public void A_month_nothing_was_written_for_is_a_miss()
    {
        Assert.Null(Cache().TryRead("jsdotnet", 2026, 8));
    }

    [Fact]
    public void Each_login_and_each_month_is_its_own_entry()
    {
        var cache = Cache();
        cache.Write("jsdotnet", 2026, 8, August());

        Assert.Null(cache.TryRead("someone-else", 2026, 8));
        Assert.Null(cache.TryRead("jsdotnet", 2026, 9));
        Assert.NotNull(cache.TryRead("JSdotNet", 2026, 8));
    }

    /// <summary>A scope this version does not know reads as Unknown, which is the
    /// honest value, rather than as a member it happens to number the same.</summary>
    [Fact]
    public void A_scope_name_this_version_does_not_know_reads_as_unknown()
    {
        var cache = Cache();
        cache.Write("jsdotnet", 2026, 8, August());

        var path = OnlyEntry();
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"Organization\"", "\"Enterprise\"", StringComparison.Ordinal));

        Assert.Equal(GitHubBillingScope.Unknown, cache.TryRead("jsdotnet", 2026, 8)!.Scope);
    }

    [Fact]
    public void An_unreadable_month_reads_as_a_miss_rather_than_throwing()
    {
        var cache = Cache();
        cache.Write("jsdotnet", 2026, 8, August());

        File.WriteAllText(OnlyEntry(), "{ not json at all");

        Assert.Null(cache.TryRead("jsdotnet", 2026, 8));
    }

    [Fact]
    public void A_month_written_under_an_older_version_reads_as_a_miss()
    {
        var cache = Cache();
        cache.Write("jsdotnet", 2026, 8, August());

        var path = OnlyEntry();
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"version\": 1", "\"version\": 0", StringComparison.Ordinal));

        Assert.Null(cache.TryRead("jsdotnet", 2026, 8));
    }

    [Fact]
    public void A_write_that_cannot_land_is_not_fatal()
    {
        var blocked = Path.Combine(_root, "blocked");
        File.WriteAllText(blocked, "not a directory");

        var cache = new AiCreditUsageCache(() => blocked);

        cache.Write("jsdotnet", 2026, 8, August());

        Assert.Null(cache.TryRead("jsdotnet", 2026, 8));
    }

    [Fact]
    public void Forgetting_drops_every_login_and_every_month()
    {
        var cache = Cache();
        cache.Write("jsdotnet", 2026, 7, August());
        cache.Write("jsdotnet", 2026, 8, August());
        cache.Write("someone-else", 2026, 8, August());

        cache.Forget();

        Assert.Null(cache.TryRead("jsdotnet", 2026, 7));
        Assert.Null(cache.TryRead("jsdotnet", 2026, 8));
        Assert.Null(cache.TryRead("someone-else", 2026, 8));
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
