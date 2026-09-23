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
}
