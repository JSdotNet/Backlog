using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// Answers the Sessions module's <see cref="ISessionRepositoryResolver"/> from the
/// repositories already configured in Settings — the same store the Repositories
/// screen writes and the Tasks module's repository directory reads.
/// <para>
/// Here rather than in the Sessions module, because the module may not reference
/// an infrastructure project (<c>ModuleBoundaryTests</c>): it declares the port
/// and the adapter conforms to it, the same direction the transcript facts cache
/// runs in. The host composes this beside that cache.
/// </para>
/// <para>
/// Read on every call rather than snapshotted, for the reason the Tasks adapter
/// gives: somebody can register a clone in Settings while the pane is open, and
/// the next reading should place the sessions under it.
/// </para>
/// <para>
/// Only a repository with a clone directory is a candidate. The clone directory is
/// machine-local by design — <see cref="GitHubRepositoryRef.CloneDirectory"/> says
/// why — so this adapter never places a folder in a repository this machine has
/// not been told the clone of, and a workspace shared with someone who has cloned
/// nothing resolves nothing.
/// </para>
/// </summary>
public sealed class SettingsSessionRepositoryResolver(GitHubSettingsStore settings) : ISessionRepositoryResolver
{
    public string? RepositoryOf(string workingFolder)
    {
        if (string.IsNullOrWhiteSpace(workingFolder)) return null;

        return RegisteredClones.RepositoryContaining(workingFolder, Clones(settings.Current));
    }

    private static IEnumerable<RegisteredClone> Clones(GitHubSettings current) =>
        current.Repositories
            .Where(repository => !string.IsNullOrWhiteSpace(repository.CloneDirectory))
            .Select(repository => new RegisteredClone(repository.FullName, repository.CloneDirectory!));
}
