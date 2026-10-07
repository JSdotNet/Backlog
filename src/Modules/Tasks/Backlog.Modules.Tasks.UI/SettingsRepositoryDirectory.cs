using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

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
/// This is where ADR 0007's "Import triggers registration; it does not perform
/// it" lands. Import asks for a name to exist; what a registered repository
/// holds is decided here, against the same store the Repositories screen writes
/// — so a repository a plan introduced is an ordinary configured repository from
/// the moment it appears, editable in the one place repositories are edited.
/// </para>
/// </remarks>
internal sealed class SettingsRepositoryDirectory(GitHubSettingsStore settings) : IRepositoryDirectory
{
    private const string RegistrationFailed = "repositories.registration_failed";

    public IReadOnlyList<TasksRepositoryRef> Repositories =>
        [.. settings.Current.Repositories.Select(repository =>
            new TasksRepositoryRef(repository.Alias, repository.Owner, repository.Name))];

    /// <summary>
    /// <see cref="GitHubSettings.Find"/>'s rule, asked of the store itself: a
    /// name with a <c>/</c> is an id, anything else an alias, and an id the
    /// registry remembers renaming away answers with the repository it became.
    /// That last part is what lets the startup reconcile pass carry an entry
    /// across a rename applied on another install, whichever of the registry
    /// and the entry reached this machine first.
    /// <para>
    /// There is deliberately no bare-name fallback onto <c>Name</c>. A configured
    /// line with no explicit alias already takes the repository name <em>as</em>
    /// its alias (see <c>GitHubRepositoryRef.TryParse</c>), so the alias branch
    /// answers that case already; a third branch would only add a way for two
    /// repositories to answer to one word.
    /// </para></summary>
    public TasksRepositoryRef? Resolve(string name)
    {
        var match = Find(name);
        return match is null ? null : new TasksRepositoryRef(match.Alias, match.Owner, match.Name);
    }

    public Result<TasksRepositoryRef> Register(string name)
    {
        // Idempotent by asking the same question Resolve does: a plan naming a
        // repository that already exists is not a request to create a second one.
        // Now that Resolve answers to an id as well, this covers a stored
        // `owner/name` arriving twice, not only an alias.
        if (Resolve(name) is { } existing) return existing;

        // The one grammar the Settings text box reads, reused rather than
        // re-derived. It takes `owner/name`, a browser URL and a `.git` suffix, so
        // a plan or a stored repo_id that states a real coordinate is registered
        // as that coordinate instead of as a placeholder. That is the defect
        // `new GitHubRepositoryRef(alias, alias, alias)` had: it turned `foo/bar`
        // into the full name `foo/bar/foo/bar`.
        var parsed = GitHubRepositoryRef.TryParse(name, out _);

        var registered = parsed is not null
            // TryParse has already derived the alias the way a configured line
            // would: the repository name, lower-cased. `bar` for `foo/bar`, which
            // is what somebody typing `repo:bar` afterwards would expect.
            //
            // Kept distinct from every other repository's alias by the settings'
            // own rule (GitHubSettings.UniqueAlias), which the harness seed uses too.
            ? parsed with { Alias = settings.Current.UniqueAlias(parsed.Alias, parsed) }
            // A bare name states no coordinate, so owner and name stand in as the
            // alias: a syntactically valid coordinate that is plainly not a
            // verified GitHub one, so the repository is usable straight away and
            // obviously wants correcting in Settings — rather than a blank that
            // would have to be special-cased everywhere a repository is drawn.
            : Placeholder(GitHubRepositoryRef.NormalizeAlias(name));

        // Nothing machine-local is stated: no clone directory, no token, knowledge
        // folders left at their defaults. A repository somebody registered on
        // another install arrives here exactly this way, so a directory-less entry
        // is the ordinary shape of a registered repository rather than a lesser
        // one — DevbookFolderSource already answers a blank clone directory with
        // "Add a local clone directory ... in Settings".
        //
        // A save that failed is a registration that failed. AddRepository rather
        // than SetRepositories, because the latter keeps a change whose write did
        // not land in memory for the session: the next Resolve of this name would
        // find that row and the early return above would report success, so a
        // retried import, or the reconcile pass meeting the same id on a second
        // entry, would file entries against a repository gone after a restart.
        // An unreadable registry refuses the change before it reaches memory at
        // all. The store's sentence is the one the Repositories screen shows for
        // the same failure, so it is passed on as it is.
        if (settings.AddRepository(registered) is { } error)
        {
            return new Error(RegistrationFailed, error);
        }

        return new TasksRepositoryRef(registered.Alias, registered.Owner, registered.Name);
    }

    public bool WasRemoved(string id) => settings.Current.WasRemoved(id);

    private static GitHubRepositoryRef Placeholder(string alias) => new(alias, alias, alias);

    /// <summary>The configured repository a name refers to, matched on shape: a
    /// name containing a <c>/</c> against the <c>owner/name</c> identity without
    /// regard to case, anything else against the alias exactly — both sides of
    /// that comparison having been through the same normalization, the stored one
    /// when it was configured and this one just now.</summary>
    /// <summary>
    /// The store's own lookup, rather than a copy of its shape dispatch: the
    /// store is where an id a rename retired still resolves to the row it
    /// became, and the reconcile pass that keeps entries current after a rename
    /// asks through here. A second spelling of the rule would have to learn
    /// about renames separately, and would not.
    /// </summary>
    private GitHubRepositoryRef? Find(string name) => settings.Current.Find(name);
}
