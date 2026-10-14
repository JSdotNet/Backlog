using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.GetSeries;

/// <summary>Every sprint series, in the order they were created.</summary>
public sealed record ListSeriesQuery;

/// <summary>One sprint series by its id.</summary>
public sealed record GetSeriesQuery(Guid SeriesId);

/// <summary>The series a repository belongs to, or none — none, too, while that
/// series is switched off.</summary>
public sealed record FindSeriesOfQuery(string RepositoryAlias);

public sealed class GetSeriesQueryHandler(ISprintSeriesRepository repository)
    : IQueryHandler<ListSeriesQuery, IReadOnlyList<SprintSeriesDto>>,
      IQueryHandler<GetSeriesQuery, Result<SprintSeriesDto>>,
      IQueryHandler<FindSeriesOfQuery, SprintSeriesDto?>
{
    public async Task<IReadOnlyList<SprintSeriesDto>> Handle(ListSeriesQuery query, CancellationToken cancellationToken = default)
    {
        var all = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        return [.. all.Select(series => series.ToDto())];
    }

    public async Task<Result<SprintSeriesDto>> Handle(GetSeriesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var series = await repository.GetAsync(query.SeriesId, cancellationToken).ConfigureAwait(false);
        return series is null ? ProgressErrors.SeriesNotFound(query.SeriesId) : series.ToDto();
    }

    public async Task<SprintSeriesDto?> Handle(FindSeriesOfQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var all = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        return SeriesAssignment.HolderOf(all, query.RepositoryAlias) is { IsOn: true } holder ? holder.ToDto() : null;
    }
}
