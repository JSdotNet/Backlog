using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.GitHub.RepositoryManagement;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Repository Management's contract composed the way the hosts compose it (local ADR
/// 0042): the real module, over the GitHub source and registry, over the
/// <see cref="GitHubIntegration"/> a test already holds — so what that test's client
/// says GitHub holds is what every reader of the contract reads, through the one cache.
/// </summary>
internal static class RepositoryManagementChain
{
    /// <param name="gitHub">The adapter whose client and settings the chain reads.</param>
    /// <param name="clock">The cache's clock; the system clock when omitted.</param>
    /// <param name="settled">The settled-fact memory; the module's in-memory one when
    /// omitted.</param>
    public static IPullRequests For(GitHubIntegration gitHub, TimeProvider? clock = null, ISettledPullRequestStore? settled = null)
    {
        ArgumentNullException.ThrowIfNull(gitHub);

        var services = new ServiceCollection();
        services.AddSingleton(gitHub);
        services.AddSingleton(gitHub.Settings);
        if (clock is not null) services.AddSingleton(clock);
        if (settled is not null) services.AddSingleton(settled);
        services.AddRepositoryManagementModule();
        services.AddRepositoryManagementGitHubAdapters();

        return services.BuildServiceProvider().GetRequiredService<IPullRequests>();
    }
}
