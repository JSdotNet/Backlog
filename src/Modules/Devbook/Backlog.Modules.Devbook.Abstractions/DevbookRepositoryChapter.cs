namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// A chapter in a configured repository's devbook, named from outside it: the
/// one-way cross-corpus reference Devbook publishes (local ADR 0027 §6, local ADR
/// 0040 §4).
/// </summary>
/// <param name="Alias">The repository alias, as written.</param>
/// <param name="Reference">The repository-relative devbook path, with an optional
/// <c>#slug</c>.</param>
public sealed record DevbookRepositoryChapter(string Alias, string Reference);
