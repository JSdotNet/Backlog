namespace Backlog.SharedKernel.Handlers;

/// <summary>
/// One use case that changes something, and the result of having done it.
/// <para>
/// Per ADR 0006 these interfaces live once in the shared kernel rather than
/// being redeclared per module, and there is deliberately no mediator behind
/// them: a host resolves the handler it wants and calls it. The indirection a
/// dispatcher would add buys nothing when the caller already knows which use
/// case it means.
/// </para>
/// </summary>
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>A use case that changes something and has nothing to report back
/// beyond having succeeded.</summary>
public interface ICommandHandler<in TCommand>
{
    Task Handle(TCommand command, CancellationToken cancellationToken = default);
}
