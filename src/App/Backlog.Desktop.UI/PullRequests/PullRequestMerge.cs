namespace Backlog.Desktop.UI.PullRequests;

/// <summary>
/// The one merge act a row of the pull requests list offers.
/// <para>
/// Its own three members rather than the task row's <c>PullRequestMergeAct</c>, which
/// says the same three things: that one belongs to the Tasks context's merge menu, and
/// this list is the Sessions context's screen. Borrowing it would tie the two screens'
/// wording together through a type neither of them owns jointly.
/// </para>
/// </summary>
internal enum PullRequestMerge
{
    /// <summary>GitHub reports it mergeable now, and refuses to queue auto-merge in
    /// that state.</summary>
    Now,

    /// <summary>Hand it to GitHub's native auto-merge, which merges it once its
    /// checks and other requirements pass.</summary>
    WhenChecksPass,

    /// <summary>Withdraw the auto-merge request GitHub is already holding.</summary>
    CancelAutoMerge
}
