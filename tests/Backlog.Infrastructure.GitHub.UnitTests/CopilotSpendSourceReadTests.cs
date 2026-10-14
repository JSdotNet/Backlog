using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.GitHub.Dashboard;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The Copilot spend adapter records each read of GitHub's AI-credit report as the one
/// GitHub usage source: any month answering is a success, every month refusing a
/// failure with GitHub's own reason.
/// </summary>
public sealed class CopilotSpendSourceReadTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_read_GitHub_answered_is_recorded_as_a_success()
    {
        var reads = new RecordingReads();
        var source = new CopilotSpendSource(new StubBilling(), reads);

        await source.GetSpendAsync(September, September.AddMonths(1), Cancellation);

        Assert.Equal([("succeeded", UsageSource.GitHub, (string?)null)], reads.Calls);
    }

    [Fact]
    public async Task A_read_every_month_refused_is_recorded_as_a_failure_with_the_reason()
    {
        var reads = new RecordingReads();
        var source = new CopilotSpendSource(new StubBilling { Refusal = "GitHub answered 403 for this organization." }, reads);

        await Assert.ThrowsAsync<GitHubException>(() => source.GetSpendAsync(September, September, Cancellation));

        Assert.Equal([("failed", UsageSource.GitHub, (string?)"GitHub answered 403 for this organization.")], reads.Calls);
    }

    /// <summary>A month before metered billing started refuses as a matter of course;
    /// the trend still read.</summary>
    [Fact]
    public async Task One_month_refused_beside_one_that_answered_is_a_success()
    {
        var reads = new RecordingReads();
        var billing = new StubBilling { RefusedMonths = { 8 } };
        var source = new CopilotSpendSource(billing, reads);

        await source.GetSpendAsync(new DateOnly(2026, 8, 1), September, Cancellation);

        Assert.Equal("succeeded", Assert.Single(reads.Calls).Outcome);
    }

    [Fact]
    public async Task A_success_after_a_failure_is_recorded_after_it()
    {
        var reads = new RecordingReads();

        await Assert.ThrowsAsync<GitHubException>(() =>
            new CopilotSpendSource(new StubBilling { Refusal = "Not reachable." }, reads).GetSpendAsync(September, September, Cancellation));
        await new CopilotSpendSource(new StubBilling(), reads).GetSpendAsync(September, September, Cancellation);

        Assert.Equal(["failed", "succeeded"], reads.Calls.Select(call => call.Outcome));
    }

    [Fact]
    public async Task A_cancelled_read_records_nothing()
    {
        var reads = new RecordingReads();
        var source = new CopilotSpendSource(new StubBilling { Cancels = true }, reads);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetSpendAsync(September, September, Cancellation));

        Assert.Empty(reads.Calls);
    }

    [Fact]
    public async Task Without_a_recorder_a_read_still_works()
    {
        var source = new CopilotSpendSource(new StubBilling());

        var report = await source.GetSpendAsync(September, September, Cancellation);

        Assert.Single(report.Entries);
    }

    private sealed class RecordingReads : IUsageReadRecorder
    {
        public List<(string Outcome, UsageSource Source, string? Reason)> Calls { get; } = [];

        public void Succeeded(UsageSource source) => Calls.Add(("succeeded", source, null));

        public void Failed(UsageSource source, string reason) => Calls.Add(("failed", source, reason));
    }

    private sealed class StubBilling : IGitHubBillingClient
    {
        public string? Refusal { get; init; }

        public HashSet<int> RefusedMonths { get; } = [];

        public bool Cancels { get; init; }

        public Task<GitHubBillingAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubBillingAvailability(true, "stub", GitHubBillingScope.PersonalAccount));

        public Task<GitHubAiCreditUsage> GetAiCreditUsageAsync(int year, int month, int? day = null, CancellationToken cancellationToken = default)
        {
            if (Cancels) throw new OperationCanceledException();

            if (Refusal is not null || RefusedMonths.Contains(month))
            {
                throw new GitHubException(Refusal ?? "No metered billing that month.");
            }

            return Task.FromResult(new GitHubAiCreditUsage(
                [new GitHubAiCreditUsageItem("copilot", "ai-credits", "gpt-5", "credits", 0.01m, 100m, 1m, 0m, 0m, 100m, 1m)],
                GitHubBillingScope.PersonalAccount));
        }
    }
}
