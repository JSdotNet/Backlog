using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The four calls a person's device makes about its own identity: claim an owner
/// of its own, ask for a code to read out, redeem a code somebody read out, and
/// read back who it turned out to be.
/// <para>
/// Registering and pairing both end with a credential that is shown once, so
/// both write it into the <see cref="IDeviceCredentialStore"/> before returning.
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
public sealed class DevicePairingClient
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

    private readonly HttpClient _http;
    private readonly IDeviceCredentialStore _credentials;

    public DevicePairingClient(HttpClient http, IDeviceCredentialStore credentials)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(credentials);

        _http = http;
        _credentials = credentials;
    }

    /// <summary>First device in: claims an owner of its own and stores the
    /// credential it is handed.</summary>
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

        return await StoreAsync(
            () => _http.PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.RegisterDevice),
                new RegisterDeviceRequest(name),
                cancellationToken),
            registration => new DeviceCredential(
                registration.OwnerId,
                registration.DeviceId,
                name,
                registration.Credential),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A paired device asks for a short code to read out loud. Bearer:
    /// only a device that is already in may invite another one.</summary>
    public async Task<Result<PairingCodeResponse>> IssuePairingCodeAsync(
        CancellationToken cancellationToken = default) =>
        await SendAsync<PairingCodeResponse>(
            () => _http.PostAsync(SyncRoutes.Absolute(SyncRoutes.PairingCodes), content: null, cancellationToken),
            cancellationToken).ConfigureAwait(false);

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
                new RedeemPairingCodeRequest(normalized, name),
                cancellationToken),
            registration => new DeviceCredential(
                registration.OwnerId,
                registration.DeviceId,
                name,
                registration.Credential),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Who the bearer of this device's token turned out to be, read
    /// back from the service rather than from what was stored locally.</summary>
    public async Task<Result<DeviceStatusResponse>> GetStatusAsync(
        CancellationToken cancellationToken = default) =>
        await SendAsync<DeviceStatusResponse>(
            () => _http.GetAsync(SyncRoutes.Absolute(SyncRoutes.DeviceStatus), cancellationToken),
            cancellationToken).ConfigureAwait(false);

    private async Task<Result<DeviceCredential>> StoreAsync(
        Func<Task<HttpResponseMessage>> send,
        Func<DeviceRegistrationResponse, DeviceCredential> toCredential,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<DeviceRegistrationResponse>(send, cancellationToken).ConfigureAwait(false);
        if (response.IsFailure) return Result.Failure<DeviceCredential>(response.Error);

        var credential = toCredential(response.Value);

        try
        {
            _credentials.Save(credential);
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

    private static async Task<Result<T>> SendAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await send().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<T>(await ReadProblemAsync(response, cancellationToken).ConfigureAwait(false));
            }

            var value = await response.Content
                .ReadFromJsonAsync<T>(cancellationToken)
                .ConfigureAwait(false);

            return value is null
                ? Result.Failure<T>(Error.Unexpected(UnreachableCode, "The sync service answered with nothing."))
                : Result.Success(value);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            return Result.Failure<T>(Error.Unexpected(UnreachableCode, $"The sync service could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<T>(Error.Unexpected(UnreachableCode, "The sync service did not answer in time."));
        }
    }

    /// <summary>
    /// The ProblemDetails body, read as the <c>code</c> extension the sync API
    /// puts on every error plus its human-readable <c>detail</c>. Read as a
    /// document rather than into a typed record because <c>code</c> is an
    /// extension member, and a body that is not problem+json at all — a proxy's
    /// HTML error page, say — still has to produce something a screen can show.
    /// </summary>
    private static async Task<Error> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? code = null;
        string? detail = null;

        try
        {
            using var document = await JsonDocument
                .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("code", out var codeElement))
                {
                    code = codeElement.GetString();
                }

                if (document.RootElement.TryGetProperty("detail", out var detailElement))
                {
                    detail = detailElement.GetString();
                }
                else if (document.RootElement.TryGetProperty("title", out var titleElement))
                {
                    detail = titleElement.GetString();
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
        }

        return new Error(
            string.IsNullOrWhiteSpace(code) ? $"sync.http_{(int)response.StatusCode}" : code,
            string.IsNullOrWhiteSpace(detail)
                ? $"The sync service answered {(int)response.StatusCode} {response.ReasonPhrase}."
                : detail,
            Classify(response.StatusCode));
    }

    private static ErrorType Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => ErrorType.Validation,
        HttpStatusCode.NotFound => ErrorType.NotFound,
        HttpStatusCode.Conflict => ErrorType.Conflict,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ErrorType.Failure,
        _ => ErrorType.Unexpected
    };
}
