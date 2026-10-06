using System.Reflection;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.GitHub.Dashboard;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddGitHubDashboardAdapters()</c>: the four
/// Dashboard ports GitHub answers, each a singleton, and one call to get them.
/// </summary>
public sealed class GitHubDashboardAdapterRegistrationTests
{
    [Fact]
    public void Every_github_backed_dashboard_port_is_a_singleton_answered_here()
    {
        var services = new ServiceCollection();
        services.AddGitHubDashboardAdapters();

        AssertSingleton<IRepositoryDirectory, SettingsRepositoryDirectory>(services);
        AssertSingleton<IActivitySource, GitHubActivitySource>(services);
        AssertSingleton<IActivityBaselineSource, GitHubActivityBaselineSource>(services);
        AssertSingleton<ICopilotSpendSource, CopilotSpendSource>(services);
        AssertSingleton<IDriftIssueSource, GitHubDriftIssueSource>(services);
        Assert.Equal(5, services.Count);
    }

    [Fact]
    public void The_project_has_one_dashboard_registration_entry_point()
    {
        var entryPoints = typeof(GitHubSettingsStore).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name.Contains("Dashboard", StringComparison.Ordinal)
                && method.GetParameters() is [{ ParameterType: var first }, ..]
                && first == typeof(IServiceCollection))
            .Select(method => method.Name)
            .ToList();

        Assert.Equal(["AddGitHubDashboardAdapters"], entryPoints);
    }

    private static void AssertSingleton<TPort, TAdapter>(IServiceCollection services)
    {
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(TPort));
        Assert.Equal(typeof(TAdapter), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
