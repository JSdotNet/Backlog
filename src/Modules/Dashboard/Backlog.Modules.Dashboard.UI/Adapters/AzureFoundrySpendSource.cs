using Backlog.Infrastructure.AzureFoundry;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.UI.Adapters;

/// <summary>
/// Answers <see cref="IAzureFoundrySpendSource"/> from Azure Cost Management's
/// daily actual cost on the Foundry resource.
/// </summary>
/// <remarks>
/// <para>
/// Azure bills a meter, not a model: for a model deployment that is the model
/// name with the direction of its tokens — input, output, cached — so one model
/// arrives as two or three rows. They are passed through as Azure names them
/// rather than folded back into one, because the fold would be a guess at
/// Azure's naming and a wrong guess merges two models.
/// </para>
/// <para>
/// No tokens. Cost Management does report a usage quantity per meter, but in the
/// meter's own unit — thousands of tokens on one, millions on another — and a
/// column headed Tokens that mixes those is worse than an em dash.
/// </para>
/// <para>
/// Not an estimate: Azure's actual cost is what the invoice carries. The running
/// month is short its last settled day or two, which the parts already say about
/// the last bucket.
/// </para>
/// </remarks>
internal sealed class AzureFoundrySpendSource(IAzureFoundryCostClient cost) : IAzureFoundrySpendSource
{
    public async Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var available = await cost.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);

        return available.IsAvailable
            ? InsightAvailability.Available
            : InsightAvailability.Unavailable(available.Reason);
    }

    public async Task<SpendReport> GetSpendAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var report = await cost.GetDailyCostAsync(from, to, cancellationToken).ConfigureAwait(false);

        return new SpendReport(
            [.. report.Lines.Select(line => new SpendEntry(
                line.Date,
                line.Meter,
                Tokens: null,
                new DashboardMoney(line.Cost, line.Currency)))],
            Allowance: null,
            IsEstimate: false);
    }
}
