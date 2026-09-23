using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Tasks' <see cref="IRoadmapPlanIntake"/> by calling Roadmap's own import
/// command (ADR 0013, ruling 3).
/// <para>
/// The first cross-context adapter here that writes, and it still holds no roadmap
/// logic: it lifts the plan sigil off each tag, maps Tasks' values onto Roadmap's DTO,
/// and delegates through <see cref="IRoadmapPlanning"/> — the same join
/// <see cref="RoadmapPlanTagSource"/> makes in the other direction.
/// </para>
/// </summary>
public sealed class RoadmapPlanIntake : IRoadmapPlanIntake
{
    private readonly IRoadmapPlanning _planning;

    public RoadmapPlanIntake(IRoadmapPlanning planning)
    {
        ArgumentNullException.ThrowIfNull(planning);
        _planning = planning;
    }

    public async Task<RoadmapIntakeResultDto> LayOutAsync(
        RoadmapPlanIntakeRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var imported = await _planning.ImportPlanItemsAsync(
            [.. request.Entries.Select(ToRoadmap)],
            [.. request.GatheredEffort.Select(effort => new PlanTagEffortDto(Lift(effort.Tag), effort.TotalEffort, effort.UnestimatedCount))],
            [.. request.LayOutIfMissing.Select(ToRoadmap)],
            cancellationToken);

        // All or nothing on the roadmap's side: a refusal left the plan as it was.
        if (imported.IsFailure) return RoadmapIntakeResultDto.Refused(imported.Error.Message);

        var result = imported.Value;
        return new RoadmapIntakeResultDto(
            result.Created.Count,
            result.Updated.Count,
            result.Relengthened.Count,
            result.SkippedWithoutTag,
            [],
            [.. result.AmbiguousTags.Select(ambiguous => ambiguous.Tag)],
            [.. result.UnresolvedDependencies.Select(unresolved => new ImportUnresolvedDependencyDto(unresolved.Tag, unresolved.After))]);
    }

    private static PlanImportEntryDto ToRoadmap(RoadmapPlanEntryDto entry) =>
        new(
            entry.Title,
            Lift(entry.Tag),
            entry.LocalId,
            entry.RepositoryAliases,
            ToPlanning(entry.Priority),
            entry.Due,
            entry.After,
            entry.Notes);

    /// <summary>The plan holds the bare slug; the backlog stores it as <c>+slug</c>.</summary>
    private static string Lift(string tag) => tag.StartsWith('+') ? tag[1..] : tag;

    /// <summary>The same four words; absent reads as medium.</summary>
    private static PlanningPriority ToPlanning(Priority? priority) => priority switch
    {
        Priority.Low => PlanningPriority.Low,
        Priority.High => PlanningPriority.High,
        Priority.Critical => PlanningPriority.Critical,
        _ => PlanningPriority.Medium
    };
}
