namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// Every tool here that writes, and the notice that refreshes the pages showing what it changed.
/// <para>
/// An MCP call runs in a scope of the desktop container of its own, and a window has scopes of
/// its own, so a page hears a write made over MCP only through a singleton notice the owning
/// module raises on every save, whoever the caller is; the module's published port holds its
/// <c>Changed</c> on that notice. That is the convention every context follows. This table is
/// where it is enforced: <c>BacklogMcpToolsTests</c> reads every tool attributed as a write and
/// fails for one not named here, so a new write tool cannot land without somebody saying how
/// its pages hear of it — or that they do not yet, and why.
/// </para>
/// <para>
/// Data only: nothing here subscribes or raises. The values are words for a reader, naming the
/// notice or the reason there is none.
/// </para>
/// </summary>
internal static class McpRefreshMap
{
    internal const string TaskChanges =
        "ITaskChangeSignal, raised by the task repository on every write: the Tasks pane, the Roadmap band, the Goals view, and their pop-outs";

    internal const string GoalChanges = "IGoals.Changed, held on GoalSaved: the Goals view and goal attachments";

    internal const string ImpedimentChanges = "IImpediments.Changed, held on ImpedimentSaved: the Goals view and the stand-up's impediments";

    internal const string InboxChanges = "IInboxChangeSignal, raised by InboxTools once a write lands: the Inbox pane and surface";

    internal const string NotificationChanges =
        "NotificationCentre.Changed, over the repository every notification write goes through: the notification centre, the waiting badge, settings";

    internal const string AnnotationChanges = "IDevbookAnnotationStore.Changed, a singleton raised on every write: the Devbook panels' notes";

    internal const string SubjectStateChanges = "IDevbookSubjectStateStore.Changed, a singleton raised on every write: the Devbook panels' subject state";

    internal const string BrainChanges = "BrainFolderSource.NotifyContentChanged, called by the note writer and the folder watcher: the Brain panels";

    internal const string SessionRequestsPolled =
        "None raised: TasksSessionRequests polls the queue every 5 seconds and raises on a difference: the Tasks session queue and row offers, Roadmap plan requests";

    internal const string NoData = "None: opens a window and changes no data";

    internal const string EventsPaneChanged =
        "IEventsPane.Changed, held on KnowledgeEventSaved: the Events pane, in the main window and popped out";

    internal const string AgentChanges =
        "IMcpGrantStore.Changed and IMcpAccessRequestStore.Changed, which McpAccessDesk.Changed relays: the MCP server settings page's Agents card and the agent request window";

    internal const string SessionsNotRefreshedYet =
        "Sessions pane: not refreshed yet - separate change (its list is read on request)";

    /// <summary>Tool name to the notice that refreshes its pages.</summary>
    internal static IReadOnlyDictionary<string, string> Notices { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [TrackerTools.Transition] = TaskChanges,
        [TrackerTools.SetBlocked] = TaskChanges,
        [TrackerTools.Comment] = TaskChanges,
        [TrackerTools.LinkChange] = TaskChanges,
        [TrackerTools.LinkSession] = TaskChanges,
        [TrackerTools.CreateItem] = TaskChanges,
        [TrackerTools.SetDevbookReferences] = TaskChanges,

        [GoalTools.CreateGoal] = GoalChanges,
        [GoalTools.UpdateGoal] = GoalChanges,
        [GoalTools.AttachToGoal] = GoalChanges,
        [GoalTools.DetachFromGoal] = GoalChanges,
        [GoalTools.DeleteGoal] = GoalChanges,

        [ImpedimentTools.RaiseImpediment] = ImpedimentChanges,
        [ImpedimentTools.SetImpedimentState] = ImpedimentChanges,
        [ImpedimentTools.LinkToImpediment] = ImpedimentChanges,
        [ImpedimentTools.UnlinkFromImpediment] = ImpedimentChanges,

        [InboxTools.CaptureToInbox] = InboxChanges,
        [InboxTools.ArchiveInboxItem] = InboxChanges,

        [NotificationTools.Notify] = NotificationChanges,
        [NotificationTools.Ask] = NotificationChanges,
        [NotificationTools.GetAnswer] = NotificationChanges,
        [NotificationTools.Close] = NotificationChanges,

        [DevbookTools.ResolveAnnotation] = AnnotationChanges,
        [DevbookTools.CaptureNote] = BrainChanges,

        [DecisionTools.SetState] = SubjectStateChanges,
        [DecisionTools.PostNote] = AnnotationChanges,
        [DecisionTools.ResolveNote] = AnnotationChanges,

        [SessionRequestTools.ClaimSessionRequest] = SessionRequestsPolled,
        [SessionRequestTools.MarkSessionRequest] = SessionRequestsPolled,
        [SessionRequestTools.ClaimDelivery] = SessionRequestsPolled,
        [SessionRequestTools.MarkDelivery] = SessionRequestsPolled,

        // The run writes: the Sessions pane reads its list on request and nothing polls
        // (.devbook/domain/sessions/features.md#re-read-on-request). Making it live is a change
        // of its own, which replaces these entries.
        [SurfaceTools.StartRun] = SessionsNotRefreshedYet,
        [SurfaceTools.RecordPrompt] = SessionsNotRefreshedYet,
        [SurfaceTools.SetRunContext] = SessionsNotRefreshedYet,
        [SurfaceTools.UpdateStage] = SessionsNotRefreshedYet,
        [SurfaceTools.FinishRun] = SessionsNotRefreshedYet,

        // The loop check-in and the schedule run report land in the Sessions pane's Loops and
        // Schedules sections, read with its list on request: the same separate change covers them.
        [LoopTools.CheckIn] = SessionsNotRefreshedYet,
        [ScheduleTools.ReportScheduleRun] = SessionsNotRefreshedYet,

        [SurfaceTools.OpenDashboard] = NoData,
        [WindowTools.OpenWindow] = NoData,

        [EventTools.AddKnowledgeEvent] = EventsPaneChanged,

        // The agent grants (local ADR 0058 §5): a request, a grant, a trim or a revoke.
        [AccessTools.RequestAccess] = AgentChanges,
        [GrantTools.GrantAgent] = AgentChanges,
        [GrantTools.EditAgentScopes] = AgentChanges,
        [GrantTools.RevokeAgent] = AgentChanges,
    };
}
