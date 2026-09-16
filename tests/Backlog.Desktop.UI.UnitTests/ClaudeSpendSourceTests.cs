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

    // --- The day cache --------------------------------------------------------

    /// <summary>Today, for these tests: 20 September. The 18th and everything
    /// before it are settled; the 19th and the 20th are not.</summary>
    private static readonly DateTimeOffset Today = new(2026, 9, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_settled_day_already_in_the_cache_is_not_fetched_again()
    {
        using var store = new TemporaryClaudeStore();
        var personal = Configure(store, "me@example.com");
        var account = store.Store.Current.Accounts[0];

        var days = new RememberingDays();
        days.Write(account, "me@example.com", new DateOnly(2026, 9, 10), new ClaudeCodeSettledDay(
            [new ClaudeCodeModelUsage("claude-opus-5", new ClaudeTokenUsage(10, 5, 0, 0), 7.50m, "USD")]));

        var usage = new StubUsage { [personal] = ("me@example.com", 1.25m) };
        var source = new ClaudeSpendSource(usage, store.Store, days, new FixedClock(Today));

        var day = new DateOnly(2026, 9, 10);
        var report = await source.GetSpendAsync(day, day, TestContext.Current.CancellationToken);

        Assert.Equal(7.50m, Assert.Single(report.Entries).Cost.Amount);
        Assert.Empty(usage.DaysAsked);
    }

    [Fact]
    public async Task A_settled_day_read_from_Anthropic_is_remembered_for_next_time()
    {
        using var store = new TemporaryClaudeStore();
        var personal = Configure(store, "me@example.com");
        var account = store.Store.Current.Accounts[0];

        var days = new RememberingDays();
        var usage = new StubUsage { [personal] = ("me@example.com", 1.25m) };
        var source = new ClaudeSpendSource(usage, store.Store, days, new FixedClock(Today));

        var day = new DateOnly(2026, 9, 10);
        _ = await source.GetSpendAsync(day, day, TestContext.Current.CancellationToken);

        var remembered = days.TryRead(account, "me@example.com", day);
        Assert.NotNull(remembered);
        Assert.Equal(1.25m, Assert.Single(remembered.Models).EstimatedCost);
    }

    /// <summary>Only the actor's rows are remembered. The report covers the whole
    /// organization, and the other actors' spend does not belong on this disk.</summary>
    [Fact]
    public async Task Only_the_actors_own_rows_are_remembered()
    {
        using var store = new TemporaryClaudeStore();
        var personal = Configure(store, "me@example.com");
        var account = store.Store.Current.Accounts[0];

        var days = new RememberingDays();
        var usage = new StubUsage { [personal] = ("someone-else@example.com", 9.99m) };
        var source = new ClaudeSpendSource(usage, store.Store, days, new FixedClock(Today));

        var day = new DateOnly(2026, 9, 10);
        _ = await source.GetSpendAsync(day, day, TestContext.Current.CancellationToken);

        var remembered = days.TryRead(account, "me@example.com", day);
        Assert.NotNull(remembered);
        Assert.Empty(remembered.Models);
    }

    /// <summary>
    /// A window that straddles the horizon: settled days come from the cache and
    /// cost nothing, the last two days are fetched and are not written.
    /// </summary>
    [Fact]
    public async Task Only_the_days_that_can_still_change_are_fetched()
    {
        using var store = new TemporaryClaudeStore();
        var personal = Configure(store, "me@example.com");
        var account = store.Store.Current.Accounts[0];

        var days = new RememberingDays();
        for (var day = new DateOnly(2026, 9, 14); day <= new DateOnly(2026, 9, 18); day = day.AddDays(1))
        {
            days.Write(account, "me@example.com", day, ClaudeCodeSettledDay.Empty);
        }

        var usage = new StubUsage { [personal] = ("me@example.com", 1.25m) };
        var source = new ClaudeSpendSource(usage, store.Store, days, new FixedClock(Today));

        var report = await source.GetSpendAsync(
            new DateOnly(2026, 9, 14),
            new DateOnly(2026, 9, 20),
            TestContext.Current.CancellationToken);

        Assert.Equal([new DateOnly(2026, 9, 19), new DateOnly(2026, 9, 20)], usage.DaysAsked.Order());
        Assert.Equal(2, report.Entries.Count);
        Assert.Null(days.TryRead(account, "me@example.com", new DateOnly(2026, 9, 19)));
        Assert.Null(days.TryRead(account, "me@example.com", new DateOnly(2026, 9, 20)));
    }

    /// <summary>A refusal is not a figure. A settled day Anthropic would not report
    /// is a gap today and a fetch tomorrow, never an empty day on disk.</summary>
    [Fact]
    public async Task A_day_Anthropic_refused_is_not_remembered_as_empty()
    {
        using var store = new TemporaryClaudeStore();
        var personal = Configure(store, "me@example.com");
        var account = store.Store.Current.Accounts[0];

        var days = new RememberingDays();
        var usage = new StubUsage { [personal] = ("me@example.com", 1.25m), Refuses = true };
        var source = new ClaudeSpendSource(usage, store.Store, days, new FixedClock(Today));

        var day = new DateOnly(2026, 9, 10);
        var report = await source.GetSpendAsync(day, day, TestContext.Current.CancellationToken);

        Assert.Empty(report.Entries);
        Assert.Null(days.TryRead(account, "me@example.com", day));
    }

    private static string Configure(TemporaryClaudeStore store, string actor)
    {
        var id = store.Store.Current.Accounts[0].Id;
        store.Store.SetAdminApiKey(id, "sk-ant-admin01-personal");
        store.Store.SetActor(id, actor);
        return id;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>The day cache, in memory: what is pinned here is the adapter's use
    /// of the port, and the disk is the Claude project's own test.</summary>
    private sealed class RememberingDays : IClaudeCodeUsageCache
    {
        private readonly Dictionary<string, ClaudeCodeSettledDay> _entries = [];

        public ClaudeCodeSettledDay? TryRead(ClaudeAccount account, string actor, DateOnly day) =>
            _entries.GetValueOrDefault($"{account.Id}/{actor.ToLowerInvariant()}/{day:O}");

        public void Write(ClaudeAccount account, string actor, DateOnly day, ClaudeCodeSettledDay usage) =>
            _entries[$"{account.Id}/{actor.ToLowerInvariant()}/{day:O}"] = usage;

        public void Forget() => _entries.Clear();
    }

    private sealed class StubUsage : Dictionary<string, (string Actor, decimal Cost)>, IClaudeUsageClient
    {
        public List<string> AccountsAsked { get; } = [];

        public List<DateOnly> DaysAsked { get; } = [];

        public bool Refuses { get; init; }

        public Task<ClaudeUsageAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ClaudeUsageAvailability(true, "stub"));

        public Task<ClaudeUsageReport> GetMessageUsageAsync(ClaudeAccount account, ClaudeUsageWindow window, ClaudeUsageBucket bucket = ClaudeUsageBucket.Day, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ClaudeCostReport> GetCostAsync(ClaudeAccount account, ClaudeUsageWindow window, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ClaudeCodeReport> GetClaudeCodeUsageAsync(ClaudeAccount account, DateOnly date, CancellationToken cancellationToken = default)
        {
            AccountsAsked.Add(account.Id);
            DaysAsked.Add(date);

            if (Refuses) return Task.FromException<ClaudeCodeReport>(new ClaudeException("Not yet aggregated."));

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
