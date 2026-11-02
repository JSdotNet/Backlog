using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.Tasks;

/// <summary>
/// The pull requests a task links to, answered out of Repository Management's contract
/// (local ADR 0042 §4).
/// <para>
/// A cross-context join, beside <see cref="LinkedSessionStates"/> for the same reason:
/// the task list may not reference Repository Management's published surface, so an
/// adapter that may see both answers Tasks' port. It is a mapping and nothing else. The
/// verdict is the module's (<see cref="PullRequestVerdict.Of(PullRequestStatus)"/>) and
/// the merge offer its one rule (<see cref="PullRequestMerges.For"/>), so the row says
/// and offers what the Pull requests pane does; what is kept, and for how long, is the
/// contract's one cache.
/// </para>
/// <para>
/// An open pull request is placed in its stack by the rule the session card places it by
/// (<see cref="PullRequestStacks.PlaceAmong"/>): a read by number carries no parent, and a
/// stacked pull request merged now would land on its parent's branch. One based on the
/// default branch waits on nothing; any other takes its parent from its repository's open
/// list — a cache hit whenever the pane, Progress or an earlier check listed the
/// repository within the minute — and waits on nothing where that list holds no parent
/// for it. Only one whose repository's list could not be read, based off the default
/// branch, has no verdict and no merge offer.
/// </para>
/// </summary>
public sealed class LinkedPullRequestStates : ILinkedPullRequestStates, IDisposable
{
    private readonly IPullRequests _pullRequests;

    public LinkedPullRequestStates(IPullRequests pullRequests)
    {
        _pullRequests = pullRequests ?? throw new ArgumentNullException(nameof(pullRequests));
        _pullRequests.Changed += OnChanged;
    }

    public event Action<IReadOnlyCollection<LinkedPullRequestKey>>? Changed;

    public IReadOnlyDictionary<LinkedPullRequestKey, LinkedPullRequest> Kept(IReadOnlyCollection<LinkedPullRequestKey> pullRequests)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);

        var answer = new Dictionary<LinkedPullRequestKey, LinkedPullRequest>();
        if (pullRequests.Count == 0) return answer;

        try
        {
            // Kept reads nothing, so an open one is placed by its own branches alone.
            var (placed, unplaced) = Place(_pullRequests.Kept(References(pullRequests)).PullRequests, openListing: null);
            Answer(pullRequests, placed, unplaced, answer);
        }
        catch (Exception)
        {
            // Nothing kept is an answer: the first read fills it in.
        }

        return answer;
    }

    public async Task<LinkedPullRequestRead> StatesOfAsync(
        IReadOnlyCollection<LinkedPullRequestKey> pullRequests,
        bool fresh = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequests);

        var answer = new Dictionary<LinkedPullRequestKey, LinkedPullRequest>();
        var unread = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (pullRequests.Count == 0) return new LinkedPullRequestRead(answer, unread);

        PullRequestListing listing;
        try
        {
            listing = await _pullRequests
                .ReadAsync(References(pullRequests), fresh ? PullRequestReadOptions.Fresh : PullRequestReadOptions.Default, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Unreadable is not a state: every repository asked waits out the interval.
            foreach (var pr in pullRequests) unread.Add(pr.Repository.Trim());
            return new LinkedPullRequestRead(answer, unread);
        }

        foreach (var failure in listing.Failures) unread.Add(failure.RepositoryFullName.Trim());

        var (placed, unplaced) = await PlacedAsync(listing.PullRequests, cancellationToken).ConfigureAwait(false);
        Answer(pullRequests, placed, unplaced, answer);
        return new LinkedPullRequestRead(answer, unread);
    }

    public Task<Result> MergeNowAsync(LinkedPullRequestKey pullRequest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        return _pullRequests.MergeAsync(Reference(pullRequest), cancellationToken);
    }

    public Task<Result> SetAutoMergeAsync(LinkedPullRequestKey pullRequest, bool enable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        return _pullRequests.SetAutoMergeAsync(Reference(pullRequest), enable, cancellationToken);
    }

    public void Dispose() => _pullRequests.Changed -= OnChanged;

    /// <summary>
    /// Translates one status into Tasks' words: the verdict only where the status was read
    /// — a REST answer from an older server or a settled fact has state and branches and
    /// nothing a verdict could stand on — and the merge offer on the same terms, because
    /// such a status carries no handle a command could act on. With
    /// <paramref name="placed"/> false — an open pull request whose place in a stack could
    /// not be read — neither is given either: it might be stacked, and the row must not
    /// offer a merge, or word a verdict, that the Pull requests pane would not.
    /// </summary>
    public static LinkedPullRequest Map(PullRequestStatus pull, bool placed = true)
    {
        ArgumentNullException.ThrowIfNull(pull);

        var judged = pull.StatusRead && (placed || !pull.IsOpen);
        var verdict = judged ? PullRequestVerdict.Of(pull) : null;

        return new LinkedPullRequest(Map(pull.State))
        {
            VerdictKind = verdict is null ? null : Map(verdict.Kind),
            VerdictLabel = verdict?.Label,
            VerdictTone = verdict is null ? null : Map(verdict.Tone),
            Checks = Map(pull.Checks),
            Review = Map(pull.Approvals.Decision),
            AutoMerge = pull.AutoMergeEnabled,
            // Unplaced, it may wait on another, so nothing that lands it is offered — but
            // an auto-merge GitHub holds can still be withdrawn, which lands nothing.
            MergeOffer = judged
                ? Map(PullRequestMerges.For(pull))
                : pull.StatusRead && pull.IsOpen && pull.AutoMergeEnabled ? LinkedPullRequestMerge.CancelAutoMerge : null,
            BaseRefName = string.IsNullOrWhiteSpace(pull.BaseRefName) ? null : pull.BaseRefName,
            DefaultBranch = pull.DefaultBranch
        };
    }

    /// <summary>
    /// Each pull request placed in its stack by the one rule the session card and the Pull
    /// requests pane place it by too (<see cref="PullRequestStacks.PlaceAmong"/>), over its
    /// repository's open list read at the default age — or no list, where that read failed
    /// or threw — and the keys of the open ones that rule could not place.
    /// </summary>
    private async Task<(IReadOnlyList<PullRequestStatus> Pulls, IReadOnlySet<string> Unplaced)> PlacedAsync(
        IReadOnlyList<PullRequestStatus> pulls,
        CancellationToken cancellationToken)
    {
        var open = pulls.Where(pull => pull.IsOpen && pull.StatusRead).ToList();
        if (open.Count == 0) return (pulls, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var repositories = open
            .Select(pull => pull.RepositoryFullName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        PullRequestListing? listed;
        try
        {
            listed = await _pullRequests.ListOpenAsync(repositories, PullRequestReadOptions.Default, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            listed = null;
        }

        return Place(pulls, listed);
    }

    /// <summary>Each pull request as <see cref="PullRequestStacks.PlaceAmongListing"/> places
    /// it over <paramref name="openListing"/> — no list for a repository the listing could
    /// not read — and the keys of those it could not place.</summary>
    private static (IReadOnlyList<PullRequestStatus> Pulls, IReadOnlySet<string> Unplaced) Place(
        IReadOnlyList<PullRequestStatus> pulls,
        PullRequestListing? openListing)
    {
        var unplaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var placed = new List<PullRequestStatus>(pulls.Count);

        foreach (var pull in pulls)
        {
            if (PullRequestStacks.PlaceAmongListing(pull, openListing) is { } known)
            {
                placed.Add(known);
            }
            else
            {
                unplaced.Add(pull.Reference.Key);
                placed.Add(pull);
            }
        }

        return (placed, unplaced);
    }

    /// <summary>Each asked key with the status that names it, under the key as asked.</summary>
    private static void Answer(
        IReadOnlyCollection<LinkedPullRequestKey> asked,
        IReadOnlyList<PullRequestStatus> pulls,
        IReadOnlySet<string> unplaced,
        Dictionary<LinkedPullRequestKey, LinkedPullRequest> answer)
    {
        foreach (var key in asked)
        {
            if (pulls.FirstOrDefault(pull => pull.Reference.Names(key.Repository, key.Number)) is { } pull)
            {
                answer[key] = Map(pull, placed: !unplaced.Contains(pull.Reference.Key));
            }
        }
    }

    private void OnChanged(PullRequestsChanged change)
    {
        if (Changed is not { } changed) return;

        IReadOnlyCollection<LinkedPullRequestKey> keys = [.. change.PullRequests.Select(pull => new LinkedPullRequestKey(pull.RepositoryFullName, pull.Number))];
        changed(keys);
    }

    private static IReadOnlyCollection<PullRequestReference> References(IReadOnlyCollection<LinkedPullRequestKey> pullRequests) =>
        [.. pullRequests.Distinct().Select(Reference)];

    private static PullRequestReference Reference(LinkedPullRequestKey key) => new(key.Repository.Trim(), key.Number);

    private static LinkedPullRequestState Map(PullRequestState state) => state switch
    {
        PullRequestState.Draft => LinkedPullRequestState.Draft,
        PullRequestState.Merged => LinkedPullRequestState.Merged,
        PullRequestState.Closed => LinkedPullRequestState.Closed,
        _ => LinkedPullRequestState.Open
    };

    private static LinkedPullRequestVerdictKind Map(PullRequestVerdictKind kind) => kind switch
    {
        PullRequestVerdictKind.Conflicts => LinkedPullRequestVerdictKind.Conflicts,
        PullRequestVerdictKind.FailingChecks => LinkedPullRequestVerdictKind.FailingChecks,
        PullRequestVerdictKind.ChangesRequested => LinkedPullRequestVerdictKind.ChangesRequested,
        PullRequestVerdictKind.Behind => LinkedPullRequestVerdictKind.Behind,
        PullRequestVerdictKind.Draft => LinkedPullRequestVerdictKind.Draft,
        PullRequestVerdictKind.ChecksRunning => LinkedPullRequestVerdictKind.ChecksRunning,
        PullRequestVerdictKind.ReviewRequired => LinkedPullRequestVerdictKind.ReviewRequired,
        PullRequestVerdictKind.Merged => LinkedPullRequestVerdictKind.Merged,
        _ => LinkedPullRequestVerdictKind.Ready
    };

    private static LinkedPullRequestTone Map(PullRequestTone tone) => tone switch
    {
        PullRequestTone.Live => LinkedPullRequestTone.Live,
        PullRequestTone.Alert => LinkedPullRequestTone.Alert,
        PullRequestTone.Fault => LinkedPullRequestTone.Fault,
        PullRequestTone.Settled => LinkedPullRequestTone.Settled,
        _ => LinkedPullRequestTone.Quiet
    };

    private static LinkedPullRequestChecks Map(PullRequestCheckState checks) => checks switch
    {
        PullRequestCheckState.Pending => LinkedPullRequestChecks.Pending,
        PullRequestCheckState.Passing => LinkedPullRequestChecks.Passing,
        PullRequestCheckState.Failing => LinkedPullRequestChecks.Failing,
        _ => LinkedPullRequestChecks.None
    };

    private static LinkedPullRequestReview Map(PullRequestReviewDecision review) => review switch
    {
        PullRequestReviewDecision.ReviewRequired => LinkedPullRequestReview.ReviewRequired,
        PullRequestReviewDecision.Approved => LinkedPullRequestReview.Approved,
        PullRequestReviewDecision.ChangesRequested => LinkedPullRequestReview.ChangesRequested,
        _ => LinkedPullRequestReview.None
    };

    private static LinkedPullRequestMerge? Map(PullRequestMergeChoice? choice) => choice switch
    {
        PullRequestMergeChoice.Now => LinkedPullRequestMerge.MergeNow,
        PullRequestMergeChoice.WhenReady => LinkedPullRequestMerge.MergeWhenReady,
        PullRequestMergeChoice.CancelAutoMerge => LinkedPullRequestMerge.CancelAutoMerge,
        _ => null
    };
}
