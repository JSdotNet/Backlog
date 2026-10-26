using System.Text.Json;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.PushNotifications;
using Backlog.Modules.Sync.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The Android pushes run inside the owner desktop's push request, after the write landed, so
/// a hub that is slow without failing must not hold that request: once the batch's send budget
/// is spent, the rest of the batch is not sent and the push still answers what it wrote.
/// </summary>
public class PushNotificationsSendBudgetTests
{
    private static readonly OwnerScope Desktop = new(
        new OwnerId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
        new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_slow_hub_stops_being_asked_once_the_batch_budget_is_spent()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero));
        var hub = new SlowPushChannel(clock, TimeSpan.FromSeconds(10));
        var handler = new PushNotificationsCommandHandler(
            new InMemoryNotificationReplica(),
            new NoNotificationNudgeChannel(),
            hub,
            clock,
            NullLogger<PushNotificationsCommandHandler>.Instance);

        var result = await handler.Handle(
            new PushNotificationsCommand(Desktop, [.. Enumerable.Range(0, 5).Select(_ => Published())]),
            Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.Accepted);

        // Ten seconds a call against a fifteen-second budget: the second call spends it.
        Assert.Equal(2, hub.Sent);
    }

    private static NotificationChange Published() =>
        new(
            Guid.CreateVersion7(),
            Guid.Parse("7f3a0000-0000-0000-0000-00000000000a"),
            NotificationStates.Open,
            new DateTimeOffset(2026, 10, 26, 8, 59, 0, TimeSpan.Zero),
            JsonSerializer.SerializeToElement(new { title = "Run the migration?" }));

    /// <summary>A hub that answers every send, ten seconds late.</summary>
    private sealed class SlowPushChannel(FakeTimeProvider clock, TimeSpan latency) : INotificationPushChannel
    {
        public int Sent { get; private set; }

        public Task<bool> RegisterAsync(OwnerScope scope, string fcmToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task RemoveAsync(DeviceId device, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendAsync(OwnerId owner, NotificationPushMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            clock.Advance(latency);
            Sent++;
            return Task.CompletedTask;
        }
    }
}
