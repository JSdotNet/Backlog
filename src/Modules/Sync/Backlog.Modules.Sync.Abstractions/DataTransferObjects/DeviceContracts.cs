namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>What a device sends to enrol as the owner's device: what to call
/// itself, the one-time code its owner sign-in handed back to its loopback, and
/// the PKCE verifier only the device that started that sign-in holds (local ADR
/// 0052 §1).</summary>
public sealed record RegisterDeviceRequest(string DeviceName, string? EnrolmentCode = null, string? CodeVerifier = null);

/// <summary>
/// What a newly registered device gets back. <paramref name="Credential"/> is a
/// long-lived secret shown exactly once — the service keeps only its hash — and
/// the device is expected to put it straight into the OS credential store.
/// <paramref name="RevokedDeviceCount"/> is how many of the owner's other devices a
/// recovery revoked: zero for a first device.
/// </summary>
public sealed record DeviceRegistrationResponse(Guid OwnerId, Guid DeviceId, string Credential, int RevokedDeviceCount = 0);

/// <summary>
/// A device starts the owner sign-in (local ADR 0052 §1). <paramref name="RedirectUri"/>
/// is the loopback address it listens on, <paramref name="CodeChallenge"/> the S256
/// hash of a verifier it keeps, and <paramref name="State"/> what it expects back
/// beside the code.
/// </summary>
public sealed record StartEnrolmentRequest(
    Uri RedirectUri,
    string CodeChallenge,
    string CodeChallengeMethod,
    string State);

/// <summary>Where the device's system browser goes next: GitHub's sign-in page, or —
/// on a Development service with no owner configured — straight to the loopback with
/// the code already on it.</summary>
public sealed record EnrolmentStartResponse(Uri SignInAddress);

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

/// <summary>One device of the caller's owner, as the device list shows it.
/// <paramref name="PairedBy"/> names the device whose pairing code it redeemed,
/// or is null for the owner's first device.</summary>
public sealed record DeviceSummary(Guid DeviceId, string Name, DateTimeOffset RegisteredAt, Guid? PairedBy);

/// <summary>Every device of the caller's owner, oldest first, and which of them
/// is the caller — so a desktop can find the devices its own codes paired.</summary>
public sealed record DeviceListResponse(Guid CallerDeviceId, IReadOnlyList<DeviceSummary> Devices);
