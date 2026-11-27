using Backlog.Modules.Repositories.Abstractions.Services;
using Backlog.Modules.Repositories.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Repositories.Extensions;

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
public static class RepositoriesModuleRegistration
{
    public static IServiceCollection AddRepositoriesModule(this IServiceCollection services)
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

        // The registry's named environments (local ADR 0073 §3): the rules here, over a
        // store a host with a per-device file replaces, and a command runner a host that
        // can start a process replaces.
        services.TryAddSingleton<IEnvironmentStore, InMemoryEnvironmentStore>();
        services.TryAddSingleton<IEnvironmentCommandRunner, NoEnvironmentCommandRunner>();
        services.TryAddSingleton<IEnvironments>(sp => new Environments(sp.GetRequiredService<IEnvironmentStore>()));

        return services;
    }
}
