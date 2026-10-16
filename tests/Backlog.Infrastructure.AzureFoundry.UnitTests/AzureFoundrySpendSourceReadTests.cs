using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.AzureFoundry.Dashboard;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.AzureFoundry.UnitTests;

/// <summary>
/// The Azure Foundry spend adapter records each read of Azure Cost Management as the one
/// Azure Foundry usage source: Azure answering is a success, Azure refusing a failure
/// with Azure's own reason.
/// </summary>
public sealed class AzureFoundrySpendSourceReadTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_read_Azure_answered_is_recorded_as_a_success()
    {
        var reads = new RecordingReads();
        var source = new AzureFoundrySpendSource(new StubCost(), reads);

        await source.GetSpendAsync(September, September.AddDays(29), Cancellation);

        Assert.Equal([("succeeded", UsageSource.AzureFoundry, (string?)null)], reads.Calls);
    }

    /// <summary>A resource that cost nothing in the window still answered.</summary>
    [Fact]
    public async Task A_read_with_no_cost_lines_is_still_a_success()
    {
        var reads = new RecordingReads();
        var source = new AzureFoundrySpendSource(new StubCost { Empty = true }, reads);

        var report = await source.GetSpendAsync(September, September, Cancellation);

        Assert.Empty(report.Entries);
        Assert.Equal("succeeded", Assert.Single(reads.Calls).Outcome);
    }

    [Fact]
    public async Task A_read_Azure_refused_is_recorded_as_a_failure_with_the_reason()
    {
        var reads = new RecordingReads();
        var source = new AzureFoundrySpendSource(
            new StubCost { Refusal = "Azure Cost Management answered 403 Forbidden for this scope." },
            reads);

        await Assert.ThrowsAsync<AzureFoundryException>(() => source.GetSpendAsync(September, September, Cancellation));

        Assert.Equal(
            [("failed", UsageSource.AzureFoundry, (string?)"Azure Cost Management answered 403 Forbidden for this scope.")],
            reads.Calls);
    }

    [Fact]
    public async Task A_success_after_a_failure_is_recorded_after_it()
    {
        var reads = new RecordingReads();

        await Assert.ThrowsAsync<AzureFoundryException>(() =>
            new AzureFoundrySpendSource(new StubCost { Refusal = "Not reachable." }, reads).GetSpendAsync(September, September, Cancellation));
        await new AzureFoundrySpendSource(new StubCost(), reads).GetSpendAsync(September, September, Cancellation);

        Assert.Equal(["failed", "succeeded"], reads.Calls.Select(call => call.Outcome));
    }

    [Fact]
    public async Task A_cancelled_read_records_nothing()
    {
        var reads = new RecordingReads();
        var source = new AzureFoundrySpendSource(new StubCost { Cancels = true }, reads);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetSpendAsync(September, September, Cancellation));

        Assert.Empty(reads.Calls);
    }

    [Fact]
    public async Task Without_a_recorder_a_read_still_works()
    {
        var source = new AzureFoundrySpendSource(new StubCost());

        var report = await source.GetSpendAsync(September, September, Cancellation);

        Assert.Single(report.Entries);
    }

    /// <summary>The hosts register one recorder; the registered adapter is handed it.</summary>
    [Fact]
    public async Task The_registered_adapter_records_through_the_hosts_recorder()
    {
        var reads = new RecordingReads();
        var services = new ServiceCollection();
        services.AddSingleton<IAzureFoundryCostClient>(new StubCost());
        services.AddSingleton<IUsageReadRecorder>(reads);
        services.AddAzureFoundryDashboardAdapters();
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IAzureFoundrySpendSource>().GetSpendAsync(September, September, Cancellation);

        Assert.Equal("succeeded", Assert.Single(reads.Calls).Outcome);
    }

    private sealed class RecordingReads : IUsageReadRecorder
    {
        public List<(string Outcome, UsageSource Source, string? Reason)> Calls { get; } = [];

        public void Succeeded(UsageSource source) => Calls.Add(("succeeded", source, null));

        public void Failed(UsageSource source, string reason) => Calls.Add(("failed", source, reason));
    }

    private sealed class StubCost : IAzureFoundryCostClient
    {
        public string? Refusal { get; init; }

        public bool Empty { get; init; }

        public bool Cancels { get; init; }

        public Task<AzureFoundryCostAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AzureFoundryCostAvailability(true, "stub"));

        public Task<AzureFoundryCostReport> GetDailyCostAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            if (Cancels) throw new OperationCanceledException();

            if (Refusal is not null) throw new AzureFoundryException(Refusal);

            return Task.FromResult(Empty
                ? AzureFoundryCostReport.Empty
                : new AzureFoundryCostReport([new AzureFoundryCostLine(from, "gpt-5.4 Input Tokens", 1.25m, "EUR")]));
        }
    }
}
