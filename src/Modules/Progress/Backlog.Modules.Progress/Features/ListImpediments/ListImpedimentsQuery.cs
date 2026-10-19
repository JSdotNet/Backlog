using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.ListImpediments;

/// <summary>The open impediments, with those resolved from <paramref name="ResolvedFrom"/>
/// to <paramref name="ResolvedTo"/> when both are given.</summary>
public sealed record ListImpedimentsQuery(DateOnly? ResolvedFrom = null, DateOnly? ResolvedTo = null);

/// <summary>One impediment by its id.</summary>
public sealed record GetImpedimentQuery(Guid ImpedimentId);

/// <summary>Reads the impediments. What the stand-up shows of them — the open ones, and
/// those resolved in the last window — is the list's filter.</summary>
public sealed class ListImpedimentsQueryHandler(IImpedimentRepository repository)
    : IQueryHandler<ListImpedimentsQuery, IReadOnlyList<ImpedimentDto>>,
      IQueryHandler<GetImpedimentQuery, Result<ImpedimentDto>>
{
    public async Task<IReadOnlyList<ImpedimentDto>> Handle(ListImpedimentsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var all = await repository.ListAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. all
                .Where(impediment => impediment.IsOpen
                    || (query.ResolvedFrom is { } from
                        && query.ResolvedTo is { } to
                        && impediment.ResolvedOn is { } resolved
                        && resolved >= from
                        && resolved <= to))
                .Select(impediment => impediment.ToDto())
        ];
    }

    public async Task<Result<ImpedimentDto>> Handle(GetImpedimentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await repository.GetAsync(query.ImpedimentId, cancellationToken).ConfigureAwait(false) is { } impediment
            ? impediment.ToDto()
            : ProgressErrors.ImpedimentNotFound(query.ImpedimentId);
    }
}
