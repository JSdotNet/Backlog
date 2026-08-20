namespace Backlog.Modules.Backlog.Abstractions.Services;

/// <summary>
/// The roadmap item tags a backlog entry may be filed under, offered to the tag
/// picker alongside the tags the backlog already uses.
/// <para>
/// A port on Backlog Management's own surface rather than a reference to Roadmap
/// Planning: a screen renders one context and asks that context's module, so the
/// backlog UI depends on this and an infrastructure adapter that can see both
/// contexts answers it from the plan
/// (<c>ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface</c>).
/// The same shape <see cref="IBacklogStore"/> takes for the same reason.
/// </para>
/// <para>
/// The values are opaque slugs. A backlog entry and a roadmap item agree on a tag
/// by its text; this side neither derives nor validates one, it only offers what
/// the plan already carries so a person can file an entry against planned work
/// before anything else has.
/// </para>
/// </summary>
public interface IRoadmapTagSource
{
    /// <summary>The distinct roadmap item tags in use across the plan, in the
    /// order they first appear. Empty when there is no plan or nothing is
    /// tagged.</summary>
    Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default);
}
