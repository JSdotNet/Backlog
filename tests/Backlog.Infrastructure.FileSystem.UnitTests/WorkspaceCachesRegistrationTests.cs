using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sessions.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What a host gets for calling <c>AddWorkspaceCaches()</c>: the per-machine
/// caches the dashboard and the session readers keep, and the session records
/// kept beside them, each a singleton over the folder the workspace store names
/// for it — never under the backlog root, which may travel to another device.
/// </summary>
public sealed class WorkspaceCachesRegistrationTests
{
    [Theory]
    [InlineData(typeof(IPullRequestDetailCache), typeof(PullRequestDetailCache))]
    [InlineData(typeof(IActivityListingCache), typeof(ActivityListingCache))]
    [InlineData(typeof(IAiCreditUsageCache), typeof(AiCreditUsageCache))]
    [InlineData(typeof(IAgentActivityCache), typeof(AgentActivityCache))]
    [InlineData(typeof(ITranscriptFactsCache), typeof(TranscriptFactsCache))]
    [InlineData(typeof(IAgentSessionRecordStore), typeof(AgentSessionRecordStore))]
    public void Every_cache_is_a_singleton_answered_from_the_workspace_store(Type port, Type implementation)
    {
        var appData = Path.Combine(Path.GetTempPath(), "backlog-workspace-caches", Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton(new WorkspaceSettingsStore(appData, Path.Combine(appData, "settings.json"), _ => null));
        services.AddWorkspaceCaches();

        var descriptor = Assert.Single(services, d => d.ServiceType == port);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType(implementation, provider.GetRequiredService(port));
    }
}
