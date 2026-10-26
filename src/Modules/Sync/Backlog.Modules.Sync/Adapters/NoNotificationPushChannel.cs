using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;

namespace Backlog.Modules.Sync.Adapters;

/// <summary>
/// The channel a host without Notification Hubs runs on — under Aspire, in a bare
/// <c>dotnet run</c> and in the endpoint tests: a registration answers "not registered" and a
/// send goes nowhere, so a phone learns of a notification on its next pull (local ADR 0038 §4).
/// Registered with <c>TryAdd</c>, so the Notification Hubs adapter wins wherever a host
/// configures one.
/// </summary>
public sealed class NoNotificationPushChannel : INotificationPushChannel
{
    public Task<bool> RegisterAsync(OwnerScope scope, string fcmToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task RemoveAsync(DeviceId device, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SendAsync(OwnerId owner, NotificationPushMessage message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
