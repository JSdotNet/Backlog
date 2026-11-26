namespace Backlog.SharedKernel.Domain;

/// <summary>
/// Marks a domain event: a type a context's <c>domain.md</c> names in a chapter typed
/// <c>domain-event</c>. It carries no members and changes no behaviour; it lets the code model
/// read which type is which without guessing from names.
/// </summary>
public interface IDomainEvent;
