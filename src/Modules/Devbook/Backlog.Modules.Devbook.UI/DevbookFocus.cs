namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The heading a followed reference opened a chapter at, and which request asked for
/// it. On a scenario page the anchor names the part to mark as current and scroll to;
/// in a document folder's file view, the heading to scroll to and move focus to.
/// </summary>
/// <param name="Anchor">The heading slug the reference named.</param>
/// <param name="Sequence">Which request it was, so the same part asked for again is a
/// new focus and is scrolled to again.</param>
public sealed record DevbookFocus(string Anchor, int Sequence)
{
    /// <summary>The file the anchor is a heading of, relative to the area's folder,
    /// when that is not the selected file: a Domain file with no row of its own —
    /// <c>tasks/domain.invariants.md</c> — opens on the page that shows it, and the
    /// anchor still names a heading of the file the reference named. Null when the
    /// anchor is the selected file's own.</summary>
    public string? File { get; init; }
}
