using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Tests;

/// <summary>
/// Repository Management's contract (local ADR 0042) as a reader of it sees it: the pull
/// requests a test says GitHub holds, each repository a test says refused, and every
/// read and command asked of it, in order. It keeps nothing between reads — the one
/// cache is the module's, and its own tests prove it — so a reader's test counts what
/// that reader asked and nothing the cache would have answered.
/// </summary>
internal sealed class InMemoryPullRequests : IPullRequests
{
    private readonly Lock _gate = new();
    private readonly List<PullRequestStatus> _pulls = [];
    private readonly Dictionary<string, string> _refusals = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _calls = [];
    private readonly List<PullRequestReadOptions> _readOptions = [];

    /// <summary>At least one repository registered; true unless a test says not.</summary>
    public bool IsConfigured { get; set; } = true;

    /// <summary>When set, every read throws it.</summary>
    public Exception? Throws { get; set; }

    /// <summary>When set, every open-list read throws it, while a read by number answers.</summary>
    public Exception? ListThrows { get; set; }

    /// <summary>The repositories an open-list read leaves out with no failure, as the
    /// module leaves out one nobody registered, or one archived.</summary>
    public HashSet<string> Unlisted { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The repositories whose open list alone is refused — a failure in the
    /// listing — while a read by number of them still answers.</summary>
    public HashSet<string> ListRefuses { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The repositories whose open list reached the source's cap: listed, but
    /// not whole.</summary>
    public HashSet<string> Capped { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pull requests an open list leaves out of a repository it still lists
    /// whole — one opened after the list was read.</summary>
    public HashSet<string> ListedWithout { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When set, a read by number answers only once it completes.</summary>
    public TaskCompletionSource? ReadGate { get; set; }

    /// <summary>What every command answers: success unless a test says GitHub refused.</summary>
    public Result ActsWith { get; set; } = Result.Success();

    /// <summary>Every call, in order: <c>list owner/name,…</c> (<c>list *</c> for every
    /// repository), <c>read owner/name#1 owner/name#2</c>, <c>merged</c>, or the command
    /// and <c>owner/name#number</c>.</summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate) return [.. _calls];
        }
    }

    /// <summary>Each read by number's options, in order.</summary>
    public IReadOnlyList<PullRequestReadOptions> ReadOptions
    {
        get
        {
            lock (_gate) return [.. _readOptions];
        }
    }

    public event Action<PullRequestsChanged>? Changed;

    /// <summary>What GitHub holds: each pull request, in whatever state. A later one under
    /// the same reference replaces the earlier.</summary>
    public InMemoryPullRequests Holds(params PullRequestStatus[] pulls)
    {
        lock (_gate)
        {
            foreach (var pull in pulls)
            {
                _pulls.RemoveAll(held => held.Reference.Names(pull.RepositoryFullName, pull.Number));
                _pulls.Add(pull);
            }
        }

        return this;
    }

    /// <summary>Every read of the repository is refused in these words.</summary>
    public InMemoryPullRequests Refuses(string repository, string message)
    {
        lock (_gate) _refusals[repository.Trim()] = message;
        return this;
    }

    /// <summary>Raises <see cref="Changed"/>, as a command made elsewhere would.</summary>
    public void Raise(PullRequestsChanged change) => Changed?.Invoke(change);

    public Task<PullRequestListing> ListOpenAsync(
        IReadOnlyCollection<string>? repositories = null,
        PullRequestReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Record(repositories is null ? "list *" : $"list {string.Join(",", repositories)}");
        if (Throws is { } thrown) return Task.FromException<PullRequestListing>(thrown);
        if (ListThrows is { } listThrown) return Task.FromException<PullRequestListing>(listThrown);

        lock (_gate)
        {
            var named = repositories?.Select(repository => repository.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var open = _pulls
                .Where(pull => pull.IsOpen && (named is null || named.Contains(pull.RepositoryFullName.Trim())))
                .Where(pull => !Unlisted.Contains(pull.RepositoryFullName.Trim()))
                .Where(pull => !ListRefuses.Contains(pull.RepositoryFullName.Trim()))
                .Where(pull => !ListedWithout.Contains(pull.Reference.Key))
                .Where(pull => !_refusals.ContainsKey(pull.RepositoryFullName.Trim()))
                .OrderByDescending(pull => pull.UpdatedAt)
                .ToList();
            var failures = _refusals
                .Where(refusal => named is null || named.Contains(refusal.Key))
                .Select(refusal => new RepositoryReadFailure(refusal.Key, refusal.Value))
                .Concat(ListRefuses
                    .Where(repository => named is null || named.Contains(repository))
                    .Select(repository => new RepositoryReadFailure(repository, "The open list was refused.")))
                .ToList();

            // Whole: each repository asked — or held, for every repository — that was
            // neither refused, left out, nor capped.
            var whole = (named ?? _pulls.Select(pull => pull.RepositoryFullName.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase))
                .Where(repository => !_refusals.ContainsKey(repository) && !ListRefuses.Contains(repository)
                    && !Unlisted.Contains(repository) && !Capped.Contains(repository))
                .ToList();

            return Task.FromResult(new PullRequestListing(PullRequestStacks.WithParents(open), failures) { CompleteRepositories = whole });
        }
    }

    public Task<MergedPullRequestListing> ListMergedAsync(
        IReadOnlyCollection<string>? repositories = null,
        PullRequestReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Record("merged");
        return Task.FromResult(MergedPullRequestListing.Empty);
    }

    public async Task<PullRequestListing> ReadAsync(
        IReadOnlyCollection<PullRequestReference> pullRequests,
        PullRequestReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);

        lock (_gate)
        {
            _calls.Add($"read {string.Join(" ", pullRequests.Select(pull => pull.Key))}");
            _readOptions.Add(options ?? PullRequestReadOptions.Default);
        }

        if (Throws is { } thrown) throw thrown;
        if (ReadGate is { } gate) await gate.Task.WaitAsync(cancellationToken);

        return Answer(pullRequests);
    }

    public PullRequestListing Kept(IReadOnlyCollection<PullRequestReference> pullRequests) => Answer(pullRequests) with { Failures = [] };

    public Task<Result> MergeAsync(PullRequestReference pullRequest, CancellationToken cancellationToken = default) => Act("merge", pullRequest);

    public Task<Result> SetAutoMergeAsync(PullRequestReference pullRequest, bool enable, CancellationToken cancellationToken = default) =>
        Act(enable ? "auto-merge" : "cancel auto-merge", pullRequest);

    public Task<Result> UpdateBranchAsync(PullRequestReference pullRequest, CancellationToken cancellationToken = default) => Act("update", pullRequest);

    public Task<Result> MarkReadyForReviewAsync(PullRequestReference pullRequest, CancellationToken cancellationToken = default) => Act("ready", pullRequest);

    public Task<Result> RerunFailedAsync(PullRequestReference pullRequest, CancellationToken cancellationToken = default) => Act("rerun", pullRequest);

    /// <summary>The held pull requests these references name, in the order asked; a
    /// refused repository's numbers are its failure, the rest answered without.</summary>
    private PullRequestListing Answer(IReadOnlyCollection<PullRequestReference> pullRequests)
    {
        lock (_gate)
        {
            var found = new List<PullRequestStatus>();
            var failures = new List<RepositoryReadFailure>();

            foreach (var asked in pullRequests)
            {
                if (_refusals.TryGetValue(asked.RepositoryFullName.Trim(), out var refusal))
                {
                    var failure = new RepositoryReadFailure(asked.RepositoryFullName.Trim(), refusal);
                    if (!failures.Contains(failure)) failures.Add(failure);
                }
                else if (_pulls.FirstOrDefault(pull => pull.Reference.Names(asked.RepositoryFullName, asked.Number)) is { } pull)
                {
                    found.Add(pull);
                }
            }

            return new PullRequestListing(found, failures);
        }
    }

    private Task<Result> Act(string act, PullRequestReference pullRequest)
    {
        Record($"{act} {pullRequest.Key}");
        Changed?.Invoke(new PullRequestsChanged([pullRequest.RepositoryFullName], [pullRequest]));
        return Task.FromResult(ActsWith);
    }

    private void Record(string call)
    {
        lock (_gate) _calls.Add(call);
    }
}
