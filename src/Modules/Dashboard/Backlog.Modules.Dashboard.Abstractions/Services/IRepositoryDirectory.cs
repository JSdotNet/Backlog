namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>One repository the filter can offer.</summary>
/// <param name="Alias">The short name a person recognises, and what
/// <see cref="DashboardScope.RepositoryAlias"/> holds.</param>
/// <param name="FullName">The <c>owner/name</c> form, which is what a provider
/// call needs.</param>
public sealed record DashboardRepository(string Alias, string FullName);

/// <summary>
/// PORT — which repositories exist.
/// <para>
/// The dashboard does not own the repository list and must not become a second
/// place it is configured, so it asks. The adapter over Settings answers; a test
/// answers with two fixed rows.
/// </para>
/// </summary>
public interface IRepositoryDirectory
{
    IReadOnlyList<DashboardRepository> Repositories { get; }
}
