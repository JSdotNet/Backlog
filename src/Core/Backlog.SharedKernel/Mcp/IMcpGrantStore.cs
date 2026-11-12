namespace Backlog.SharedKernel.Mcp;

/// <summary>
/// The grants the desktop app keeps (local ADR 0052 §3). The token broker mints only for a
/// grant found here and not revoked, and the listener checks every grant token against what
/// is stored now, which is why a revoke reaches a token already handed out.
/// </summary>
public interface IMcpGrantStore
{
    /// <summary>Raised after a grant is created or revoked.</summary>
    event Action? Changed;

    /// <summary>Every grant, revoked ones included, in the order they were created.</summary>
    IReadOnlyList<McpGrant> List();

    /// <summary>The grant with <paramref name="id"/>, or null when there is none.</summary>
    McpGrant? Find(string id);

    /// <summary>A new grant at version 1, with a new id and its own sealed secret.</summary>
    McpGrant Create(string name, IReadOnlyList<string> scopes, string createdBy);

    /// <summary>Marks the grant revoked and raises its version, so a token minted before the
    /// revoke is refused on its next request. Null when there is no such grant.</summary>
    McpGrant? Revoke(string id);
}
