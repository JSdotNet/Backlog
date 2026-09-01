namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Which checkout this window was started from, when the host knows.
/// <para>
/// Several worktrees of the repository are routinely run side by side, and the
/// shell looks the same in all of them. A host that runs out of a checkout —
/// the web harnesses always, the desktop head in a Debug build — registers this
/// so the header can say which one you are looking at, in the place the version
/// occupies when it does not.
/// </para>
/// <para>
/// It is a registration rather than a lookup on purpose: deriving it means
/// reading the folder chain and a git file, which is the host's business, not
/// the shell's. Nothing registers it in an installed build, and the header then
/// shows the version as before.
/// </para>
/// </summary>
/// <param name="Marker">
/// The worktree folder, with its branch beside it when the branch does not
/// already say the same thing.
/// </param>
public sealed record DevelopmentWorkspaceLabel(string Marker);
