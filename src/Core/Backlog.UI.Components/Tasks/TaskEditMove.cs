namespace Backlog.UI.Components.Tasks;

/// <summary>
/// A row has finished being edited and is asking which one is next. The row it
/// happened in and which way the reader was going, and nothing else: which row
/// that comes out as is the list's, because a row cannot see the rows around it
/// — the same division <see cref="TaskRename"/> makes about saving.
/// </summary>
/// <param name="Id">The row the edit was in when the key was pressed. Sent even
/// though the list usually knows, because it is the one fact that makes the
/// answer computable from a single event rather than from an event plus whatever
/// the list last remembered.</param>
/// <param name="Forward">Down the list, which is Tab, or back up it, which is
/// Shift+Tab. Not "next id", because next of what is exactly the question being
/// asked.</param>
public sealed record TaskEditMove(string Id, bool Forward);
