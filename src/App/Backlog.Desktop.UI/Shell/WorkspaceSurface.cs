namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Which surface the shell is showing below its chrome.
/// <para>
/// One field rather than a flag per takeover, and that is the
/// whole point: a takeover cannot coexist with the workspace, and the takeovers
/// cannot coexist with each other. Each belongs to one context — Tools to Dev PC
/// Management, Dashboard to the Dashboard, Sessions and Pull requests to
/// Sessions, the Inbox to the Inbox — and opening any of them means the
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
/// Deliberately a field of its own beside the main view (<see cref="ShellView"/>).
/// Closing a surface simply reveals the view that was there all along — no save,
/// no restore, nothing to get wrong. The side panes that once sat beside the view
/// had a capacity rule the viewport set; each of them, the Inbox and then the
/// Devbook, became a member here instead, and none is left.
/// </para>
/// </summary>
internal enum WorkspaceSurface
{
    /// <summary>The main view the view switch picks, alone.</summary>
    Workspace,

    /// <summary>Dev PC Management's configuration, taking the whole screen.</summary>
    Tools,

    /// <summary>The Dashboard context, taking the whole screen.</summary>
    Dashboard,

    /// <summary>The roadmap's name while it was a takeover. It is a
    /// <see cref="ShellView"/> now, opened by the view switch, and nothing assigns
    /// this member: it stays only because the navigation file stores names, so a
    /// file from then still reads, and <c>ShellNavigationStore</c> migrates it to
    /// the Roadmap view over the workspace. Never written again.</summary>
    Roadmap,

    /// <summary>The Sessions context's list, taking the whole screen. Written after
    /// <see cref="Roadmap"/> because the navigation file stores names, and "Sessions"
    /// is the name every earlier build wrote for the list — as a surface and, while
    /// it was a Dashboard tab, for that tab — so each of those files reopens here.</summary>
    Sessions,

    /// <summary>The open pull requests of every registered repository, taking the
    /// whole screen — the Sessions context's second surface, what its sessions
    /// shipped. Appended, as <see cref="Roadmap"/> and <see cref="Sessions"/> were:
    /// the navigation file stores names, so every surface an earlier build wrote
    /// reopens unchanged, and a new name is simply one no earlier file holds. The
    /// header orders its segments for itself; this order is nobody's layout.</summary>
    PullRequests,

    /// <summary>The Inbox, taking the whole screen (local ADR 0022, amendment of
    /// 2026-10-12). It was a side pane beside the main view until
    /// triage wanted the width; <c>ShellNavigationStore</c> reads a file from then
    /// that had it open over the workspace as this surface. Appended for the reason
    /// <see cref="PullRequests"/> was. Unlike the other takeovers Escape does not
    /// close it: Escape belongs to the Inbox's own keys there, leaving triage for the
    /// list, so only its header option — or another surface's — leaves it.</summary>
    Inbox,

    /// <summary>The Devbook — a repository's devbook or the Brain — taking the whole
    /// screen (local ADR 0022, amendment of 2026-10-15). It was the last side pane,
    /// beside the main view, until a chapter wanted the width;
    /// <c>ShellNavigationStore</c> reads a file from then that had it open over the
    /// workspace, as <c>Devbook</c> or the older <c>Knowledge</c>, as this surface.
    /// Appended after <see cref="Inbox"/> for the reason <see cref="PullRequests"/>
    /// was. Escape closes it as it closes the other takeovers.</summary>
    Devbook,

    /// <summary>The notification centre — the owner's alerts and questions, grouped by
    /// subject, with the one picked drawn and answered beside the list — taking the whole
    /// screen. Appended after <see cref="Devbook"/> for the reason
    /// <see cref="PullRequests"/> was. Escape closes it as it closes the other takeovers.</summary>
    Notifications
}
