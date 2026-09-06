namespace Backlog.UI.Components.Tasks;

/// <summary>
/// A row was asked for its menu: which row, and where the pointer was when it
/// asked. The gesture and nothing else — a row cannot know what a host would
/// put in a menu, so it reports that one was asked for and the host draws it,
/// exactly as a <see cref="TaskMove"/> reports a drop and leaves the order to
/// whoever keeps it.
/// </summary>
/// <param name="Id">The row that was right-clicked.</param>
/// <param name="X">The pointer's horizontal position in the viewport, in CSS
/// pixels — what <c>ContextMenu</c> takes as its <c>X</c>.</param>
/// <param name="Y">The pointer's vertical position, on the same terms.</param>
public sealed record TaskContextMenu(string Id, double X, double Y);
