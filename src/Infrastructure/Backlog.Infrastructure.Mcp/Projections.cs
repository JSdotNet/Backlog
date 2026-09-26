using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The modules' DTOs as this project's payloads, in one place.
/// <para>
/// One file rather than a static method beside each tool, because the projection
/// is the thing most likely to be copied badly: two tools answer with an entry
/// and both have to answer with the same entry.
/// </para>
/// </summary>
internal static class Projections
{
    internal static EntryPayload Entry(TaskItemDto entry) => new(
        entry.Id,
        entry.Title,
        entry.Body,
        EnumMap.ToWire(entry.Type),
        EnumMap.ToWire(entry.Status),
        EnumMap.ToWire(entry.Priority),
        entry.Area,
        entry.Tags,
        entry.Order,
        entry.TotalSubItems,
        entry.CompletedSubItems,
        entry.DueOn,
        entry.StartedOn,
        entry.CompletedOn,
        entry.Effort,
        entry.RepoIds ?? [],
        entry.DependsOn ?? [],
        entry.ImportPlanId,
        entry.ImportItemId,
        entry.CreatedAt);

    internal static RoadmapItemPayload RoadmapItem(RoadmapItemDto item) => new(
        item.Id,
        item.Title,
        item.Tag,
        item.Start,
        item.End,
        item.Priority.ToString(),
        item.RepositoryAliases,
        item.Lane,
        item.TaskId,
        item.DependsOn,
        item.Notes,
        item.Knowledge);

    internal static RoadmapMilestonePayload Milestone(RoadmapMilestoneDto milestone) => new(
        milestone.Id,
        milestone.Title,
        milestone.On,
        milestone.Kind.ToString(),
        milestone.RepositoryAliases,
        milestone.Lane,
        milestone.DependsOn,
        milestone.IsPlanWide);

    internal static RoadmapContradictionPayload Contradiction(PlanContradictionDto contradiction) =>
        new(contradiction.NodeId, contradiction.DependsOnId, contradiction.Reason);

    internal static KnowledgeContextPayload KnowledgeContext(DevbookFolderSetting folder, DevbookFolderLocation location) => new(
        folder.Key,
        folder.DisplayName,
        folder.EffectivePath,
        location.Available,
        location.Message,
        location.ScopeLabel,
        location.Source.ToString(),
        location.Pending);

    internal static ChapterNotePayload Note(DevbookAnnotation annotation) => new(
        annotation.Id,
        annotation.BlockIndex,
        annotation.Body,
        annotation.Author,
        annotation.CreatedAt,
        annotation.UpdatedAt,
        annotation.Resolved);

    internal static SessionPayload Session(AgentSession session) => new(
        session.Id,
        session.Kind.ToString(),
        session.Environment,
        session.Title,
        session.WorkingFolder,
        session.Repository,
        session.ResolvedRepository,
        session.Branch,
        session.StartedAt,
        session.LastActivityAt,
        session.State.ToString(),
        session.TurnCount,
        session.Origin.ToString());

    internal static SurfaceOpenedPayload SurfaceOpened(DeliverySurfaceOpened opened) => new(
        // The member name, never the ordinal: DeliverySurfaceActivation's own
        // doc calls its order load-bearing, which is exactly the kind of enum a
        // number silently re-points the day somebody inserts a member.
        opened.Activation.ToString(),
        opened.Answer);

    internal static RunStartedPayload RunStarted(DeliveryRunStarted started) => new(
        started.RunId,
        started.Resumed,
        started.SessionTitle);

    internal static StageUpdatedPayload StageUpdated(DeliveryStageUpdated updated) => new(
        updated.RunId,
        updated.StageIndex,
        updated.Status,
        updated.DoneCount,
        updated.SessionTitle);

    internal static RunPayload Run(DeliveryRun run) => new(
        run.Id,
        run.Worktree,
        run.SkillId,
        run.Title,
        run.Status,
        run.ChangeKind,
        run.InProgress,
        run.StartedAt,
        run.UpdatedAt,
        [.. run.Stages.Select(stage => new RunStagePayload(stage.Name, stage.Status, stage.DurationMs, stage.DoneCount))],
        run.SessionIds);

    internal static RunsPayload Runs(string worktree, IReadOnlyList<DeliveryRun> runs) => new(
        worktree,
        runs.Count,
        [.. runs.Select(Run)]);

    /// <summary>The argument records as the port's own. Null stays null rather
    /// than becoming an empty list: the port reads absent as "this call says
    /// nothing about links" and an empty list as "there are none", and a stage
    /// updated for its status alone must not erase the links an earlier call set.</summary>
    internal static IReadOnlyList<DeliveryStageLink>? StageLinks(IReadOnlyList<StageLinkInput>? links) =>
        links is null ? null : [.. links.Select(link => new DeliveryStageLink(link.Label, link.Url, link.Description))];

    internal static IReadOnlyList<DeliveryScenario>? Scenarios(IReadOnlyList<ScenarioInput>? scenarios) =>
        scenarios is null
            ? null
            : [.. scenarios.Select(scenario => new DeliveryScenario(
                scenario.Name,
                scenario.Status,
                scenario.Notes,
                scenario.Evidence))];

    internal static DeliveryMonitoring? Monitoring(MonitoringInput? monitoring) =>
        monitoring is null ? null : new DeliveryMonitoring(monitoring.Summary, monitoring.Findings);
}
