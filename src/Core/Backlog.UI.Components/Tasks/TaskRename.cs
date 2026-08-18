namespace Backlog.UI.Components.Tasks;

/// <summary>
/// A row was given a new title. The id and the text, and nothing else: whether
/// the rename is saved, and where, is the host's — the same bargain
/// <see cref="TaskMove"/> makes about order.
/// </summary>
/// <param name="Id">The row that was renamed.</param>
/// <param name="Title">What it is called now, trimmed. Never empty and never the
/// title it already had: neither is a rename, and raising one would put a no-op
/// into whatever the host does with these.</param>
public sealed record TaskRename(string Id, string Title);
