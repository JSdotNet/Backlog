namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The main view the workspace shows, picked by the header's view switch.
/// <para>
/// Exactly one is always on screen while the workspace is: a view is never closed,
/// only replaced by another. That is the difference from a <see cref="GlobalPane"/>,
/// which opens beside whichever view is showing and may be closed, and from a
/// <see cref="WorkspaceSurface"/> takeover, which hides the workspace altogether and
/// which Escape closes. The Tasks pane used to be one of the panes and the roadmap
/// one of the takeovers; both are views now, so the Inbox and the Devbook open
/// beside the roadmap as they do beside the task list.
/// </para>
/// <para>
/// The shell stores the member's name as <c>lastView</c> in
/// <c>shell-navigation.json</c>, so a name here is a stored value. Board, Calendar
/// and In progress join as members of their own, each one option in the switch.
/// </para>
/// </summary>
internal enum ShellView
{
    /// <summary>The task list and its details — the view the shell opens on when
    /// nothing else was remembered.</summary>
    Tasks,

    /// <summary>The Roadmap context's plans on a graduated axis, under its own
    /// Planning heading row. Offered while <c>RoadmapFeatures.Roadmap</c> is on.</summary>
    Roadmap
}
