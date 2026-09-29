using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>What a row's menu can do about its pull request's merge.</summary>
public enum PullRequestMergeAct
{
    /// <summary>Hand the pull request to GitHub's native auto-merge, which merges
    /// it once its checks and other requirements pass.</summary>
    MergeWhenChecksPass,

    /// <summary>Merge it now. Offered instead of auto-merge when GitHub reports it
    /// already mergeable, because GitHub refuses to queue one in that state.</summary>
    MergeNow,

    /// <summary>Withdraw a pending auto-merge request.</summary>
    CancelAutoMerge
}

/// <summary>
/// The one merge act a row's menu offers, and the pull request it is about.
/// <para>
/// Worked out from the last status read rather than asked for at the click,
/// because the menu has to say what it will do before anyone chooses it — and a
/// label that names the act ("Merge #710 now") is the difference between a command
/// a reader trusts and one they have to open GitHub to check. A status that has
/// moved on since the read is GitHub's to refuse, and the refusal says what to do
/// instead.
/// </para>
/// </summary>
public sealed record PullRequestMergeOffer(EntryPullRequestLink PullRequest, GitHubPullRequestStatus Status)
{
    /// <summary>Queued already is the first question: a pull request GitHub is
    /// holding is offered the way out whatever its merge state, because enabling
    /// again would be refused and merging now would jump the queue it was put in
    /// on purpose.</summary>
    public PullRequestMergeAct Act =>
        Status.AutoMergeEnabled ? PullRequestMergeAct.CancelAutoMerge
        : Status.MergeReady ? PullRequestMergeAct.MergeNow
        : PullRequestMergeAct.MergeWhenChecksPass;

    /// <summary>The menu's words, naming the pull request by number because a row
    /// can record more than one.</summary>
    public string Label => Act switch
    {
        PullRequestMergeAct.CancelAutoMerge => $"Cancel auto-merge for #{PullRequest.Number}",
        PullRequestMergeAct.MergeNow => $"Merge #{PullRequest.Number} now",
        _ => $"Merge #{PullRequest.Number} when checks pass"
    };
}
