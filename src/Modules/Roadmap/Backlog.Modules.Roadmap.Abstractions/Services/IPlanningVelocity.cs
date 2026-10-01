using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// How many story points the reader gets through in a week — the figure placing
/// an imported plan divides by.
/// <para>
/// It is a <em>reading preference the person owns, not an estimate the plan
/// registers</em> (ADR 0013, ruling 4). Placing an imported plan that states no due
/// date divides the effort its tasks registered by this, in calendar days — seven to
/// the week — and rounds up. Which pace
/// that is — typed, or measured over the last two, four or eight weeks — is the
/// reader's choice on the roadmap (<see cref="IPlanningPace"/>).
/// </para>
/// <para>
/// Kept per repository, because productivity differs from one project to the next
/// (ADR 0013, ruling 4 as amended on 2026-09-26). A plan filed under one configured
/// repository is placed at that repository's pace; one filed under several, at the
/// lowest of theirs, which draws the longest bar; one filed under none, or under a
/// repository nobody configured, at the global pace.
/// </para>
/// </summary>
public interface IPlanningVelocity
{
    /// <summary>
    /// Every pace placement may divide by, read once — for a caller placing many
    /// items, which would otherwise read the backlog once per item.
    /// </summary>
    Task<PacesInUseDto> ReadPacesInUseAsync(CancellationToken cancellationToken = default);

    /// <summary>The pace an item filed under <paramref name="repositoryAliases"/> is
    /// placed at: the global pace for none, otherwise the lowest in use among them.
    /// Always positive, so a caller may divide by it without guarding. The reader
    /// having chosen nothing reads as 7 — one a day.</summary>
    Task<decimal> GetStoryPointsPerWeekAsync(
        IReadOnlyCollection<string> repositoryAliases,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The paces the roadmap offers, and the reader's choice among them — globally, or
/// for one repository. The roadmap view's side of <see cref="IPlanningVelocity"/>.
/// <para>
/// A <c>repository</c> of <c>null</c> means the global pace, the one every plan filed
/// under no configured repository is placed at. The typed pace is one for every
/// scope, the fallback a scope places by when none of its stretches measured
/// anything. Which stretch a scope uses is chosen per repository; a repository nobody
/// chose one for reads the global choice, measured over its own finished work.
/// </para>
/// </summary>
public interface IPlanningPace
{
    /// <summary>Raised when a typed pace or a choice changed, on whatever thread
    /// changed it. Finished work changing a measured pace is
    /// <see cref="IRoadmapWorkChanges"/>'s news, not this.</summary>
    event Action? Changed;

    /// <summary>The paces for <paramref name="repository"/>, by alias. A repository
    /// that is not configured reads as the global paces, because that is what its
    /// plans are placed at.</summary>
    Task<PlanningPacesDto> ReadAsync(string? repository = null, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="IPlanningVelocity.ReadPacesInUseAsync"/>
    Task<PacesInUseDto> ReadPacesInUseAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the typed pace — the one fallback every scope shares — from what
    /// a text field hands back. Returns <c>null</c> when it took and was saved, and a
    /// message to show beside the field otherwise.</summary>
    string? SetManual(string? typed);

    /// <summary>Chooses the pace placement uses. Returns <c>null</c> when saved, a
    /// warning when it took but could not be written for next time.</summary>
    string? Choose(PaceSource source, string? repository = null);

    /// <summary>Sets the scope's own pace — <paramref name="repository"/>'s, or the
    /// heading's typed pace for <c>null</c> — and chooses
    /// <see cref="PaceSource.Set"/>, so placement uses it: what a lane's slider does
    /// on release. Returns <c>null</c> when saved, a refusal for a figure that is not
    /// a pace, and a warning when it took but could not be written for next time.</summary>
    string? SetOwn(decimal storyPointsPerWeek, string? repository = null);
}

/// <summary>
/// Where the reader's typed paces and choices are kept, globally and per repository.
/// A port the host answers over the per-device settings file, because a module may
/// not reference infrastructure
/// (<c>ModuleBoundaryTests.A_module_never_references_infrastructure</c>).
/// </summary>
public interface IPlanningVelocitySettings
{
    event Action? Changed;

    /// <summary>Story points a week, for <paramref name="repository"/> or — for
    /// <c>null</c> — globally. Always positive; 7 when the reader never typed one. A
    /// repository with no pace of its own answers the global one.</summary>
    decimal Manual(string? repository = null);

    /// <summary>The chosen pace, for <paramref name="repository"/> or globally. A
    /// repository with no choice of its own answers the global one.</summary>
    PaceSource Source(string? repository = null);

    /// <inheritdoc cref="IPlanningPace.SetManual"/>
    string? SetManual(string? typed, string? repository = null);

    /// <inheritdoc cref="IPlanningPace.Choose"/>
    string? Choose(PaceSource source, string? repository = null);

    /// <summary>Sets the scope's typed pace from what a text field hands back and
    /// chooses <see cref="PaceSource.Set"/>, as one change: one write and one
    /// <see cref="Changed"/>, so a slider let go of is heard once. Returns what
    /// <see cref="SetManual"/> would, and chooses nothing for a refused figure.</summary>
    string? SetOwn(string? typed, string? repository = null);
}

/// <summary>
/// The estimated backlog work finished since a day — what a measured pace is counted
/// from — and the repositories it can be counted apart for. Answered by an adapter
/// over the Tasks context.
/// </summary>
public interface IRoadmapCompletedWork
{
    /// <summary>The configured repositories, by alias: the ones a pace is kept and
    /// measured for. A plan filed under an alias outside this list is placed at the
    /// global pace.</summary>
    IReadOnlyList<string> Repositories { get; }

    /// <summary>Entries finished on or after <paramref name="since"/> that carry an
    /// estimate, each with the repositories it was filed under. An entry with none
    /// has nothing to add to a pace.</summary>
    Task<IReadOnlyList<CompletedEffortDto>> CompletedSinceAsync(
        DateOnly since,
        CancellationToken cancellationToken = default);
}
