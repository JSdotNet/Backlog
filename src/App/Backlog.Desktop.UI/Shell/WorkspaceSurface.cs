namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Which surface the shell is showing below its chrome.
/// <para>
/// One field with four states, rather than a flag per takeover, and that is the
/// whole point: a takeover cannot coexist with the workspace, and the takeovers
/// cannot coexist with each other. Each belongs to one context — Roadmap to
/// Roadmap, Tools to Dev PC Management, Dashboard to the Dashboard — and opening
/// any of them means the
/// reader has stopped looking at the backlog, so there is no arrangement in which
/// one shares the screen with the panes or with another. Two booleans would have
/// to be kept out of the impossible states by hand; this cannot reach any of them.
/// </para>
/// <para>
/// Adding a member is the whole cost of adding a takeover, which is the point of
/// the field being an enum rather than a set of flags: the exclusivity comes for
/// free and nothing existing has to be revisited to keep it.
/// </para>
/// <para>
/// That second attribution used to read "Dashboard is Monitoring's", which is what
/// it was while the dashboard was a derived view inside Monitoring with no module
/// behind it. It is its own context now, with its own module, and the only thing
/// that changed here is which context the takeover belongs to.
/// </para>
/// <para>
/// Sessions was a third member for a while. It is a tab of the Dashboard surface
/// now — see <see cref="DashboardTab"/> — because what the reader wanted from it
/// was a second view of the same question the dashboard answers, and two
/// full-screen takeovers about how the assistants have been doing read as two
/// applications. The Sessions context did not move: its pane is still its own,
/// and the shell only changed where it makes room for it.
/// </para>
/// <para>
/// Deliberately not a fourth <see cref="GlobalPane"/>. The panes have a capacity
/// rule the viewport sets and an invariant that one of them is always on screen;
/// a takeover has neither, and folding it in would let the window width close a
/// takeover or a takeover evict the reader's pane selection. Because this is a
/// separate field, closing a surface simply reveals the selection that was there
/// all along — no save, no restore, nothing to get wrong.
/// </para>
/// </summary>
internal enum WorkspaceSurface
{
    /// <summary>The three side-by-side panes.</summary>
    Workspace,

    /// <summary>Dev PC Management's configuration, taking the whole screen.</summary>
    Tools,

    /// <summary>The Dashboard context, taking the whole screen — on one of its
    /// <see cref="DashboardTab"/>s.</summary>
    Dashboard,

    /// <summary>The Roadmap context's plan, taking the whole screen. It was a band
    /// above the panes until a plan read in a strip of the screen proved to be a plan
    /// paged through; written last so the stored names of the others are unchanged.</summary>
    Roadmap
}

/// <summary>
/// Which view the Dashboard surface is showing.
/// <para>
/// A second field beside <see cref="WorkspaceSurface"/> rather than two more
/// members of it. The surface answers "what owns the screen", and both of these
/// answer it the same way; what they differ on is which of the Dashboard's views
/// is in front, which is a question that only exists while the Dashboard is the
/// surface. Two surface members would have let a reader be "on Sessions" while
/// the Dashboard's own tab strip said otherwise.
/// </para>
/// </summary>
internal enum DashboardTab
{
    /// <summary>Productivity, the sessions summary and cost — the Dashboard module's own pane.</summary>
    Overview,

    /// <summary>The full session list — the Sessions module's pane.</summary>
    Sessions
}

/// <summary>
/// The names the shell writes to <c>shell-navigation.json</c> for what it is
/// showing, and how it reads them back.
/// <para>
/// A stored name is a value, not an identifier: a layout saved by an earlier
/// build has to reopen where the reader left it, or the app reads as having
/// forgotten. "Sessions" was a surface of its own before it became a tab of the
/// Dashboard, and it still names the sessions list — so it is still written when
/// the reader is on that tab and still read back to it, which is what keeps the
/// file compatible in both directions without a field for the tab. The other
/// names are the enum members as they always were.
/// </para>
/// </summary>
internal static class WorkspaceSurfaceNames
{
    private const string SessionsName = "Sessions";

    public static string Name(WorkspaceSurface surface, DashboardTab tab) =>
        surface == WorkspaceSurface.Dashboard && tab == DashboardTab.Sessions
            ? SessionsName
            : surface.ToString();

    /// <summary>Reads a persisted name. False for an absent or unrecognised one —
    /// an older or newer build's surface, or a hand-edited file — so the caller's
    /// defaults stand and the app opens regardless.</summary>
    public static bool TryParse(string? name, out WorkspaceSurface surface, out DashboardTab tab)
    {
        if (string.Equals(name, SessionsName, StringComparison.Ordinal))
        {
            surface = WorkspaceSurface.Dashboard;
            tab = DashboardTab.Sessions;
            return true;
        }

        tab = DashboardTab.Overview;
        return Enum.TryParse(name, out surface) && Enum.IsDefined(surface);
    }
}
