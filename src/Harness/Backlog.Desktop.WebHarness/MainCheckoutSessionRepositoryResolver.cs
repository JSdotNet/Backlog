using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Desktop.WebHarness;

/// <summary>
/// The harness's session placement: the registered clones first, then the main
/// checkout of the worktree the harness runs from.
/// <para>
/// The harness registers its own checkout as the repository's clone directory,
/// because the Devbook panels read from that folder and QA must read the worktree
/// under test. When that checkout is a linked worktree, though, it is the only
/// folder the registered clones cover, and every other session this machine ran
/// in the repository — the main checkout, sibling worktrees, and worktrees long
/// since removed — would be placed nowhere. The main checkout contains all of
/// them lexically, so asking it second places them without the Devbook panels
/// moving and without reading anything off a path leaf.
/// </para>
/// </summary>
internal sealed class MainCheckoutSessionRepositoryResolver(
    ISessionRepositoryResolver registered,
    RegisteredClone mainCheckout) : ISessionRepositoryResolver
{
    public string? RepositoryOf(string workingFolder) =>
        registered.RepositoryOf(workingFolder)
            ?? RegisteredClones.RepositoryContaining(workingFolder, [mainCheckout]);
}
