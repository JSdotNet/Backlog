using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The one merge act a row's menu offers, and the pull request it is about.
/// <para>
/// Which act is Repository Management's rule, the one the Pull requests pane follows
/// (<see cref="LinkedPullRequest.MergeOffer"/>, local ADR 0042), worked out from the last
/// status read rather than asked for at the click, because the menu has to say what it
/// will do before anyone chooses it — and a label that names the act ("Merge #710 now")
/// is the difference between a command a reader trusts and one they have to open GitHub
/// to check. A status that has moved on since the read is GitHub's to refuse, and the
/// refusal says what to do instead.
/// </para>
/// </summary>
public sealed record PullRequestMergeOffer(EntryPullRequestLink PullRequest, LinkedPullRequestMerge Act)
{
    /// <summary>The menu's words, naming the pull request by number because a row
    /// can record more than one.</summary>
    public string Label => Act switch
    {
        LinkedPullRequestMerge.CancelAutoMerge => $"Cancel auto-merge for #{PullRequest.Number}",
        LinkedPullRequestMerge.MergeNow => $"Merge #{PullRequest.Number} now",
        _ => $"Merge #{PullRequest.Number} when checks pass"
    };
}
