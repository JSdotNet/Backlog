using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.Services;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// The published <see cref="IGitHubSettingsReplication"/> port over the settings
/// store: the repository registry and the account identities as two whole
/// documents, last write wins on each one's own stamp (local ADR 0021).
/// <para>
/// A thin door rather than a second owner of the rules. Which copy wins and what a
/// copy may change on this machine are the store's, because only the store knows
/// which half of a repository is shared and which is this machine's; this forwards
/// and nothing more. Both are synchronous file work behind an asynchronous port, as
/// the roadmap's pace file is.
/// </para>
/// <para>
/// A singleton over the store singleton, so the subscription below never keeps
/// anything alive past its time.
/// </para>
/// </summary>
internal sealed class GitHubSettingsReplication : IGitHubSettingsReplication
{
    private readonly GitHubSettingsStore _store;

    public GitHubSettingsReplication(GitHubSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _store.SharedDocumentChanged += () => Changed?.Invoke();
    }

    public event Action? Changed;

    public Task<GitHubReplicaCopyDto?> ReadAsync(
        GitHubReplicaDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.ReadReplica(document));
    }

    public Task<GitHubReplicaOutcome> ApplyAsync(
        GitHubReplicaDocument document,
        GitHubReplicaCopyDto inbound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbound);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_store.ApplyReplica(document, inbound));
    }
}
