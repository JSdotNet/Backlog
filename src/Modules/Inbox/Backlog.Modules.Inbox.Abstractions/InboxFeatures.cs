namespace Backlog.Modules.Inbox.Abstractions;

/// <summary>
/// The feature keys the Inbox context owns.
/// <para>
/// The pane key sat on the Shell's own catalog while the Shell was its only
/// reader. The Inbox's settings section reads it too now, and that section lives
/// in the Inbox's <c>.UI</c> project, which may not read the Shell — so the key
/// moved here, where both can see it, the same journey the dashboard and roadmap
/// keys made. The string did not change, so nobody's <c>features.json</c> forgot
/// what they had switched on; <c>AppFeatures.InboxPane</c> still names it for the
/// Shell's catalog and its readers.
/// </para>
/// </summary>
public static class InboxFeatures
{
    /// <summary>Show the Inbox option and pane in the Home shell, and the Inbox's
    /// page on the settings screen with it.</summary>
    public const string Pane = "inbox-pane";
}
