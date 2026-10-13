using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>One configured repository's instruction documents, as the
/// instructions screens show them: the listing
/// <see cref="InstructionSourceDiscovery"/> read, with the repository it was read
/// for.</summary>
/// <param name="CanEdit">Whether these documents may be written to. False for a
/// branch snapshot, which is a copy of somebody's commit — an edit to it would
/// survive exactly until the next fetch. Defaults to true so that every caller
/// predating branch loading keeps offering the edit it always offered.</param>
public sealed record InstructionRepositoryView(
    GitHubRepositoryRef Repository,
    string? LocalRoot,
    IReadOnlyList<InstructionDocument> Documents,
    string? Message,
    bool CanEdit = true);

/// <summary>
/// Discovery over the configured repositories. The reading is the adapter's
/// (issue #738); the adapter does not know the GitHub settings' repository record,
/// so this maps each repository in and its listing back out.
/// </summary>
public static class InstructionRepositoryDiscovery
{
    /// <summary>
    /// Reads each repository's instruction documents.
    /// <para>
    /// <paramref name="folders"/> is how a repository read from a branch finds
    /// its root. Instructions are the one area whose folder <em>is</em> the
    /// repository root — the setting carries an empty relative path — so the
    /// devbook-folder port already answers exactly the question this needed to
    /// ask, and asking it is what stops this from being a second, independent
    /// clone-directory gate that branch loading silently walks past. Omitting it
    /// falls back to the clone, which is what every caller did before branch
    /// loading existed.
    /// </para>
    /// </summary>
    public static IReadOnlyList<InstructionRepositoryView> Discover(
        this InstructionSourceDiscovery discovery,
        IEnumerable<GitHubRepositoryRef> repositories,
        IDevbookFolderSource? folders = null)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(repositories);

        return
        [
            .. repositories.Select(repository =>
            {
                var listing = discovery.Read(
                    new InstructionRepository(repository.Alias, repository.CloneDirectory, repository.DevbookFolders),
                    folders);

                return new InstructionRepositoryView(
                    repository,
                    listing.LocalRoot,
                    listing.Documents,
                    listing.Message,
                    listing.CanEdit);
            })
        ];
    }
}
