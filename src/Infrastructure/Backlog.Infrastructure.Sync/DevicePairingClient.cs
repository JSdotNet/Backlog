using System.Net.Http.Json;
using System.Security.Cryptography;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The four calls a person's device makes about its own identity: claim an owner
/// of its own, ask for a code to read out, redeem a code somebody read out, and
/// read back who it turned out to be.
/// <para>
/// Registering and pairing both end with a credential that is shown once, so
/// both write it into the <see cref="IDeviceCredentialStore"/> before returning.
/// With an <see cref="IDeviceKeyStore"/> both send the device's public key instead,
/// and what is stored is the device's identity with no secret in it (local ADR 0058
/// §2).
/// Doing it here rather than in the screen is what stops a second caller — the
/// mobile pairing panel beside the desktop one — from being the place the secret
/// is dropped.
/// </para>
/// <para>
/// An expected 4xx comes back as a failed <see cref="Result{TValue}"/> carrying
/// the ProblemDetails <c>code</c> and <c>detail</c>, not as an exception: an
/// expired pairing code and a mistyped one are ordinary outcomes of a person
/// typing eight characters, and the screen has to say which happened.
/// <see cref="SyncErrorCodes"/> is what it branches on.
/// </para>
/// </summary>
public sealed class DevicePairingClient : IDeviceRoster
{
    /// <summary>Where a failure that never reached the service is filed. It is
    /// not one of <see cref="SyncErrorCodes"/> on purpose — those are codes the
    /// service issues, and claiming one for a transport failure would let a
    /// screen report an answer nobody gave.</summary>
    public const string UnreachableCode = "sync.unreachable";

    /// <summary>Where a credential that the service issued but this machine
    /// could not keep is filed. Also not one of <see cref="SyncErrorCodes"/>:
    /// the service did its part, and the failure is local.</summary>
    public const string CredentialStoreFailedCode = "sync.credential_store_failed";

    /// <summary>Where a first registration that cannot sign in here is filed: no
    /// browser on this host. A phone pairs by code instead.</summary>
    public const string SignInUnavailableCode = "sync.sign_in_unavailable";

    /// <summary>Where an owner sign-in that came back without a code is filed — the
    /// service refused the GitHub account, or the person cancelled at GitHub.</summary>
    public const string SignInRefusedCode = "sync.sign_in_refused";

    /// <summary>Where an owner sign-in nobody finished in time is filed.</summary>
    public const string SignInTimedOutCode = "sync.sign_in_timed_out";

    /// <summary>How long the browser has to come back by default.</summary>
    public static readonly TimeSpan DefaultSignInTimeout = TimeSpan.FromMinutes(5);

    /// <summary>How long the Windows Hello prompt for a refusal stands before it expires
    /// (<see cref="RefuseDeviceAsync"/>). A Refuse answered on the phone raises it on a desktop
    /// nobody may be at, and the answers queued behind it wait until it settles.</summary>
    public static readonly TimeSpan RefuseConfirmationWindow = TimeSpan.FromMinutes(2);

    private readonly HttpClient _http;
    private readonly IDeviceCredentialStore _credentials;
    private readonly PairingSubjects? _pairings;
    private readonly ISystemBrowser? _browser;
    private readonly IDeviceKeyStore? _keys;
    private readonly DeviceStepUp? _stepUp;
    private readonly TimeSpan _signInTimeout;
    private readonly ILogger _log;

    /// <summary>1 while the key has failed since it last opened, so a key that keeps
    /// failing is a warning once and a debug line after it.</summary>
    private int _keyFailing;

    /// <param name="http">The client <c>AddSyncClient</c> configures.</param>
    /// <param name="credentials">Where a credential the service issues is kept.</param>
    /// <param name="pairings">Where an issued code opens a pairing a notification can be
    /// about; none on a host that composed no notifications.</param>
    /// <param name="browser">Where the owner signs in to register a first device; none on a
    /// host that only pairs by code.</param>
    /// <param name="signInTimeout">How long to wait for the browser to come back.</param>
    /// <param name="keys">This device's key (local ADR 0058 §2): with one, registering and
    /// pairing send its public key and the service hands back no credential to keep. None
    /// on a host that keeps no key, which goes on with a credential.</param>
    /// <param name="stepUp">Windows Hello before minting a pairing code, revoking a device or
    /// refusing one (local ADR 0058 §6). None on a host that keeps no step-up — the web harness
    /// and the phone — which mints, revokes and refuses without asking.</param>
    public DevicePairingClient(
        HttpClient http,
        IDeviceCredentialStore credentials,
        PairingSubjects? pairings = null,
        ISystemBrowser? browser = null,
        TimeSpan? signInTimeout = null,
        IDeviceKeyStore? keys = null,
        ILogger<DevicePairingClient>? log = null,
        DeviceStepUp? stepUp = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(credentials);

        _http = http;
        _credentials = credentials;
        _pairings = pairings;
        _browser = browser;
        _keys = keys;
        _stepUp = stepUp;
        _signInTimeout = signInTimeout ?? DefaultSignInTimeout;
        _log = log ?? NullLogger<DevicePairingClient>.Instance;
    }

    /// <summary>
    /// First device in, or recovery of a lost owner (local ADR 0058 §1): signs the owner
    /// in through the system browser, then registers with the one-time code the sign-in
    /// handed back to this machine's loopback, and stores the credential it is handed.
    /// <para>
    /// PKCE keeps the code useless to anything that sees it on the way: only this call
    /// holds the verifier. The browser goes to the address the service answers — GitHub,
    /// or on a Development service with no owner configured, straight back to the
    /// loopback, which is read here without opening a browser at all.
    /// </para>
    /// </summary>
    public async Task<Result<DeviceCredential>> RegisterAsync(
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        var name = (deviceName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return Result.Failure<DeviceCredential>(
                Error.Validation(SyncErrorCodes.DeviceNameRequired, "Give this device a name first."));
        }

        if (_browser is null)
        {
            return Result.Failure<DeviceCredential>(Error.Validation(
                SignInUnavailableCode,
                "This device cannot open a browser to sign in. Pair it with a code from a device that is already in."));
        }

        var verifier = EnrolmentPkce.CreateVerifier();
        var state = EnrolmentPkce.CreateVerifier();
        using var loopback = LoopbackRedirectListener.Start();

        var started = await SyncHttp.SendAsync<EnrolmentStartResponse>(
            () => _http.PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.EnrolmentStart),
                new StartEnrolmentRequest(loopback.RedirectUri, EnrolmentPkce.Challenge(verifier), EnrolmentPkce.Method, state),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (started.IsFailure) return Result.Failure<DeviceCredential>(started.Error);

        var answer = await SignInAsync(loopback, started.Value.SignInAddress, cancellationToken).ConfigureAwait(false);
        if (answer.IsFailure) return Result.Failure<DeviceCredential>(answer.Error);

        var code = ReadAnswer(answer.Value, state);
        if (code.IsFailure) return Result.Failure<DeviceCredential>(code.Error);

        return await StoreAsync(
            () => _http.PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.RegisterDevice),
                new RegisterDeviceRequest(name, code.Value, verifier, PublicKeyOrNull()),
                cancellationToken),
            registration => new DeviceCredential(registration.OwnerId, registration.DeviceId, name, registration.Credential),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<Uri>> SignInAsync(LoopbackRedirectListener loopback, Uri signInAddress, CancellationToken cancellationToken)
    {
        if (signInAddress.AbsoluteUri.StartsWith(loopback.RedirectUri.AbsoluteUri, StringComparison.Ordinal))
        {
            return signInAddress;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_signInTimeout);

        try
        {
            await _browser!.OpenAsync(signInAddress, cancellationToken).ConfigureAwait(false);
            return await loopback.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<Uri>(Error.Validation(
                SignInTimedOutCode,
                "The sign-in did not come back in time. Register again to start a new one."));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser registered for web addresses on this machine.
            return Result.Failure<Uri>(Error.Validation(
                SignInUnavailableCode,
                "No browser could be opened to sign in. Pair this device with a code from a device that is already in."));
        }
    }

    /// <summary>The code on the loopback answer, once its state is this sign-in's.</summary>
    private static Result<string> ReadAnswer(Uri answer, string state)
    {
        var query = answer.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .GroupBy(pair => pair[0], StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Uri.UnescapeDataString(group.First().ElementAtOrDefault(1) ?? string.Empty),
                StringComparer.Ordinal);

        if (!query.TryGetValue("state", out var returned) || !string.Equals(returned, state, StringComparison.Ordinal))
        {
            return Result.Failure<string>(Error.Validation(SignInRefusedCode, "The sign-in answer was not for this request. Register again."));
        }

        if (query.ContainsKey("error"))
        {
            return Result.Failure<string>(Error.Validation(
                SignInRefusedCode,
                query.TryGetValue("error_description", out var why) && why.Length > 0 ? why : "The sign-in was refused."));
        }

        return query.TryGetValue("code", out var code) && code.Length > 0
            ? Result.Success(code)
            : Result.Failure<string>(Error.Validation(SignInRefusedCode, "The sign-in came back without a code. Register again."));
    }

    /// <summary>A paired device asks for a short code to read out loud. Bearer:
    /// only a device that is already in may invite another one. A code issued opens a
    /// pairing in <see cref="PairingSubjects"/>, which ends when the code expires. With a
    /// <see cref="DeviceStepUp"/>, the person confirms with Windows Hello first, and a code they
    /// do not confirm is never asked for.</summary>
    public async Task<Result<PairingCodeResponse>> IssuePairingCodeAsync(
        CancellationToken cancellationToken = default)
    {
        if (_stepUp is not null)
        {
            var confirmed = await _stepUp.ConfirmAsync(
                "Make a pairing code that lets another device into your backlog?",
                "No pairing code was made",
                cancellationToken).ConfigureAwait(false);

            if (confirmed.IsFailure) return Result.Failure<PairingCodeResponse>(confirmed.Error);
        }

        var issued = await SyncHttp.SendAsync<PairingCodeResponse>(
            () => _http.PostAsync(SyncRoutes.Absolute(SyncRoutes.PairingCodes), content: null, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (issued.IsSuccess) _pairings?.Issued(issued.Value);

        return issued;
    }

    /// <summary>Second device in: trades a code somebody read out for a
    /// credential under the same owner, and stores it.</summary>
    public async Task<Result<DeviceCredential>> PairAsync(
        string code,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        var name = (deviceName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return Result.Failure<DeviceCredential>(
                Error.Validation(SyncErrorCodes.DeviceNameRequired, "Give this device a name first."));
        }

        // Normalized here as well as on the service, so a code pasted back in its
        // displayed form is not sent with the hyphen the service would then have
        // to forgive. Well-formedness is checked before the round trip for the
        // same reason the format exists: a typo should not cost a request.
        var normalized = PairingCodeFormat.Normalize(code ?? string.Empty);
        if (!PairingCodeFormat.IsWellFormed(normalized))
        {
            return Result.Failure<DeviceCredential>(Error.Validation(
                SyncErrorCodes.PairingCodeMalformed,
                $"A pairing code is {PairingCodeFormat.Length} characters. Check what you typed and try again."));
        }

        return await StoreAsync(
            () => _http.PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
                new RedeemPairingCodeRequest(normalized, name, PublicKeyOrNull()),
                cancellationToken),
            registration => new DeviceCredential(registration.OwnerId, registration.DeviceId, name, registration.Credential),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Who the bearer of this device's token turned out to be, read
    /// back from the service rather than from what was stored locally.</summary>
    public async Task<Result<DeviceStatusResponse>> GetStatusAsync(
        CancellationToken cancellationToken = default) =>
        await SyncHttp.SendAsync<DeviceStatusResponse>(
            () => _http.GetAsync(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), cancellationToken),
            cancellationToken).ConfigureAwait(false);

    /// <summary>Every device of this device's owner, with the device that paired each.</summary>
    public async Task<Result<DeviceListResponse>> ListDevicesAsync(CancellationToken cancellationToken = default) =>
        await SyncHttp.SendAsync<DeviceListResponse>(
            () => _http.GetAsync(SyncRoutes.Absolute(SyncRoutes.Devices), cancellationToken),
            cancellationToken).ConfigureAwait(false);

    /// <summary>Revokes another device of this owner. A device that is gone already
    /// answers <see cref="SyncErrorCodes.DeviceNotFound"/>, which a caller that only
    /// wants it gone treats as done. With a <see cref="DeviceStepUp"/>, the person confirms
    /// with Windows Hello first, and a revocation they do not confirm is never sent.</summary>
    public async Task<Result> RevokeDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        if (_stepUp is not null)
        {
            var confirmed = await _stepUp.ConfirmAsync(
                "Revoke a device, so it can no longer sync your backlog?",
                "The device was not revoked",
                cancellationToken).ConfigureAwait(false);

            if (confirmed.IsFailure) return confirmed;
        }

        return await DeleteDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> RefuseDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        if (_stepUp is not null)
        {
            var confirmed = await _stepUp.ConfirmWithinAsync(
                "Refuse a device that just paired, so it can no longer sync your backlog?",
                "The device was not refused",
                RefuseConfirmationWindow,
                cancellationToken).ConfigureAwait(false);

            if (confirmed.IsFailure) return confirmed;
        }

        return await DeleteDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result> DeleteDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var revoked = await SyncHttp.SendAsync(
            () => _http.DeleteAsync(SyncRoutes.DeviceFor(deviceId), cancellationToken),
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);

        return revoked.IsSuccess ? Result.Success() : Result.Failure(revoked.Error);
    }

    /// <summary>This device's public key to bind, or null to be handed a credential
    /// instead: on a host with no key store, and where the store cannot open or make a
    /// key — a device on a credential still syncs, where one with no proof at all
    /// would not.</summary>
    private string? PublicKeyOrNull()
    {
        if (_keys is null) return null;

        try
        {
            var publicKey = _keys.GetOrCreate().PublicKey;
            Interlocked.Exchange(ref _keyFailing, 0);
            return publicKey;
        }
        catch (CryptographicException ex)
        {
            var level = Interlocked.Exchange(ref _keyFailing, 1) == 0 ? LogLevel.Warning : LogLevel.Debug;
            _log.Log(level, ex, "This device's key could not be opened or made, so it registers with a credential instead.");
            return null;
        }
    }

    private async Task<Result<DeviceCredential>> StoreAsync(
        Func<Task<HttpResponseMessage>> send,
        Func<DeviceRegistrationResponse, DeviceCredential> toCredential,
        CancellationToken cancellationToken)
    {
        var response = await SyncHttp.SendAsync<DeviceRegistrationResponse>(send, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure) return Result.Failure<DeviceCredential>(response.Error);

        var credential = toCredential(response.Value);

        try
        {
            // Not cancellable: the service has already issued this credential and
            // will not issue it again, so a save abandoned now would throw away
            // the only copy of a registration that did happen.
            await _credentials.SaveAsync(credential, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // The service registered this device and will not do so again for
            // the same credential, but nothing here can keep it, so the pairing
            // is reported as failed rather than as a success that evaporates on
            // the next restart. Pairing again is the recovery, and it is the
            // thing the screen already offers.
            return Result.Failure<DeviceCredential>(Error.Unexpected(
                CredentialStoreFailedCode,
                "This device was registered, but its credential could not be saved on this machine. Try again."));
        }

        return Result.Success(credential);
    }
}
