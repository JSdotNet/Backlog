namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Which surface the shell is showing below its chrome.
/// <para>
/// One field with four states, rather than a flag per takeover, and that is the
/// whole point: a takeover cannot coexist with the workspace, and the takeovers
/// cannot coexist with each other. Each belongs to one context — Tools to Dev PC
/// Management, Dashboard to the Dashboard, Sessions to Sessions — and opening
/// any of them means the reader has stopped looking at the backlog, so there is no
/// arrangement in which one shares the screen with the panes or with another. Three
/// booleans would have to be kept out of five impossible states by hand; this cannot
/// reach any of them.
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
    /// <summary>The three side-by-side panes, with the roadmap band above them.</summary>
    Workspace,

    /// <summary>Dev PC Management's configuration, taking the whole screen.</summary>
    Tools,

    /// <summary>The Dashboard context, taking the whole screen.</summary>
    Dashboard,

    /// <summary>The Sessions context, taking the whole screen.
    /// <para>
    /// It sat beside <see cref="Tools"/> as a second Dev PC Management surface while
    /// it was first built, which read plausibly — both are about the machine — and
    /// was wrong about the boundary: what has been running is its own subject with
    /// its own language, not a view of how a PC is configured. It is its own bounded
    /// context now, and the only thing that changed here is which context the
    /// takeover belongs to.
    /// </para></summary>
    Sessions
}
