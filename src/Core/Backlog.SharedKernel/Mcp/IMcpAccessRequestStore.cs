namespace Backlog.SharedKernel.Mcp;

/// <summary>
/// The agents' access requests the desktop app keeps (local ADR 0052 §5), beside the grants
/// (<see cref="IMcpGrantStore"/>). The store keeps them; the rules — one open request per name,
/// five open at once, a day's block after a decline — are the desk's that writes them.
/// </summary>
public interface IMcpAccessRequestStore
{
    /// <summary>Raised after a request is added or answered.</summary>
    event Action? Changed;

    /// <summary>Every request, answered ones included, in the order they were made.</summary>
    IReadOnlyList<McpAccessRequest> List();

    /// <summary>The request with <paramref name="id"/>, or null when there is none.</summary>
    McpAccessRequest? Find(string id);

    /// <summary>Stores a new open request for <paramref name="grantId"/>, with a new id.</summary>
    McpAccessRequest Add(
        string grantId,
        McpAccessRequestKind kind,
        string name,
        IReadOnlyList<string> scopes,
        string? executable,
        string? folder);

    /// <summary>Marks the open request allowed with <paramref name="allowed"/>, or declined, at
    /// the store's clock. Null when there is no such request or it is answered already: a
    /// request is answered once.</summary>
    McpAccessRequest? Answer(string id, McpAccessRequestState state, IReadOnlyList<string> allowed);
}
