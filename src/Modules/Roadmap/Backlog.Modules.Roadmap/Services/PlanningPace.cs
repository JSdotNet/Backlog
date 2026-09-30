using System.Globalization;

using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Modules.Roadmap.Services;

/// <summary>
/// The reader's paces: the one they typed, kept by the host, and three measured from
/// the estimated work they finished over the last two, four and eight weeks — once
/// globally and once for each configured repository.
/// <para>
/// Every pace is story points a week. A measured one is finished effort over the
/// whole weeks of its stretch, and placement turns a week into seven
/// <em>calendar</em> days, weekends included, because that is what the roadmap draws
/// in: a window is placed in calendar days and ignores the working week (ADR 0013).
/// Taking the week as five working days instead would draw every bar about a third
/// shorter than the stretch it was measured over actually took.
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

    /// <summary>The finest pace the settings file can hold; anything under it is
    /// refused by the settings themselves.</summary>
    private const decimal SmallestPace = 0.0001m;

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

        return new PacesInUseDto(PacesOf(null, finished, today).InUse, byRepository);
    }

    public async Task<decimal> GetStoryPointsPerWeekAsync(
        IReadOnlyCollection<string> repositoryAliases,
        CancellationToken cancellationToken = default) =>
        (await ReadPacesInUseAsync(cancellationToken)).For(repositoryAliases);

    public string? SetManual(string? typed) => settings.SetManual(typed);

    public string? Choose(PaceSource source, string? repository = null) =>
        settings.Choose(source, Scope(repository));

    /// <summary>Sets the scope's own typed pace and chooses it. The typed text goes
    /// through the settings' one parsing rule, in the invariant spelling, so a refusal
    /// reads the same here as beside the heading's field.</summary>
    public string? SetOwn(decimal storyPointsPerWeek, string? repository = null)
    {
        var scope = Scope(repository);

        // A figure the settings cannot hold is refused with its message and nothing
        // is chosen; past that, whatever comes back is a warning and the pace took.
        if (storyPointsPerWeek < SmallestPace)
        {
            return settings.SetManual(storyPointsPerWeek.ToString(CultureInfo.InvariantCulture), scope);
        }

        var warning = settings.SetManual(storyPointsPerWeek.ToString(CultureInfo.InvariantCulture), scope);

        return settings.Choose(PaceSource.Set, scope) ?? warning;
    }

    internal static PlanningPacesDto Paces(
        decimal manual,
        PaceSource source,
        IReadOnlyList<CompletedEffortDto> finished,
        DateOnly today,
        decimal? own = null) =>
        new(
            manual,
            Measured(finished, today, 2),
            Measured(finished, today, 4),
            Measured(finished, today, LongestWeeks),
            source)
        {
            Own = own ?? manual
        };

    /// <summary>
    /// Effort finished in the <paramref name="weeks"/> weeks ending today, today
    /// included, per week — four decimals, the finest pace the typed one can
    /// hold. <c>null</c> when nothing estimated was finished: that stretch measured
    /// no pace, which is not the same as a pace of zero.
    /// </summary>
    internal static decimal? Measured(IReadOnlyList<CompletedEffortDto> finished, DateOnly today, int weeks)
    {
        var from = StartOf(today, weeks);
        var effort = finished
            .Where(entry => entry.CompletedOn >= from && entry.CompletedOn <= today && entry.Effort > 0)
            .Sum(entry => (decimal)entry.Effort);

        if (effort <= 0) return null;

        return Math.Round(effort / weeks, 4, MidpointRounding.AwayFromZero);
    }

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
        if (repository is null) return Paces(settings.Manual(), settings.Source(), finished, today);

        IReadOnlyList<CompletedEffortDto> own =
        [
            .. finished.Where(entry => entry.RepositoryAliases.Contains(repository, StringComparer.OrdinalIgnoreCase))
        ];

        return Paces(settings.Manual(), settings.Source(repository), own, today, settings.Manual(repository));
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
