namespace Backlog.Infrastructure.Sync;

/// <summary>
/// What this device is, from its own point of view: who it belongs to, which
/// device it is, what it calls itself, and the long-lived secret it trades for a
/// short-lived token.
/// <para>
/// <paramref name="Credential"/> is shown by the service exactly once, at
/// registration or pairing — the service keeps only its hash. Losing it means
/// pairing again, so it is written straight into an
/// <see cref="IDeviceCredentialStore"/> and never anywhere else.
/// </para>
/// </summary>
public sealed record DeviceCredential(Guid OwnerId, Guid DeviceId, string DeviceName, string Credential);
