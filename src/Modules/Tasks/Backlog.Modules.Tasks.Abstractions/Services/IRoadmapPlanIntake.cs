using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// Where Import hands the <c>plan</c> entries of a document once its task entries are
/// down (ADR 0013, ruling 3).
/// <para>
/// A port on Tasks' own surface rather than a reference to Roadmap Planning, the
/// shape <see cref="IRoadmapTagSource"/> takes in the other direction: this module
/// asks its own port, and an infrastructure adapter that may see both contexts
/// answers it by calling Roadmap's own import command. Nothing of the parser's
/// crosses — the request is plain values.
/// </para>
/// <para>
/// A person pressing Import is a person changing the plan, so this is a command
/// carried across, not an event Roadmap reacts to.
/// </para>
/// </summary>
public interface IRoadmapPlanIntake
{
    /// <summary>
    /// Lays the request out on the roadmap. A refusal — a circular <c>after:</c> —
    /// comes back as <see cref="RoadmapIntakeResultDto.Refusal"/> rather than a
    /// failure: the tasks were written first and stay written, so the caller reports
    /// it beside them instead of failing the whole import.
    /// </summary>
    Task<RoadmapIntakeResultDto> LayOutAsync(
        RoadmapPlanIntakeRequestDto request,
        CancellationToken cancellationToken = default);
}
