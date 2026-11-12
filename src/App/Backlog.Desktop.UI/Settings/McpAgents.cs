using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Backlog.SharedKernel.Mcp;
using Backlog.UI.Components.Settings;

// The component's published namespace, not the folder's: see the note at the top
// of Settings.razor for why the two must differ.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// What the MCP server page's Agents card says about a grant (local ADR 0052 §3 and §4): the
/// scopes it offers as switches and the line each one carries, the roles that fill those
/// switches in, the line under an agent's name, and the registration a new agent is given.
/// Words and shapes only, so the page and its tests agree on them without rendering.
/// <para>
/// <b>A role is a preset, not a stored fact.</b> The grant stores scopes; the row names the
/// role whose preset the scopes equal, and Custom when none does — so trimming one switch
/// turns a Coding agent into Custom, exactly as it would have in the Add agent form.
/// </para>
/// </summary>
public static class McpAgents
{
    /// <summary>What a grant made on the Agents card records as its creator, so its row can
    /// say where it came from.</summary>
    public const string CreatedInSettings = "Settings";

    /// <summary>The scopes drawn locked: <see cref="McpScopes.DevbookDecide"/>, which no grant
    /// holds and no tool serves (ADR 0052 §7), so the boundary shows; and
    /// <see cref="McpScopes.AgentsGrant"/>, until switching it on asks for Windows Hello (ADR 0052
    /// §5 and §6), which the delegation step brings.</summary>
    public static IReadOnlyList<string> Locked { get; } = [McpScopes.DevbookDecide, McpScopes.AgentsGrant];

    /// <summary>What each scope lets the agent do, one line per name in <see cref="McpScopes.All"/>.</summary>
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        [McpScopes.TasksRead] = "Find and read items, goals, the roadmap",
        [McpScopes.TasksWrite] = "Create, comment, transition, block",
        [McpScopes.TasksAdmin] = "Delete goals, bulk changes; Windows Hello each time",
        [McpScopes.DevbookRead] = "Chapters, notes, states",
        [McpScopes.DevbookWrite] = "Post and resolve notes, set to draft",
        [McpScopes.DevbookDecide] = "Never for an agent: approve and accept happen in the app",
        [McpScopes.RunsWrite] = "Report delivery runs",
        [McpScopes.NotifyAsk] = "Ask you a question, tell you something",
        [McpScopes.UiOpen] = "Open a Backlog window or page",
        [McpScopes.SessionsDispatch] = "Only for the Manager session",
        [McpScopes.TelemetryWrite] = "Send log records; only for the telemetry hook",
        [McpScopes.InboxCapture] = "Capture into your Inbox",
        [McpScopes.AgentsGrant] = "Grant other agents a subset of its own scopes; not switchable yet, it will ask for Windows Hello",
    };

    /// <summary>Every scope ADR 0052 §4 names, as <see cref="McpScopes.All"/> orders them, with
    /// what each lets the agent do and whether it is drawn locked. A scope added there without a
    /// line here fails the first read of this list.</summary>
    public static IReadOnlyList<AgentScopeOption> Scopes { get; } =
        [.. McpScopes.All.Select(name => new AgentScopeOption(name, Descriptions[name], Locked.Contains(name, StringComparer.Ordinal)))];

    /// <summary>The scopes drawn in the alert tone on a row: the ones that reach furthest.</summary>
    public static IReadOnlyList<string> Highlighted { get; } = [McpScopes.AgentsGrant, McpScopes.TasksAdmin];

    /// <summary>The role that matches no preset, and the one a changed switch turns any role into.</summary>
    public const string Custom = "Custom";

    /// <summary>The roles the Add agent form offers, each with the scopes it fills in, Custom last.
    /// Every preset holds only <see cref="McpScopes.Grantable"/> scopes that are not locked.</summary>
    public static IReadOnlyList<McpAgentRole> Roles { get; } =
    [
        new("Coding agent", [McpScopes.TasksRead, McpScopes.TasksWrite, McpScopes.DevbookRead, McpScopes.DevbookWrite, McpScopes.NotifyAsk, McpScopes.UiOpen]),
        new("Delivery flow", [McpScopes.TasksRead, McpScopes.TasksWrite, McpScopes.RunsWrite, McpScopes.DevbookRead, McpScopes.NotifyAsk]),
        new("Read-only + capture", [McpScopes.TasksRead, McpScopes.DevbookRead, McpScopes.InboxCapture]),
        new("Manager", [McpScopes.SessionsDispatch, McpScopes.TasksRead, McpScopes.TasksWrite]),
        new(Custom, []),
    ];

    /// <summary>The scopes a grant may be written with: <paramref name="scopes"/> in the switches'
    /// order, less any the store would refuse — a name a hand-written grant carries that no
    /// policy knows grants nothing, and is dropped on the next change rather than refusing it.</summary>
    public static IReadOnlyList<string> Writable(IEnumerable<string> scopes) =>
        [.. Ordered(scopes).Where(McpScopes.IsGrantable)];

    /// <summary>The scopes switched on, in the switches' order: the order a grant stores them
    /// in, whatever order they were flipped.</summary>
    public static IReadOnlyList<string> Ordered(IEnumerable<string> scopes)
    {
        var on = scopes.ToHashSet(StringComparer.Ordinal);
        var known = Scopes.Select(scope => scope.Name).Where(on.Contains);
        var unknown = on.Where(scope => Scopes.All(option => option.Name != scope)).Order(StringComparer.Ordinal);

        return [.. known, .. unknown];
    }

    /// <summary>The role whose preset is exactly <paramref name="scopes"/>, else Custom.</summary>
    public static string RoleOf(IEnumerable<string> scopes)
    {
        var held = scopes.ToHashSet(StringComparer.Ordinal);

        return Roles.FirstOrDefault(role => role.Name != Custom && held.SetEquals(role.Scopes))?.Name ?? Custom;
    }

    /// <summary>The preset scopes of <paramref name="role"/>, or null for Custom or a name no
    /// role has: those keep the switches as they are.</summary>
    public static IReadOnlyList<string>? PresetOf(string? role) =>
        Roles.FirstOrDefault(candidate => candidate.Name == role && candidate.Name != Custom)?.Scopes;

    /// <summary>Where a grant came from, as its row says it.</summary>
    public static string? Source(McpGrant grant) => grant.CreatedBy switch
    {
        CreatedInSettings => "added in Settings",
        { } who when !string.IsNullOrWhiteSpace(who) => $"created by {who}",
        _ => null,
    };

    /// <summary>The line under an agent's name: its role, where it came from, and when it was
    /// last used — or that it has not been.</summary>
    public static string Meta(McpGrant grant, DateTimeOffset? lastUsed, DateTimeOffset now)
    {
        var used = lastUsed is { } at ? $"last used {AiProvidersOverview.Ago(at, now)}" : "not used yet";
        var source = Source(grant);

        return source is null
            ? $"Role: {RoleOf(grant.Scopes)} · {used}"
            : $"Role: {RoleOf(grant.Scopes)} · {source} · {used}";
    }

    /// <summary>
    /// When each grant last made a call, from the audit log: the newest record whose caller
    /// names the grant's id — <c>McpScopePolicies.GrantCaller</c> writes <c>name (id)</c>. By id
    /// and not by name, so an agent added under a revoked agent's name never borrows its time.
    /// A grant with none is not in the map.
    /// </summary>
    public static IReadOnlyDictionary<string, DateTimeOffset> LastUsed(
        IEnumerable<McpGrant> grants,
        IReadOnlyList<McpAuditRecord> newestFirst)
    {
        var used = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        foreach (var grant in grants)
        {
            var record = newestFirst.FirstOrDefault(call => NamesGrant(call.Caller, grant.Id));

            if (record is not null) used[grant.Id] = record.At;
        }

        return used;
    }

    /// <summary>Whether an audit caller is the grant with <paramref name="id"/>: the id alone, or
    /// a name followed by the id in brackets.</summary>
    private static bool NamesGrant(string caller, string id) =>
        string.Equals(caller, id, StringComparison.Ordinal)
        || caller.EndsWith($"({id})", StringComparison.Ordinal);

    /// <summary>The headers helper a registration runs, naming the grant: the repository's own
    /// <c>tools/mcp/backlog-mcp-headers.mjs</c>, with <c>--debug</c> for a Debug build, whose
    /// broker listens on a pipe of its own.</summary>
    public static string HeadersHelper(string grantId, bool debug) =>
        $"node tools/mcp/backlog-mcp-headers.mjs --grant {grantId}{(debug ? " --debug" : string.Empty)}";

    /// <summary>The <c>backlog</c> entry as Claude Code's <c>mcpServers</c> takes it — a
    /// repository's <c>.mcp.json</c> or the user's own configuration.</summary>
    public static string ClaudeCode(Uri endpoint, string grantId, bool debug) => Entry(new JsonObject
    {
        ["type"] = "http",
        ["url"] = endpoint.ToString(),
        ["headersHelper"] = HeadersHelper(grantId, debug),
    });

    /// <summary>The <c>backlog</c> entry as the Copilot CLI's <c>mcp-config.json</c> takes it:
    /// Claude Code's, with the <c>tools</c> list Copilot asks every server for.</summary>
    public static string Copilot(Uri endpoint, string grantId, bool debug) => Entry(new JsonObject
    {
        ["type"] = "http",
        ["url"] = endpoint.ToString(),
        ["headersHelper"] = HeadersHelper(grantId, debug),
        ["tools"] = new JsonArray("*"),
    });

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>One <c>"backlog": { … }</c> member, ready to paste inside an <c>mcpServers</c>
    /// object, indented two spaces the way both files are.</summary>
    private static string Entry(JsonObject server)
    {
        var body = server.ToJsonString(Indented).ReplaceLineEndings("\n");

        return string.Create(CultureInfo.InvariantCulture, $"\"backlog\": {body}");
    }
}

/// <summary>A role the Add agent form offers, and the scopes it fills the switches with.</summary>
public sealed record McpAgentRole(string Name, IReadOnlyList<string> Scopes);
