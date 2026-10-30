using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.RepositoryManagement.UnitTests.Fakes;

/// <summary>
/// GitHub, as far as the module can tell: what each repository holds, every call it
/// was sent in order, and the acts changing what it holds the way GitHub would — a
/// merge takes the pull request out of the open list and answers it merged by number.
/// </summary>
internal sealed class CountingPullRequestSource : IPullRequestSource
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PullRequestStatus> _pulls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<MergedPullRequest>> _merged = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _refusals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _calls = [];
    private string? _actRefusal;

    /// <summary>Every call, in order: <c>list owner/name</c>, <c>read owner/name 1,2</c>,
    /// <c>merged owner/name</c>, or the act and <c>owner/name#number</c>.</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate) return [.. _calls];
        }
    }

    /// <summary>When set, every read waits on it before it answers — how a test keeps a
    /// read out while a second caller asks. The answer is what GitHub held when the read
    /// was sent, so a read held here comes back with what was true before anything that
    /// happened while it waited.</summary>
    public TaskCompletionSource? Hold { get; set; }

    private readonly Dictionary<string, TaskCompletionSource> _holdsNext = new(StringComparer.Ordinal);

    /// <summary>The next call written <paramref name="call"/> — <c>list owner/name</c>,
    /// <c>read owner/name 1</c> — waits until the test completes what this returns; the
    /// calls after it do not. How a test keeps one command's re-read out while another
    /// command runs.</summary>
    public TaskCompletionSource HoldsNext(string call)
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) _holdsNext[call] = held;
        return held;
    }

    /// <summary>When set, every read by number throws it — a transport that gave up,
    /// such as an HTTP timeout surfacing as a cancellation nobody asked for.</summary>
    public Exception? ReadThrows { get; set; }

    /// <summary>What GitHub holds: each pull request, in whatever state.</summary>
    public CountingPullRequestSource Holds(params PullRequestStatus[] pulls)
    {
        lock (_gate)
        {
            foreach (var pull in pulls) _pulls[pull.Reference.Key] = pull;
        }

        return this;
    }

    public CountingPullRequestSource HoldsMerged(params MergedPullRequest[] pulls)
    {
        lock (_gate)
        {
            foreach (var pull in pulls)
            {
                if (!_merged.TryGetValue(pull.RepositoryFullName, out var list)) _merged[pull.RepositoryFullName] = list = [];
                list.Add(pull);
            }
        }

        return this;
    }

    /// <summary>Every read of the repository is refused in these words, until
    /// <see cref="Answers"/>.</summary>
    public CountingPullRequestSource Refuses(string repository, string message)
    {
        lock (_gate) _refusals[repository] = message;
        return this;
    }

    public CountingPullRequestSource Answers(string repository)
    {
        lock (_gate) _refusals.Remove(repository);
        return this;
    }

    /// <summary>Every act is refused in these words.</summary>
    public CountingPullRequestSource RefusesActs(string message)
    {
        lock (_gate) _actRefusal = message;
        return this;
    }

    public async Task<PullRequestSourceRead> ListOpenAsync(string repositoryFullName, CancellationToken cancellationToken)
    {
        var call = $"list {repositoryFullName}";
        Record(call);
        var answer = OpenOf(repositoryFullName);
        await WaitAsync(call, cancellationToken);

        return answer;
    }

    private PullRequestSourceRead OpenOf(string repositoryFullName)
    {
        lock (_gate)
        {
            if (_refusals.TryGetValue(repositoryFullName, out var refusal))
            {
                return new PullRequestSourceRead([], new RepositoryReadFailure(repositoryFullName, refusal));
            }

            return new PullRequestSourceRead(
                [.. _pulls.Values
                    .Where(pull => pull.IsOpen && Same(pull.RepositoryFullName, repositoryFullName))
                    .OrderByDescending(pull => pull.UpdatedAt)],
                null);
        }
    }

    public async Task<PullRequestSourceRead> ReadAsync(string repositoryFullName, IReadOnlyCollection<int> numbers, CancellationToken cancellationToken)
    {
        var call = $"read {repositoryFullName} {string.Join(",", numbers.Order())}";
        Record(call);
        if (ReadThrows is { } thrown) throw thrown;
        var answer = NumbersOf(repositoryFullName, numbers);
        await WaitAsync(call, cancellationToken);

        return answer;
    }

    private PullRequestSourceRead NumbersOf(string repositoryFullName, IReadOnlyCollection<int> numbers)
    {
        lock (_gate)
        {
            if (_refusals.TryGetValue(repositoryFullName, out var refusal))
            {
                return new PullRequestSourceRead([], new RepositoryReadFailure(repositoryFullName, refusal));
            }

            return new PullRequestSourceRead(
                [.. _pulls.Values.Where(pull => Same(pull.RepositoryFullName, repositoryFullName) && numbers.Contains(pull.Number))],
                null);
        }
    }

    public async Task<MergedPullRequestSourceRead> ListMergedAsync(string repositoryFullName, CancellationToken cancellationToken)
    {
        var call = $"merged {repositoryFullName}";
        Record(call);
        var answer = MergedOf(repositoryFullName);
        await WaitAsync(call, cancellationToken);

        return answer;
    }

    private MergedPullRequestSourceRead MergedOf(string repositoryFullName)
    {
        lock (_gate)
        {
            if (_refusals.TryGetValue(repositoryFullName, out var refusal))
            {
                return new MergedPullRequestSourceRead([], new RepositoryReadFailure(repositoryFullName, refusal), false);
            }

            return new MergedPullRequestSourceRead(_merged.TryGetValue(repositoryFullName, out var list) ? [.. list] : [], null, false);
        }
    }

    public Task<Result> MergeAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) =>
        Act("merge", pullRequest, pull => pull with { State = PullRequestState.Merged, MergedAt = pull.UpdatedAt, DefaultBranch = "main" });

    public Task<Result> SetAutoMergeAsync(PullRequestStatus pullRequest, bool enable, CancellationToken cancellationToken) =>
        Act(enable ? "auto-merge" : "cancel auto-merge", pullRequest, pull => pull with { AutoMergeEnabled = enable });

    public Task<Result> UpdateBranchAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) =>
        Act("update", pullRequest, pull => pull with { IsBehind = false, BehindBy = 0 });

    public Task<Result> MarkReadyForReviewAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) =>
        Act("ready", pullRequest, pull => pull with { State = PullRequestState.Open });

    public Task<Result> RerunFailedAsync(PullRequestStatus pullRequest, CancellationToken cancellationToken) =>
        Act("rerun", pullRequest, pull => pull with { Checks = PullRequestCheckState.Pending });

    private Task<Result> Act(string act, PullRequestStatus pullRequest, Func<PullRequestStatus, PullRequestStatus> change)
    {
        Record($"{act} {pullRequest.Reference.Key}");

        lock (_gate)
        {
            if (_actRefusal is not null) return Task.FromResult(Result.Failure(PullRequestErrors.Refused(_actRefusal)));

            if (_pulls.TryGetValue(pullRequest.Reference.Key, out var held)) _pulls[pullRequest.Reference.Key] = change(held);
        }

        return Task.FromResult(Result.Success());
    }

    private async Task WaitAsync(string call, CancellationToken cancellationToken)
    {
        TaskCompletionSource? next;
        lock (_gate)
        {
            _holdsNext.Remove(call, out next);
        }

        if (next is not null) await next.Task.WaitAsync(cancellationToken);
        if (Hold is { } hold) await hold.Task.WaitAsync(cancellationToken);
    }

    private void Record(string call)
    {
        lock (_gate) _calls.Add(call);
    }

    private static bool Same(string repository, string other) =>
        string.Equals(repository.Trim(), other.Trim(), StringComparison.OrdinalIgnoreCase);
}
