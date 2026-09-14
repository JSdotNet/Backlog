namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// What somebody typed into the Inbox's Add dialog: a title, and whatever they
/// had to say about it.
/// <para>
/// The Inbox's own words for a manual capture. The pane hands it to
/// <see cref="InboxDesktopState.CaptureAsync"/>, which files it through the
/// module's port as an item in the unfiled inbox with the notes as its body;
/// nothing outside the Inbox ever sees it.
/// </para>
/// </summary>
/// <param name="Title">Trimmed, never blank: the dialog does not submit
/// without one.</param>
/// <param name="Notes">Trimmed; empty when nothing was written.</param>
public sealed record InboxCapture(string Title, string Notes);
