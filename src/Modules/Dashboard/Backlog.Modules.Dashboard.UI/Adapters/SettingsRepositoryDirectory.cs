using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Dashboard.Abstractions.Services;

namespace Backlog.Modules.Dashboard.UI.Adapters;

/// <summary>
/// Answers <see cref="IRepositoryDirectory"/> from the repositories already
/// configured in Settings.
/// </summary>
/// <remarks>
/// The dashboard does not own the repository list and must not become a second
/// place it is configured, so it asks for one. Reading the store on every access
/// rather than caching a snapshot is deliberate: somebody can add a repository in
/// Settings while the dashboard is open, and the filter should offer it the next
/// time it renders.
/// </remarks>
internal sealed class SettingsRepositoryDirectory(GitHubSettingsStore settings) : IRepositoryDirectory
{
    public IReadOnlyList<DashboardRepository> Repositories =>
        [.. settings.Current.Repositories.Select(repository =>
            new DashboardRepository(repository.Alias, repository.FullName))];
}
