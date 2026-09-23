using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The thirteen tools, the five groups they come in, and the feature key each
/// group answers to.
/// <para>
/// Seven of them read and six of those are all there used to be — the assembly
/// was read-only until the tracker operations arrived, and several docs under it
/// said so in those words. What replaced that claim is narrower and still worth
/// stating: nothing here registers a repository (see <see cref="RepositoryScope"/>),
/// nothing here deletes, and every write goes through the Tasks port's one text
/// grammar rather than setting a field.
/// </para>
/// <para>
/// Local ADR 0012 §7 exposes tools "in groups, each behind one
/// <see cref="IAppFeatureSettings"/> check", and a group whose feature is off is
/// <em>absent</em> from <c>tools/list</c> rather than present and refusing. A
/// group is therefore a tool class — one <c>[McpServerToolType]</c> per switchable
/// area — and this is the table that says which key each one reads.
/// </para>
/// <para>
/// <b>A key may be read by two groups, and one is.</b> §7 asks for one check per
/// group; it does not ask for one group per key, and the two are different
/// requirements. A group is the unit the check is applied at, so that a class
/// cannot half-appear; a key is the switchable <em>area</em> a person turns off.
/// <see cref="Work"/> and <see cref="Tracker"/> are two classes because they are
/// two descriptions of what a tool is for, and one key because reading the
/// backlog and moving it are the same thing to switch off.
/// </para>
/// <para>
/// It lives in this project rather than in the host because both hosts need it
/// and neither owns it: the desktop listener and the web harness each install the
/// same list-tools filter, and a second copy of "which tools are the task tools"
/// would be a second thing to keep in step with the classes below. It is also the
/// only part of the gating that can be tested without a transport.
/// </para>
/// <para>
/// What it deliberately does not do is read the flags itself. The check has to
/// happen per <c>tools/list</c> request for a flag flip to take effect without a
/// restart, and the settings instance that answers it belongs to whichever
/// request scope is in play — so the host passes one in.
/// </para>
/// </summary>
public static class BacklogMcpTools
{
    /// <summary>One switchable group of tools: the class the host registers with
    /// <c>WithTools&lt;T&gt;()</c>, the feature key that decides whether its tools
    /// are listed at all, and the names those tools carry on the wire.</summary>
    /// <param name="FeatureKey">The <see cref="IAppFeatureSettings"/> key. One per
    /// group, per local ADR 0012 §7.</param>
    /// <param name="ToolType">The attributed class. The host hands this to
    /// <c>WithTools</c>; nothing here constructs it.</param>
    /// <param name="ToolNames">The <c>tools/list</c> names, exactly as the
    /// <c>[McpServerTool(Name = …)]</c> attributes spell them.</param>
    public sealed record McpToolGroup(string FeatureKey, Type ToolType, IReadOnlyList<string> ToolNames);

    /// <summary>The backlog and import-plan tools, behind <c>backlog</c> — the
    /// key the Tasks context owns and the pane reads.</summary>
    public static McpToolGroup Work { get; } = new(
        TasksFeatures.Tasks,
        typeof(WorkTools),
        [WorkTools.ListEntries, WorkTools.GetPlanItems]);

    /// <summary>
    /// The tracker operations — find, read, transition, comment, link and create
    /// — behind <c>backlog</c>, the same key <see cref="Work"/> reads.
    /// <para>
    /// <b>The shared key is the decision, not an oversight.</b> Local ADR 0012 §7
    /// requires one <see cref="IAppFeatureSettings"/> check per group, which this
    /// satisfies: the check runs once for this group and once for
    /// <see cref="Work"/>, and each group's tools appear or vanish whole. What §7
    /// does not require is a key per group. A key names a switchable area, and
    /// reading the backlog and moving it are one area — somebody switching Tasks
    /// off means "no backlog tools", not "no backlog tools except the ones that
    /// write".
    /// </para>
    /// <para>
    /// The split into two classes is about description rather than gating.
    /// <see cref="WorkTools"/> answers one question two ways and says so; these
    /// six do six different things, four of them writes. Folding them together
    /// would leave one class whose doc could not honestly describe it, and a
    /// model reads that doc to decide what it is looking at.
    /// </para>
    /// </summary>
    public static McpToolGroup Tracker { get; } = new(
        TasksFeatures.Tasks,
        typeof(TrackerTools),
        [
            TrackerTools.FindItem,
            TrackerTools.ReadItem,
            TrackerTools.Transition,
            TrackerTools.Comment,
            TrackerTools.LinkChange,
            TrackerTools.CreateItem
        ]);

    /// <summary>The roadmap tool, behind <c>roadmap</c>.
    /// <para>
    /// Its own group because Roadmap Planning is its own bounded context with its
    /// own key — the one the Home shell reads to decide whether to draw the band
    /// — and §7's rule is one check per group, each group behind its own
    /// context's flag. It rode with the backlog tools once, on the argument that a
    /// session reading a plan item wants the roadmap entry it executes; that is an
    /// argument about what a session will ask for next, not about what somebody
    /// switching the roadmap off meant by it.
    /// </para></summary>
    public static McpToolGroup Roadmap { get; } = new(
        RoadmapFeatures.Roadmap,
        typeof(RoadmapTools),
        [RoadmapTools.GetRoadmap]);

    /// <summary>The devbook tools, behind <c>repository-devbook</c> — the key the
    /// Shell reads to decide whether to offer the pane at all, which is the same
    /// question asked of a session.</summary>
    public static McpToolGroup Devbook { get; } = new(
        DevbookFeatures.RepositoryDevbook,
        typeof(DevbookTools),
        [DevbookTools.ListKnowledgeContexts, DevbookTools.ReadKnowledgeChapter, DevbookTools.ListAnnotations]);

    /// <summary>The sessions tool, behind <c>sessions</c>. Its own group because
    /// it is its own switchable area: a person who has turned the sessions
    /// inventory off has turned off the thing this tool reads.</summary>
    public static McpToolGroup Sessions { get; } = new(
        SessionFeatures.Sessions,
        typeof(SessionTools),
        [SessionTools.ListSessions]);

    /// <summary>Every group, in the order a host should register them.</summary>
    public static IReadOnlyList<McpToolGroup> Groups { get; } = [Work, Tracker, Roadmap, Devbook, Sessions];

    /// <summary>Every tool name this library publishes. What a <c>tools/list</c>
    /// with every flag on has to come back with.</summary>
    public static IReadOnlyList<string> ToolNames { get; } =
        [.. Groups.SelectMany(group => group.ToolNames)];

    /// <summary>
    /// Whether a tool belongs in <c>tools/list</c> right now.
    /// <para>
    /// The predicate behind both filters the host installs: the list-tools filter
    /// drops what this refuses, and the call-tool filter refuses a call to it —
    /// because a client holding a list from before the flip will still try, and a
    /// tool that is absent from the list but answers anyway is not absent.
    /// </para>
    /// <para>
    /// A name this library does not publish is left alone. The host may map other
    /// tools onto the same server, and a filter that hid everything it did not
    /// recognise would take them with it.
    /// </para>
    /// </summary>
    public static bool IsExposed(string toolName, IAppFeatureSettings features) =>
        IsExposed(toolName, features, StringComparer.Ordinal);

    /// <summary>
    /// Whether a call to a tool may be dispatched right now — the same question
    /// as <see cref="IsExposed"/>, asked the way a refusal has to ask it.
    /// <para>
    /// The difference is one comparer, and it is the difference between a filter
    /// and a gate. A published name is exact, so the list filter compares
    /// ordinally: that is what it means to publish <c>list_entries</c> and not
    /// <c>list_ENTRIES</c>. A refusal cannot afford the same literalism. The SDK
    /// dispatches by ordinal name today, so the two answers agree today — but
    /// <c>IsExposed</c> answers <c>true</c> for every name it does not recognise,
    /// on purpose, so that a host mapping other tools onto this server keeps them.
    /// Put those two together on a dispatcher that ever matched loosely and
    /// <c>list_ENTRIES</c> is an unrecognised name that is allowed through to the
    /// task tool behind a switched-off flag.
    /// </para>
    /// <para>
    /// So the call path recognises this library's own names without regard to
    /// case and refuses on a hit, and leaves everything else alone exactly as
    /// before. It costs nothing to be right about a case the dispatcher does not
    /// produce, and the alternative is a security property that rests on an
    /// implementation detail of somebody else's package.
    /// </para>
    /// </summary>
    public static bool IsCallable(string toolName, IAppFeatureSettings features) =>
        IsExposed(toolName, features, StringComparer.OrdinalIgnoreCase);

    private static bool IsExposed(string toolName, IAppFeatureSettings features, StringComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(features);

        var group = Groups.FirstOrDefault(candidate => candidate.ToolNames.Contains(toolName, comparer));

        return group is null || features.IsEnabled(group.FeatureKey);
    }
}
