namespace Backlog.Modules.Sync.DomainModels;

/// <summary>Which step of a connector authorization a record stands for.</summary>
public enum ConnectorAuthorizationKind
{
    /// <summary>An authorize request that left for GitHub. Keyed by the hash of the
    /// service's own state.</summary>
    SignIn,

    /// <summary>The owner signed in and the consent page is showing. Keyed by the hash of
    /// the one-time consent ticket the page posts back.</summary>
    Consent,

    /// <summary>The owner allowed it: an authorization code on its way to claude.ai. Keyed
    /// by the hash of the code.</summary>
    Code,
}

/// <summary>
/// One step of a connector authorization (local ADR 0058 §8), stored by hash the way an
/// <see cref="EnrolmentTicket"/> is. Short-lived — minutes — and burned once.
/// </summary>
/// <param name="KeyHash">SHA-256 of the state, the ticket or the code, hex.</param>
/// <param name="Kind">Which step this is.</param>
/// <param name="CodeChallenge">The S256 challenge claude.ai started with.</param>
/// <param name="RedirectUri">The registered redirect the answer goes to.</param>
/// <param name="ClientState">claude.ai's own state, handed back beside the answer; null when it sent none.</param>
/// <param name="Scope">The scope asked for, space-separated.</param>
/// <param name="Resource">The resource the token is for: the <c>/mcp</c> URL.</param>
/// <param name="ClientGeneration">The client generation the request began under.</param>
/// <param name="FamilyId">The refresh-token family a code starts, so a code used twice can
/// revoke what its first use issued.</param>
/// <param name="ExpiresAt">When it stops working.</param>
/// <param name="Owner">The owner who signed in; null until the GitHub sign-in.</param>
/// <param name="Redeemed">Set once, by the call that consumed it.</param>
public sealed record ConnectorAuthorization(
    string KeyHash,
    ConnectorAuthorizationKind Kind,
    string CodeChallenge,
    string RedirectUri,
    string? ClientState,
    string Scope,
    string Resource,
    int ClientGeneration,
    Guid FamilyId,
    DateTimeOffset ExpiresAt,
    OwnerId? Owner = null,
    bool Redeemed = false)
{
    /// <summary>Whether the window has closed at <paramref name="now"/>.</summary>
    public bool HasExpired(DateTimeOffset now) => now >= ExpiresAt;
}
