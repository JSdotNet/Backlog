using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.Services;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The GitHub settings replication port, holding one copy of each document and
/// answering whatever the test set — <see cref="RecordingRoadmapReplication"/>'s
/// shape for the registry and the accounts (local ADR 0021). The sync client never
/// sees past this port, so what the store does with a copy is not this project's
/// business: only that it was handed over whole, and how the answer is counted.
/// </summary>
internal sealed class RecordingGitHubSettingsReplication : IGitHubSettingsReplication
{
    public event Action? Changed;

    /// <summary>What <see cref="ReadAsync"/> answers, per document. Absent is a
    /// document this device never saved.</summary>
    public Dictionary<GitHubReplicaDocument, GitHubReplicaCopyDto> Held { get; } = [];

    /// <summary>Every copy offered, in order.</summary>
    public List<(GitHubReplicaDocument Document, GitHubReplicaCopyDto Copy)> Offered { get; } = [];

    /// <summary>What <see cref="ApplyAsync"/> answers. Taken by default.</summary>
    public GitHubReplicaOutcome Answer { get; set; } = GitHubReplicaOutcome.Taken;

    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>Whether anything still listens — what a disposed worker must have
    /// let go of.</summary>
    public bool HasListeners => Changed is not null;

    public Task<GitHubReplicaCopyDto?> ReadAsync(
        GitHubReplicaDocument document,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Held.TryGetValue(document, out var copy) ? copy : null);

    public Task<GitHubReplicaOutcome> ApplyAsync(
        GitHubReplicaDocument document,
        GitHubReplicaCopyDto inbound,
        CancellationToken cancellationToken = default)
    {
        Offered.Add((document, inbound));
        return Task.FromResult(Answer);
    }
}

/// <summary>GitHub settings documents the way the desktop writes them onto the
/// feed.</summary>
internal static class GitHubReplicaChanges
{
    public static TaskChange Registry(string content, DateTimeOffset updatedAt) =>
        GitHubReplicaDocuments.ToChange(GitHubReplicaDocument.Registry, content, updatedAt);

    public static TaskChange Accounts(string content, DateTimeOffset updatedAt) =>
        GitHubReplicaDocuments.ToChange(GitHubReplicaDocument.Accounts, content, updatedAt);
}
