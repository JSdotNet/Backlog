using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;

namespace Backlog.Modules.RepositoryManagement.Abstractions;

/// <summary>
/// The one merge act a pull request is offered, whichever screen offers it: the pull
/// requests list, or a task's row. One vocabulary for both, because both are asking the
/// same question of the same status (local ADR 0042).
/// </summary>
public enum PullRequestMergeChoice
{
    /// <summary>GitHub reports it mergeable now, and refuses to queue auto-merge in
    /// that state.</summary>
    Now,

    /// <summary>Hand it to GitHub's native auto-merge, which merges it once its
    /// checks and other requirements pass.</summary>
    WhenReady,

    /// <summary>Withdraw the auto-merge request GitHub is already holding.</summary>
    CancelAutoMerge
}

/// <summary>
/// Which merge a pull request is offered — the rule the pull requests list and a task's
/// row share, so the two never offer different merges for one pull request.
/// </summary>
public static class PullRequestMerges
{
    /// <summary>
    /// The merge offered, or null for none.
    /// <list type="bullet">
    /// <item>Not open: none — nothing is left to merge.</item>
    /// <item>Held for auto-merge: withdraw it, whatever else holds — enabling again would
    /// be refused, merging now would jump the queue it was put in on purpose, and a
    /// withdrawal undoes a request rather than making one, so even a stacked pull request
    /// keeps it.</item>
    /// <item>A draft, a branch in conflict, or a pull request stacked on another: none.
    /// GitHub merges no draft; a conflict only a change to the branch resolves; and a
    /// stacked one merged now would land on its parent's branch rather than where the
    /// stack is going.</item>
    /// <item>Mergeable now: merge now, since GitHub refuses to queue auto-merge then.</item>
    /// <item>Otherwise: merge when ready.</item>
    /// </list>
    /// </summary>
    public static PullRequestMergeChoice? For(PullRequestStatus pull)
    {
        ArgumentNullException.ThrowIfNull(pull);

        return !pull.IsOpen ? null
            : pull.AutoMergeEnabled ? PullRequestMergeChoice.CancelAutoMerge
            : pull.IsDraft || pull.HasConflicts || pull.StackParent is not null ? null
            : pull.MergeReady ? PullRequestMergeChoice.Now
            : PullRequestMergeChoice.WhenReady;
    }
}
