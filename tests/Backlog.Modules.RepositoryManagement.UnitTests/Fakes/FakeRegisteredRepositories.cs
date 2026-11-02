using Backlog.Modules.RepositoryManagement.Abstractions.Services;

namespace Backlog.Modules.RepositoryManagement.UnitTests.Fakes;

/// <summary>The registry as a test sets it: the registered repositories, by
/// <c>owner/name</c>, and which of them are archived.</summary>
internal sealed class FakeRegisteredRepositories(params string[] repositories) : IRegisteredRepositories
{
    private readonly List<string> _repositories = [.. repositories];
    private readonly HashSet<string> _archived = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConfigured => _repositories.Count > 0;

    public IReadOnlyList<string> Active => [.. _repositories.Where(repository => !_archived.Contains(repository))];

    public bool IsArchived(string repositoryFullName) =>
        _archived.Contains(repositoryFullName.Trim())
        && _repositories.Contains(repositoryFullName.Trim(), StringComparer.OrdinalIgnoreCase);

    public FakeRegisteredRepositories Archive(string repository)
    {
        _archived.Add(repository);
        return this;
    }
}
