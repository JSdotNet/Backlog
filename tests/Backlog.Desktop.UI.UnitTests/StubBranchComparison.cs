using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A branch comparison that reaches no network: the whole-branch answer is
/// <see cref="Comparison"/>, each commit is looked up in <see cref="Commits"/>,
/// and every call is counted so a test can see what was asked twice.
/// </summary>
internal sealed class StubBranchComparison : IGitHubBranchComparison
{
    public GitHubComparison? Comparison { get; set; }

    public Dictionary<string, GitHubCommitChange> Commits { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, every call refuses the way GitHub does.</summary>
    public string? Failure { get; set; }

    /// <summary>The base and head of every comparison asked for, in order.</summary>
    public List<(string Base, string Head)> Compared { get; } = [];

    public List<string> CommitsRead { get; } = [];

    public Task<GitHubComparison> CompareAsync(
        GitHubRepositoryRef repository,
        string baseRef,
        string headRef,
        CancellationToken cancellationToken = default)
    {
        Compared.Add((baseRef, headRef));

        if (Failure is not null) throw new GitHubException(Failure);

        return Task.FromResult(Comparison ?? throw new GitHubException("No comparison was scripted."));
    }

    /// <summary>Commits that answer only once their gate is set — for a test
    /// about an answer arriving after the reader moved on.</summary>
    public Dictionary<string, TaskCompletionSource> Gates { get; } = new(StringComparer.Ordinal);

    /// <summary>The task each commit read handed back, in order — so a test can
    /// wait for the read itself to finish, failure included, rather than for a
    /// guess at how long it takes.</summary>
    public List<Task<GitHubCommitChange>> Reads { get; } = [];

    public Task<GitHubCommitChange> ReadCommitAsync(
        GitHubRepositoryRef repository,
        string commitSha,
        CancellationToken cancellationToken = default)
    {
        var read = ReadCommitCoreAsync(commitSha, cancellationToken);
        Reads.Add(read);
        return read;
    }

    private async Task<GitHubCommitChange> ReadCommitCoreAsync(string commitSha, CancellationToken cancellationToken)
    {
        CommitsRead.Add(commitSha);

        if (Gates.TryGetValue(commitSha, out var gate)) await gate.Task.WaitAsync(cancellationToken);

        if (Failure is not null) throw new GitHubException(Failure);

        return Commits.TryGetValue(commitSha, out var change)
            ? change
            : throw new GitHubException($"No commit found for SHA: {commitSha}") { Status = System.Net.HttpStatusCode.NotFound };
    }
}
