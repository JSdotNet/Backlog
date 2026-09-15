using Backlog.Infrastructure.Claude;
using Backlog.Modules.Dashboard.UI.Adapters;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Claude spend adapter over several accounts: one person, several
/// organizations, one figure.
/// </summary>
public sealed class ClaudeSpendSourceTests
{
    [Fact]
    public async Task Spend_is_read_from_every_account_with_a_key_and_an_actor_and_added_up()
    {
        using var store = new TemporaryClaudeStore();
        var personal = store.Store.Current.Accounts[0].Id;
        store.Store.SetAdminApiKey(personal, "sk-ant-admin01-personal");
        store.Store.SetActor(personal, "me@example.com");
        store.Store.AddAccount();
        var work = store.Store.Current.Accounts[1].Id;
        store.Store.SetAdminApiKey(work, "sk-ant-admin01-work");
        store.Store.SetActor(work, "me@employer.example");

        // A third card somebody added and never filled in. Skipped, not fatal.
        store.Store.AddAccount();

        var usage = new StubUsage
        {
            [personal] = ("me@example.com", 1.25m),
            [work] = ("me@employer.example", 4.00m)
        };
        var source = new ClaudeSpendSource(usage, store.Store);

        Assert.True((await source.GetAvailabilityAsync(TestContext.Current.CancellationToken)).IsAvailable);

        var day = new DateOnly(2026, 9, 1);
        var report = await source.GetSpendAsync(day, day, TestContext.Current.CancellationToken);

        Assert.Equal(2, report.Entries.Count);
        Assert.Equal(5.25m, report.Entries.Sum(entry => entry.Cost.Amount));
        Assert.Equal([personal, work], usage.AccountsAsked);
    }

    /// <summary>Only the named account's actor is kept from each organization's
    /// report: the same email can be an actor in both, but the personal report's
    /// rows for the work address are somebody else's spend there.</summary>
    [Fact]
    public async Task Each_account_is_narrowed_to_its_own_actor()
    {
        using var store = new TemporaryClaudeStore();
        var personal = store.Store.Current.Accounts[0].Id;
        store.Store.SetAdminApiKey(personal, "sk-ant-admin01-personal");
        store.Store.SetActor(personal, "me@example.com");

        var usage = new StubUsage { [personal] = ("someone-else@example.com", 9.99m) };
        var source = new ClaudeSpendSource(usage, store.Store);

        var day = new DateOnly(2026, 9, 1);
        var report = await source.GetSpendAsync(day, day, TestContext.Current.CancellationToken);

        Assert.Empty(report.Entries);
    }

    [Fact]
    public async Task A_key_without_an_actor_on_every_account_is_unavailable_and_says_why()
    {
        using var store = new TemporaryClaudeStore();
        store.Store.SetAdminApiKey(store.Store.Current.Accounts[0].Id, "sk-ant-admin01-personal");

        var source = new ClaudeSpendSource(new StubUsage(), store.Store);

        var availability = await source.GetAvailabilityAsync(TestContext.Current.CancellationToken);

        Assert.False(availability.IsAvailable);
        Assert.Contains("which actor is you", availability.Reason, StringComparison.Ordinal);
    }

    private sealed class StubUsage : Dictionary<string, (string Actor, decimal Cost)>, IClaudeUsageClient
    {
        public List<string> AccountsAsked { get; } = [];

        public Task<ClaudeUsageAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ClaudeUsageAvailability(true, "stub"));

        public Task<ClaudeUsageReport> GetMessageUsageAsync(ClaudeAccount account, ClaudeUsageWindow window, ClaudeUsageBucket bucket = ClaudeUsageBucket.Day, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ClaudeCostReport> GetCostAsync(ClaudeAccount account, ClaudeUsageWindow window, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ClaudeCodeReport> GetClaudeCodeUsageAsync(ClaudeAccount account, DateOnly date, CancellationToken cancellationToken = default)
        {
            AccountsAsked.Add(account.Id);

            if (!TryGetValue(account.Id, out var row)) return Task.FromResult(ClaudeCodeReport.Empty(date));

            var model = new ClaudeCodeModelUsage("claude-opus-5", new ClaudeTokenUsage(10, 5, 0, 0), row.Cost, "USD");
            var actor = new ClaudeCodeDailyUsage(date, row.Actor, 1, 0, 0, 0, 0, [model]);

            return Task.FromResult(new ClaudeCodeReport(date, [actor]));
        }
    }

    private sealed class TemporaryClaudeStore : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-claude-spend-tests", Guid.NewGuid().ToString("N"));

        public TemporaryClaudeStore() => Store = new ClaudeSettingsStore(Path.Combine(_root, "claude.json"));

        public ClaudeSettingsStore Store { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
