namespace Backlog.SharedKernel.Handlers;

/// <summary>
/// One question a caller can ask, and the answer. Queries never change state,
/// which is what makes it safe for a view to run one on every render.
/// </summary>
public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken = default);
}
