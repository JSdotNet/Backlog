using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// Gives a repository to a series, and holds the one rule that spans series: a
/// repository belongs to at most one (<c>progress.repository_in_other_series</c>,
/// <c>.devbook/domain/progress/domain.invariants.md#series-assignment</c>). A service
/// because no single series can see the others.
/// </summary>
public static class SeriesAssignment
{
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

    /// <summary>The series <paramref name="alias"/> belongs to, or null.</summary>
    public static SprintSeries? HolderOf(IEnumerable<SprintSeries> allSeries, string? alias) =>
        string.IsNullOrWhiteSpace(alias) ? null : allSeries.FirstOrDefault(series => series.HasRepository(alias));
}
