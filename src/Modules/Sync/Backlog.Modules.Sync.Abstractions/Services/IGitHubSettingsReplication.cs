using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Replication;

namespace Backlog.Modules.Sync.Abstractions.Services;

/// <summary>The two GitHub settings documents that travel between a person's
/// devices (local ADR 0021): the repository registry, and the account identities.
/// Two rather than one, so an account added on one PC and a binding changed on
/// another both survive.</summary>
public enum GitHubReplicaDocument
{
    Registry,
    Accounts,
}

/// <summary>
/// The port the sync client hands the replicated GitHub settings documents to, and
/// reads them from (local ADR 0021).
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
    Task<ReplicaOutcome> ApplyAsync(
        GitHubReplicaDocument document,
        GitHubReplicaCopyDto inbound,
        CancellationToken cancellationToken = default);
}
