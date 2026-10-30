namespace Backlog.Modules.RepositoryManagement.Abstractions.Services;

/// <summary>
/// The repository registry as Repository Management reads pull requests by it: which
/// repositories are registered, which of them are read, and which are archived and so
/// never read (local ADR 0029, local ADR 0042). Answered by an adapter over the
/// registry's own store.
/// </summary>
public interface IRegisteredRepositories
{
    /// <summary>At least one repository is registered.</summary>
    bool IsConfigured { get; }

    /// <summary>The registered repositories that are not archived, by <c>owner/name</c>,
    /// in their configured order.</summary>
    IReadOnlyList<string> Active { get; }

    /// <summary>Whether <paramref name="repositoryFullName"/> names a registered repository
    /// that is archived. A name nothing registered answers to is not archived: a link
    /// outlives the registration it was made under and is still read.</summary>
    bool IsArchived(string repositoryFullName);
}
