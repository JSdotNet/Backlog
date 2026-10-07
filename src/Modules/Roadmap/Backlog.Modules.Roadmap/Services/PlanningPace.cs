using System.Globalization;

using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Roadmap.Services;

/// <summary>
/// The reader's paces: the one they typed, kept by the host, and three measured from
/// the estimated work they finished over the last two, four and eight weeks — once
/// globally and once for each configured repository.
/// <para>
/// Every pace is story points a working week, a week being the hours of the person's
/// working week (local ADR 0019). A measured one is the finished effort over the working
/// hours in its stretch, times the hours of a week. The hours are counted date by date,
/// so a blocked date adds none and an unblocked one adds its weekday's; with no override
/// in the stretch, every stretch being whole weeks, that is the effort over the weeks,
/// whatever the pattern. Placement counts the same hours forward (<c>EffortWindow</c>),
/// so a bar spans the days the work was measured to take.
/// </para>
/// <para>
/// A repository's measured paces count only the work filed under it, and work filed
/// under two counts in full toward both: each repository was worked on at the pace
/// its own finished work shows. The global paces count every finished task once.
/// The typed pace is one for every scope — the fallback a scope places by when none
/// of its stretches measured anything — and which stretch a scope uses is chosen per
/// repository; a repository nobody chose one for reads the global choice.
/// </para>
/// <para>
/// Read on every call and kept nowhere, so a task ticked off is in the next reading.
/// The windows the importer still owns are read at the pace in use wherever they are
/// drawn or reported (<c>EffortWindow</c>), so a person changing the pace or the
/// choice — or finished work moving a measured pace — redraws them without writing the
/// plan (ADR 0013, ruling 5 as amended; local ADR 0018).
/// </para>
/// </summary>
internal sealed class PlanningPace(
    IPlanningVelocitySettings settings,
    IRoadmapCompletedWork completed,
    TimeProvider clock) : IPlanningPace, IPlanningVelocity
{
    /// <summary>The longest stretch measured, so one read covers all three.</summary>
    private const int LongestWeeks = 8;

    public event Action? Changed
    {
        add => settings.Changed += value;
        remove => settings.Changed -= value;
    }

    public async Task<PlanningPacesDto> ReadAsync(
        string? repository = null,
        CancellationToken cancellationToken = default)
    {
        var (today, finished) = await FinishedAsync(cancellationToken);

        return PacesOf(Configured(repository), finished, today);
    }

    public async Task<PacesInUseDto> ReadPacesInUseAsync(CancellationToken cancellationToken = default)
    {
        var (today, finished) = await FinishedAsync(cancellationToken);

        var byRepository = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in completed.Repositories)
        {
            if (string.IsNullOrWhiteSpace(alias)) continue;

            var key = alias.Trim();
            if (byRepository.ContainsKey(key)) continue;

            byRepository[key] = PacesOf(key, finished, today).InUse;
        }

        return new PacesInUseDto(PacesOf(null, finished, today).InUse, byRepository)
        {
            Week = Week,
            AliasesById = completed.AliasesById
        };
    }

    public async Task<decimal> GetStoryPointsPerWeekAsync(
        string? repository = null,
        CancellationToken cancellationToken = default)
    {
        var paces = await ReadPacesInUseAsync(cancellationToken);
        return paces.PaceOf(paces.AliasOf(repository));
    }

    public string? SetManual(string? typed) => settings.SetManual(typed);

    public string? Choose(PaceSource source, string? repository = null) =>
        settings.Choose(source, Scope(repository));

    /// <summary>Sets the scope's own typed pace and chooses it, as one change, so every
    /// pace on screen hears it once. The typed text goes through the settings' one
    /// parsing rule, in the invariant spelling, so a refusal reads the same here as
    /// beside the heading's field.</summary>
    public string? SetOwn(decimal storyPointsPerWeek, string? repository = null) =>
        settings.SetOwn(storyPointsPerWeek.ToString(CultureInfo.InvariantCulture), Scope(repository));

    /// <summary>Blocks or unblocks a date in the week kept with the pace. The settings
    /// raise <see cref="Changed"/> when it took, and this event is theirs, so a band
    /// listening re-places its bars at once.</summary>
    public string? ToggleWorkedDay(DateOnly date) => settings.ToggleWorkedDay(date);

    /// <summary>A range of days off in the week kept with the pace, as one change: the
    /// settings raise <see cref="Changed"/> once.</summary>
    public string? BlockDays(DateOnly from, DateOnly through) => settings.BlockDays(from, through);

    /// <summary>A worked day in the week kept with the pace.</summary>
    public string? AddWorkedDay(DateOnly date) => settings.AddWorkedDay(date);

    /// <summary>A date back on its pattern in the week kept with the pace.</summary>
    public string? RemoveDayOverride(DateOnly date) => settings.RemoveDayOverride(date);

    internal static PlanningPacesDto Paces(
        decimal manual,
        PaceSource source,
        IReadOnlyList<CompletedEffortDto> finished,
        DateOnly today,
        decimal? own = null,
        WorkingHours? week = null)
    {
        var counted = (week ?? WorkingHours.Default).Effective;

        return new(
            manual,
            Measured(finished, today, 2, counted),
            Measured(finished, today, 4, counted),
            Measured(finished, today, LongestWeeks, counted),
            source)
        {
            Own = own ?? manual,
            Week = counted
        };
    }

    /// <summary>
    /// Effort finished in the <paramref name="weeks"/> weeks ending today, today
    /// included, per working week: the effort over the working hours in the stretch,
    /// times the hours of <paramref name="week"/> (local ADR 0019) — four decimals, the
    /// finest pace the typed one can hold. <c>null</c> when nothing estimated was
    /// finished: that stretch measured no pace, which is not the same as a pace of zero.
    /// <c>null</c> too when every date in it was blocked: no hours, nothing to divide by.
    /// <para>
    /// Each date counts its own hours, its override first (local ADR 0019, §2); the
    /// hours of a week stay the pattern's, so a blocked week in the stretch raises the
    /// figure and an unblocked Saturday lowers it.
    /// </para>
    /// <para>
    /// Multiplied before dividing, and counted in ticks, so a stretch of whole weeks
    /// measures exactly the effort over the weeks.
    /// </para>
    /// </summary>
    internal static decimal? Measured(
        IReadOnlyList<CompletedEffortDto> finished,
        DateOnly today,
        int weeks,
        WorkingHours? week = null)
    {
        var counted = (week ?? WorkingHours.Default).Effective;
        var from = StartOf(today, weeks);
        var effort = finished
            .Where(entry => entry.CompletedOn >= from && entry.CompletedOn <= today && entry.Effort > 0)
            .Sum(entry => (decimal)entry.Effort);

        if (effort <= 0) return null;

        long stretch = 0;
        for (var day = from; day <= today; day = day.AddDays(1)) stretch += counted.WorkedOn(day).Ticks;

        if (stretch <= 0) return null;

        return Math.Round(effort * counted.PerWeek.Ticks / stretch, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>The week every pace is counted in and every window counted through:
    /// the one kept with the pace, or the default when it works no hours.</summary>
    private WorkingHours Week => settings.WorkingWeek.Effective;

    /// <summary>One read of the longest stretch, which every pace of every scope is
    /// counted from.</summary>
    private async Task<(DateOnly Today, IReadOnlyList<CompletedEffortDto> Finished)> FinishedAsync(
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var finished = await completed.CompletedSinceAsync(StartOf(today, LongestWeeks), cancellationToken);
        return (today, finished);
    }

    /// <summary>The paces for one scope: the global ones for <c>null</c>, otherwise
    /// the repository's choice over the work filed under it. The typed pace is the
    /// global one either way: it is the single fallback.</summary>
    private PlanningPacesDto PacesOf(string? repository, IReadOnlyList<CompletedEffortDto> finished, DateOnly today)
    {
        // The global scope's own pace is the heading's typed one.
        if (repository is null) return Paces(settings.Manual(), settings.Source(), finished, today, week: Week);

        IReadOnlyList<CompletedEffortDto> own =
        [
            .. finished.Where(entry => entry.RepositoryAliases.Contains(repository, StringComparer.OrdinalIgnoreCase))
        ];

        return Paces(settings.Manual(), settings.Source(repository), own, today, settings.Manual(repository), Week);
    }

    /// <summary>The configured repository an alias names, or <c>null</c> — the global
    /// scope — for none, or for one nobody configured: its plans are placed at the
    /// global pace, so that is the pace to show for it.</summary>
    private string? Configured(string? repository)
    {
        var alias = Scope(repository);
        if (alias is null) return null;

        return completed.Repositories.Any(known => string.Equals(known?.Trim(), alias, StringComparison.OrdinalIgnoreCase))
            ? alias
            : null;
    }

    private static string? Scope(string? repository) =>
        string.IsNullOrWhiteSpace(repository) ? null : repository.Trim();

    private static DateOnly StartOf(DateOnly today, int weeks) => today.AddDays(-(weeks * 7 - 1));
}
