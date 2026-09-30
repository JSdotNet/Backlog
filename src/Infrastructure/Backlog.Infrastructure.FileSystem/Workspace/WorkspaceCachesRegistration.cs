using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sessions.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The per-machine caches the dashboard's GitHub clients and the session readers
/// keep, and the session records kept beside them, registered in one place both
/// desktop heads reach through their shared composition.
/// </summary>
/// <remarks>
/// Here rather than beside the ports they answer: the GitHub adapter may never see
/// this project (see <c>ModuleBoundaryTests</c>) and the Sessions adapter does not,
/// and every folder below is one <see cref="WorkspaceSettingsStore"/> names. A host
/// registers that store before calling this.
/// </remarks>
public static class WorkspaceCachesRegistration
{
    /// <summary>
    /// Registers the six stores, each a singleton over its folder, read per call so
    /// a folder that moves is followed. Every folder is beside the per-user settings
    /// and never under the backlog root: ADR 0005 syncs the workspace, and a
    /// per-machine cache travelling to another device is exactly the hazard.
    /// </summary>
    public static IServiceCollection AddWorkspaceCaches(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The detail cache is what keeps a dashboard read from re-fetching every
        // pull request it already read, and the listing cache what keeps it from
        // re-walking the pages that found them. Same folder, so forgetting a
        // repository is one gesture that drops both.
        services.AddSingleton<IPullRequestDetailCache>(sp => new PullRequestDetailCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().ActivityCacheDirectory));
        services.AddSingleton<IActivityListingCache>(sp => new ActivityListingCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().ActivityCacheDirectory));
        // Settled Copilot months, kept beside settled Claude days under the spend
        // cache, so a seven-month trend costs the running month and nothing else.
        services.AddSingleton<IAiCreditUsageCache>(sp => new AiCreditUsageCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().SpendCacheDirectory));

        // What a transcript's parsed runs are kept in, so an activity read parses
        // only the transcripts that have changed, and the other pass over the same
        // transcripts: the folder, branch and turn count the session list reads.
        // Same folder, forgotten together.
        services.AddSingleton<IAgentActivityCache>(sp => new AgentActivityCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().SessionActivityCacheDirectory));
        services.AddSingleton<ITranscriptFactsCache>(sp => new TranscriptFactsCache(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().SessionActivityCacheDirectory));
        // This machine's own session records, kept after the transcripts are gone.
        // Not a cache - often the only copy of a session - so a folder of its own
        // that clearing the cache never touches.
        services.AddSingleton<IAgentSessionRecordStore>(sp => new AgentSessionRecordStore(
            () => sp.GetRequiredService<WorkspaceSettingsStore>().SessionRecordsDirectory));

        return services;
    }
}
