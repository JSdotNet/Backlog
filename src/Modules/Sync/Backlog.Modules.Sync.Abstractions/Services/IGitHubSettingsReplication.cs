using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.Abstractions.Services;

/// <summary>The two GitHub settings documents that travel between a person's
/// devices (local ADR 0020): the repository registry, and the account identities.
/// Two rather than one, so an account added on one PC and a binding changed on
/// another both survive.</summary>
public enum GitHubReplicaDocument
{
    Registry,
    Accounts,
}

/// <summary>What <see cref="IGitHubSettingsReplication.ApplyAsync"/> did with an
/// inbound copy. The four answers the roadmap's port gives (local ADR 0018), for
/// the same reason: the sync client counts the first as work applied, the last as a
/// document this build could not read, and the two between as nothing to do.</summary>
public enum GitHubReplicaOutcome
{
    /// <summary>The copy was later than the local one, or there was none, and it
    /// replaced it.</summary>
    Taken,

    /// <summary>The copy carries the stamp the local one already has: this
    /// device's own push coming back, or a replayed page. Nothing was
    /// written.</summary>
    Echo,

    /// <summary>The copy is older than the local one, or the local one cannot be
    /// read and so cannot be weighed against it. Nothing was written.</summary>
    Refused,

    /// <summary>The copy does not read as the document it claims to be. The local
    /// one was left exactly as it was.</summary>
    Unreadable,
}

/// <summary>
/// The port the sync client hands the replicated GitHub settings documents to, and
/// reads them from (local ADR 0020).
/// <para>
/// The client routes by kind and nothing else: it carries a document's text and its
/// stamp and never looks inside. Which copy wins, what a copy may change on this
/// machine, and whether it can be read at all is decided by whoever answers this —
/// the GitHub settings store. No token and no credential kind is ever in a document
/// this port hands out.
/// </para>
/// <para>
/// Published here rather than by a module because the documents belong to no
/// module: the registry and the accounts are the GitHub adapter's own settings, and
/// an adapter may see a module's published surface but never another adapter.
/// </para>
/// </summary>
public interface IGitHubSettingsReplication
{
    /// <summary>
    /// Raised after the registry or the account list was changed on this machine,
    /// so a sync loop can push it within seconds rather than on its next tick.
    /// <para>
    /// Never for a copy <see cref="ApplyAsync"/> took — a loop that heard it would
    /// start a cycle on the heels of every cycle that received one — and never for
    /// a change that leaves both documents as they were: a pasted token, a
    /// credential choice or a clone directory is this machine's alone. It may
    /// arrive on any thread.
    /// </para>
    /// </summary>
    event Action? Changed;

    /// <summary>The document as this device holds it, or <c>null</c> when it was
    /// never saved here — so a newly paired device has nothing to send, and never
    /// replaces another device's registry or accounts with an empty list.</summary>
    Task<GitHubReplicaCopyDto?> ReadAsync(
        GitHubReplicaDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Offers a copy that arrived from another device. A later stamp is taken, at
    /// the inbound stamp rather than this machine's clock; an equal stamp is an
    /// echo and an older one is refused, neither writing anything. A copy that does
    /// not read as the document it claims to be leaves the local one as it was.
    /// </summary>
    Task<GitHubReplicaOutcome> ApplyAsync(
        GitHubReplicaDocument document,
        GitHubReplicaCopyDto inbound,
        CancellationToken cancellationToken = default);
}
