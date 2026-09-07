namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>What a device sends to claim an owner of its own. There is no
/// account and no login, so the only thing it can tell us is what to call
/// itself.</summary>
public sealed record RegisterDeviceRequest(string DeviceName);

/// <summary>
/// What a newly registered device gets back. <paramref name="Credential"/> is a
/// long-lived secret shown exactly once — the service keeps only its hash — and
/// the device is expected to put it straight into the OS credential store.
/// </summary>
public sealed record DeviceRegistrationResponse(Guid OwnerId, Guid DeviceId, string Credential);

/// <summary>
/// A short code a person carries from one device to the other, out of band. It
/// is single-use and expires quickly, so it is not a credential — it is
/// permission to mint one.
/// </summary>
public sealed record PairingCodeResponse(string Code, DateTimeOffset ExpiresAt);

/// <summary>What the second device sends: the code it was given, and what to
/// call itself once it is in.</summary>
public sealed record RedeemPairingCodeRequest(string Code, string DeviceName);

/// <summary>The registration credential, offered in exchange for a short-lived
/// access token. Sent on every sync, so it never leaves the device except over
/// TLS to this one endpoint.</summary>
public sealed record DeviceTokenRequest(Guid DeviceId, string Credential);

/// <summary>
/// The short-lived token every other sync call carries. Per inherited ADR 0012
/// it is minutes rather than hours; <paramref name="ExpiresAt"/> is here so a
/// client can renew before a call fails rather than after.
/// </summary>
public sealed record DeviceTokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string TokenType = "Bearer");

/// <summary>Who the caller turned out to be, read back from its own token. The
/// device count is what a settings screen shows beside "paired devices".</summary>
public sealed record DeviceStatusResponse(Guid OwnerId, Guid DeviceId, string DeviceName, int PairedDeviceCount);
