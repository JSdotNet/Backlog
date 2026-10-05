using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.SpecManager;

/// <summary>
/// Where what a spec-manager installation gave this machine is kept, one entry per
/// base URL: the client registration and, while someone is signed in, the token
/// pair.
/// <para>
/// A port of its own rather than a line in a settings file because a refresh token
/// is a long-lived secret: the desktop keeps it in a DPAPI envelope
/// (<see cref="DpapiSpecManagerTokenStore"/>), and nothing else in the app writes it
/// anywhere.
/// </para>
/// </summary>
public interface ISpecManagerTokenStore
{
    /// <summary>What is kept for the installation at <paramref name="baseUrl"/>, or
    /// null when nothing is.</summary>
    SpecManagerCredential? Get(string baseUrl);

    /// <summary>Keeps <paramref name="credential"/> for <paramref name="baseUrl"/>,
    /// replacing what was kept. Throws when it could not be kept; the caller says so
    /// to the person.</summary>
    void Save(string baseUrl, SpecManagerCredential credential);

    /// <summary>Forgets everything kept for <paramref name="baseUrl"/>.</summary>
    void Remove(string baseUrl);
}

/// <summary>
/// What one spec-manager installation gave this machine.
/// <para>
/// The client registration outlives a sign-out — the installation registered this
/// app once, and registering again on every sign-in would leave a client behind
/// each time — so a signed-out entry keeps <see cref="ClientId"/> and drops the
/// tokens.
/// </para>
/// <para>
/// <see cref="ToString"/> names no token, so a credential that reaches a log or an
/// exception message by accident does not carry one there.
/// </para>
/// </summary>
/// <param name="ClientId">The id dynamic client registration answered with.</param>
/// <param name="RedirectUri">The redirect URI the client was registered with; the
/// sign-in adds the loopback port it listens on.</param>
/// <param name="AccessToken">The bearer for the REST calls, or null.</param>
/// <param name="AccessTokenExpiresAt">When <paramref name="AccessToken"/> stops being
/// honoured.</param>
/// <param name="RefreshToken">What a new pair is asked for with. Rotated on every
/// refresh, so the one kept is always the last one given.</param>
/// <param name="AccountName">Who is signed in, as the installation names them.</param>
/// <param name="SignedInAt">When the sign-in happened.</param>
public sealed record SpecManagerCredential(
    string ClientId,
    string RedirectUri,
    string? AccessToken = null,
    DateTimeOffset? AccessTokenExpiresAt = null,
    string? RefreshToken = null,
    string? AccountName = null,
    DateTimeOffset? SignedInAt = null)
{
    /// <summary>Whether someone is signed in: there is a token pair to call
    /// with.</summary>
    [JsonIgnore]
    public bool IsSignedIn => !string.IsNullOrEmpty(RefreshToken) || !string.IsNullOrEmpty(AccessToken);

    /// <summary>The same registration with the tokens and the account dropped.</summary>
    public SpecManagerCredential SignedOut() => new(ClientId, RedirectUri);

    public override string ToString() =>
        $"SpecManagerCredential {{ ClientId = {ClientId}, SignedIn = {IsSignedIn}, AccountName = {AccountName} }}";
}
