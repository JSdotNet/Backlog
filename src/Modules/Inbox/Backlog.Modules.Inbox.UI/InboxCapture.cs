namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// What somebody typed into the Inbox's Add dialog: a title, and whatever they
/// had to say about it.
/// <para>
/// The Inbox's own words for a manual capture, handed up through
/// <c>InboxPane.OnAdd</c>. What becomes of it — a backlog draft, today — is the
/// receiving context's business; the pane never learns.
/// </para>
/// </summary>
/// <param name="Title">Trimmed, never blank: the dialog does not submit
/// without one.</param>
/// <param name="Notes">Trimmed; empty when nothing was written.</param>
public sealed record InboxCapture(string Title, string Notes);
