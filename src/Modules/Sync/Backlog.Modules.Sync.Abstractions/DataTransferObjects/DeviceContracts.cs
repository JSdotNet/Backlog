namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>What a device sends to enrol as the owner's device: what to call
/// itself, the one-time code its owner sign-in handed back to its loopback, and
/// the PKCE verifier only the device that started that sign-in holds (local ADR
/// 0052 §1). <paramref name="PublicKey"/> is the base64url SubjectPublicKeyInfo of
/// the device's non-exportable P-256 key (§2): with it the device is bound to that
/// key and gets no credential.</summary>
public sealed record RegisterDeviceRequest(string DeviceName, string? EnrolmentCode = null, string? CodeVerifier = null, string? PublicKey = null);

/// <summary>
/// What a newly registered device gets back. <paramref name="Credential"/> is a
/// long-lived secret shown exactly once — the service keeps only its hash — and
/// the device is expected to put it straight into the OS credential store. It is
/// null for a device that registered a public key: that device proves itself by
/// signing an assertion instead (local ADR 0052 §2).
/// <paramref name="RevokedDeviceCount"/> is how many of the owner's other devices a
/// recovery revoked: zero for a first device.
/// </summary>
public sealed record DeviceRegistrationResponse(Guid OwnerId, Guid DeviceId, string? Credential, int RevokedDeviceCount = 0);

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

/// <summary>What the second device sends: the code it was given, what to call
/// itself once it is in, and — for a device bound to a key — its public key, as
/// on <see cref="RegisterDeviceRequest"/>.</summary>
public sealed record RedeemPairingCodeRequest(string Code, string DeviceName, string? PublicKey = null);

/// <summary>
/// What a device offers in exchange for a short-lived access token: either a
/// signed assertion or its registration credential.
/// <para>
/// A device bound to a key sends <paramref name="ClientAssertion"/>, a
/// <c>private_key_jwt</c> signed with that key, and <paramref name="ClientAssertionType"/>
/// set to <see cref="SyncAssertion.ClientAssertionType"/> (local ADR 0052 §2); its
/// credential is refused. A device still on a credential sends
/// <paramref name="Credential"/>, and may add <paramref name="PublicKey"/> once to
/// bind a key in its place: the service then forgets the credential.
/// </para>
/// </summary>
public sealed record DeviceTokenRequest(
    Guid DeviceId,
    string? Credential = null,
    string? ClientAssertionType = null,
    string? ClientAssertion = null,
    string? PublicKey = null);

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
