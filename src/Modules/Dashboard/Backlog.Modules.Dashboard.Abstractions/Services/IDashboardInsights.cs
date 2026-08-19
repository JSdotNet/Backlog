using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>
/// What the productivity half of the dashboard asks for.
/// <para>
/// Four methods rather than one that returns everything, and that is the
/// independent-parts rule expressed in the contract. A single call would make the
/// slowest source decide when the fastest part renders, and would make one
/// unconfigured provider able to fail the whole surface. Each part awaits its own
/// method, shows its own status, and refreshes on its own.
/// </para>
/// </summary>
public interface IProductivityInsights
{
    Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);

    Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);

    Task<InsightResult<ProductivityTrend>> GetTrendAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);

    Task<InsightResult<ReworkInsight>> GetReworkAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>Drops whatever was cached for this scope, so the next call goes
    /// back to the provider. What a refresh button does.</summary>
    void Invalidate(DashboardScope scope);
}

/// <summary>
/// What the cost half of the dashboard asks for.
/// <para>
/// No <see cref="DashboardScope"/> anywhere, on purpose. Neither provider reports
/// spend per repository — Anthropic groups its Claude Code report by actor and
/// model, GitHub groups AI credits by product, SKU and model — so a method that
/// took a scope would accept a narrowing it cannot honour. Leaving it out of the
/// signature is how the constraint reaches the screen instead of being lost in a
/// parameter that gets quietly ignored.
/// </para>
/// </summary>
public interface ICostInsights
{
    Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(CancellationToken cancellationToken = default);

    Task<InsightResult<SpendTrendInsight>> GetTrendAsync(CancellationToken cancellationToken = default);

    Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops whatever was cached, so the next call goes back to the providers.</summary>
    void Invalidate();
}
