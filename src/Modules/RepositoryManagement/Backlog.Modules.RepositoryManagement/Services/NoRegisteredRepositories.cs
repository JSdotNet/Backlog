using Backlog.Modules.RepositoryManagement.Abstractions.Services;

namespace Backlog.Modules.RepositoryManagement.Services;

/// <summary>The registry of a host that composes none: nothing registered, so the
/// contract is not configured.</summary>
internal sealed class NoRegisteredRepositories : IRegisteredRepositories
{
    public bool IsConfigured => false;

    public IReadOnlyList<string> Active => [];

    public bool IsArchived(string repositoryFullName) => false;
}
