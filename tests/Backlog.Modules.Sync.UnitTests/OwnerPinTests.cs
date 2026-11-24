using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.Modules.Sync.Features.RegisterFirstDevice;
using Backlog.Modules.Sync.Services;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// Which owner a signed-in enrolment joins, and the refusal that keeps an unpinned
/// deployed service from orphaning the owner whose devices it already holds (local
/// ADR 0058 §1): with no <c>Owner:OwnerId</c>, the derived owner is a new, empty one,
/// and a recovery under it would leave every existing device out of reach.
/// </summary>
public class OwnerPinTests
{
    private const long GitHubUser = 7655166;
    private static readonly OwnerId Pinned = new(Guid.Parse("01a0a754-2903-7ecb-8f5b-dcb92b9a3010"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void A_pinned_owner_id_is_the_owner_ahead_of_the_derived_one()
    {
        Assert.Equal(Pinned, new OwnerEnrolment(GitHubUser, Pinned.Value).Owner);
    }

    [Fact]
    public void Without_a_pin_the_owner_is_derived_from_the_GitHub_user_the_same_on_every_start()
    {
        var derived = new OwnerEnrolment(GitHubUser).Owner;

        Assert.NotNull(derived);
        Assert.Equal(derived, new OwnerEnrolment(GitHubUser).Owner);
        Assert.NotEqual(Pinned, derived);
    }

    [Fact]
    public void Without_a_GitHub_user_there_is_no_owner()
    {
        Assert.Null(OwnerEnrolment.Open.Owner);
    }

    [Fact]
    public async Task The_registry_tells_a_device_under_another_owner_from_the_owners_own()
    {
        var registry = new InMemoryDeviceRegistry();
        var other = OwnerId.New();

        Assert.False(await registry.AnyOutside(Pinned, Cancellation));

        await registry.Add(DeviceOf(Pinned, "Study desktop"), Cancellation);
        Assert.False(await registry.AnyOutside(Pinned, Cancellation));

        await registry.Add(DeviceOf(other, "Laptop"), Cancellation);
        Assert.True(await registry.AnyOutside(Pinned, Cancellation));
    }

    [Fact]
    public async Task An_unpinned_deployed_owner_with_devices_under_another_owner_refuses_enrolment_and_changes_nothing()
    {
        var fixture = new SyncModuleFixture();
        var legacy = DeviceOf(Pinned, "Study desktop");
        await fixture.Devices.Add(legacy, Cancellation);
        var log = new CapturingLogger<RegisterFirstDeviceCommandHandler>();
        var unpinned = new OwnerEnrolment(GitHubUser, GuardsUnpinnedOwner: true);

        var result = await Register(fixture, unpinned, log).Handle(await fixture.Enrolled("New laptop"), Cancellation);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.EnrolmentOwnerUnpinned, result.Error.Code);
        Assert.Contains("Owner__OwnerId", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal([legacy], await fixture.Devices.ListByOwner(Pinned, Cancellation));
        Assert.Equal(0, await fixture.Devices.CountByOwner(unpinned.Owner!.Value, Cancellation));
        Assert.Contains(log.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("Owner__OwnerId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unpinned_deployed_owner_with_no_other_owner_on_record_enrols_under_the_derived_one()
    {
        var fixture = new SyncModuleFixture();
        var unpinned = new OwnerEnrolment(GitHubUser, GuardsUnpinnedOwner: true);

        var result = await Register(fixture, unpinned).Handle(await fixture.Enrolled("Study desktop"), Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(unpinned.Owner!.Value.Value, result.Value.OwnerId);
    }

    [Fact]
    public async Task A_development_service_without_a_pin_does_not_refuse()
    {
        var fixture = new SyncModuleFixture();
        await fixture.Devices.Add(DeviceOf(OwnerId.New(), "Someone's desktop"), Cancellation);
        var development = new OwnerEnrolment(GitHubUser);

        var result = await Register(fixture, development).Handle(await fixture.Enrolled("Study desktop"), Cancellation);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task A_pinned_owner_recovers_its_own_devices_and_revokes_the_rest()
    {
        var fixture = new SyncModuleFixture();
        await fixture.Devices.Add(DeviceOf(Pinned, "Lost desktop"), Cancellation);
        var pinned = new OwnerEnrolment(GitHubUser, Pinned.Value, GuardsUnpinnedOwner: true);

        var result = await Register(fixture, pinned).Handle(await fixture.Enrolled("New laptop"), Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(Pinned.Value, result.Value.OwnerId);
        Assert.Equal(1, result.Value.RevokedDeviceCount);
        Assert.Equal([result.Value.DeviceId], (await fixture.Devices.ListByOwner(Pinned, Cancellation)).Select(device => device.Id.Value));
    }

    private static Device DeviceOf(OwnerId owner, string name) =>
        new(DeviceId.New(), owner, name, CredentialHash: "hash", new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));

    private static RegisterFirstDeviceCommandHandler Register(
        SyncModuleFixture fixture,
        OwnerEnrolment owner,
        ILogger<RegisterFirstDeviceCommandHandler>? log = null) =>
        new(
            fixture.Devices,
            fixture.Tickets,
            owner,
            fixture.Hasher,
            new RandomRegistrationCredentialGenerator(),
            new NoNotificationPushChannel(),
            fixture.Clock,
            log ?? new CapturingLogger<RegisterFirstDeviceCommandHandler>());

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
