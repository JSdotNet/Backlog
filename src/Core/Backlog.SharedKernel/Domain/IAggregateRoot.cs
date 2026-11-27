namespace Backlog.SharedKernel.Domain;

/// <summary>
/// Marks the root of an aggregate: a type a context's <c>domain.md</c> names in a chapter typed
/// <c>aggregate</c>. It carries no members and changes no behaviour; it lets the code model
/// read which type is which without guessing from names.
/// </summary>
public interface IAggregateRoot;
