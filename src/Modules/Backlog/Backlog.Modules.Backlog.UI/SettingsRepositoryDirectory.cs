using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Backlog.Abstractions.Services;

namespace Backlog.Desktop.UI.BacklogManagement;

/// <summary>
/// Answers <see cref="IRepositoryDirectory"/> from the repositories already
/// configured in Settings.
/// </summary>
/// <remarks>
/// <para>
/// Backlog Management does not own the repository list and must not become a
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
    public IReadOnlyList<BacklogRepositoryRef> Repositories =>
        [.. settings.Current.Repositories.Select(repository =>
            new BacklogRepositoryRef(repository.Alias, repository.Owner, repository.Name))];

    /// <summary>
    /// Dispatches on shape rather than trying one match and falling back to the
    /// other — the same rule <see cref="GitHubSettings.Find"/> now carries, said
    /// in the same order, because two spellings of one rule is one too many.
    /// <para>
    /// There is deliberately no bare-name fallback onto <c>Name</c>. A configured
    /// line with no explicit alias already takes the repository name <em>as</em>
    /// its alias (see <c>GitHubRepositoryRef.TryParse</c>), so the alias branch
    /// answers that case already; a third branch would only add a way for two
    /// repositories to answer to one word.
    /// </para></summary>
    public BacklogRepositoryRef? Resolve(string name)
    {
        var match = Find(name);
        return match is null ? null : new BacklogRepositoryRef(match.Alias, match.Owner, match.Name);
    }

    public BacklogRepositoryRef Register(string name)
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
            ? parsed with { Alias = UniqueAlias(parsed.Alias, parsed) }
            // A bare name states no coordinate, so owner and name stand in as the
            // alias: a syntactically valid coordinate that is plainly not a
            // verified GitHub one, so the repository is usable straight away and
            // obviously wants correcting in Settings — rather than a blank that
            // would have to be special-cased everywhere a repository is drawn.
            : Placeholder(GitHubRepositoryRef.NormalizeAlias(name));

        // The store takes the whole list; there is no narrower "add one", which is
        // the same bargain the Repositories screen already makes when it saves.
        //
        // Nothing machine-local is stated: no clone directory, no token, knowledge
        // folders left at their defaults. A repository somebody registered on
        // another install arrives here exactly this way, so a directory-less entry
        // is the ordinary shape of a registered repository rather than a lesser
        // one — KnowledgeFolderSource already answers a blank clone directory with
        // "Add a local clone directory ... in Settings".
        _ = settings.SetRepositories([.. settings.Current.Repositories, registered]);

        return new BacklogRepositoryRef(registered.Alias, registered.Owner, registered.Name);
    }

    private static GitHubRepositoryRef Placeholder(string alias) => new(alias, alias, alias);

    /// <summary>
    /// The derived alias, or a distinct one when a <em>different</em> repository
    /// already answers to it.
    /// <para>
    /// Aliases have to stay unique for two reasons that predate this: a
    /// repository list is judged invalid on a duplicate alias
    /// (<c>GitHubSettings.ParseText</c>), and <c>RepositoryColours.Resolve</c>
    /// keys its answer on the alias, so two rows sharing one would share a hue
    /// and a filter selection. Registering <c>foo/thing</c> beside a configured
    /// <c>other/thing</c> must not do that.
    /// </para>
    /// <para>
    /// An existing alias is never renamed to make room. A rename would be this
    /// code changing a label somebody chose, and it would orphan the roadmap
    /// bands keyed on exactly that label. The newcomer takes the compound
    /// <c>owner-name</c> form instead, then a counter.
    /// </para></summary>
    private string UniqueAlias(string alias, GitHubRepositoryRef repository)
    {
        if (!IsTaken(alias, repository)) return alias;

        var compound = GitHubRepositoryRef.NormalizeAlias($"{repository.Owner}-{repository.Name}");
        if (!IsTaken(compound, repository)) return compound;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = GitHubRepositoryRef.NormalizeAlias($"{compound}-{suffix}");
            if (!IsTaken(candidate, repository)) return candidate;
        }
    }

    /// <summary>Whether a repository <em>other than this one</em> already answers
    /// to the alias. "Other than this one" is judged on the id, because a
    /// repository holding its own alias is not a collision — that is the
    /// idempotent case <see cref="Register"/> has already returned from.</summary>
    private bool IsTaken(string alias, GitHubRepositoryRef repository) =>
        settings.Current.Repositories.Any(other =>
            string.Equals(other.Alias, alias, StringComparison.Ordinal)
            && !string.Equals(other.FullName, repository.FullName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The configured repository a name refers to, matched on shape: a
    /// name containing a <c>/</c> against the <c>owner/name</c> identity without
    /// regard to case, anything else against the alias exactly — both sides of
    /// that comparison having been through the same normalization, the stored one
    /// when it was configured and this one just now.</summary>
    private GitHubRepositoryRef? Find(string name)
    {
        if (name.Contains('/', StringComparison.Ordinal))
        {
            return settings.Current.Repositories.FirstOrDefault(repository =>
                string.Equals(repository.FullName, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var alias = GitHubRepositoryRef.NormalizeAlias(name);
        return settings.Current.Repositories.FirstOrDefault(repository =>
            string.Equals(repository.Alias, alias, StringComparison.Ordinal));
    }
}
