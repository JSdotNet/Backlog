using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel.Mcp;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// Which scope each served tool requires (local ADR 0052 §4), beside the group table that says
/// which flag lists it (<see cref="BacklogMcpTools"/>).
/// <para>
/// <b>Per tool, not per group, because a group is a flag's unit and a scope is not.</b> The
/// tracker group holds reads and writes behind one switch; §4 gives them two scopes. So the
/// table names every tool, and a test holds it to <see cref="BacklogMcpTools.ToolNames"/>
/// both ways: a tool with no scope here is one no grant can call, which is fallback-deny doing
/// its job on a tool somebody forgot to map.
/// </para>
/// <para>
/// No tool requires <see cref="McpScopes.DevbookDecide"/>: approving and accepting are not
/// served over MCP at all (§7). <see cref="McpScopes.TelemetryWrite"/>,
/// <see cref="McpScopes.InboxCapture"/> and <see cref="McpScopes.AgentsGrant"/> have no tool
/// yet: the first guards a route, the second is a capture into the Inbox — write-only, so the
/// Inbox's reads and its archive are the backlog's <c>tasks:read</c> and <c>tasks:write</c> —
/// and the third the granting tools a later step adds.
/// </para>
/// </summary>
public static class McpToolScopes
{
    private static readonly Dictionary<string, string> ByTool = Build();

    /// <summary>Every served tool and the scope it requires.</summary>
    public static IReadOnlyDictionary<string, string> All => ByTool;

    /// <summary>
    /// The tools any admitted caller may call, whatever scopes it holds: <c>request_access</c>
    /// alone, because an agent that lacks a scope is the one that must ask for it (local ADR
    /// 0052 §5). Named here, beside the table, so a tool reaches no caller by being forgotten:
    /// one in neither still meets the fallback-deny.
    /// </summary>
    public static IReadOnlySet<string> AnyCaller { get; } =
        new HashSet<string>([AccessTools.RequestAccess], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The scope <paramref name="toolName"/> requires, or null for a tool this table does not
    /// name — which the call filter refuses rather than lets through. Matched without regard
    /// to case, for the reason <see cref="BacklogMcpTools.IsCallable"/> gives: a refusal must
    /// not rest on how loosely a dispatcher matches names.
    /// </summary>
    public static string? ScopeOf(string? toolName) =>
        toolName is not null && ByTool.TryGetValue(toolName, out var scope) ? scope : null;

    private static Dictionary<string, string> Build()
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Map(string scope, params IEnumerable<string> tools)
        {
            foreach (var tool in tools) table.Add(tool, scope);
        }

        Map(McpScopes.TasksRead,
            TrackerTools.FindItem, TrackerTools.ReadItem, TrackerTools.ListDevbookReferences,
            WorkTools.ListEntries, WorkTools.GetPlanItems,
            RoadmapTools.GetRoadmap,
            GoalTools.ListGoals,
            InboxTools.ListInboxItems, InboxTools.ReadInboxItem);

        Map(McpScopes.TasksWrite,
            TrackerTools.CreateItem, TrackerTools.Comment, TrackerTools.Transition, TrackerTools.SetBlocked,
            TrackerTools.SetDevbookReferences,
            GoalTools.CreateGoal, GoalTools.UpdateGoal, GoalTools.AttachToGoal, GoalTools.DetachFromGoal,
            ImpedimentTools.RaiseImpediment, ImpedimentTools.SetImpedimentState,
            ImpedimentTools.LinkToImpediment, ImpedimentTools.UnlinkFromImpediment,
            DevbookTools.CaptureNote,
            InboxTools.ArchiveInboxItem);

        Map(McpScopes.TasksAdmin, GoalTools.DeleteGoal);

        Map(McpScopes.DevbookRead,
            DevbookTools.ListKnowledgeContexts, DevbookTools.ReadKnowledgeChapter, DevbookTools.ListAnnotations,
            DecisionTools.GetState, DecisionTools.ListNotes,
            SyncDirectionTools.ListSyncDirections);

        // devbook_set_state is a write because over MCP it sets draft and nothing else (§7).
        Map(McpScopes.DevbookWrite,
            DevbookTools.ResolveAnnotation,
            DecisionTools.PostNote, DecisionTools.ResolveNote, DecisionTools.SetState);

        // The sessions inventory rides with the run tools: it reads what the delivery
        // surface reports into, behind the same flag.
        Map(McpScopes.RunsWrite,
            [.. DeliverySurfaceOperations.All,
            TrackerTools.LinkChange, TrackerTools.LinkSession,
            UsageTools.ListRunUsage,
            SessionTools.ListSessions]);

        Map(McpScopes.NotifyAsk,
            NotificationTools.Notify, NotificationTools.Ask, NotificationTools.GetAnswer, NotificationTools.Close);

        Map(McpScopes.UiOpen, WindowTools.OpenWindow);

        Map(McpScopes.SessionsDispatch,
            SessionRequestTools.ClaimSessionRequest, SessionRequestTools.MarkSessionRequest,
            SessionRequestTools.ListSessionRequests,
            SessionRequestTools.ClaimDelivery, SessionRequestTools.MarkDelivery, SessionRequestTools.ListDeliveries);

        return table;
    }
}
