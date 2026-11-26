namespace Backlog.SharedKernel.Domain;

/// <summary>
/// Marks an entity an aggregate owns: a type a context's <c>domain.md</c> names in a chapter
/// typed <c>entity</c>. It carries no members and changes no behaviour; it lets the code model
/// read which type is which without guessing from names.
/// </summary>
public interface IEntity;
