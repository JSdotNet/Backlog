using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// Carries the inbox items filed against a renamed repository across to its new
/// id, for the Repositories settings page's Rename.
/// <para>
/// A port on Tasks' surface answered by an adapter over the Inbox's own port, the
/// shape <see cref="ILinkedSessionStates"/> takes for the same reason: the page is
/// the Tasks module's, and a module's screens may not reference another context's
/// published surface
/// (<c>ModuleBoundaryTests.A_module_ui_asks_only_its_own_modules_published_surface</c>).
/// The entries are the module's own and are carried through
/// <see cref="ITaskItems.RenameRepositoryAsync"/>. A host that registers no adapter
/// — the storybook, a unit test of another page — has no inbox items to carry.
/// </para>
/// </summary>
public interface IInboxRepositoryRename
{
    /// <summary>Re-points every inbox item that names <paramref name="oldId"/> at
    /// <paramref name="newId"/>, and answers how many moved — or the Inbox's
    /// refusal, unchanged.</summary>
    Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default);
}
