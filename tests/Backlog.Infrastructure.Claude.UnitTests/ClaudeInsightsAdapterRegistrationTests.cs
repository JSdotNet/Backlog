using System.Reflection;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.Claude.Insights;
using Backlog.Modules.Insights.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Claude.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddClaudeInsightsAdapters()</c>: the Claude
/// spend port, a singleton, and one call to get it.
/// </summary>
public sealed class ClaudeInsightsAdapterRegistrationTests
{
    [Fact]
    public void The_claude_spend_port_is_a_singleton_answered_here()
    {
        var services = new ServiceCollection();
        services.AddClaudeInsightsAdapters();

        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(IClaudeSpendSource), descriptor.ServiceType);
        Assert.Equal(typeof(ClaudeSpendSource), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void The_project_has_one_insights_registration_entry_point()
    {
        var entryPoints = typeof(ClaudeSettingsStore).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name.Contains("Insights", StringComparison.Ordinal)
                && method.GetParameters() is [{ ParameterType: var first }, ..]
                && first == typeof(IServiceCollection))
            .Select(method => method.Name)
            .ToList();

        Assert.Equal(["AddClaudeInsightsAdapters"], entryPoints);
    }
}
