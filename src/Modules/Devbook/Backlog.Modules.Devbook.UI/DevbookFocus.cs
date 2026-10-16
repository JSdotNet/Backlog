namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The heading a followed reference opened a chapter at, and which request asked for
/// it. On a scenario page the anchor names the part to mark as current and scroll to.
/// </summary>
/// <param name="Anchor">The heading slug the reference named.</param>
/// <param name="Sequence">Which request it was, so the same part asked for again is a
/// new focus and is scrolled to again.</param>
public sealed record DevbookFocus(string Anchor, int Sequence);
