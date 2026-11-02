using Backlog.Modules.RepositoryManagement.Abstractions.Services;

namespace Backlog.Infrastructure.GitHub.RepositoryManagement;

/// <summary>
/// The repository registry as Repository Management reads pull requests by it (local
/// ADR 0029, ADR 0042), answered from <see cref="GitHubSettingsStore"/> as it stands at
/// each ask, so a repository archived in Settings is not read from the next ask on.
/// </summary>
public sealed class GitHubRegisteredRepositories(GitHubSettingsStore settings) : IRegisteredRepositories
{
    public bool IsConfigured => settings.Current.Repositories.Count > 0;

    public IReadOnlyList<string> Active => [.. settings.Current.ActiveRepositories.Select(repository => repository.FullName)];

    public bool IsArchived(string repositoryFullName) =>
        !string.IsNullOrWhiteSpace(repositoryFullName)
        && settings.Current.Find(repositoryFullName.Trim()) is { Archived: true };
}
