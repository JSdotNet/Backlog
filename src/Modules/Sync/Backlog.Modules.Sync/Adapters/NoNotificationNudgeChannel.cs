using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// The channel a host without Web PubSub runs on — under Aspire, in a bare <c>dotnet run</c>
/// and in the endpoint tests: negotiate hands out no URL and a publish goes nowhere, so every
/// desktop stays on its two-minute poll (local ADR 0038 §4). Registered with <c>TryAdd</c>, so
/// the Web PubSub adapter wins wherever a host configures one.
/// </summary>
public sealed class NoNotificationNudgeChannel : INotificationNudgeChannel
{
    public Task<Uri?> GetClientAccessUrlAsync(OwnerScope scope, CancellationToken cancellationToken = default) =>
        Task.FromResult<Uri?>(null);

    public Task PublishAsync(OwnerId owner, string kind, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
