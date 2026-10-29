using System.ComponentModel;
using System.Reflection;

using Backlog.Modules.Devbook.Abstractions;
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

                // Progress's impediments, beside set_blocked (local ADR 0035).
                "raise_impediment",
                "set_impediment_state",

                // Progress's goals: read, set, edit, attach, detach, delete (local ADR 0035).
                "list_goals",
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

                // Notifications' four (local ADR 0039).
                "notify",
                "ask",
                "get_answer",
                "close",

                // The surface windows (local ADR 0042 §7).
                "open_window"
            ],
            BacklogMcpTools.ToolNames);
    }

    /// <summary>
    /// One check per group, each group behind the flag of the area it belongs to
    /// (local ADR 0012 §7). Six groups over four bounded contexts, and the
    /// roadmap is one of them: it has a key of its own and a person switching it
    /// off has switched off the thing that tool reads.
    /// </summary>
    [Fact]
    public void Each_group_reads_the_feature_key_its_area_owns()
    {
        Assert.Equal(TasksFeatures.Tasks, BacklogMcpTools.Work.FeatureKey);
        Assert.Equal(TasksFeatures.Tasks, BacklogMcpTools.Tracker.FeatureKey);
        Assert.Equal(RoadmapFeatures.Roadmap, BacklogMcpTools.Roadmap.FeatureKey);
        Assert.Equal(DevbookFeatures.RepositoryDevbook, BacklogMcpTools.Devbook.FeatureKey);
        Assert.Equal(SessionFeatures.Sessions, BacklogMcpTools.Sessions.FeatureKey);
        Assert.Equal(SessionFeatures.Sessions, BacklogMcpTools.Surface.FeatureKey);
        // Progress has no switch: its impediment and goal tools ride the always-on Tasks key.
        Assert.Equal(TasksFeatures.Tasks, BacklogMcpTools.Impediments.FeatureKey);
        Assert.Equal(TasksFeatures.Tasks, BacklogMcpTools.Goals.FeatureKey);
        // The dispatcher tools have the queue's own key, off by default (local ADR 0038 §5).
        Assert.Equal(SessionFeatures.SessionRequests, BacklogMcpTools.SessionRequests.FeatureKey);
        Assert.Equal(NotificationsFeatures.Notifications, BacklogMcpTools.Notifications.FeatureKey);
        // open_window has no flag (local ADR 0042 §7): a surface's own flag hides it in the catalog.
        Assert.Null(BacklogMcpTools.Windows.FeatureKey);
    }

    /// <summary>
    /// Two pairs of groups read one key each, and that is the decision rather
    /// than a duplicate to be cleaned up.
    /// <para>
    /// This used to assert that every group's key was distinct, which read §7's
    /// "one check per group" as "one group per key". They are different
    /// requirements. A group is the unit the check is applied at, so that a class
    /// cannot half-appear in <c>tools/list</c>; a key is the switchable
    /// <em>area</em>. Reading the backlog and moving it are one area — somebody
    /// switching Tasks off means "no backlog tools", not "no backlog tools except
    /// the ones that write" — while being two descriptions of what a tool class
    /// is for, which is what a model reads.
    /// </para>
    /// <para>
    /// The sessions key is the second pair and the same argument: reading what
    /// agents left on this machine and recording what a flow is doing on it now
    /// are one area, drawn in one pane, behind one switch. Somebody switching
    /// sessions off means "Backlog is not where I watch my agents work", which
    /// does not stop at the reads.
    /// </para>
    /// <para>
    /// No key crosses a bounded context but one: the Tasks key also gates Progress's
    /// impediment and goal tools, because Progress has no switch of its own — goals are
    /// always on (local ADR 0035 §10) — and the Tasks key cannot be turned off either. The
    /// keys the ten groups name are six: the session request queue's dispatcher tools
    /// have <c>session-requests</c> to themselves, and the notification tools
    /// <c>notifications</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void Groups_share_a_key_only_where_they_are_one_area()
    {
        // The one ungated group has no key to share: open_window is behind no flag.
        Assert.Equal([BacklogMcpTools.Windows], BacklogMcpTools.Groups.Where(group => group.FeatureKey is null));

        var byKey = BacklogMcpTools.Groups
            .Where(group => group.FeatureKey is not null)
            .GroupBy(group => group.FeatureKey!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        Assert.Equal(6, byKey.Count);

        // The impediment and goal tools are Progress's, which has no switch of its own:
        // goals are always on, so they ride the Tasks key, which cannot be turned off either.
        Assert.Equal(
            [BacklogMcpTools.Work, BacklogMcpTools.Tracker, BacklogMcpTools.Impediments, BacklogMcpTools.Goals],
            byKey[TasksFeatures.Tasks]);

        Assert.Equal(
            [BacklogMcpTools.Sessions, BacklogMcpTools.Surface],
            byKey[SessionFeatures.Sessions]);

        // Every other key is one group's. Named as the two pairs rather than
        // asserted as "at most two share", so adding a third group to a key is a
        // failing test that asks for the reason to be written down here — which
        // is the whole of what this assertion is for.
        Assert.All(
            byKey.Where(pair => pair.Key != TasksFeatures.Tasks && pair.Key != SessionFeatures.Sessions),
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
    /// </summary>
    [Fact]
    public void Every_tool_declares_what_it_does_and_is_described()
    {
        string[] writers =
        [
            DevbookTools.ResolveAnnotation,
            DevbookTools.CaptureNote,
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
            GoalTools.CreateGoal,
            GoalTools.UpdateGoal,
            GoalTools.AttachToGoal,
            GoalTools.DetachFromGoal,
            GoalTools.DeleteGoal,
            SessionRequestTools.ClaimSessionRequest,
            SessionRequestTools.MarkSessionRequest,
            SessionRequestTools.ClaimDelivery,
            SessionRequestTools.MarkDelivery,
            NotificationTools.Notify,
            NotificationTools.Ask,
            NotificationTools.Close,
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
        // second delete is refused as progress.goal_not_found.
        string[] repeatable =
        [
            DevbookTools.ResolveAnnotation,
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
            GoalTools.DetachFromGoal
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
    /// which reads the state of every pull request it counts from GitHub. Every other tool here
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
            GoalTools.ListGoals
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

        features.SetEnabled(TasksFeatures.Tasks, enabled: false);

        // Both task groups go, and they go together: the key names the area, so
        // switching the backlog off takes reading it and moving it at once.
        // Leaving the tracker operations listed would offer a session six tools
        // for a pane the person has switched off.
        Assert.All(
            BacklogMcpTools.Work.ToolNames.Concat(BacklogMcpTools.Tracker.ToolNames),
            name => Assert.False(BacklogMcpTools.IsExposed(name, features)));

        // And only the groups that read that key go.
        Assert.All(
            BacklogMcpTools.Groups
                .Where(group => group.FeatureKey != TasksFeatures.Tasks)
                .SelectMany(group => group.ToolNames),
            name => Assert.True(BacklogMcpTools.IsExposed(name, features)));
    }

    /// <summary>
    /// The roadmap answers to the roadmap's own switch and not the backlog's.
    /// It rode with the task tools once, which meant switching the roadmap off
    /// left its tool listed and switching the backlog off took the roadmap with
    /// it — two ways for the gate to say something nobody meant.
    /// </summary>
    [Fact]
    public void The_roadmap_tool_follows_the_roadmap_switch_and_not_the_backlog()
    {
        var features = Everything();

        features.SetEnabled(RoadmapFeatures.Roadmap, enabled: false);

        Assert.False(BacklogMcpTools.IsExposed(RoadmapTools.GetRoadmap, features));
        Assert.All(BacklogMcpTools.Work.ToolNames, name => Assert.True(BacklogMcpTools.IsExposed(name, features)));

        features.SetEnabled(RoadmapFeatures.Roadmap, enabled: true);
        features.SetEnabled(TasksFeatures.Tasks, enabled: false);

        Assert.True(BacklogMcpTools.IsExposed(RoadmapTools.GetRoadmap, features));
    }

    /// <summary>A name this library does not publish is left alone: a host may
    /// map other tools onto the same server, and a filter that hid everything it
    /// did not recognise would take them with it.</summary>
    [Fact]
    public void A_tool_this_library_does_not_publish_is_left_alone()
    {
        var features = Everything();

        features.SetEnabled(TasksFeatures.Tasks, enabled: false);

        Assert.True(BacklogMcpTools.IsExposed("somebody_elses_tool", features));
        Assert.True(BacklogMcpTools.IsCallable("somebody_elses_tool", features));
    }

    /// <summary>
    /// The call path recognises this library's own names whatever case they
    /// arrive in, and refuses one whose group is off.
    /// <para>
    /// <see cref="BacklogMcpTools.IsExposed"/> answers <c>true</c> for a name it
    /// does not recognise, which is what keeps another host's tools on the same
    /// server. Matched ordinally, <c>list_ENTRIES</c> is such a name — so a
    /// dispatcher that ever routed it to <c>list_entries</c> would be routing a
    /// call the gate had waved through as somebody else's. The list filter keeps
    /// the ordinal comparison, because a published name is exact.
    /// </para>
    /// </summary>
    [Fact]
    public void The_call_path_refuses_this_librarys_own_names_in_any_case()
    {
        var features = Everything();

        features.SetEnabled(TasksFeatures.Tasks, enabled: false);

        var shouted = WorkTools.ListEntries.ToUpperInvariant();
        var mixed = "List_Entries";

        Assert.False(BacklogMcpTools.IsCallable(WorkTools.ListEntries, features));
        Assert.False(BacklogMcpTools.IsCallable(shouted, features));
        Assert.False(BacklogMcpTools.IsCallable(mixed, features));

        // The list filter is unchanged: those spellings are not names this
        // library publishes, and publishing is exact.
        Assert.True(BacklogMcpTools.IsExposed(shouted, features));
        Assert.True(BacklogMcpTools.IsExposed(mixed, features));

        // And with the group on, the call path allows them again — this is a
        // gate, not a second refusal of its own.
        features.SetEnabled(TasksFeatures.Tasks, enabled: true);

        Assert.True(BacklogMcpTools.IsCallable(shouted, features));
    }

    /// <summary>Every key the catalog names, all on. Built from the catalog so a
    /// new group cannot be added without this knowing about it — the fake throws
    /// for a key it has never heard of, the way the real store does.</summary>
    private static FakeAppFeatureSettings Everything() =>
        new([.. BacklogMcpTools.Groups.Select(group => group.FeatureKey).OfType<string>()]);
}
