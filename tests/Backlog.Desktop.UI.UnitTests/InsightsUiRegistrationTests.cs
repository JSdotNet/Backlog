using Backlog.Modules.Insights.Abstractions.Insights;
using Backlog.Modules.Insights.Abstractions.Services;
using Backlog.Modules.Insights.UI;
using Backlog.Modules.Insights.UI.Extensions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What stays with the pane once the provider adapters live beside their providers:
/// the machine directory, which wraps no provider at all, and the Ask AI pair, which
/// reads what one reader's pane is showing — and the Dashboard's two pages on the
/// settings screen, its usage reset and the working week, which both desktop heads
/// get through this one call.
/// </summary>
public sealed class InsightsUiRegistrationTests
{
    /// <summary>The shell's own pages, which win a collision, and the Inbox's.</summary>
    private static readonly string[] TakenPageIds = ["features", "ai", "storage", "accounts", "repositories", "devices", "inbox"];

    [Fact]
    public void The_pane_registers_the_ask_ai_pair_with_its_lifetimes_and_no_machine_directory_of_its_own()
    {
        var services = new ServiceCollection();
        services.AddInsightsUi();

        // Dev PCs owns the Machine (local ADR 0073 §7): the filter reads its registry,
        // which infrastructure registers, so the pane registers nothing for it.
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(Backlog.Modules.DevPc.Abstractions.IMachineRegistry));

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(InsightsScopeInView)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(IAiContentSource)).Lifetime);
        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(SettingsSection)));
        Assert.Equal(4, services.Count);
    }

    /// <summary>The Ask AI source is composed from the insights the container holds —
    /// the same scoped ones the parts inject — so its figures are the parts' figures.
    /// Here every insight answers "unavailable", and each says so in its own line.</summary>
    [Fact]
    public async Task The_ask_ai_source_reads_the_insights_the_container_holds()
    {
        var services = new ServiceCollection();
        services.AddUnavailableInsights("backlog");
        services.AddSingleton<IActivitySource>(new NoGitHub());
        services.AddInsightsAiContentSource();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var source = Assert.IsType<InsightsAiContentSource>(scope.ServiceProvider.GetRequiredService<IAiContentSource>());

        var content = await source.ComposeAsync(new AiContentRequest("anything", AiContentBudget.DefaultCharacters), TestContext.Current.CancellationToken);

        Assert.Contains($"Throughput: unavailable: {InsightsTestHost.UnavailableReason}", content.Body.Split('\n'));
        Assert.Contains($"Hours worked: unavailable: {InsightsTestHost.UnavailableReason}", content.Body.Split('\n'));
    }

    [Fact]
    public void The_pane_registers_its_settings_page_first_among_the_product_areas_behind_the_dashboard_switch()
    {
        var services = new ServiceCollection();
        services.AddInsightsUi();

        using var provider = services.BuildServiceProvider();
        var section = Assert.Single(provider.GetServices<SettingsSection>(), s => s.Id == "insights");
        Assert.Equal(typeof(InsightsSettings), section.Component);
        Assert.Equal("Insights", section.Title);
        Assert.DoesNotContain(section.Id, TakenPageIds);
        Assert.Equal(SettingsGroup.ProductAreas, section.Group);
        Assert.True(section.Order < InboxSettingsRegistration.Order);
        Assert.Null(section.FeatureKey);
    }

    /// <summary>The working week is read by the roadmap as well as the dashboard
    /// (ADR 0019), so its page is offered whether or not the dashboard is on, after
    /// the dashboard's own page.</summary>
    [Fact]
    public void The_pane_registers_the_working_week_page_with_no_switch()
    {
        var services = new ServiceCollection();
        services.AddInsightsUi();

        using var provider = services.BuildServiceProvider();
        var section = Assert.Single(provider.GetServices<SettingsSection>(), s => s.Id == "working-week");
        Assert.Equal(typeof(WorkingWeekSettings), section.Component);
        Assert.Equal("Working week", section.Title);
        Assert.DoesNotContain(section.Id, TakenPageIds);
        Assert.Equal(SettingsGroup.ProductAreas, section.Group);
        Assert.True(section.Order > InsightsSettingsRegistration.Order);
        Assert.True(section.Order < InboxSettingsRegistration.Order);
        Assert.Null(section.FeatureKey);
    }

    private sealed class NoGitHub : IActivitySource
    {
        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightAvailability.Unavailable(InsightsTestHost.UnavailableReason));

        public Task<ActivityReport> GetActivityAsync(
            IReadOnlyList<InsightsRepository> repositories,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ActivityReport.Empty);
    }
}
