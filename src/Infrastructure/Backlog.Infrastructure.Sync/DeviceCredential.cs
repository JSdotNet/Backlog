using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// What this device is, from its own point of view: who it belongs to, which
/// device it is, what it calls itself, and — for a device still on one — the
/// long-lived secret it trades for a short-lived token.
/// <para>
/// <paramref name="Credential"/> is shown by the service exactly once, at
/// registration or pairing — the service keeps only its hash. Losing it means
/// pairing again, so it is written straight into an
/// <see cref="IDeviceCredentialStore"/> and never anywhere else.
/// </para>
/// <para>
/// It is null for a device bound to a key (local ADR 0052 §2): that device proves
/// itself with an assertion its <see cref="IDeviceKey"/> signs, and keeps no secret
/// at all. A device that moves onto a key drops its secret the same way.
/// </para>
/// </summary>
public sealed record DeviceCredential(Guid OwnerId, Guid DeviceId, string DeviceName, string? Credential)
{
    /// <summary>Whether this device proves itself with its key rather than a secret.</summary>
    [JsonIgnore]
    public bool IsKeyBound => Credential is null;

    /// <summary>Whether a record read back from a store is one this device can use:
    /// a device id, and either no secret or a non-blank one.</summary>
    internal bool IsUsable => DeviceId != Guid.Empty && (Credential is null || !string.IsNullOrWhiteSpace(Credential));
}
