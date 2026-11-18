using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// Where a device stands with the owner, read from the sync service's device list
/// (local ADR 0052, Phase 2): paired while listed, revoked once listed and then gone,
/// unknown when never listed, and never paired when the list cannot be read.
/// </summary>
public sealed class RosterPairedDevicesTests
{
    private static readonly Guid ThisInstallation = Guid.Parse("0d6b4c1e-0000-4000-8000-000000000001");
    private static readonly Guid ThisSyncDevice = Guid.Parse("0d6b4c1e-0000-4000-8000-000000000002");
    private static readonly Guid Phone = Guid.Parse("0d6b4c1e-0000-4000-8000-000000000003");
    private static readonly Guid Stranger = Guid.Parse("0d6b4c1e-0000-4000-8000-000000000004");

    private readonly FakeRoster _roster = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly RosterPairedDevices _devices;

    public RosterPairedDevicesTests()
    {
        _devices = new RosterPairedDevices(() => _roster, new FixedIdentity(), _clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_listed_device_is_paired()
    {
        _roster.Devices.Add(Device(Phone));

        Assert.Equal(DeviceStanding.Paired, await _devices.StandingOfAsync(Phone, Ct));
    }

    [Fact]
    public async Task This_installation_is_paired_without_reading_the_list()
    {
        Assert.Equal(DeviceStanding.Paired, await _devices.StandingOfAsync(ThisInstallation, Ct));
        Assert.Equal(0, _roster.Reads);
    }

    [Fact]
    public async Task This_desktops_own_sync_device_is_paired()
    {
        Assert.Equal(DeviceStanding.Paired, await _devices.StandingOfAsync(ThisSyncDevice, Ct));
    }

    [Fact]
    public async Task A_device_listed_once_and_then_gone_is_revoked()
    {
        _roster.Devices.Add(Device(Phone));
        await _devices.StandingOfAsync(Phone, Ct);

        _roster.Devices.Clear();

        Assert.Equal(DeviceStanding.Revoked, await _devices.StandingOfAsync(Phone, Ct));
    }

    [Fact]
    public async Task A_device_never_listed_is_unknown()
    {
        _roster.Devices.Add(Device(Phone));

        Assert.Equal(DeviceStanding.Unknown, await _devices.StandingOfAsync(Stranger, Ct));
    }

    [Fact]
    public async Task A_list_the_service_keeps_refusing_is_never_read_as_paired()
    {
        _roster.Devices.Add(Device(Phone));
        _roster.Failure = Error.Unexpected("sync.unreachable", "The sync service did not answer.");

        var checking = _devices.StandingOfAsync(Phone, Ct);
        for (var i = 1; i < RosterPairedDevices.Attempts; i++)
        {
            await WaitForReadsAsync(i);
            _clock.Advance(RosterPairedDevices.RetryAfter);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => checking);
        Assert.Equal(RosterPairedDevices.Attempts, _roster.Reads);
    }

    [Fact]
    public async Task A_list_refused_once_is_read_again()
    {
        _roster.Devices.Add(Device(Phone));
        _roster.Failure = Error.Unexpected("sync.unreachable", "The sync service did not answer.");
        _roster.FailuresLeft = 1;

        var checking = _devices.StandingOfAsync(Phone, Ct);
        await WaitForReadsAsync(1);
        _clock.Advance(RosterPairedDevices.RetryAfter);

        Assert.Equal(DeviceStanding.Paired, await checking);
        Assert.Equal(2, _roster.Reads);
    }

    private async Task WaitForReadsAsync(int reads)
    {
        // The delay is registered with the fake clock right after a read; yield until it is.
        for (var spin = 0; spin < 200 && _roster.Reads < reads; spin++) await Task.Delay(5, Ct);
        await Task.Delay(20, Ct);
    }

    [Fact]
    public async Task No_sync_client_composed_is_never_read_as_paired()
    {
        var devices = new RosterPairedDevices(() => null, new FixedIdentity());

        await Assert.ThrowsAsync<InvalidOperationException>(() => devices.StandingOfAsync(Phone, Ct));
    }

    private static DeviceSummary Device(Guid id) => new(id, "Pixel", DateTimeOffset.UnixEpoch, ThisSyncDevice);

    private sealed class FixedIdentity : IDeviceIdentitySource
    {
        public DeviceIdentity Current { get; } = new(ThisInstallation, "DESKTOP");
    }

    private sealed class FakeRoster : IDeviceRoster
    {
        public List<DeviceSummary> Devices { get; } = [];

        public Error? Failure { get; set; }

        /// <summary>How many reads still fail while <see cref="Failure"/> is set; every one when null.</summary>
        public int? FailuresLeft { get; set; }

        public int Reads { get; private set; }

        public Task<Result<DeviceListResponse>> ListDevicesAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            var fails = Failure is not null && (FailuresLeft is null || FailuresLeft-- > 0);
            return Task.FromResult(fails && Failure is { } failure
                ? Result.Failure<DeviceListResponse>(failure)
                : Result.Success(new DeviceListResponse(ThisSyncDevice, [.. Devices])));
        }

        public Task<Result> RevokeDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> RefuseDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }
}
