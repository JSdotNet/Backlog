using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Backlog.Modules.Dashboard.UI.Adapters;
using Backlog.Modules.Dashboard.UI.Extensions;
using Backlog.SharedKernel.Ai;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What stays with the pane once the provider adapters live beside their providers:
/// the machine directory, which wraps no provider at all, and the Ask AI pair, which
/// reads what one reader's pane is showing.
/// </summary>
public sealed class DashboardUiRegistrationTests
{
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
        Assert.Equal(3, services.Count);
    }
}
