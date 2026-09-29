using System.Reflection;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.Claude.Dashboard;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Claude.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddClaudeDashboardAdapters()</c>: the Claude
/// spend port, a singleton, and one call to get it.
/// </summary>
public sealed class ClaudeDashboardAdapterRegistrationTests
{
    [Fact]
    public void The_claude_spend_port_is_a_singleton_answered_here()
    {
        var services = new ServiceCollection();
        services.AddClaudeDashboardAdapters();

        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(IClaudeSpendSource), descriptor.ServiceType);
        Assert.Equal(typeof(ClaudeSpendSource), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void The_project_has_one_dashboard_registration_entry_point()
    {
        var entryPoints = typeof(ClaudeSettingsStore).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name.Contains("Dashboard", StringComparison.Ordinal)
                && method.GetParameters() is [{ ParameterType: var first }, ..]
                && first == typeof(IServiceCollection))
            .Select(method => method.Name)
            .ToList();

        Assert.Equal(["AddClaudeDashboardAdapters"], entryPoints);
    }
}
