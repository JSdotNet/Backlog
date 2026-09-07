using System.Net;
using System.Net.Http.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Turns the long-lived registration credential into the short-lived access
/// token every other sync call carries, and holds on to it until just before it
/// expires.
/// <para>
/// It calls the token endpoint through a named client of its own,
/// <see cref="HttpClientName"/>, which deliberately has no
/// <see cref="SyncAuthenticationHandler"/> on it. A provider reached through the
/// same pipeline it feeds would ask itself for a token in order to ask for a
/// token; the second client is what makes that impossible rather than merely
/// unlikely.
/// </para>
/// <para>
/// A token is renewed <see cref="RenewalWindow"/> before it expires rather than
/// after it fails, because the alternative is a burst of calls that all 401 at
/// the same moment. The renewal is single-flight: several screens waking up
/// together share one request instead of racing to mint four tokens and keeping
/// the last one.
/// </para>
/// <para>
/// Failure is <c>null</c>, consistently — no credential, a 401 from the token
/// endpoint, an unreachable service. Callers cannot usefully tell those apart:
/// each of them means "send this request unauthenticated and let the service
/// answer", and the service's own 401 is the answer a screen reports. A 401 here
/// is never retried (inherited ADR 0015: an authentication failure is not a
/// transient one) — it clears the cache and returns, so the next call starts
/// from the credential again rather than from a token known to be rejected.
/// </para>
/// <para>
/// There is one thing callers can tell apart, and it is not the token: whether
/// the service has said this credential is no good. <see cref="CredentialRejected"/>
/// carries that verdict out of here, because nowhere else can reach it. The
/// token endpoint is anonymous and the only thing it authenticates is the
/// credential in the body, so a 401 from it means precisely that - unlike a 401
/// from a bearer endpoint, which a request sent with no token at all also earns.
/// Every other failure leaves the verdict alone: a service that is down has said
/// nothing about the credential, and a screen that unpaired a device over it
/// would throw away a good pairing every time a laptop woke on a dead network.
/// </para>
/// </summary>
public sealed class SyncTokenProvider : IDisposable
{
    /// <summary>The named client the token endpoint is called through.</summary>
    public const string HttpClientName = "sync-token";

    /// <summary>How long before expiry a token stops being handed out. Tokens
    /// live thirty minutes, so this spends about seven per cent of one to be
    /// sure a request never leaves with a token that expires in flight.</summary>
    internal static readonly TimeSpan RenewalWindow = TimeSpan.FromMinutes(2);

    private readonly IHttpClientFactory _clients;
    private readonly IDeviceCredentialStore _credentials;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt;
    private bool _credentialRejected;

    /// <summary>Bumped every time the cache is dropped. A fetch reads it on the
    /// way out and again on the way back: a token minted under a credential that
    /// has since been cleared or replaced is thrown away rather than committed,
    /// because the request that produced it left before the device stopped being
    /// the device it was for.</summary>
    private int _generation;

    public SyncTokenProvider(IHttpClientFactory clients, IDeviceCredentialStore credentials, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(time);

        _clients = clients;
        _credentials = credentials;
        _time = time;

        // Pairing, unpairing and re-registering all replace the credential the
        // cached token was minted from, so the cache is dropped the moment the
        // store says so rather than when the old token happens to expire.
        _credentials.Changed += OnCredentialChanged;
    }

    /// <summary>
    /// Whether the token endpoint has told this device its credential is not one
    /// the service knows - the state a screen offers registering again from,
    /// rather than going on showing a paired device that cannot talk.
    /// <para>
    /// Only the token endpoint's own 401 sets it. It is taken back by a token
    /// that is subsequently issued, and by the credential being replaced or
    /// forgotten; it is deliberately <em>not</em> taken back by
    /// <see cref="Invalidate"/>, which throws a token away and says nothing
    /// about the pairing behind it.
    /// </para>
    /// </summary>
    public bool CredentialRejected => _credentialRejected;

    /// <summary>Raised when <see cref="CredentialRejected"/> changes, so a screen
    /// showing that state redraws without having to poll for it. Raised on the
    /// thread the change happened on, which for a background sync is not the UI
    /// one - a handler that touches a component has to marshal.</summary>
    public event Action? CredentialRejectedChanged;

    /// <summary>
    /// A token for the next call, or <c>null</c> when this device cannot get
    /// one. Never throws for an expected failure.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // No credential is answered before the cache, before the gate and
        // before the network. Before the cache because an unpaired device must
        // not go on bearing the token the previous pairing minted; before the
        // gate because an unpaired device asking for a token is the ordinary
        // case on first run, not something to serialize every caller behind.
        if (_credentials.Current is null) return null;

        if (Cached() is { } cached) return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var credential = _credentials.Current;
            if (credential is null) return null;

            // Whoever was ahead in the queue has already renewed it.
            if (Cached() is { } fresh) return fresh;

            return await FetchAsync(credential, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets the cached token. The next call mints a new one, and any
    /// fetch already in flight no longer counts. It leaves
    /// <see cref="CredentialRejected"/> where it is: the token is what is being
    /// thrown away, not the pairing.</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _token = null;
        _expiresAt = default;
    }

    public void Dispose()
    {
        _credentials.Changed -= OnCredentialChanged;
        _gate.Dispose();
    }

    /// <summary>The credential was replaced or forgotten. The cached token was
    /// minted from the old one, and whatever the service said about the old one
    /// is not a verdict on the new one.</summary>
    private void OnCredentialChanged()
    {
        Invalidate();
        SetCredentialRejected(false);
    }

    private void SetCredentialRejected(bool rejected)
    {
        if (_credentialRejected == rejected) return;

        _credentialRejected = rejected;
        CredentialRejectedChanged?.Invoke();
    }

    private string? Cached() =>
        _token is not null && _expiresAt - _time.GetUtcNow() >= RenewalWindow ? _token : null;

    private async Task<string?> FetchAsync(DeviceCredential credential, CancellationToken cancellationToken)
    {
        var client = _clients.CreateClient(HttpClientName);
        var generation = Volatile.Read(ref _generation);

        try
        {
            using var response = await client
                .PostAsJsonAsync(
                    SyncRoutes.Absolute(SyncRoutes.DeviceToken),
                    new DeviceTokenRequest(credential.DeviceId, credential.Credential),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // 401 means the credential itself is no longer accepted, which
                // sending it again cannot fix. Anything else is the service
                // being unavailable, which the caller reports rather than
                // hammers.
                //
                // The verdict is recorded only while the credential that earned
                // it is still the one this device holds: a 401 for a credential
                // that has since been replaced judges a device that no longer
                // exists. Read before Invalidate, which moves the generation.
                var rejected = response.StatusCode == HttpStatusCode.Unauthorized
                    && Volatile.Read(ref _generation) == generation
                    && _credentials.Current is { } holder
                    && holder.DeviceId == credential.DeviceId;

                Invalidate();
                if (rejected) SetCredentialRejected(true);
                return null;
            }

            var token = await response.Content
                .ReadFromJsonAsync<DeviceTokenResponse>(cancellationToken)
                .ConfigureAwait(false);

            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                Invalidate();
                return null;
            }

            // The credential moved under this request — the device was unpaired,
            // or paired again as somebody else. What came back was minted for
            // the device that asked, so it is dropped rather than cached, and
            // the caller goes out unauthenticated to hear the service's own
            // answer. Committing it here is what would let a device that is no
            // longer paired keep bearing the previous owner's token.
            if (Volatile.Read(ref _generation) != generation) return null;
            if (_credentials.Current is not { } current || current.DeviceId != credential.DeviceId) return null;

            _token = token.AccessToken;
            _expiresAt = token.ExpiresAt;

            // A service that has just minted a token for this credential is not
            // one that refuses it, whatever it said last time. This is what
            // makes the state recoverable without a restart.
            SetCredentialRejected(false);

            return _token;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Invalidate();
            return null;
        }
    }
}
