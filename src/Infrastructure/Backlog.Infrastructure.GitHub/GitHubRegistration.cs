using Backlog.Modules.Sync.Abstractions.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// The GitHub adapter — the credential route, the transport, and every client
/// over it — registered in one place both desktop heads reach through their
/// shared composition, so the two cannot drift.
/// </summary>
public static class GitHubRegistration
{
    /// <summary>
    /// Registers the GitHub clients, each a singleton.
    /// <para>
    /// A host registers <see cref="GitHubSettingsStore"/> first, because half of a
    /// repository's configuration follows the workspace root and this project may
    /// not see the store that knows it. The file caches the activity and billing
    /// clients read — <see cref="IPullRequestDetailCache"/>,
    /// <see cref="IActivityListingCache"/> and <see cref="IAiCreditUsageCache"/> —
    /// are the host's too, for the same reason: their folders sit beside the
    /// per-user settings, which is the file-system adapter's to say.
    /// </para>
    /// <para>
    /// Which credential a call leaves with is decided per call, against the settings
    /// as they are at that moment: a repository bound to an account goes out as that
    /// account, and never as whoever <c>gh</c> happens to be switched to. The gh CLI
    /// is a source of credentials as well as a way of sending, so the account source
    /// is registered once and shared — its token cache and its account list are the
    /// things "Check the connection" invalidates. The token route asks the factory
    /// for its client on every send, and
    /// <see cref="GitHubHttpRegistration.AddGitHubHttpClient"/> puts GitHub's own
    /// pipeline on that client.
    /// </para>
    /// </summary>
    public static IServiceCollection AddGitHub(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IGhCliAccountSource>(_ => new GhCliAccountSource());
        services.AddSingleton<IGitHubCredentialResolver>(sp => new GitHubCredentialResolver(
            sp.GetRequiredService<GitHubSettingsStore>(),
            sp.GetRequiredService<IGhCliAccountSource>()));
        services.AddGitHubHttpClient();
        services.AddSingleton(sp => new ResolvingGitHubTransport(
            sp.GetRequiredService<GitHubSettingsStore>(),
            credentials: sp.GetRequiredService<IGitHubCredentialResolver>(),
            accounts: sp.GetRequiredService<IGhCliAccountSource>(),
            httpClients: sp.GetRequiredService<IHttpClientFactory>()));
        services.AddSingleton<IGitHubConnectionProbe>(sp => sp.GetRequiredService<ResolvingGitHubTransport>());
        services.AddSingleton<IGitHubAccountProbe>(sp => sp.GetRequiredService<ResolvingGitHubTransport>());

        // Devbook read from a repository branch, for a repository nobody has
        // cloned: one listing per commit, one blob per file somebody opens. The
        // disk half is the snapshot cache the host registers beside its Devbook.
        services.AddSingleton<IGitHubBranchCatalog>(sp => new GitHubBranchCatalog(
            sp.GetRequiredService<ResolvingGitHubTransport>()));
        services.AddSingleton<IGitHubTreeClient>(sp => new GitHubTreeClient(
            sp.GetRequiredService<ResolvingGitHubTransport>()));

        services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        services.AddSingleton<IGitFileHistoryService, GitFileHistoryService>();
        services.AddSingleton<IGitHubClient>(sp => new GitHubClient(sp.GetRequiredService<ResolvingGitHubTransport>()));
        services.AddSingleton<ICopilotUsageClient>(sp => new CopilotUsageClient(sp.GetRequiredService<ResolvingGitHubTransport>()));

        // The three clients the dashboard reads. Identity is shared by the other
        // two: the activity client filters to the signed-in author, and the billing
        // client chooses between the user and organization endpoints by the same
        // login, so neither needs a setting for it.
        services.AddSingleton<IGitHubIdentityClient>(sp => new GitHubIdentityClient(sp.GetRequiredService<ResolvingGitHubTransport>()));
        services.AddSingleton<IGitHubActivityClient>(sp => new GitHubActivityClient(
            sp.GetRequiredService<ResolvingGitHubTransport>(),
            sp.GetRequiredService<IPullRequestDetailCache>(),
            sp.GetRequiredService<IActivityListingCache>()));
        // Counts only, over the search API, for the stretches of history the
        // detailed client is too expensive to walk.
        services.AddSingleton<IGitHubActivityBaselineClient>(sp => new GitHubActivityBaselineClient(
            sp.GetRequiredService<ResolvingGitHubTransport>()));
        services.AddSingleton<IGitHubBillingClient>(sp => new GitHubBillingClient(
            sp.GetRequiredService<ResolvingGitHubTransport>(),
            sp.GetRequiredService<IGitHubIdentityClient>(),
            sp.GetRequiredService<GitHubSettingsStore>(),
            sp.GetRequiredService<IAiCreditUsageCache>()));

        services.AddSingleton<GitHubIntegration>();

        // The registry and the account identities ride the task feed (local ADR
        // 0021). The sync client takes this port as optional, so a head that never
        // calls AddGitHub — the phone — composes without it and skips both kinds.
        services.AddSingleton<IGitHubSettingsReplication>(sp => new GitHubSettingsReplication(
            sp.GetRequiredService<GitHubSettingsStore>()));

        return services;
    }
}
