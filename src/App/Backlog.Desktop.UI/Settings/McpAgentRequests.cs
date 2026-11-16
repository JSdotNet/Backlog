using System.Globalization;

using Backlog.SharedKernel.Mcp;
using Backlog.UI.Components.Settings;

// The component's published namespace, not the folder's: see the note at the top
// of Settings.razor for why the two must differ.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// What the MCP server page and the request window say about an agent's request for access
/// (local ADR 0052 §5): the line under its name, the switches it offers, which of them start
/// on, and the headline that counts it. Words and shapes only, so the screens and their tests
/// agree on them without rendering.
/// </summary>
public static class McpAgentRequests
{
    /// <summary>The waiting card's title.</summary>
    public const string WaitingCardTitle = "Waiting for your approval";

    /// <summary>What the waiting card says under its rows.</summary>
    public const string WaitingNote =
        "Allow saves the grant with the scopes switched on, after Windows Hello. Trim scopes opens the switches first. "
        + "Decline leaves the agent with no token and blocks its next request for a day.";

    /// <summary>
    /// The scopes a request offers as switches: the ones it asked for, each with its line, in
    /// <see cref="McpScopes.All"/>'s order, locked as the Agents card locks them: <c>agents:grant</c>
    /// starts off (<see cref="Initial"/>), and switching it on is confirmed by the Windows Hello
    /// that Allow asks for anyway, so a request cannot grant what Edit scopes cannot.
    /// </summary>
    public static IReadOnlyList<AgentScopeOption> Options(McpAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var asked = request.Scopes.ToHashSet(StringComparer.Ordinal);

        return [.. McpAgents.Scopes.Where(option => asked.Contains(option.Name))];
    }

    /// <summary>The switches on when a request is first shown: what it asked for, except
    /// <c>agents:grant</c>, which always starts off (local ADR 0052 §5).</summary>
    public static IReadOnlyList<string> Initial(McpAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return [.. McpAgents.Ordered(request.Scopes.Where(scope => scope != McpScopes.AgentsGrant))];
    }

    /// <summary>The line under the request's name: what it asks, from which program and
    /// folder, and how long ago.</summary>
    public static string Meta(McpAccessRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);

        var what = request.Kind == McpAccessRequestKind.Register ? "Registered itself" : "Asks for more";
        var parts = new List<string> { $"{what} {AiProvidersOverview.Ago(request.RequestedAt, now)}" };

        if (!string.IsNullOrWhiteSpace(request.Executable)) parts.Add($"from {request.Executable}");
        if (!string.IsNullOrWhiteSpace(request.Folder)) parts.Add($"in {request.Folder}");

        return string.Join(" · ", parts);
    }

    /// <summary>The request window's heading.</summary>
    public static string Heading(McpAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Kind == McpAccessRequestKind.Register
            ? $"{request.Name} wants access"
            : $"{request.Name} wants more access";
    }

    /// <summary>The headline while the listener holds its port and this host keeps grants: how
    /// many agents can reach Backlog, and how many requests wait for the person.</summary>
    public static string Headline(int agents, int waiting)
    {
        var reach = McpServerSettingsPage.AgentsHeadline(agents);
        if (waiting <= 0) return reach;

        var waits = waiting == 1 ? "1 is waiting for you" : $"{waiting.ToString(CultureInfo.InvariantCulture)} are waiting for you";

        return $"{reach.TrimEnd('.')}, and {waits}.";
    }

    /// <summary>The agents to list: every grant not revoked whose registration is not still
    /// waiting — a pending grant shows in the waiting card, not among the agents.</summary>
    public static IReadOnlyList<McpGrant> Agents(IEnumerable<McpGrant> grants, IReadOnlyList<McpAccessRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(requests);

        var pending = requests
            .Where(request => request.IsOpen && request.Kind == McpAccessRequestKind.Register)
            .Select(request => request.GrantId)
            .ToHashSet(StringComparer.Ordinal);

        return [.. grants.Where(grant => !grant.Revoked && !pending.Contains(grant.Id))];
    }
}
