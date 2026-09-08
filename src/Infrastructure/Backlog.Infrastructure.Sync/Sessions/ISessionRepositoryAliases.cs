using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// PORT — what a repository this machine recorded is called, in the word
/// .arc42/adr/0005 §Session records puts on the wire.
/// <para>
/// A port of one method rather than an injected
/// <see cref="IRepositoryDirectory"/>, even though this project already
/// references the Tasks module and could take one directly. Two reasons, and the
/// second is the load-bearing one.
/// </para>
/// <para>
/// The narrow one: <see cref="IRepositoryDirectory"/> also carries
/// <see cref="IRepositoryDirectory.Register"/>, which creates a repository in the
/// person's settings. A background loop must not be able to reach that. ADR 0007
/// puts registration behind a deliberate act — "Import triggers registration; it
/// does not perform it" — and a push cycle that quietly registered whatever an
/// agent happened to record would be exactly the accident that line forbids. A
/// port that can only answer cannot do it by mistake.
/// </para>
/// <para>
/// The substantive one: what session sync needs is one lookup, and stating it as
/// one lookup is what keeps the fallback honest. The alias is a convenience the
/// pushing machine may or may not have configured, so a machine with no alias for
/// a repository sends the <c>owner/name</c> the agent recorded and never null —
/// see <see cref="SessionRecordMapping"/>. Expressed as this port, "no alias
/// configured" and "no repository recorded" cannot be confused for one another,
/// because only the second is ever null on the way in.
/// </para>
/// </summary>
public interface ISessionRepositoryAliases
{
    /// <summary>
    /// The alias configured for <paramref name="repository"/>, or null when this
    /// machine has none.
    /// <para>
    /// Null is an ordinary answer. The caller sends what the agent recorded
    /// instead; it never invents an alias, and it never falls back to anything
    /// derived from a working folder.
    /// </para>
    /// </summary>
    /// <param name="repository">The <c>owner/name</c> the agent recorded.</param>
    string? AliasFor(string repository);
}

/// <summary>
/// The alias directory this product already has, behind
/// <see cref="ISessionRepositoryAliases"/>.
/// <para>
/// It wraps the Tasks module's <see cref="IRepositoryDirectory"/>, which is the
/// one place a repository's alias is configured — the same store the Repositories
/// settings screen writes and the same resolution a <c>repo:</c> token gets.
/// Composing over it rather than reading <c>GitHubSettingsStore</c> directly
/// keeps the "Tasks does not own the repository list and must not become a second
/// place it is configured" rule pointing at one adapter rather than two.
/// </para>
/// <para>
/// <strong>The directory is optional and its absence is not a fault.</strong> A
/// head can compose session sync without having composed Tasks' adapters —
/// nothing about a session needs a task database — and a host that has not is
/// simply a host where no repository has an alias yet. Answering null there sends
/// the recorded <c>owner/name</c>, which is a true statement about the session;
/// throwing would take the whole exchange down over a convenience.
/// </para>
/// </summary>
public sealed class RepositoryDirectorySessionAliases : ISessionRepositoryAliases
{
    private readonly IRepositoryDirectory? _repositories;

    public RepositoryDirectorySessionAliases(IRepositoryDirectory? repositories) =>
        _repositories = repositories;

    /// <summary>
    /// Asked of the directory on every call rather than cached, for the reason
    /// <c>SettingsRepositoryDirectory</c> reads its store on every access:
    /// somebody can add a repository in Settings while a cycle is running, and
    /// the next batch should carry the alias they just chose.
    /// </summary>
    public string? AliasFor(string repository)
    {
        if (_repositories is null || string.IsNullOrWhiteSpace(repository)) return null;

        // Resolve dispatches on shape — a name containing a slash is matched
        // against owner/name without regard to case — so the recorded coordinate
        // is exactly what it expects, and nothing here has to parse it first.
        return _repositories.Resolve(repository)?.Alias;
    }
}
