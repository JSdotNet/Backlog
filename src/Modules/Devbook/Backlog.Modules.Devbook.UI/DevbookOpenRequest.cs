namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Something outside the Devbook pane asking it to open one page or chapter — a
/// task's reference, pressed in the Tasks pane and handed on by the shell.
/// <para>
/// A parameter rather than a method on the pane, because the shell renders the pane
/// and a parameter is how a parent says anything to a child. That makes a request a
/// value the pane sees on every render, so it carries a <paramref name="Sequence"/>:
/// the pane follows each sequence once, which is what lets the same reference
/// pressed twice — after the reader has moved on to another chapter — open it again,
/// and what keeps an ordinary re-render from dragging the reader back.
/// </para>
/// </summary>
/// <param name="Reference">The reference as authored: <c>path</c> or
/// <c>path#anchor</c>, repository-relative.</param>
/// <param name="Sequence">Which request this is. A new one is a new number.</param>
public sealed record DevbookOpenRequest(string Reference, long Sequence);
