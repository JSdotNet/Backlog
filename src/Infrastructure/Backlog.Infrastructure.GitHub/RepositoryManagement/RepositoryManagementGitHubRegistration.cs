using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Infrastructure.GitHub.RepositoryManagement;

/// <summary>
/// Puts GitHub behind Repository Management's pull-request contract (local ADR 0042):
/// the pull-request source and the registry, in place of the fallbacks
/// <c>AddRepositoryManagementModule()</c> registers.
/// </summary>
/// <remarks>
/// Separate from the module's own registration for the reason the dashboard's adapters
/// are: the module brings the cache and the commands, and which provider is behind
/// them is the host's choice. A host calls this after <c>AddGitHub()</c> and
/// <c>AddRepositoryManagementModule()</c>; the adapters hold the
/// <see cref="GitHubIntegration"/> and <see cref="GitHubSettingsStore"/> that call
/// registered, and construct neither.
/// </remarks>
public static class RepositoryManagementGitHubRegistration
{
    public static IServiceCollection AddRepositoryManagementGitHubAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Replace(ServiceDescriptor.Singleton<IPullRequestSource>(sp =>
            new GitHubPullRequestSource(sp.GetRequiredService<GitHubIntegration>())));
        services.Replace(ServiceDescriptor.Singleton<IRegisteredRepositories>(sp =>
            new GitHubRegisteredRepositories(sp.GetRequiredService<GitHubSettingsStore>())));

        return services;
    }
}
