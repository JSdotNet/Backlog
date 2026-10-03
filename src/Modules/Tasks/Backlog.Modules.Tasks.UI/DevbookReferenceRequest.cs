namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// A reader asking, from an entry, to read one of the Devbook pages or chapters it
/// points at.
/// <para>
/// The pane raises it and the shell answers it, because the entry belongs to Tasks
/// and the chapter to Devbook, and this project may reference neither the Devbook
/// screens nor their published surface — the same split
/// <c>TasksPane.OnOpenSession</c> makes for a session. Two strings, so the shell can
/// hand them on without either side learning the other's types.
/// </para>
/// </summary>
/// <param name="RepositoryAlias">The repository whose devbook holds the reference:
/// the entry's own, else the one the list is scoped to; null when there is
/// neither.</param>
/// <param name="Reference">The reference as the entry stores it — <c>path</c> or
/// <c>path#anchor</c>.</param>
public sealed record DevbookReferenceRequest(string? RepositoryAlias, string Reference);
