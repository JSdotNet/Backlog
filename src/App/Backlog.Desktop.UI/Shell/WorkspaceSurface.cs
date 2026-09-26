namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Which surface the shell is showing below its chrome.
/// <para>
/// One field with five states, rather than a flag per takeover, and that is the
/// whole point: a takeover cannot coexist with the workspace, and the takeovers
/// cannot coexist with each other. Each belongs to one context — Roadmap to
/// Roadmap, Tools to Dev PC Management, Dashboard to the Dashboard, Sessions to
/// Sessions — and opening
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
/// Sessions was a member, then for a while a tab of the Dashboard surface, and is a
/// member again. As a tab it put the one screen a person opens to see what the
/// assistants are running one click deeper than the summary about them, and the
/// Dashboard's own Sessions section already answers the summary question — so the
/// list went back to a segment of its own in the header.
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

    /// <summary>The Dashboard context, taking the whole screen.</summary>
    Dashboard,

    /// <summary>The Roadmap context's plan, taking the whole screen. It was a band
    /// above the panes until a plan read in a strip of the screen proved to be a plan
    /// paged through; written after the others so their stored names are unchanged.</summary>
    Roadmap,

    /// <summary>The Sessions context's list, taking the whole screen. Written after
    /// <see cref="Roadmap"/> because the navigation file stores names, and "Sessions"
    /// is the name every earlier build wrote for the list — as a surface and, while
    /// it was a Dashboard tab, for that tab — so each of those files reopens here.</summary>
    Sessions
}
