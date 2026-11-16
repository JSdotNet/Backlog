namespace Backlog.SharedKernel.Mcp;

/// <summary>
/// The scopes a grant token claims (local ADR 0052 §4), one per area a tool group or route
/// serves. A scope decides which grant may call a tool that is listed; a tool group's feature
/// flag still decides whether it is listed at all (ADR 0012 §7). The names are a starting cut.
/// <para>
/// <see cref="DevbookDecide"/> is named so the boundary can be shown, and for nothing else: no
/// grant holds it, no policy admits it and no served tool needs it — approving and accepting a
/// devbook change are the person's, in the app (§7).
/// </para>
/// </summary>
public static class McpScopes
{
    /// <summary>Find, read and list backlog entries, plans, the roadmap and goals.</summary>
    public const string TasksRead = "tasks:read";

    /// <summary>Create, comment on, move and block entries; set goals and impediments.</summary>
    public const string TasksWrite = "tasks:write";

    /// <summary>Delete a goal and any bulk change.</summary>
    public const string TasksAdmin = "tasks:admin";

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
        TasksRead, TasksWrite, TasksAdmin,
        DevbookRead, DevbookWrite, DevbookDecide,
        RunsWrite, NotifyAsk, UiOpen, SessionsDispatch, TelemetryWrite, InboxCapture, AgentsGrant
    ];

    /// <summary>Every scope a grant may hold: <see cref="All"/> without <see cref="DevbookDecide"/>.</summary>
    public static IReadOnlyList<string> Grantable { get; } = [.. All.Where(scope => scope != DevbookDecide)];

    /// <summary>Whether a grant may hold <paramref name="scope"/>: one of <see cref="Grantable"/>,
    /// spelled exactly.</summary>
    public static bool IsGrantable(string? scope) => scope is not null && Grantable.Contains(scope, StringComparer.Ordinal);

    /// <summary>The scopes an agent never grants another (local ADR 0052 §5): deciding, which no
    /// grant holds; granting, so delegation stays one level deep; and the admin scope, which asks
    /// the person for Windows Hello each time.</summary>
    public static IReadOnlyList<string> NonDelegable { get; } = [DevbookDecide, AgentsGrant, TasksAdmin];

    /// <summary>Whether a granter agent may hand <paramref name="scope"/> on: grantable and not
    /// in <see cref="NonDelegable"/>.</summary>
    public static bool IsDelegable(string? scope) => IsGrantable(scope) && !NonDelegable.Contains(scope!, StringComparer.Ordinal);
}
