using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What a shell test needs in the container for the dashboard surface to render.
/// </summary>
/// <remarks>
/// <para>
/// The doubles answer "unavailable", which is not a cop-out: it is the state the
/// dashboard is actually in on a machine with no Anthropic admin key and no GitHub
/// sign-in, which is every machine running these tests. A shell test asking whether
/// the takeover replaced the panes should not also be deciding what a productivity
/// score is.
/// </para>
/// <para>
/// It also makes these tests prove something worth proving: the surface renders,
/// the filter renders, and every part explains itself, with no provider reachable
/// at all. A pane that only worked with data behind it would fail here.
/// </para>
/// </remarks>
internal static class DashboardTestHost
{
    internal const string UnavailableReason = "No provider is configured in this test.";

    internal static IServiceCollection AddUnavailableDashboard(
        this IServiceCollection services,
        params string[] repositoryAliases)
    {
        services.AddSingleton<IRepositoryDirectory>(new FixedRepositoryDirectory(repositoryAliases));
        services.AddSingleton<IProductivityInsights, UnavailableProductivityInsights>();
        services.AddSingleton<ICostInsights, UnavailableCostInsights>();

        return services;
    }

    private sealed class FixedRepositoryDirectory(IReadOnlyList<string> aliases) : IRepositoryDirectory
    {
        public IReadOnlyList<DashboardRepository> Repositories { get; } =
            [.. aliases.Select(alias => new DashboardRepository(alias, $"JSdotNet/{alias}"))];
    }

    private sealed class UnavailableProductivityInsights : IProductivityInsights
    {
        public Task<InsightResult<ProductivityHeadline>> GetHeadlineAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityHeadline>.Unavailable(UnavailableReason));

        public Task<InsightResult<ProductivityScoreInsight>> GetScoreAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityScoreInsight>.Unavailable(UnavailableReason));

        public Task<InsightResult<ProductivityTrend>> GetTrendAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ProductivityTrend>.Unavailable(UnavailableReason));

        public Task<InsightResult<ReworkInsight>> GetReworkAsync(
            DashboardScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<ReworkInsight>.Unavailable(UnavailableReason));

        public void Invalidate(DashboardScope scope)
        {
        }
    }

    private sealed class UnavailableCostInsights : ICostInsights
    {
        public Task<InsightResult<SpendThisMonthInsight>> GetThisMonthAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendThisMonthInsight>.Unavailable(UnavailableReason));

        public Task<InsightResult<SpendTrendInsight>> GetTrendAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendTrendInsight>.Unavailable(UnavailableReason));

        public Task<InsightResult<SpendByModelInsight>> GetByModelAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightResult<SpendByModelInsight>.Unavailable(UnavailableReason));

        public void Invalidate()
        {
        }
    }
}
