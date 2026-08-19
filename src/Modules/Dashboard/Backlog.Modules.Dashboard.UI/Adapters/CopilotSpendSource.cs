using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.UI.Adapters;

/// <summary>
/// Answers <see cref="ICopilotSpendSource"/> from GitHub's AI-credit usage report.
/// </summary>
/// <remarks>
/// <para>
/// Copilot has been metered since 1 June 2026: premium request units became AI
/// credits, billed on token consumption at each model's published rates. The report
/// returns both the credits and the money, so this adapter reads two figures rather
/// than multiplying one by a price it would have to hold.
/// </para>
/// <para>
/// The endpoint buckets by calendar month, or by one day of one month. This adapter
/// asks per month and dates each entry to the first of it, which is what the trend
/// draws on — asking day by day would be thirty times the calls for a resolution no
/// part of this dashboard renders.
/// </para>
/// <para>
/// <see cref="SpendReport.Allowance"/> carries the discount GitHub applied, which is
/// the part of the month's consumption the plan already covered. The plan's total
/// included credit is not in the response, so it is not invented: what can be said
/// is how much was consumed and how much of that was charged.
/// </para>
/// </remarks>
internal sealed class CopilotSpendSource(IGitHubBillingClient billing) : ICopilotSpendSource
{
    /// <summary>The longest window this adapter will walk, in months. The trend asks
    /// for seven; anything past a couple of years is a caller mistake.</summary>
    private const int MaxMonths = 24;

    public async Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        var available = await billing.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);

        return available.IsAvailable
            ? InsightAvailability.Available
            : InsightAvailability.Unavailable(available.Reason);
    }

    public async Task<SpendReport> GetSpendAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<SpendEntry>();
        var covered = 0m;
        var read = false;

        foreach (var month in Months(from, to))
        {
            GitHubAiCreditUsage usage;
            try
            {
                usage = await billing
                    .GetAiCreditUsageAsync(month.Year, month.Month, day: null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (GitHubException)
            {
                // A month GitHub will not report — before the account existed, or
                // before metered billing started — is a gap in the trend rather
                // than a failure of it. The first month that does answer proves the
                // source works.
                continue;
            }
            catch (GitHubNotConfiguredException)
            {
                continue;
            }

            read = true;

            entries.AddRange(usage.Items
                .Where(item => item.NetAmount != 0m || item.NetQuantity != 0m || item.GrossAmount != 0m)
                .Select(item => new SpendEntry(
                    month,
                    item.Model ?? item.Sku,
                    // Credits are the unit here, not tokens. Reporting them in the
                    // token column would put two different units under one heading,
                    // so the quantity is left unreported and the money speaks.
                    Tokens: null,
                    new DashboardMoney(item.NetAmount, GitHubBillingClient.Currency))));

            covered += usage.Items.Sum(item => item.DiscountAmount);
        }

        if (!read)
        {
            throw new GitHubException(
                "GitHub would not report AI-credit usage for any month in this window. A Copilot seat paid for by "
                + "an organization is billed to that organization, and reading one person's usage there needs "
                + "organization admin rights.");
        }

        return new SpendReport(
            entries,
            covered == 0m ? null : new DashboardMoney(covered, GitHubBillingClient.Currency),
            IsEstimate: false);
    }

    /// <summary>The first of each calendar month the window touches.</summary>
    private static IReadOnlyList<DateOnly> Months(DateOnly from, DateOnly to)
    {
        var months = new List<DateOnly>();
        var cursor = new DateOnly(from.Year, from.Month, 1);
        var last = new DateOnly(to.Year, to.Month, 1);

        while (cursor <= last && months.Count < MaxMonths)
        {
            months.Add(cursor);
            cursor = cursor.AddMonths(1);
        }

        return months;
    }
}
