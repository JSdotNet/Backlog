using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.RepositoryManagement.Extensions;

/// <summary>
/// The module's composition root. A host calls this once and gets
/// <see cref="IPullRequests"/>: one instance for the process, so every reader the host
/// composes shares its one cache (local ADR 0042 §3).
/// <para>
/// <see cref="IPullRequestSource"/>, <see cref="IRegisteredRepositories"/> and
/// <see cref="ISettledPullRequestStore"/> fall back to answers that read nothing, so a
/// host without GitHub still resolves the contract and it reads as not configured. A
/// host with GitHub replaces them after this call, with the GitHub adapters and the
/// on-disk memory.
/// </para>
/// </summary>
public static class RepositoryManagementModuleRegistration
{
    public static IServiceCollection AddRepositoryManagementModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPullRequestSource, NoPullRequestSource>();
        services.TryAddSingleton<IRegisteredRepositories, NoRegisteredRepositories>();
        services.TryAddSingleton<ISettledPullRequestStore, InMemorySettledPullRequests>();
        services.TryAddSingleton<IPullRequests>(sp => new PullRequests(
            sp.GetRequiredService<IPullRequestSource>(),
            sp.GetRequiredService<IRegisteredRepositories>(),
            sp.GetRequiredService<ISettledPullRequestStore>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<PullRequests>>()));

        return services;
    }
}
