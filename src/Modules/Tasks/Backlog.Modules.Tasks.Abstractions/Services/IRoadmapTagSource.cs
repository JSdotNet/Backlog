namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// The roadmap item tags a backlog entry may be filed under, offered to the tag
/// picker alongside the tags the backlog already uses.
/// <para>
/// A port on Tasks' own surface rather than a reference to Roadmap
/// Planning: a screen renders one context and asks that context's module, so the
/// backlog UI depends on this and an infrastructure adapter that can see both
/// contexts answers it from the plan
/// (<c>ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface</c>).
/// The same shape <see cref="ITaskStore"/> takes for the same reason.
/// </para>
/// <para>
/// The values are the plan's slugs wearing the plan sigil — <c>+release-q4</c> for
/// a roadmap item tagged <c>release-q4</c> — because that is the stored form of a
/// backlog tag that names a roadmap item, and the picker has to offer the value an
/// entry will keep. A backlog entry and a roadmap item agree on a tag by its text
/// under that sigil; this side neither derives nor validates one, it only offers
/// what the plan already carries so a person can file an entry against planned
/// work before anything else has.
/// </para>
/// </summary>
public interface IRoadmapTagSource
{
    /// <summary>The distinct roadmap item tags in use across the plan, each as
    /// <c>+slug</c>, in the order they first appear. Empty when there is no plan
    /// or nothing is tagged.</summary>
    Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default);
}
