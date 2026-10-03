using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;

namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// How many story points the reader gets through in a week — the figure placing
/// an imported plan divides by.
/// <para>
/// It is a <em>reading preference the person owns, not an estimate the plan
/// registers</em> (ADR 0013, ruling 4). Placing an imported plan that states no due
/// date counts the effort its tasks registered at this pace through the person's
/// working week, in hours (local ADR 0019; <see cref="EffortWindow"/>). Which pace
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
    /// having chosen nothing reads as 7 a working week.</summary>
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

    /// <summary>
    /// Blocks <paramref name="date"/> when it is worked, or unblocks it when it is not —
    /// what pressing a day column's head does (local ADR 0019, §§4 and 5). Raises
    /// <see cref="Changed"/> when it took, so every bar still sized by its effort is
    /// placed again at once. Returns <c>null</c> when saved, a refusal when the host keeps
    /// no week, and a warning when it took but could not be written for next time.
    /// </summary>
    string? ToggleWorkedDay(DateOnly date);

    /// <summary>
    /// Makes every date from <paramref name="from"/> through <paramref name="through"/> a
    /// day off — what adding a range in the Days off dialog does (local ADR 0019, §4): a
    /// date the pattern works is blocked, one it leaves off holds no override. One change
    /// for the whole range, so <see cref="Changed"/> is raised once and the bars are placed
    /// again once. Returns <c>null</c> when saved or nothing changed, a refusal for a range
    /// ending before it starts or longer than <see cref="WorkingHours.MaxDaysOffRange"/>
    /// days, and a warning when it took but could not be written for next time.
    /// </summary>
    string? BlockDays(DateOnly from, DateOnly through);

    /// <summary>
    /// Makes <paramref name="date"/> worked — what adding a worked day in the Days off
    /// dialog does: a date the pattern leaves off is unblocked. Raises
    /// <see cref="Changed"/> when it took. Returns <c>null</c> when saved, and a note when
    /// the pattern already works the date and nothing was added.
    /// </summary>
    string? AddWorkedDay(DateOnly date);

    /// <summary>
    /// Returns <paramref name="date"/> to its weekday's pattern — what removing an entry in
    /// the Days off dialog does. Raises <see cref="Changed"/> when it had an override.
    /// Returns <c>null</c> when saved or there was nothing to remove.
    /// </summary>
    string? RemoveDayOverride(DateOnly date);
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

    /// <summary>
    /// The person's working week, which a week of every pace means and every window is
    /// counted through (local ADR 0019). It travels with the pace, so every device sizes
    /// the same bars. <see cref="Changed"/> is raised when it changes too. The default
    /// week for a host that keeps none.
    /// </summary>
    WorkingHours WorkingWeek => WorkingHours.Default;

    /// <summary>
    /// Blocks <paramref name="date"/> when it is worked, or unblocks it when it is not
    /// (local ADR 0019, §5), in the week <see cref="WorkingWeek"/> answers, and raises
    /// <see cref="Changed"/>. Returns <c>null</c> when stored, or why not. A host that
    /// keeps no week has none to change, and says so.
    /// </summary>
    string? ToggleWorkedDay(DateOnly date) => "This device keeps no working week to block or unblock a day in.";

    /// <summary>
    /// Makes every date from <paramref name="from"/> through <paramref name="through"/> a
    /// day off (<see cref="WorkingHours.WithDaysOff"/>; local ADR 0019, §4) in the week
    /// <see cref="WorkingWeek"/> answers, as one change: one write and one
    /// <see cref="Changed"/>. Returns <c>null</c> when stored or nothing changed, or why
    /// not. A host that keeps no week has none to change, and says so.
    /// </summary>
    string? BlockDays(DateOnly from, DateOnly through) => "This device keeps no working week to block or unblock a day in.";

    /// <summary>
    /// Makes <paramref name="date"/> worked (<see cref="WorkingHours.WithWorkedDay"/>) and
    /// raises <see cref="Changed"/>. Returns <c>null</c> when stored, or a note saying why
    /// nothing was: the pattern already works the date.
    /// </summary>
    string? AddWorkedDay(DateOnly date) => "This device keeps no working week to block or unblock a day in.";

    /// <summary>
    /// Returns <paramref name="date"/> to its pattern (<see cref="WorkingHours.WithoutOverride"/>)
    /// and raises <see cref="Changed"/> when it had an override. Returns <c>null</c> when
    /// stored or there was nothing to remove, or why not.
    /// </summary>
    string? RemoveDayOverride(DateOnly date) => "This device keeps no working week to block or unblock a day in.";
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
