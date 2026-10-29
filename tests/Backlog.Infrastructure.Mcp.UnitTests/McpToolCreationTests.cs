using System.Reflection;
using System.Text.Json;

using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Tests;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The SDK's own reading of these tool classes.
/// <para>
/// Everything else here tests what a tool answers once it has been called. This
/// tests that it can be registered at all — the schema the SDK builds from a
/// method's parameters is built by reflection at startup, so a return type or a
/// parameter shape it will not take is a failure the host meets on its first
/// <c>tools/list</c> and no unit test about behaviour would ever see.
/// </para>
/// <para>
/// <c>McpServerTool.Create</c> rather than <c>WithTools&lt;T&gt;()</c> because
/// the latter lives in the hosting package, which this library deliberately does
/// not reference. It is the same call <c>WithTools</c> makes.
/// </para>
/// </summary>
public class McpToolCreationTests
{
    /// <summary>
    /// The tools that only read, named. Everything else this library publishes
    /// writes.
    /// <para>
    /// Pinned as a list rather than derived from the attributes, which would make
    /// the assertion below agree with whatever the attribute happens to say. This
    /// is the statement the attributes are checked against, and the point of it is
    /// that turning a read into a write has to be done here as well as there.
    /// </para>
    /// </summary>
    private static readonly string[] ReadOnly =
    [
        WorkTools.ListEntries,
        WorkTools.GetPlanItems,
        TrackerTools.FindItem,
        TrackerTools.ReadItem,
        TrackerTools.ListDevbookReferences,
        RoadmapTools.GetRoadmap,
        DevbookTools.ListKnowledgeContexts,
        DevbookTools.ReadKnowledgeChapter,
        DevbookTools.ListAnnotations,
        SessionTools.ListSessions,
        SurfaceTools.ListRuns,
        SurfaceTools.GetRun,
        GoalTools.ListGoals,
        SessionRequestTools.ListSessionRequests,
        SessionRequestTools.ListDeliveries,
        NotificationTools.GetAnswer
    ];

    /// <summary>The one writer that destroys: <c>delete_goal</c> removes a goal and what
    /// it holds. Every other writer adds or replaces.</summary>
    private static readonly string[] Destructive = [GoalTools.DeleteGoal];

    /// <summary>
    /// Every tool builds, under the name the catalog claims, and reaches the wire
    /// saying honestly whether it writes.
    /// <para>
    /// This asserted <c>ReadOnlyHint == true</c> for every tool once, which was a
    /// true statement about a read-only assembly and stopped being one twice over
    /// in the same week — <c>resolve_annotation</c> and the tracker operations
    /// arrived independently, each the first write its own author had seen. It is
    /// a per-tool expectation now rather than a check the writers are excused
    /// from: the hint is what a client shows a person before it runs something, so
    /// a write claiming to be a read is worse than an unstated hint, and a write
    /// quietly flattened into one more read is how that happens. Every writer is
    /// held to <c>DestructiveHint == false</c> besides, but the one in
    /// <see cref="Destructive"/>: <c>delete_goal</c> is the only delete here.
    /// </para>
    /// <para>
    /// Naming the readers rather than the writers is deliberate. A tool added
    /// later is a write until somebody says otherwise, which is the safe way round
    /// for an assertion whose whole job is to catch the one nobody thought about.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_tool_builds_under_its_name_and_says_honestly_whether_it_writes()
    {
        var built = Tools().ToList();

        Assert.Equal([.. BacklogMcpTools.ToolNames.Order()], [.. built.Select(tool => tool.ProtocolTool.Name).Order()]);

        foreach (var tool in built)
        {
            var annotations = tool.ProtocolTool.Annotations;

            if (ReadOnly.Contains(tool.ProtocolTool.Name, StringComparer.Ordinal))
            {
                Assert.True(
                    annotations?.ReadOnlyHint,
                    $"{tool.ProtocolTool.Name} does not reach the wire as read-only.");
            }
            else
            {
                Assert.NotEqual(true, annotations?.ReadOnlyHint);

                Assert.Equal(
                    Destructive.Contains(tool.ProtocolTool.Name, StringComparer.Ordinal),
                    annotations?.DestructiveHint);
            }

            Assert.False(
                string.IsNullOrWhiteSpace(tool.ProtocolTool.Description),
                $"{tool.ProtocolTool.Name} reaches the wire with no description.");
        }
    }

    /// <summary>The list above is the whole catalog split in two, so a tool added
    /// to neither half is a tool nobody said anything about.
    /// <para>
    /// The writers span two areas and arrived from two directions —
    /// <c>resolve_annotation</c> against the devbook's private notes, the four
    /// tracker operations against the backlog — which is the case this assertion
    /// is for. Each author saw their own as the assembly's first write, and a
    /// split derived from the attributes would have agreed with both of them
    /// separately and with neither of them together.
    /// </para></summary>
    [Fact]
    public void Every_published_tool_is_on_one_side_of_the_read_write_line()
    {
        Assert.All(ReadOnly, name => Assert.Contains(name, BacklogMcpTools.ToolNames));

        var writers = BacklogMcpTools.ToolNames.Except(ReadOnly, StringComparer.Ordinal).Order();

        Assert.Equal(
            [
                NotificationTools.Ask,
                GoalTools.AttachToGoal,
                DevbookTools.CaptureNote,
                SessionRequestTools.ClaimDelivery,
                SessionRequestTools.ClaimSessionRequest,
                NotificationTools.Close,
                TrackerTools.Comment,
                GoalTools.CreateGoal,
                TrackerTools.CreateItem,
                GoalTools.DeleteGoal,
                GoalTools.DetachFromGoal,
                SurfaceTools.FinishRun,
                TrackerTools.LinkChange,
                TrackerTools.LinkSession,
                SessionRequestTools.MarkDelivery,
                SessionRequestTools.MarkSessionRequest,
                NotificationTools.Notify,
                SurfaceTools.OpenDashboard,
                WindowTools.OpenWindow,
                ImpedimentTools.RaiseImpediment,
                SurfaceTools.RecordPrompt,
                DevbookTools.ResolveAnnotation,
                TrackerTools.SetBlocked,
                TrackerTools.SetDevbookReferences,
                ImpedimentTools.SetImpedimentState,
                SurfaceTools.SetRunContext,
                SurfaceTools.StartRun,
                TrackerTools.Transition,
                GoalTools.UpdateGoal,
                SurfaceTools.UpdateStage
            ],
            [.. writers]);
    }

    /// <summary>
    /// The input schema is the arguments a session has to supply, and nothing
    /// else. A <see cref="CancellationToken"/> is bound by the SDK and must not
    /// appear as a parameter a model is invited to fill in.
    /// </summary>
    [Fact]
    public void The_schemas_ask_for_the_arguments_and_not_the_cancellation_token()
    {
        var schemas = Tools().ToDictionary(
            tool => tool.ProtocolTool.Name,
            tool => Properties(tool.ProtocolTool.InputSchema));

        Assert.Equal(["repository"], schemas["list_entries"]);
        Assert.Equal(["planId"], schemas["get_plan_items"]);

        // Every selector and every filter is advertised, because a model has to
        // be told a narrowing exists to use it — and find_item's whole contract
        // is that the caller picks one of the three rather than the tool picking
        // for them.
        Assert.Equal(
            ["id", "repoId", "externalId", "title", "status", "repository", "tag"],
            schemas["find_item"]);
        Assert.Equal(["id"], schemas["read_item"]);
        Assert.Equal(["id", "status"], schemas["transition"]);
        Assert.Equal(["id", "blocked"], schemas["set_blocked"]);
        Assert.Equal(["title", "holdsBack", "note"], schemas["raise_impediment"]);
        Assert.Equal(["id", "state", "with"], schemas["set_impediment_state"]);
        Assert.Empty(schemas["list_goals"]);
        Assert.Equal(["title", "dueDate", "outcome"], schemas["create_goal"]);
        Assert.Equal(["id", "title", "outcome", "dueDate"], schemas["update_goal"]);
        Assert.Equal(["id"], Required(Tools().Single(tool => tool.ProtocolTool.Name == "update_goal")));
        Assert.Equal(["goalId", "planTag", "taskId", "repository", "number", "moveFromOtherGoal"], schemas["attach_to_goal"]);
        Assert.Equal(["goalId", "planTag", "taskId", "repository", "number"], schemas["detach_from_goal"]);

        // "Exactly one of plan, task or pull request" is no `required` list either, for the
        // reason find_item gives: only the goal is required.
        Assert.Equal(["goalId"], Required(Tools().Single(tool => tool.ProtocolTool.Name == "attach_to_goal")));
        Assert.Equal(["goalId"], Required(Tools().Single(tool => tool.ProtocolTool.Name == "detach_from_goal")));
        Assert.Equal(["id"], schemas["delete_goal"]);
        Assert.Equal(["title", "summary", "body", "severity", "sessionId"], schemas["notify"]);
        Assert.Equal(
            ["title", "choices", "allowMultiple", "openAnswer", "placeholder", "summary", "body", "severity", "sessionId", "toolUseId", "questions", "kind"],
            schemas["ask"]);
        Assert.Equal(["id", "sessionId", "waitSeconds"], schemas["get_answer"]);
        Assert.Equal(["id", "reason", "sessionId", "answers"], schemas["close"]);

        // A session hook relaying its question passes questions instead of a title (local ADR 0041),
        // so ask requires neither at the schema level; the tool refuses a call with neither.
        Assert.Empty(Required(Tools().Single(tool => tool.ProtocolTool.Name == "ask")));
        Assert.Equal(["id"], Required(Tools().Single(tool => tool.ProtocolTool.Name == "get_answer")));
        Assert.Equal(["id", "text"], schemas["comment"]);
        Assert.Equal(["id", "repository", "externalId"], schemas["link_change"]);
        Assert.Equal(["id", "repository", "sessionId"], schemas["link_session"]);
        Assert.Equal(["rawText", "repository"], schemas["create_item"]);
        Assert.Equal(["id", "repository", "references"], schemas["set_devbook_references"]);
        Assert.Equal(["id", "repository"], schemas["list_devbook_references"]);

        // find_item requires none of its arguments at the schema level, on
        // purpose: "exactly one of these three" is not something a JSON schema
        // `required` list can say, and marking any one of them required would
        // rule out the other two.
        Assert.Empty(Required(Tools().Single(tool => tool.ProtocolTool.Name == "find_item")));

        Assert.Equal(["repository"], schemas["get_roadmap"]);
        Assert.Equal(["repository"], schemas["list_knowledge_contexts"]);
        Assert.Equal(["repository", "chapterPath", "review"], schemas["read_knowledge_chapter"]);
        Assert.Equal(["repository", "chapterPath"], schemas["list_annotations"]);
        Assert.Equal(["repository", "note"], schemas["resolve_annotation"]);
        Assert.Equal(["title", "body"], schemas["capture_note"]);
        Assert.Equal(["title"], Required(Tools().Single(tool => tool.ProtocolTool.Name == "capture_note")));

        // Optional, and optional means absent from `required` rather than absent
        // from the schema: a model has to be told the narrowing exists to use it.
        Assert.Equal(["repository"], schemas["list_sessions"]);
        Assert.DoesNotContain("repository", Required(Tools().Single(tool => tool.ProtocolTool.Name == "list_sessions")));

        // The surface is addressed by worktree rather than by repository, so
        // every operation but one opens with it. open_dashboard is the one: there
        // is a single application and it is either showing the pane or it is not,
        // which is why its schema asks for nothing at all.
        Assert.Empty(schemas["open_dashboard"]);
        Assert.Equal(["worktree", "skillId", "title", "stages", "changeKind", "sessionId", "trigger", "schedule", "repo"], schemas["start_run"]);
        Assert.Equal(["worktree", "runId", "prompt", "kind", "label"], schemas["record_prompt"]);
        Assert.Equal(
            ["worktree", "runId", "changeKind", "approval", "approvalNote", "model", "runContext"],
            schemas["set_run_context"]);
        Assert.Equal(
            ["worktree", "runId", "stageIndex", "status", "output", "links", "scenarios", "monitoring", "execution", "evidence"],
            schemas["update_stage"]);
        Assert.Equal(["worktree", "runId", "status", "summary", "verdicts", "quality"], schemas["finish_run"]);
        Assert.Equal(["worktree"], schemas["list_runs"]);
        Assert.Equal(["worktree", "runId"], schemas["get_run"]);

        // The dispatcher tools take a request id and a dispatcher, never a brief
        // (local ADR 0038 §4).
        Assert.Equal(["id", "dispatcher"], schemas["claim_session_request"]);
        Assert.Equal(["id", "outcome", "dispatcher", "sessionId"], schemas["mark_session_request"]);
        Assert.Equal(["id", "outcome", "dispatcher"], Required(Tools().Single(tool => tool.ProtocolTool.Name == "mark_session_request")));
        Assert.Equal(["state"], schemas["list_session_requests"]);
        Assert.Empty(Required(Tools().Single(tool => tool.ProtocolTool.Name == "list_session_requests")));

        // The surface and where to show it are required; the query keys of local ADR
        // 0041 §6 are each optional, and advertised so a model knows they exist.
        Assert.Equal(["surface", "target", "plan", "entry", "chapter", "scope"], schemas["open_window"]);
        Assert.Equal(["surface", "target"], Required(Tools().Single(tool => tool.ProtocolTool.Name == "open_window")));
        // The delivery tools take a delivery id and a dispatcher, never a message (local ADR 0041).
        Assert.Equal(["id", "dispatcher"], schemas["claim_delivery"]);
        Assert.Equal(["id", "outcome", "dispatcher"], schemas["mark_delivery"]);
        Assert.Equal(["state"], schemas["list_deliveries"]);
        Assert.Empty(Required(Tools().Single(tool => tool.ProtocolTool.Name == "list_deliveries")));
    }

    /// <summary>
    /// The notification tools build the way the SDK builds them, with the question relay and
    /// its timings composed or not: a host without them keeps the Notifications-only tools.
    /// </summary>
    [Fact]
    public async Task The_notification_tools_build_with_or_without_the_question_relay()
    {
        var port = new FakeNotifications();
        var relay = new FakeQuestionRelay(TimeProvider.System);

        var bare = ActivatorUtilities.CreateInstance<NotificationTools>(new ServiceCollection()
            .AddSingleton<INotifications>(port)
            .AddSingleton<INotificationStatuses>(port)
            .BuildServiceProvider());
        var relayed = ActivatorUtilities.CreateInstance<NotificationTools>(new ServiceCollection()
            .AddSingleton<INotifications>(port)
            .AddSingleton<INotificationStatuses>(port)
            .AddSingleton<IQuestionRelay>(relay)
            .BuildServiceProvider());

        AskQuestion[] questions = [new("What should the module be called?")];
        await Assert.ThrowsAsync<ModelContextProtocol.McpException>(() =>
            bare.AskAsync(sessionId: "sess-1", questions: questions, cancellationToken: TestContext.Current.CancellationToken));
        var payload = await relayed.AskAsync(sessionId: "sess-1", questions: questions, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("session", payload.Route);
        Assert.Single(relay.Asked);
    }

    /// <summary>
    /// A host that composes no Brain still builds the devbook tools, the way the
    /// SDK builds them — <c>ActivatorUtilities.CreateInstance</c> over the
    /// request's services — and answers the Brain unavailable rather than failing
    /// every devbook tool on its first call.
    /// </summary>
    [Fact]
    public void A_host_without_the_brain_ports_still_builds_the_devbook_tools()
    {
        var services = new ServiceCollection()
            .AddSingleton<IDevbookFolderSource>(
                new FakeDevbookFolderSource(Path.GetTempPath(), new DevbookFolderSetting(".arc42", "Architecture", ".arc42")))
            .AddSingleton<IDevbookAnnotationStore>(new FakeDevbookAnnotationStore())
            .AddSingleton<IRepositoryDirectory>(new FakeRepositoryDirectory())
            .BuildServiceProvider();

        var tools = ActivatorUtilities.CreateInstance<DevbookTools>(services);

        var context = Assert.Single(tools.ListKnowledgeContextsFor("#brain").Contexts);
        Assert.Equal(BrainConventions.FolderKey, context.Key);
        Assert.False(context.Available);
    }

    private static IEnumerable<McpServerTool> Tools()
    {
        var targets = new Dictionary<Type, object>
        {
            [typeof(WorkTools)] = new WorkTools(new FakeTaskItems(), new FakeRepositoryDirectory()),
            [typeof(TrackerTools)] = new TrackerTools(new FakeTaskItems(), new FakeRepositoryDirectory(), new FakeDevbookReferenceResolver()),
            [typeof(RoadmapTools)] = new RoadmapTools(
                new FakeRoadmapPlanning(RoadmapPlanDto.Empty),
                new FakeRepositoryDirectory()),
            [typeof(DevbookTools)] = new DevbookTools(
                new FakeDevbookFolderSource(Path.GetTempPath(), new DevbookFolderSetting(".arc42", "Architecture", ".arc42")),
                new FakeDevbookAnnotationStore(),
                new FakeRepositoryDirectory()),
            [typeof(SessionTools)] = new SessionTools(
                new FakeAgentSessionSource(AgentSessionCatalog.Empty),
                new FakeRepositoryDirectory()),
            [typeof(SurfaceTools)] = new SurfaceTools(new FakeDeliverySurfaceLifecycle()),
            [typeof(ImpedimentTools)] = new ImpedimentTools(new FakeImpediments()),
            [typeof(GoalTools)] = new GoalTools(new FakeGoals(), new FakeRepositoryDirectory()),
            [typeof(SessionRequestTools)] = new SessionRequestTools(new InMemorySessionRequestStore()),
            [typeof(NotificationTools)] = new NotificationTools(new FakeNotifications(), new FakeNotifications()),
            [typeof(WindowTools)] = new WindowTools(FakeSurfaceWindows.Catalog(), new FakeSurfaceWindows())
        };

        foreach (var group in BacklogMcpTools.Groups)
        {
            foreach (var method in group.ToolType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is null) continue;

                yield return McpServerTool.Create(method, targets[group.ToolType]);
            }
        }
    }

    private static IReadOnlyList<string> Required(McpServerTool tool) =>
        tool.ProtocolTool.InputSchema.TryGetProperty("required", out var required)
            ? [.. required.EnumerateArray().Select(name => name.GetString() ?? string.Empty)]
            : [];

    private static IReadOnlyList<string> Properties(JsonElement schema) =>
        schema.TryGetProperty("properties", out var properties)
            ? [.. properties.EnumerateObject().Select(property => property.Name)]
            : [];
}
