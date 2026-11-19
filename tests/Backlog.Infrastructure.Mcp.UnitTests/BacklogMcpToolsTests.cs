using System.ComponentModel;
using System.Reflection;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The catalog the hosts gate on, and the attributes it claims to describe.
/// <para>
/// The catalog is a second statement of what the classes below it say, which is
/// the sort of thing that drifts silently — a tool renamed in an attribute and
/// not in the table would be a tool the filter never hides. So the table is
/// checked against the attributes rather than trusted.
/// </para>
/// </summary>
public class BacklogMcpToolsTests
{
    [Fact]
    public void The_published_tools_are_the_ones_the_items_name()
    {
        Assert.Equal(
            [
                "list_entries",
                "get_plan_items",
                "find_item",
                "read_item",
                "transition",
                "set_blocked",
                "comment",
                "link_change",
                "link_session",
                "create_item",
                "set_devbook_references",
                "list_devbook_references",
                "get_roadmap",
                "list_knowledge_contexts",
                "read_knowledge_chapter",
                "list_annotations",
                "resolve_annotation",
                "capture_note",
                "list_brain_reviews",

                // devbook.decision@1's seven operations — four on state, three on
                // notes — beside the devbook tools and behind the same key.
                "devbook_get_state",
                "devbook_set_state",
                "devbook_approve",
                "devbook_accept",
                "devbook_list_notes",
                "devbook_post_note",
                "devbook_resolve_note",

                // The sync direction map the sweeps read, behind the same key.
                "list_sync_directions",

                "list_sessions",

                // The delivery surface's eight, in the order the engine's
                // capability lists them. Spelled out here rather than deferred to
                // DeliverySurfaceOperations.All, which is what the catalog itself
                // uses: a test that asserted the catalog equals the constant the
                // catalog is built from would assert nothing.
                "open_dashboard",
                "start_run",
                "record_prompt",
                "set_run_context",
                "update_stage",
                "finish_run",
                "list_runs",
                "get_run",

                // The measured usage of every run, beside the surface rather than on it.
                "list_run_usage",

                // A scheduled run's report (local ADR 0051).
                "report_schedule_run",

                // Progress's impediments: raise, move, link and unlink what they hold back (local ADR 0035).
                "raise_impediment",
                "set_impediment_state",
                "link_to_impediment",
                "unlink_from_impediment",

                // Progress's goals: read them and their iterations, set, edit, attach, detach,
                // delete (local ADR 0035).
                "list_goals",
                "read_iterations",
                "create_goal",
                "update_goal",
                "attach_to_goal",
                "detach_from_goal",
                "delete_goal",

                // The dispatcher tools of the session request queue (local ADR 0038).
                "claim_session_request",
                "mark_session_request",
                "list_session_requests",

                // The Manager's answer deliveries, beside them (local ADR 0041).
                "claim_delivery",
                "mark_delivery",
                "list_deliveries",

                // The loop sessions' two-way check-in (local ADR 0051 §3).
                "check_in",

                // Notifications' four (local ADR 0039).
                "notify",
                "ask",
                "get_answer",
                "close",

                // The Inbox's captures: list, read, archive once filed, and capture an action (local ADR 0054).
                "list_inbox_items",
                "read_inbox_item",
                "archive_inbox_item",
                "capture_to_inbox",

                // Events: add a knowledge event as the pane's Add does (local ADR 0050).
                "add_knowledge_event",

                // The surface windows (local ADR 0043 §7).
                "open_window"
            ],
            BacklogMcpTools.ToolNames);
    }

    /// <summary>
    /// One check per group, each group behind the flag of the area it belongs to
    /// (local ADR 0012 §7) — or behind none, for an area no switch turns off: the
    /// backlog, the roadmap and the devbook are permanent.
    /// </summary>
    [Fact]
    public void Each_group_reads_the_feature_key_its_area_owns()
    {
        Assert.Null(BacklogMcpTools.Work.FeatureKey);
        Assert.Null(BacklogMcpTools.Tracker.FeatureKey);
        Assert.Null(BacklogMcpTools.Roadmap.FeatureKey);
        Assert.Null(BacklogMcpTools.Devbook.FeatureKey);
        Assert.Null(DecisionTools.Group.FeatureKey);
        Assert.Null(SyncDirectionTools.Group.FeatureKey);
        Assert.Equal(SessionFeatures.Sessions, BacklogMcpTools.Sessions.FeatureKey);
        Assert.Equal(SessionFeatures.Sessions, BacklogMcpTools.Surface.FeatureKey);
        Assert.Equal(SessionFeatures.Sessions, BacklogMcpTools.Usage.FeatureKey);
        Assert.Equal(SessionFeatures.Sessions, BacklogMcpTools.Schedules.FeatureKey);
        Assert.Equal(SessionFeatures.Sessions, BacklogMcpTools.Loops.FeatureKey);
        // Progress has no switch: its impediment and goal tools are always listed.
        Assert.Null(BacklogMcpTools.Impediments.FeatureKey);
        Assert.Null(BacklogMcpTools.Goals.FeatureKey);
        // The dispatcher tools have the queue's own key, off by default (local ADR 0038 §5).
        Assert.Equal(SessionFeatures.SessionRequests, BacklogMcpTools.SessionRequests.FeatureKey);
        Assert.Equal(NotificationsFeatures.Notifications, BacklogMcpTools.Notifications.FeatureKey);
        // Events is withdrawn whole by its own key (local ADR 0050 §6).
        Assert.Equal(EventsFeatures.Events, BacklogMcpTools.Events.FeatureKey);
        // open_window has no flag (local ADR 0043 §7): a surface's own flag hides it in the catalog.
        Assert.Null(BacklogMcpTools.Windows.FeatureKey);
    }

    /// <summary>
    /// One key is read by several groups, and that is the decision rather than a
    /// duplicate to be cleaned up.
    /// <para>
    /// §7 asks for one check per group, not one group per key. Reading what agents
    /// left on this machine and recording what a flow is doing on it now are one
    /// area, drawn in one pane, behind one switch: somebody switching sessions off
    /// means "Backlog is not where I watch my agents work", which does not stop at
    /// the reads. The usage tool reads the same runs and sessions, apart from the
    /// surface so the flow driving a run is never handed its measured figures, and
    /// <c>check_in</c> rides the same key (local ADR 0051 §6).
    /// </para>
    /// <para>
    /// The groups of an area no switch turns off carry no key at all: the backlog,
    /// the roadmap and the devbook are permanent, Progress has no switch of its own
    /// (local ADR 0035 §10), and <c>open_window</c> is ungated (local ADR 0043 §7).
    /// The keys the groups name are five: sessions, the session request queue's
    /// <c>session-requests</c>, the notification tools' <c>notifications</c>, the Inbox
    /// tools' <c>inbox-pane</c>, and the Events tool's <c>events</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void Groups_share_a_key_only_where_they_are_one_area()
    {
        Assert.Equal(
            [
                BacklogMcpTools.Work,
                BacklogMcpTools.Tracker,
                BacklogMcpTools.Roadmap,
                BacklogMcpTools.Devbook,
                DecisionTools.Group,
                SyncDirectionTools.Group,
                BacklogMcpTools.Impediments,
                BacklogMcpTools.Goals,
                BacklogMcpTools.Windows
            ],
            BacklogMcpTools.Groups.Where(group => group.FeatureKey is null));

        var byKey = BacklogMcpTools.Groups
            .Where(group => group.FeatureKey is not null)
            .GroupBy(group => group.FeatureKey!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        Assert.Equal(5, byKey.Count);

        Assert.Equal(
            [BacklogMcpTools.Sessions, BacklogMcpTools.Surface, BacklogMcpTools.Usage, BacklogMcpTools.Schedules, BacklogMcpTools.Loops],
            byKey[SessionFeatures.Sessions]);

        // Every other key is one group's. Named rather than asserted as "a few may
        // share", so adding another group to a key is a failing test that asks for
        // the reason to be written down here.
        Assert.All(
            byKey.Where(pair => pair.Key != SessionFeatures.Sessions),
            pair => Assert.Single(pair.Value));

        // And two groups are two classes, which is what keeps the registration
        // from listing one class's tools twice.
        Assert.Equal(
            BacklogMcpTools.Groups.Count,
            BacklogMcpTools.Groups.Select(group => group.ToolType).Distinct().Count());
    }

    /// <summary>The table and the attributes are one statement. Every name the
    /// catalog claims is a name the SDK will actually publish, and every
    /// attributed method is in the catalog.</summary>
    [Fact]
    public void Every_group_lists_exactly_the_tools_its_class_is_attributed_with()
    {
        foreach (var group in BacklogMcpTools.Groups)
        {
            Assert.NotNull(group.ToolType.GetCustomAttribute<McpServerToolTypeAttribute>());

            var attributed = group.ToolType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
                .Where(attribute => attribute is not null)
                .Select(attribute => attribute!.Name)
                .ToList();

            Assert.Equal([.. group.ToolNames.Order()], [.. attributed.Order()]);
        }
    }

    /// <summary>
    /// Every tool says what it is for and whether it writes, and every writer is
    /// honest about what repeating it costs.
    /// <para>
    /// This asserted <c>ReadOnly</c> of every tool once, which was true of a
    /// read-only assembly and stopped being true twice in the same week:
    /// <c>resolve_annotation</c> and the tracker operations arrived
    /// independently, each the first write its own author had seen. The writers
    /// are named here rather than matched by a pattern, so a sixth one has to be
    /// a deliberate edit to this list rather than something a rule quietly
    /// absorbs.
    /// </para>
    /// <para>
    /// <b>Idempotency is a fact about each tool, not a property of writing.</b>
    /// Resolving an already-resolved note is the state it is already in, and
    /// moving an entry to the status it already has saves nothing — both are safe
    /// to repeat. The four beside them are not, and say so:
    /// <c>TaskItem.AddProjectionRef</c> appends without looking, so a repeated
    /// <c>link_change</c> leaves two identical projections, a repeated
    /// <c>comment</c> leaves two dated lines, <c>create_item</c> creates a
    /// second entry, and <c>capture_note</c> writes a second Brain note, the
    /// writer taking the next free name rather than overwriting the first. A
    /// blanket "a write is idempotent" would be the kind of true-of-one-tool
    /// rule that invites a client to retry the ones it is false for. The surface's six split the same way and for reasons just as
    /// particular — the list below says which and why.
    /// </para>
    /// <para>
    /// One tool declares itself destructive, and it is the one delete here:
    /// <c>delete_goal</c> removes a goal and what it holds, which no later call brings
    /// back. Every other writer adds or replaces. The description is the other half: a
    /// model reads it to decide whether to call the tool at all, and a tool with no
    /// description is one that gets called for the wrong reason.
    /// </para>
    /// <para>
    /// The goal writes split on the same line as the rest: <c>create_goal</c> sets a
    /// second goal when repeated; <c>update_goal</c> writes the same fields again,
    /// attaching or detaching what is already so changes nothing, and a deleted goal
    /// stays deleted — those four are safe to repeat.
    /// </para>
    /// <para>
    /// The devbook state writes split the same way: <c>devbook_set_state</c> to the
    /// state a subject already has leaves it as it was, while <c>devbook_approve</c>
    /// and <c>devbook_accept</c> each record a person's decision with its own time —
    /// and approving again withdraws an acceptance — so no client may repeat one on
    /// its own account.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_tool_declares_what_it_does_and_is_described()
    {
        string[] writers =
        [
            DevbookTools.ResolveAnnotation,
            DevbookTools.CaptureNote,
            InboxTools.ArchiveInboxItem,
            InboxTools.CaptureToInbox,
            EventTools.AddKnowledgeEvent,
            DecisionTools.SetState,
            DecisionTools.Approve,
            DecisionTools.Accept,
            DecisionTools.PostNote,
            DecisionTools.ResolveNote,
            TrackerTools.Transition,
            TrackerTools.SetBlocked,
            TrackerTools.Comment,
            TrackerTools.LinkChange,
            TrackerTools.LinkSession,
            TrackerTools.CreateItem,
            TrackerTools.SetDevbookReferences,
            SurfaceTools.OpenDashboard,
            SurfaceTools.StartRun,
            SurfaceTools.RecordPrompt,
            SurfaceTools.SetRunContext,
            SurfaceTools.UpdateStage,
            SurfaceTools.FinishRun,
            ImpedimentTools.RaiseImpediment,
            ImpedimentTools.SetImpedimentState,
            ImpedimentTools.LinkToImpediment,
            ImpedimentTools.UnlinkFromImpediment,
            GoalTools.CreateGoal,
            GoalTools.UpdateGoal,
            GoalTools.AttachToGoal,
            GoalTools.DetachFromGoal,
            GoalTools.DeleteGoal,
            SessionRequestTools.ClaimSessionRequest,
            SessionRequestTools.MarkSessionRequest,
            SessionRequestTools.ClaimDelivery,
            SessionRequestTools.MarkDelivery,
            ScheduleTools.ReportScheduleRun,
            NotificationTools.Notify,
            NotificationTools.Ask,
            NotificationTools.GetAnswer,
            NotificationTools.Close,
            LoopTools.CheckIn,
            WindowTools.OpenWindow
        ];

        string[] destructive = [GoalTools.DeleteGoal];

        // The writes a client may safely repeat, and which ones is not guessable
        // from the verb. On the surface: showing a pane that is already showing
        // changes nothing, start_run reattaches rather than starting a second
        // run, set_run_context overwrites the fields it is given, and finish_run
        // closes a run that is already closed to the same place. The two that are
        // not: record_prompt appends, and update_stage counts every transition to
        // done — a stage re-run after requested changes has to read as a second
        // pass rather than a retry of the first. delete_goal is not either: a
        // second delete is refused as progress.goal_not_found. get_answer writes too —
        // it records that the asking hook is still waiting — and a repeated poll records
        // the same fact again, so it is safe to repeat.
        string[] repeatable =
        [
            DevbookTools.ResolveAnnotation,
            DecisionTools.SetState,
            DecisionTools.ResolveNote,
            TrackerTools.Transition,
            TrackerTools.SetBlocked,
            TrackerTools.LinkSession,
            TrackerTools.SetDevbookReferences,
            SurfaceTools.OpenDashboard,
            SurfaceTools.StartRun,
            SurfaceTools.SetRunContext,
            SurfaceTools.FinishRun,
            GoalTools.UpdateGoal,
            GoalTools.AttachToGoal,
            GoalTools.DetachFromGoal,
            ImpedimentTools.LinkToImpediment,
            ImpedimentTools.UnlinkFromImpediment,
            NotificationTools.GetAnswer,
            LoopTools.CheckIn
        ];

        var seen = new List<string>();

        foreach (var group in BacklogMcpTools.Groups)
        {
            foreach (var method in group.ToolType.GetMethods())
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not { } tool) continue;

                var writes = writers.Contains(tool.Name, StringComparer.Ordinal);

                Assert.Equal(!writes, tool.ReadOnly);

                Assert.False(
                    string.IsNullOrWhiteSpace(method.GetCustomAttribute<DescriptionAttribute>()?.Description),
                    $"{tool.Name} has no description for a model to read.");

                if (!writes) continue;

                seen.Add(tool.Name!);

                // Destructive is only asked of the writers, because the
                // attribute's own default is true when nobody sets it — the
                // protocol's default for a tool that has not said. A read-only
                // tool never reaches the wire with the hint at all (the SDK omits
                // it), so reading the attribute for one would be asserting against
                // a default rather than against a claim anybody made.
                Assert.Equal(destructive.Contains(tool.Name, StringComparer.Ordinal), tool.Destructive);

                Assert.Equal(repeatable.Contains(tool.Name, StringComparer.Ordinal), tool.Idempotent);
            }
        }

        // Both directions: every named writer was found, and nothing else
        // declared itself one.
        Assert.Equal([.. writers.Order()], [.. seen.Order()]);
    }

    /// <summary>
    /// The tools that can reach GitHub say so, and the tools that cannot say
    /// that.
    /// <para>
    /// <c>OpenWorld</c> is contract metadata a client acts on, so it has to
    /// describe what the tool does rather than what the group would like to be
    /// true of it. The two devbook tools that resolve a devbook folder can meet
    /// a repository whose devbook is read from a branch snapshot (local ADR
    /// 0008): <c>read_knowledge_chapter</c> fetches the named file through
    /// <c>PrepareContentAsync</c> and <c>list_knowledge_contexts</c> asks the
    /// auto-fetch to make sure the branch has been indexed. The two Devbook
    /// reference tools resolve what an entry points at the same way — a page the
    /// database does not hold is read through <c>PrepareContentAsync</c>, which
    /// fetches it for a branch snapshot. <c>list_goals</c> answers each goal's progress,
    /// which reads the state of every pull request it counts from GitHub, and
    /// <c>read_iterations</c> reads the same pull requests for its goals, its finished
    /// items and what its impediments hold back. Every other tool here
    /// reads this machine and nothing else — the SQLite backlog, the roadmap
    /// store, the annotation store, the session files — and the replicated
    /// session source "reads a store and never the network".
    /// </para>
    /// <para>
    /// Pinned rather than derived, deliberately: the point of the assertion is
    /// that changing what a tool reaches for makes somebody change what it claims.
    /// </para>
    /// </summary>
    [Fact]
    public void Only_the_tools_that_can_reach_github_are_declared_open_world()
    {
        string[] openWorld =
        [
            DevbookTools.ListKnowledgeContexts,
            DevbookTools.ReadKnowledgeChapter,
            TrackerTools.SetDevbookReferences,
            TrackerTools.ListDevbookReferences,
            GoalTools.ListGoals,
            GoalTools.ReadIterations
        ];

        foreach (var group in BacklogMcpTools.Groups)
        {
            foreach (var method in group.ToolType.GetMethods())
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is not { } tool) continue;

                Assert.Equal(openWorld.Contains(tool.Name, StringComparer.Ordinal), tool.OpenWorld);
            }
        }
    }

    /// <summary>
    /// The exposure predicate both host filters read. A group whose feature is
    /// off is absent from <c>tools/list</c> — not present and refusing — which is
    /// what feature enablement asks of every switchable capability.
    /// </summary>
    [Fact]
    public void A_group_whose_feature_is_off_is_not_exposed()
    {
        var features = Everything();

        Assert.All(BacklogMcpTools.ToolNames, name => Assert.True(BacklogMcpTools.IsExposed(name, features)));

        features.SetEnabled(SessionFeatures.Sessions, enabled: false);

        // Every group on the key goes, and they go together: the key names the area.
        var gated = BacklogMcpTools.Groups.Where(group => group.FeatureKey == SessionFeatures.Sessions).ToList();
        Assert.All(
            gated.SelectMany(group => group.ToolNames),
            name => Assert.False(BacklogMcpTools.IsExposed(name, features)));

        // And only the groups that read that key go.
        Assert.All(
            BacklogMcpTools.Groups.Except(gated).SelectMany(group => group.ToolNames),
            name => Assert.True(BacklogMcpTools.IsExposed(name, features)));
    }

    /// <summary>The backlog, the roadmap and the devbook are permanent: their
    /// groups carry no key, so no switch takes their tools out of the list.</summary>
    [Fact]
    public void The_permanent_areas_stay_listed_whatever_is_switched_off()
    {
        var features = Everything();
        foreach (var key in BacklogMcpTools.Groups.Select(group => group.FeatureKey).OfType<string>().Distinct())
        {
            features.SetEnabled(key, enabled: false);
        }

        Assert.All(
            BacklogMcpTools.Work.ToolNames
                .Concat(BacklogMcpTools.Tracker.ToolNames)
                .Concat(BacklogMcpTools.Roadmap.ToolNames)
                .Concat(BacklogMcpTools.Devbook.ToolNames),
            name => Assert.True(BacklogMcpTools.IsExposed(name, features)));
    }

    /// <summary>
    /// The Events tool answers to the <c>events</c> key alone (local ADR 0050 §6): off, it is
    /// absent from <c>tools/list</c> and a call to it is refused as an unknown tool, in any
    /// case, while every other group stays; on, it is listed and callable again.
    /// </summary>
    [Fact]
    public void The_events_tool_follows_the_events_switch_and_nothing_else()
    {
        var features = Everything();

        features.SetEnabled(EventsFeatures.Events, enabled: false);

        Assert.False(BacklogMcpTools.IsExposed(EventTools.AddKnowledgeEvent, features));
        Assert.False(BacklogMcpTools.IsCallable(EventTools.AddKnowledgeEvent, features));
        Assert.False(BacklogMcpTools.IsCallable("ADD_KNOWLEDGE_EVENT", features));
        Assert.All(
            BacklogMcpTools.ToolNames.Where(name => name != EventTools.AddKnowledgeEvent),
            name => Assert.True(BacklogMcpTools.IsExposed(name, features)));

        features.SetEnabled(EventsFeatures.Events, enabled: true);

        Assert.True(BacklogMcpTools.IsExposed(EventTools.AddKnowledgeEvent, features));
        Assert.True(BacklogMcpTools.IsCallable(EventTools.AddKnowledgeEvent, features));
    }

    /// <summary>
    /// Every tool that writes says how the pages showing what it changed hear of it
    /// (<see cref="McpRefreshMap"/>). Read off the attributes rather than off a list, so a new
    /// write tool fails here until somebody writes down which notice refreshes its pages — or
    /// that none does yet, and why.
    /// </summary>
    [Fact]
    public void Every_write_tool_names_the_notice_that_refreshes_its_pages()
    {
        var writes = BacklogMcpTools.Groups
            .SelectMany(group => group.ToolType.GetMethods())
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(tool => tool is { ReadOnly: false })
            .Select(tool => tool!.Name!)
            .ToList();

        Assert.NotEmpty(writes);
        Assert.All(writes, name => Assert.True(
            McpRefreshMap.Notices.TryGetValue(name, out var notice) && !string.IsNullOrWhiteSpace(notice),
            $"{name} writes, and the refresh map does not say which notice refreshes its pages."));

        // And nothing else: a read in the map, or a tool renamed away, is an entry nobody reads.
        Assert.Equal([.. writes.Order()], [.. McpRefreshMap.Notices.Keys.Order()]);
    }

    /// <summary>The surface's run writes do not refresh the Sessions pane yet: its list is read
    /// on request (<c>.devbook/domain/sessions/features.md#re-read-on-request</c>), and making it
    /// live is a change of its own. The map says so rather than leaving them out.</summary>
    [Fact]
    public void The_surface_run_writes_are_declared_as_not_refreshed_yet()
    {
        Assert.All(
            [SurfaceTools.StartRun, SurfaceTools.RecordPrompt, SurfaceTools.SetRunContext, SurfaceTools.UpdateStage, SurfaceTools.FinishRun],
            name => Assert.Equal(McpRefreshMap.SessionsNotRefreshedYet, McpRefreshMap.Notices[name]));
        Assert.Equal(McpRefreshMap.EventsPaneChanged, McpRefreshMap.Notices[EventTools.AddKnowledgeEvent]);
    }

    /// <summary>A name this library does not publish is left alone: a host may
    /// map other tools onto the same server, and a filter that hid everything it
    /// did not recognise would take them with it.</summary>
    [Fact]
    public void A_tool_this_library_does_not_publish_is_left_alone()
    {
        var features = Everything();

        features.SetEnabled(SessionFeatures.Sessions, enabled: false);

        Assert.True(BacklogMcpTools.IsExposed("somebody_elses_tool", features));
        Assert.True(BacklogMcpTools.IsCallable("somebody_elses_tool", features));
    }

    /// <summary>
    /// The call path recognises this library's own names whatever case they
    /// arrive in, and refuses one whose group is off.
    /// <para>
    /// <see cref="BacklogMcpTools.IsExposed"/> answers <c>true</c> for a name it
    /// does not recognise, which is what keeps another host's tools on the same
    /// server. Matched ordinally, <c>list_SESSIONS</c> is such a name — so a
    /// dispatcher that ever routed it to <c>list_sessions</c> would be routing a
    /// call the gate had waved through as somebody else's. The list filter keeps
    /// the ordinal comparison, because a published name is exact.
    /// </para>
    /// </summary>
    [Fact]
    public void The_call_path_refuses_this_librarys_own_names_in_any_case()
    {
        var features = Everything();

        features.SetEnabled(SessionFeatures.Sessions, enabled: false);

        var shouted = SessionTools.ListSessions.ToUpperInvariant();
        var mixed = char.ToUpperInvariant(SessionTools.ListSessions[0]) + SessionTools.ListSessions[1..];

        Assert.False(BacklogMcpTools.IsCallable(SessionTools.ListSessions, features));
        Assert.False(BacklogMcpTools.IsCallable(shouted, features));
        Assert.False(BacklogMcpTools.IsCallable(mixed, features));

        // The list filter is unchanged: those spellings are not names this
        // library publishes, and publishing is exact.
        Assert.True(BacklogMcpTools.IsExposed(shouted, features));
        Assert.True(BacklogMcpTools.IsExposed(mixed, features));

        // And with the group on, the call path allows them again — this is a
        // gate, not a second refusal of its own.
        features.SetEnabled(SessionFeatures.Sessions, enabled: true);

        Assert.True(BacklogMcpTools.IsCallable(shouted, features));
    }

    /// <summary>Every key the catalog names, all on. Built from the catalog so a
    /// new group cannot be added without this knowing about it — the fake throws
    /// for a key it has never heard of, the way the real store does.</summary>
    private static FakeAppFeatureSettings Everything() =>
        new([.. BacklogMcpTools.Groups.Select(group => group.FeatureKey).OfType<string>()]);
}
