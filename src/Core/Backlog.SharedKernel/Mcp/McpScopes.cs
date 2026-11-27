using System.Collections.Frozen;

namespace Backlog.SharedKernel.Mcp;

/// <summary>
/// The scopes a grant token claims (local ADR 0058 §4), one per area a tool group or route
/// serves. A scope decides which grant may call a tool that is listed; a tool group's feature
/// flag still decides whether it is listed at all (ADR 0012 §7). The names are a starting cut.
/// <para>
/// <see cref="DevbookDecide"/> is named so the boundary can be shown, and for nothing else: no
/// grant holds it, no policy admits it and no served tool needs it — approving and accepting a
/// devbook change are the person's, in the app (§7).
/// </para>
/// <para>
/// The Work scopes were named <c>tasks:*</c> before the Tasks context merged into Work (local
/// ADR 0073 §10). <see cref="Resolve(string)"/> reads an old name as its new one, so a grant
/// stored, a token minted or a scope asked for under the old name still means the new scope.
/// </para>
/// </summary>
public static class McpScopes
{
    /// <summary>Find, read and list backlog entries, plans, the roadmap and goals.</summary>
    public const string WorkRead = "work:read";

    /// <summary>Create, comment on, move and block entries; set goals and impediments.</summary>
    public const string WorkWrite = "work:write";

    /// <summary>Delete a goal and any bulk change.</summary>
    public const string WorkAdmin = "work:admin";

    /// <summary>The scope names retired by local ADR 0073 §10, each to the name it now goes by.</summary>
    public static IReadOnlyDictionary<string, string> Renamed { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["tasks:read"] = WorkRead,
        ["tasks:write"] = WorkWrite,
        ["tasks:admin"] = WorkAdmin,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The name <paramref name="scope"/> goes by now: its new name when it is one
    /// <see cref="Renamed"/> retired, else itself, spelling and all — null included, which every
    /// predicate here then refuses.</summary>
    public static string Resolve(string scope) =>
        scope is not null && Renamed.TryGetValue(scope, out var renamed) ? renamed : scope!;

    /// <summary><paramref name="scopes"/> under the names they go by now, in their order, each
    /// once: an old name and its new one asked for together are one scope.</summary>
    public static IReadOnlyList<string> Resolve(IEnumerable<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        return [.. scopes.Select(Resolve).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Read devbook chapters, their notes, states and sync directions.</summary>
    public const string DevbookRead = "devbook:read";

    /// <summary>Post and resolve notes; set a devbook subject's state to draft.</summary>
    public const string DevbookWrite = "devbook:write";

    /// <summary>Approve, accept, decide or decline. In no grant and served by no tool.</summary>
    public const string DevbookDecide = "devbook:decide";

    /// <summary>The delivery surface, the <c>link_*</c> tools and the run figures.</summary>
    public const string RunsWrite = "runs:write";

    /// <summary>Notify the person, ask a question and read its answer.</summary>
    public const string NotifyAsk = "notify:ask";

    /// <summary>Open a surface in a window, and the link tokens that opens.</summary>
    public const string UiOpen = "ui:open";

    /// <summary>Claim and mark session requests and answer deliveries, and the wait route.</summary>
    public const string SessionsDispatch = "sessions:dispatch";

    /// <summary>Claude Code's OpenTelemetry logs route.</summary>
    public const string TelemetryWrite = "telemetry:write";

    /// <summary>The Inbox's captures.</summary>
    public const string InboxCapture = "inbox:capture";

    /// <summary>Grant, trim and revoke other agents, within the granter's own scopes.</summary>
    public const string AgentsGrant = "agents:grant";

    /// <summary>Every scope §4 names, <see cref="DevbookDecide"/> included, in its order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        WorkRead, WorkWrite, WorkAdmin,
        DevbookRead, DevbookWrite, DevbookDecide,
        RunsWrite, NotifyAsk, UiOpen, SessionsDispatch, TelemetryWrite, InboxCapture, AgentsGrant
    ];

    /// <summary>Every scope a grant may hold: <see cref="All"/> without <see cref="DevbookDecide"/>.</summary>
    public static IReadOnlyList<string> Grantable { get; } = [.. All.Where(scope => scope != DevbookDecide)];

    /// <summary>Whether a grant may hold <paramref name="scope"/>: one of <see cref="Grantable"/>,
    /// spelled exactly, or the old name of one (<see cref="Resolve(string)"/>).</summary>
    public static bool IsGrantable(string? scope) => scope is not null && Grantable.Contains(Resolve(scope), StringComparer.Ordinal);

    /// <summary>The scopes that guard one route or one hook and no tool an agent works with:
    /// the telemetry exporter's logs route and the Question hook's asks. A grant holding nothing
    /// beyond them may not call <c>request_access</c> either, so a leaked Telemetry or Question
    /// hook block cannot ask for more (local ADR 0024).</summary>
    public static IReadOnlyList<string> SinglePurpose { get; } = [TelemetryWrite, NotifyAsk];

    /// <summary>Whether a caller holding <paramref name="held"/> may ask for more scopes: it holds
    /// at least one grantable scope outside <see cref="SinglePurpose"/>.</summary>
    public static bool MayRequestAccess(IEnumerable<string> held)
    {
        ArgumentNullException.ThrowIfNull(held);
        return held.Any(scope => IsGrantable(scope) && !SinglePurpose.Contains(Resolve(scope), StringComparer.Ordinal));
    }

    /// <summary>The scopes an agent never grants another (local ADR 0058 §5): deciding, which no
    /// grant holds; granting, so delegation stays one level deep; and the admin scope, which asks
    /// the person for Windows Hello each time.</summary>
    public static IReadOnlyList<string> NonDelegable { get; } = [DevbookDecide, AgentsGrant, WorkAdmin];

    /// <summary>Whether a granter agent may hand <paramref name="scope"/> on: grantable and not
    /// in <see cref="NonDelegable"/>.</summary>
    public static bool IsDelegable(string? scope) => IsGrantable(scope) && !NonDelegable.Contains(Resolve(scope!), StringComparer.Ordinal);

    /// <summary>The scopes the Unauthenticated row draws locked (local ADR 0059 §2), in
    /// <see cref="All"/>'s order: deletes and bulk changes, which need a caller that answers for
    /// them; granting; dispatching sessions, which run code; deciding, which no agent does; the
    /// telemetry exporter's own route; and pop-out links, whose cookie outlives the call. The
    /// listener refuses them to a tokenless call even where a hand edit put them in the row.</summary>
    public static IReadOnlyList<string> NeverUnauthenticated { get; } =
        [WorkAdmin, DevbookDecide, UiOpen, SessionsDispatch, TelemetryWrite, AgentsGrant];

    /// <summary>The scopes the Unauthenticated row offers as switches (local ADR 0059 §2):
    /// <see cref="Grantable"/> without <see cref="NeverUnauthenticated"/>, in its order.</summary>
    public static IReadOnlyList<string> UnauthenticatedOfferable { get; } =
        [.. Grantable.Where(scope => !NeverUnauthenticated.Contains(scope, StringComparer.Ordinal))];

    /// <summary>Whether the Unauthenticated row may hold <paramref name="scope"/>: one of
    /// <see cref="UnauthenticatedOfferable"/>, spelled exactly, or the old name of one.</summary>
    public static bool IsUnauthenticatedOfferable(string? scope) =>
        scope is not null && UnauthenticatedOfferable.Contains(Resolve(scope), StringComparer.Ordinal);
}
