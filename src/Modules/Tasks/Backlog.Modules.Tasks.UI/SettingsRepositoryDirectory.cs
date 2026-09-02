using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// Answers <see cref="IRepositoryDirectory"/> from the repositories already
/// configured in Settings.
/// </summary>
/// <remarks>
/// <para>
/// Tasks does not own the repository list and must not become a
/// second place it is configured, so it asks for one. Reading the store on every
/// access rather than caching a snapshot is deliberate: somebody can add a
/// repository in Settings while a plan is being pasted, and the import should
/// resolve against it.
/// </para>
/// <para>
/// This is where ADR 0004's "Import triggers registration; it does not perform
/// it" lands. Import asks for a name to exist; what a registered repository
/// holds is decided here, against the same store the Repositories screen writes
/// — so a repository a plan introduced is an ordinary configured repository from
/// the moment it appears, editable in the one place repositories are edited.
/// </para>
/// </remarks>
internal sealed class SettingsRepositoryDirectory(GitHubSettingsStore settings) : IRepositoryDirectory
{
    public IReadOnlyList<TasksRepositoryRef> Repositories =>
        [.. settings.Current.Repositories.Select(repository =>
            new TasksRepositoryRef(repository.Alias, repository.Owner, repository.Name))];

    public TasksRepositoryRef? Resolve(string name)
    {
        var alias = GitHubRepositoryRef.NormalizeAlias(name);

        // Ordinal, because both sides have been through the same normalization —
        // the stored alias when it was configured, this one just now.
        var match = settings.Current.Repositories
            .FirstOrDefault(repository => string.Equals(repository.Alias, alias, StringComparison.Ordinal));

        return match is null ? null : new TasksRepositoryRef(match.Alias, match.Owner, match.Name);
    }

    public TasksRepositoryRef Register(string name)
    {
        // Idempotent by asking the same question Resolve does: a plan naming a
        // repository that already exists is not a request to create a second one.
        if (Resolve(name) is { } existing) return existing;

        // The plan stated a name and nothing else, which is all a repository is
        // registered with. Owner and name stand in as the alias: a syntactically
        // valid coordinate that is plainly not a verified GitHub one, so the
        // repository is usable straight away and obviously wants correcting in
        // Settings — rather than a blank that would have to be special-cased
        // everywhere a repository is drawn.
        var alias = GitHubRepositoryRef.NormalizeAlias(name);
        var registered = new GitHubRepositoryRef(alias, alias, alias);

        // The store takes the whole list; there is no narrower "add one", which is
        // the same bargain the Repositories screen already makes when it saves.
        _ = settings.SetRepositories([.. settings.Current.Repositories, registered]);

        return new TasksRepositoryRef(registered.Alias, registered.Owner, registered.Name);
    }
}
