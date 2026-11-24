using System.Collections.Concurrent;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The <see cref="IPairedDevices"/> a desktop composes: the owner's device list, read from the
/// sync service through <see cref="IDeviceRoster"/> on every question, so a device revoked a
/// moment ago reads as revoked at once (local ADR 0058, Phase 2).
/// <para>
/// This installation is paired without a read: an answer given at this desktop is named by its
/// installation identity (<c>NotificationSyncSession</c>), and the sync device id the service
/// knows this desktop by is the caller's own. A device listed once and missing from a later
/// list reads <see cref="DeviceStanding.Revoked"/>; one never listed while this process ran
/// reads <see cref="DeviceStanding.Unknown"/>. The service forgets a revoked device, so after a
/// restart a device revoked earlier reads Unknown — dropped all the same.
/// </para>
/// <para>
/// A list the service still refuses after <see cref="Attempts"/> reads, or no sync client
/// composed, throws: a device that could not be checked is never taken as paired.
/// </para>
/// </summary>
public sealed class RosterPairedDevices : IPairedDevices
{
    private readonly Func<IDeviceRoster?> _roster;
    private readonly IDeviceIdentitySource? _identity;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<Guid, byte> _seen = new();

    /// <summary>How many times one check reads the device list before it gives up.</summary>
    internal const int Attempts = 3;

    /// <summary>How long a check waits before reading a refused list again.</summary>
    internal static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(2);

    /// <summary>What a host composes: the roster is the sync client, resolved per read
    /// because it is a transient typed client.</summary>
    public RosterPairedDevices(IServiceProvider services)
        : this(
            () => services.GetService<DevicePairingClient>(),
            services.GetService<IDeviceIdentitySource>(),
            services.GetService<TimeProvider>())
    {
        ArgumentNullException.ThrowIfNull(services);
    }

    internal RosterPairedDevices(Func<IDeviceRoster?> roster, IDeviceIdentitySource? identity, TimeProvider? clock = null)
    {
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        _identity = identity;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<DeviceStanding> StandingOfAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        if (_identity?.Current.Id == deviceId) return DeviceStanding.Paired;

        var roster = _roster() ?? throw new InvalidOperationException("No sync client is composed to read the owner's devices with.");
        var listed = await roster.ListDevicesAsync(cancellationToken).ConfigureAwait(false);

        // A read the service refuses is tried again a few times, so a passing blip does not
        // hold a paired device's answer until the next start.
        for (var attempt = 1; listed.IsFailure && attempt < Attempts; attempt++)
        {
            await Task.Delay(RetryAfter, _clock, cancellationToken).ConfigureAwait(false);
            listed = await roster.ListDevicesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (listed.IsFailure)
            throw new InvalidOperationException($"The owner's devices could not be read: {listed.Error.Code}.");

        _seen.TryAdd(listed.Unwrap().CallerDeviceId, 0);
        foreach (var device in listed.Unwrap().Devices) _seen.TryAdd(device.DeviceId, 0);

        if (deviceId == listed.Unwrap().CallerDeviceId || listed.Unwrap().Devices.Any(device => device.DeviceId == deviceId))
            return DeviceStanding.Paired;

        return _seen.ContainsKey(deviceId) ? DeviceStanding.Revoked : DeviceStanding.Unknown;
    }
}
