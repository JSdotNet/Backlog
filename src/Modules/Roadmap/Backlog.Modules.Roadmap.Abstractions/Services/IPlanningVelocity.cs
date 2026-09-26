using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// How many story points the reader gets through in a week — the one figure placing
/// an imported plan divides by.
/// <para>
/// It is a <em>reading preference the person owns, not an estimate the plan
/// registers</em> (ADR 0013, ruling 4). Placing an imported plan that states no due
/// date divides the effort its tasks registered by this, in calendar days — seven to
/// the week — and rounds up. Which pace
/// that is — typed, or measured over the last two, four or eight weeks — is the
/// reader's choice on the roadmap (<see cref="IPlanningPace"/>).
/// </para>
/// </summary>
public interface IPlanningVelocity
{
    /// <summary>Always positive, so a caller may divide by it without guarding.
    /// The reader having chosen nothing reads as 7 — one a day.</summary>
    Task<decimal> GetStoryPointsPerWeekAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The paces the roadmap offers, and the reader's choice among them. The roadmap
/// view's side of <see cref="IPlanningVelocity"/>.
/// </summary>
public interface IPlanningPace
{
    /// <summary>Raised when the typed pace or the choice changed, on whatever thread
    /// changed it. Finished work changing a measured pace is
    /// <see cref="IRoadmapWorkChanges"/>'s news, not this.</summary>
    event Action? Changed;

    Task<PlanningPacesDto> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the typed pace from what a text field hands back. Returns
    /// <c>null</c> when it took and was saved, and a message to show beside the
    /// field otherwise.</summary>
    string? SetManual(string? typed);

    /// <summary>Chooses the pace placement uses. Returns <c>null</c> when saved, a
    /// warning when it took but could not be written for next time.</summary>
    string? Choose(PaceSource source);
}

/// <summary>
/// Where the reader's typed pace and choice are kept. A port the host answers over
/// the per-device settings file, because a module may not reference infrastructure
/// (<c>ModuleBoundaryTests.A_module_never_references_infrastructure</c>).
/// </summary>
public interface IPlanningVelocitySettings
{
    event Action? Changed;

    /// <summary>Story points a week. Always positive; 7 when the reader never typed
    /// one.</summary>
    decimal Manual { get; }

    PaceSource Source { get; }

    /// <inheritdoc cref="IPlanningPace.SetManual"/>
    string? SetManual(string? typed);

    /// <inheritdoc cref="IPlanningPace.Choose"/>
    string? Choose(PaceSource source);
}

/// <summary>
/// The estimated backlog work finished since a day — what a measured pace is counted
/// from. Answered by an adapter over the Tasks context.
/// </summary>
public interface IRoadmapCompletedWork
{
    /// <summary>Entries finished on or after <paramref name="since"/> that carry an
    /// estimate. An entry with none has nothing to add to a pace.</summary>
    Task<IReadOnlyList<CompletedEffortDto>> CompletedSinceAsync(
        DateOnly since,
        CancellationToken cancellationToken = default);
}
