using System.Net.Http.Headers;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Puts the device's short-lived token on an outgoing sync request.
/// <para>
/// It does one thing and stops. There is no refresh-and-retry on a 401, and that
/// is the decision rather than an omission (inherited ADR 0015: never retry an
/// authentication failure). <see cref="SyncTokenProvider"/> already renews
/// before expiry, so a 401 that still arrives means the credential itself is no
/// longer accepted — sending the same request again with a token minted from the
/// same credential would fail identically, twice as loudly, and hide the real
/// answer from the screen that has to show it.
/// </para>
/// <para>
/// With no token the request goes out unauthenticated instead of being blocked
/// here. An unpaired device asking a bearer-only endpoint should hear the
/// service's own 401; inventing one locally would be a second source of truth
/// about what this device is allowed to do.
/// </para>
/// </summary>
public sealed class SyncAuthenticationHandler(SyncTokenProvider tokens) : DelegatingHandler
{
    private readonly SyncTokenProvider _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = await _tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
