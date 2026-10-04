using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Backlog.Modules.Dashboard.UI.Adapters;
using Backlog.Modules.Dashboard.UI.Extensions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What stays with the pane once the provider adapters live beside their providers:
/// the machine directory, which wraps no provider at all, and the Ask AI pair, which
/// reads what one reader's pane is showing — and the Dashboard's page on the
/// settings screen, which both desktop heads get through this one call.
/// </summary>
public sealed class DashboardUiRegistrationTests
{
    /// <summary>The shell's own pages, which win a collision, and the Inbox's.</summary>
    private static readonly string[] TakenPageIds = ["features", "ai", "storage", "accounts", "repositories", "devices", "inbox"];

    [Fact]
    public void The_pane_registers_the_machine_directory_and_the_ask_ai_pair_with_their_lifetimes()
    {
        var services = new ServiceCollection();
        services.AddDashboardUi();

        var machines = Assert.Single(services, d => d.ServiceType == typeof(IMachineDirectory));
        Assert.Equal(typeof(DeviceMachineDirectory), machines.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, machines.Lifetime);

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(DashboardScopeInView)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, d => d.ServiceType == typeof(IAiContentSource)).Lifetime);
        Assert.Single(services, d => d.ServiceType == typeof(SettingsSection));
        Assert.Equal(4, services.Count);
    }

    [Fact]
    public void The_pane_registers_its_settings_page_after_the_inbox_behind_the_dashboard_switch()
    {
        var services = new ServiceCollection();
        services.AddDashboardUi();

        using var provider = services.BuildServiceProvider();
        var section = Assert.Single(provider.GetServices<SettingsSection>());
        Assert.Equal(typeof(DashboardSettings), section.Component);
        Assert.Equal("Dashboard", section.Title);
        Assert.DoesNotContain(section.Id, TakenPageIds);
        Assert.True(section.Order > InboxSettingsRegistration.Order);
        Assert.Equal(DashboardFeatures.Dashboard, section.FeatureKey);
    }
}
