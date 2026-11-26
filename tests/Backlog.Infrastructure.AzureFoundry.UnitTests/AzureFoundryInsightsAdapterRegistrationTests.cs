using System.Reflection;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.AzureFoundry.Insights;
using Backlog.Modules.Insights.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.AzureFoundry.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddAzureFoundryInsightsAdapters()</c>: the
/// Azure Foundry spend port, a singleton, and one call to get it. Here rather
/// than in an infrastructure test project because the Azure Foundry adapter has
/// none; its client's tests are next door.
/// </summary>
public sealed class AzureFoundryInsightsAdapterRegistrationTests
{
    [Fact]
    public void The_azure_foundry_spend_port_is_a_singleton_answered_there()
    {
        var services = new ServiceCollection();
        services.AddAzureFoundryInsightsAdapters();

        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(IAzureFoundrySpendSource), descriptor.ServiceType);
        Assert.Equal(typeof(AzureFoundrySpendSource), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public void The_project_has_one_insights_registration_entry_point()
    {
        var entryPoints = typeof(AzureFoundryRegistration).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name.Contains("Insights", StringComparison.Ordinal)
                && method.GetParameters() is [{ ParameterType: var first }, ..]
                && first == typeof(IServiceCollection))
            .Select(method => method.Name)
            .ToList();

        Assert.Equal(["AddAzureFoundryInsightsAdapters"], entryPoints);
    }
}
