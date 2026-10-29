namespace Backlog.Desktop.UI.Tasks;

/// <summary>The Board as a shell surface: the Tasks view's filtered rows in columns of
/// cards, beside the same detail panel (local ADR 0041 §1).</summary>
public sealed class BoardSurface : TasksSurface
{
    protected override TasksLayout Layout => TasksLayout.Board;
}
