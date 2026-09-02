namespace Backlog.UI.Components.Tasks;

/// <summary>
/// A row's selection box having been pressed: which row, what it now reads as,
/// and whether the reader was asking for a range.
/// <para>
/// A record rather than three callbacks, on the terms <see cref="TaskRename"/>
/// and <see cref="TaskBodyChange"/> already set: the row raises one event and the
/// list turns it into a set. The id travels with it because a row cannot see the
/// rows around it, so only the list can work out what "everything between here
/// and the last one" means.
/// </para>
/// </summary>
/// <param name="Id">The row whose box was pressed.</param>
/// <param name="Selected">What the box now reads as — true for picked, false for
/// given back. The state rather than "it was toggled", so a list applying it to a
/// whole range writes one value instead of flipping each row it passes.</param>
/// <param name="Range">The reader held Shift. A request rather than an
/// instruction: the row knows the modifier was down and nothing at all about
/// which rows lie between this one and wherever the range starts.</param>
public readonly record struct TaskSelectionToggle(string Id, bool Selected, bool Range);
