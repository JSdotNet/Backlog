using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Services;

/// <summary>
/// The load–change–save every command over one series shares: under the
/// <see cref="ProgressGate"/>, load the series, apply the change, and save only when it
/// succeeded — a refused change leaves the stored series as it was.
/// </summary>
public sealed class SeriesChanges(ISprintSeriesRepository repository, ProgressGate gate)
{
    public async Task<Result<SprintSeriesDto>> ChangeAsync(
        Guid seriesId,
        Func<SprintSeries, Result> change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        using var held = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        var series = await repository.GetAsync(seriesId, cancellationToken).ConfigureAwait(false);
        if (series is null) return ProgressErrors.SeriesNotFound(seriesId);

        var changed = change(series);
        if (changed.IsFailure) return changed.Error;

        await repository.SaveAsync(series, cancellationToken).ConfigureAwait(false);
        return series.ToDto();
    }

    /// <summary>The same, for a change that has to see every other series too.</summary>
    public async Task<Result<SprintSeriesDto>> ChangeAcrossAsync(
        Guid seriesId,
        Func<IReadOnlyList<SprintSeries>, SprintSeries, Result> change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        using var held = await gate.EnterAsync(cancellationToken).ConfigureAwait(false);

        var all = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var series = all.FirstOrDefault(candidate => candidate.Id == seriesId);
        if (series is null) return ProgressErrors.SeriesNotFound(seriesId);

        var changed = change(all, series);
        if (changed.IsFailure) return changed.Error;

        await repository.SaveAsync(series, cancellationToken).ConfigureAwait(false);
        return series.ToDto();
    }
}
