using System.ComponentModel;

using Backlog.SharedKernel.Mcp;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// <c>request_access</c>, the one tool an agent asks the person for more through (local ADR
/// 0052 §5): it opens a request for scopes the calling grant does not hold and brings
/// Backlog's request window forward. It changes no grant — the caller's least of all — because
/// only the person answers a request, in the desktop window, with Windows Hello.
/// <para>
/// <b>Any grant may call it, whatever its scopes</b> (<see cref="McpToolScopes.AnyCaller"/>):
/// an agent that lacks a scope is exactly the one that needs to ask. A caller that holds no
/// grant — the shared token, the harness — is refused by the host, which has no grant to add
/// to.
/// </para>
/// <para>
/// <b>Ungated</b>, like <c>open_window</c>: asking adds nothing and removes nothing, and a host
/// that keeps no grants composes no <see cref="IAgentAccessRequests"/>, so the tool says that.
/// </para>
/// </summary>
/// <param name="call">The principal the listener put on this call.</param>
/// <param name="requests">The host's requests, or null in a host that keeps no grants.</param>
[McpServerToolType]
public sealed class AccessTools(McpCallContext call, IAgentAccessRequests? requests = null)
{
    internal const string RequestAccess = "request_access";

    /// <summary>The group <c>request_access</c> is served in: behind no key.</summary>
    public static BacklogMcpTools.McpToolGroup Group { get; } = new(
        FeatureKey: null,
        typeof(AccessTools),
        [RequestAccess]);

    [McpServerTool(
        Name = RequestAccess,
        Title = "Ask the person for more access",
        ReadOnly = false,
        Idempotent = false,
        Destructive = false,
        OpenWorld = false)]
    [Description(
        "Asks the person to give this agent's grant more scopes, and brings Backlog's request window forward on "
        + "their desktop. It changes nothing by itself: only the person allows a request, in that window, with "
        + "Windows Hello, and may trim the scopes first. Ask for the scopes a refused tool named, such as tasks:write. "
        + "One request per agent is open at a time; asking again while one is open brings its window forward. "
        + "After a decline, the agent cannot ask again for a day. Answers the request's id; the new scopes arrive "
        + "with the agent's next token once allowed.")]
    public async Task<AccessRequestedPayload> RequestAsync(
        [Description("The scopes to ask for, such as [\"tasks:write\", \"runs:write\"].")]
        string[] scopes,
        [Description("Optional. The folder this agent works in, shown to the person beside the request.")]
        string? folder = null,
        CancellationToken cancellationToken = default)
    {
        if (requests is null)
        {
            throw RepositoryScope.Failure(Error.Validation(
                "access.not_available",
                "This host keeps no agent grants, so there is nothing to ask for: every caller reaches every tool here."));
        }

        if (scopes is not { Length: > 0 })
        {
            throw RepositoryScope.Failure(Error.Validation("access.scopes_required", "Name at least one scope to ask for."));
        }

        var answer = await requests
            .RequestMoreAsync(call.User, [.. scopes.Select(scope => scope?.Trim() ?? string.Empty)], folder, cancellationToken)
            .ConfigureAwait(false);

        if (answer.Refused)
        {
            throw RepositoryScope.Failure(Error.Validation("access." + answer.Refusal!.Replace('-', '_'), answer.Message));
        }

        return new AccessRequestedPayload(answer.RequestId!, answer.WindowOpened, answer.Message);
    }
}

/// <summary>What <c>request_access</c> answers.</summary>
/// <param name="RequestId">The request now waiting for the person.</param>
/// <param name="WindowOpened">Whether its window is on the person's screen.</param>
/// <param name="Answer">The sentence to report.</param>
public sealed record AccessRequestedPayload(string RequestId, bool WindowOpened, string Answer);
