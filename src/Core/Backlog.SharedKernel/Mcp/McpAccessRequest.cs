namespace Backlog.SharedKernel.Mcp;

/// <summary>
/// An agent asking the person for access (local ADR 0052 §5): a new agent that registered
/// itself, or an agent with a grant that wants more scopes. Only the person answers it, in the
/// desktop app; nothing the agent can call changes its own grant.
/// <para>
/// A registration's grant is created with the request, holding no scope, and mints no token
/// while its request is <see cref="McpAccessRequestState.Open"/>: that is what "pending" means.
/// A request for more scopes leaves its grant as it is until the person allows it.
/// </para>
/// </summary>
/// <param name="Id">The request's own id: <c>r_</c> and 16 random characters.</param>
/// <param name="GrantId">The grant the request is for: the pending grant a registration made,
/// or the existing grant that wants more.</param>
/// <param name="Kind">A new agent, or more scopes for one.</param>
/// <param name="Name">The agent's name, as it registered it.</param>
/// <param name="Scopes">The scopes it asked for, in the order <see cref="McpScopes.All"/> gives.</param>
/// <param name="Executable">The program that asked, as the broker read it; a label, not an identity.</param>
/// <param name="Folder">The folder it asked from, as the agent said it.</param>
/// <param name="RequestedAt">When it asked.</param>
/// <param name="State">Open until the person allows or declines it.</param>
/// <param name="AnsweredAt">When the person answered it, or null while it is open.</param>
/// <param name="Allowed">The scopes the person allowed, once allowed; empty otherwise.</param>
public sealed record McpAccessRequest(
    string Id,
    string GrantId,
    McpAccessRequestKind Kind,
    string Name,
    IReadOnlyList<string> Scopes,
    string? Executable,
    string? Folder,
    DateTimeOffset RequestedAt,
    McpAccessRequestState State,
    DateTimeOffset? AnsweredAt,
    IReadOnlyList<string> Allowed)
{
    /// <summary>What every request id starts with.</summary>
    public const string IdPrefix = "r_";

    /// <summary>Whether the person has still to answer it.</summary>
    public bool IsOpen => State == McpAccessRequestState.Open;
}

/// <summary>What an agent asked for.</summary>
public enum McpAccessRequestKind
{
    /// <summary>A new agent, which registered itself and holds a pending grant.</summary>
    Register,

    /// <summary>An agent with a grant, asking for scopes it does not hold.</summary>
    MoreScopes
}

/// <summary>Where a request stands.</summary>
public enum McpAccessRequestState
{
    /// <summary>Waiting for the person.</summary>
    Open,

    /// <summary>The person allowed it, with Windows Hello.</summary>
    Allowed,

    /// <summary>The person declined it; the name cannot ask again for a day.</summary>
    Declined
}
