namespace Backlog.SharedKernel.Mcp;

/// <summary>
/// The grants the desktop app keeps (local ADR 0052 §3). The token broker mints only for a
/// grant found here and not revoked, and the listener checks every grant token against what
/// is stored now, which is why a revoke reaches a token already handed out.
/// </summary>
public interface IMcpGrantStore
{
    /// <summary>Raised after a grant is created, given new scopes or revoked.</summary>
    event Action? Changed;

    /// <summary>Every grant, revoked ones included, in the order they were created.</summary>
    IReadOnlyList<McpGrant> List();

    /// <summary>The grant with <paramref name="id"/>, or null when there is none.</summary>
    McpGrant? Find(string id);

    /// <summary>A new grant at version 1, with a new id and its own sealed secret. Refuses
    /// any scope outside <see cref="McpScopes.Grantable"/> — <see cref="McpScopes.DevbookDecide"/>
    /// among them — with an <see cref="ArgumentException"/> (local ADR 0052 §4).</summary>
    McpGrant Create(string name, IReadOnlyList<string> scopes, string createdBy);

    /// <summary>Replaces the grant's scopes and raises its version, so a token minted under the
    /// old scopes is refused on its next request and the client's headers helper fetches one
    /// with the new scopes — and raises the version of every grant it delegated, whose tokens are
    /// cut to its scopes. Null when there is no such grant, or it is revoked: a revoked grant
    /// stays as it was revoked. Refuses any scope outside <see cref="McpScopes.Grantable"/> with
    /// an <see cref="ArgumentException"/>, as <see cref="Create"/> does.</summary>
    McpGrant? SetScopes(string id, IReadOnlyList<string> scopes);

    /// <summary>
    /// Makes the grant <paramref name="id"/> a delegated one (local ADR 0052 §5): granted by the
    /// grant <paramref name="grantedBy"/>, holding <paramref name="scopes"/> until
    /// <paramref name="expiresAt"/>, its version raised. The store records it; the rules a
    /// granter is held to are the access desk's that calls it. Null when there is no such grant,
    /// or it is revoked, or when <paramref name="grantedBy"/> is missing, revoked or itself
    /// delegated. Refuses any scope that is not <see cref="McpScopes.IsDelegable"/>.
    /// </summary>
    McpGrant? Delegate(string id, IReadOnlyList<string> scopes, string grantedBy, DateTimeOffset expiresAt);

    /// <summary>Marks the grant revoked and raises its version, so a token minted before the
    /// revoke is refused on its next request — and with it, in the same write, every grant it
    /// delegated (local ADR 0052 §5). Null when there is no such grant.</summary>
    McpGrant? Revoke(string id);
}
