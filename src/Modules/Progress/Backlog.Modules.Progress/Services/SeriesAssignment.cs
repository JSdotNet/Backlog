using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// Gives a repository to a series, and holds the one rule that spans series: a
/// repository belongs to at most one (<c>progress.repository_in_other_series</c>,
/// <c>.devbook/domain/progress/domain.invariants.md#series-assignment</c>). A service
/// because no single series can see the others.
/// <para>
/// A person can break the rule only on two desktops at once, offline, which leaves the
/// repository in both series once both have synced. <see cref="HolderOf"/> then answers
/// by <see cref="ClaimOrder"/>, and <see cref="ResolveClaims"/> switches every other
/// switched-on series off. Both read the series alone, so every desktop decides alike.
/// </para>
/// </summary>
public static class SeriesAssignment
{
    /// <summary>Which of two series that both have a repository keeps it: a switched-on
    /// series before a switched-off one, then the series set up first — a series stored
    /// before the moment was kept counts as first — then the lower id.</summary>
    public static IComparer<SprintSeries> ClaimOrder { get; } = Comparer<SprintSeries>.Create((left, right) =>
    {
        var on = right.IsOn.CompareTo(left.IsOn);
        if (on != 0) return on;

        var created = (left.CreatedAt ?? DateTimeOffset.MinValue).CompareTo(right.CreatedAt ?? DateTimeOffset.MinValue);
        return created != 0 ? created : left.Id.CompareTo(right.Id);
    });

    /// <summary>Gives <paramref name="alias"/> to <paramref name="target"/>, refused when
    /// another of <paramref name="allSeries"/> has it.</summary>
    public static Result Assign(IEnumerable<SprintSeries> allSeries, SprintSeries target, string? alias)
    {
        ArgumentNullException.ThrowIfNull(allSeries);
        ArgumentNullException.ThrowIfNull(target);

        var trimmed = alias?.Trim() ?? string.Empty;
        var holder = HolderOf(allSeries.Where(series => series.Id != target.Id), trimmed);

        return holder is not null
            ? Result.Failure(ProgressErrors.RepositoryInOtherSeries(trimmed))
            : target.AddRepository(trimmed);
    }

    /// <summary>Whether a new series over <paramref name="repositories"/> may be created:
    /// none of them may already belong to one of <paramref name="allSeries"/>.</summary>
    public static Result CanCreate(IEnumerable<SprintSeries> allSeries, IEnumerable<string> repositories)
    {
        var existing = allSeries.ToList();

        foreach (var alias in repositories)
        {
            if (HolderOf(existing, alias) is not null)
            {
                return Result.Failure(ProgressErrors.RepositoryInOtherSeries(alias.Trim()));
            }
        }

        return Result.Success();
    }

    /// <summary>Switches <paramref name="target"/> on or off. On is refused while another
    /// switched-on series of <paramref name="allSeries"/> has one of its repositories.</summary>
    public static Result SwitchOn(IEnumerable<SprintSeries> allSeries, SprintSeries target, bool on)
    {
        ArgumentNullException.ThrowIfNull(allSeries);
        ArgumentNullException.ThrowIfNull(target);

        if (on)
        {
            var others = allSeries.Where(series => series.Id != target.Id && series.IsOn).ToList();
            var taken = target.Repositories.FirstOrDefault(alias => others.Any(series => series.HasRepository(alias)));
            if (taken is not null) return Result.Failure(ProgressErrors.RepositoryInOtherSeries(taken));
        }

        target.Switch(on);
        return Result.Success();
    }

    /// <summary>
    /// Switches off every switched-on series that has a repository a series earlier in
    /// <see cref="ClaimOrder"/> already holds, each with an off reason naming the
    /// repository and the series that keeps it. Nothing else of theirs changes.
    /// </summary>
    /// <returns>The series it switched off, to be saved; empty when every repository is
    /// in one switched-on series.</returns>
    public static IReadOnlyList<SprintSeries> ResolveClaims(IEnumerable<SprintSeries> allSeries)
    {
        ArgumentNullException.ThrowIfNull(allSeries);

        var holders = new Dictionary<string, SprintSeries>(StringComparer.OrdinalIgnoreCase);
        var switchedOff = new List<SprintSeries>();

        foreach (var series in allSeries.Where(series => series.IsOn).Order(ClaimOrder))
        {
            var taken = series.Repositories.FirstOrDefault(holders.ContainsKey);
            if (taken is not null)
            {
                series.SwitchOff(OffReasonFor(taken, holders[taken]));
                switchedOff.Add(series);
                continue;
            }

            foreach (var alias in series.Repositories) holders[alias] = series;
        }

        return switchedOff;
    }

    /// <summary>The series <paramref name="alias"/> belongs to, or null. When syncing left
    /// it in more than one, the first in <see cref="ClaimOrder"/>.</summary>
    public static SprintSeries? HolderOf(IEnumerable<SprintSeries> allSeries, string? alias) =>
        string.IsNullOrWhiteSpace(alias)
            ? null
            : allSeries.Where(series => series.HasRepository(alias)).Order(ClaimOrder).FirstOrDefault();

    private static string OffReasonFor(string alias, SprintSeries keeper) =>
        $"Switched off: {alias} already works to \"{keeper.Name}\", which was set up first.";
}
