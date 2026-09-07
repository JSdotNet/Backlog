using System.Net;
using System.Net.Http.Headers;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Puts the device's short-lived token on an outgoing sync request.
/// <para>
/// It does one thing and stops. There is no refresh-and-retry on a 401, and that
/// is the decision rather than an omission (inherited ADR 0015: never retry an
/// authentication failure). Sending the same request again with a token minted
/// from the same credential would fail identically, twice as loudly, and hide
/// the real answer from the screen that has to show it.
/// </para>
/// <para>
/// It does drop the token that earned the 401, which is not the same thing. An
/// earlier version of this note said a 401 could only mean the credential was no
/// longer accepted, because <see cref="SyncTokenProvider"/> renews before expiry
/// - and that was wrong. A token can be refused while it is still perfectly
/// fresh: the development service signs with a key generated at startup, so
/// restarting it invalidates every token in flight without touching a single
/// expiry. Nothing then went back to the credential on its own, and the same
/// dead token was handed to every call for the rest of the half hour, which is
/// exactly the silent 401 loop this handler is meant to make legible. Dropping
/// it costs one request and lets the next call reach the token endpoint, where
/// a refusal is unambiguous and gets recorded.
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

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Only when this request actually carried one: a 401 earned by a request
        // with no Authorization header at all is the ordinary answer to an
        // unpaired device, and says nothing about any token.
        if (response.StatusCode == HttpStatusCode.Unauthorized && token is not null)
        {
            _tokens.Invalidate();
        }

        return response;
    }
}
