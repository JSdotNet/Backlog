namespace Backlog.UI.Components.Integrations;

/// <summary>
/// One agent session as a work card draws it: what it is called, where it stands,
/// and one line of what it last did.
/// <para>
/// The card's own record rather than an <see cref="IntegrationLinkRef"/>, because a
/// card says more than a reference does — a title of its own and a line of detail —
/// and asks less: it has no drift, no URL and no artifact state. The host writes
/// <see cref="Detail"/> because the library holds no clock: "6 min ago" is the
/// host's sentence.
/// </para>
/// </summary>
/// <param name="Id">The host's id for the session, handed back when it is opened.</param>
/// <param name="Title">What the session is called; the host supplies a fallback.</param>
/// <param name="State">Where it stands. Stalled is the host's to decide — see
/// <see cref="IntegrationSessionState.Stalled"/>.</param>
/// <param name="Detail">One line under the title, already written — the repository,
/// how many prompts, when it last moved.</param>
public sealed record WorkSession(
    string Id,
    string Title,
    IntegrationSessionState State,
    string? Detail = null,
    IntegrationProvider Provider = IntegrationProvider.Claude);

/// <summary>
/// One pull request as a work card draws it: its number and title, draft or open,
/// what the reviewers decided, how its checks stand and how big it is.
/// </summary>
/// <param name="Key">The host's key for the pull request — <c>owner/name#number</c>
/// in the desktop app — handed back when it is opened.</param>
/// <param name="ChecksText">The checks in the host's words where it has a count to
/// give ("Checks running 4/7"); unset, the card words <see cref="Checks"/> itself.</param>
/// <param name="Additions">Lines added, where the host read them.</param>
/// <param name="Deletions">Lines removed, where the host read them.</param>
public sealed record WorkPullRequest(
    string Key,
    int Number,
    string Title,
    IntegrationArtifactState State,
    IntegrationReviewState Review = IntegrationReviewState.None,
    IntegrationCheckState Checks = IntegrationCheckState.None,
    string? ChecksText = null,
    int? Additions = null,
    int? Deletions = null);

/// <summary>One choice a link picker offers: a task to link a session or pull
/// request to, or a session or pull request to link to a task.</summary>
/// <param name="Id">The host's id for the choice, handed back when it is chosen.</param>
/// <param name="Label">What the choice is called.</param>
/// <param name="Detail">A line under it, where the host has one.</param>
public sealed record WorkLinkOption(string Id, string Label, string? Detail = null);

/// <summary>
/// The words and the one rule the work cards share.
/// <para>
/// Public, unlike <c>IntegrationStates</c>, because the rule is a product rule the
/// host and its tests read too: which session and which pull request ask for the
/// person. Written once here so a card, a section and a test cannot disagree about it.
/// </para>
/// </summary>
public static class WorkStates
{
    /// <summary>
    /// What a session's state is called on a card. Stalled is "Quiet 30 min": the
    /// session is still live and has not moved for longer than the host's threshold,
    /// and saying how long is more use to a reader than a word for it.
    /// </summary>
    public static string SessionWords(IntegrationSessionState state) => state switch
    {
        IntegrationSessionState.Running => "Running",
        IntegrationSessionState.Stalled => "Quiet 30 min",
        IntegrationSessionState.Waiting => "Waiting for you",
        IntegrationSessionState.Starting => "Starting",
        IntegrationSessionState.Finished => "Finished",
        IntegrationSessionState.Failed => "Failed",
        _ => "Unknown"
    };

    /// <summary>What a pull request's own state is called on a card.</summary>
    public static string PullRequestWords(IntegrationArtifactState state) => state switch
    {
        IntegrationArtifactState.Draft => "Draft",
        IntegrationArtifactState.Open => "Open",
        IntegrationArtifactState.Merged => "Merged",
        IntegrationArtifactState.Closed => "Closed",
        _ => "Unknown"
    };

    /// <summary>What the reviewers decided, or null where there is nothing to say.</summary>
    public static string? ReviewWords(IntegrationReviewState review) => review switch
    {
        IntegrationReviewState.ChangesRequested => "Changes requested",
        IntegrationReviewState.Approved => "Approved",
        IntegrationReviewState.ReviewRequired => "Review required",
        _ => null
    };

    /// <summary>How the checks stand, or null for a pull request with none.</summary>
    public static string? ChecksWords(IntegrationCheckState checks) => checks switch
    {
        IntegrationCheckState.Passing => "Checks pass",
        IntegrationCheckState.Failing => "Checks failing",
        IntegrationCheckState.Pending => "Checks running",
        _ => null
    };

    /// <summary>The diff size as <c>+412 −198</c>, or null where the host read none.</summary>
    public static string? DiffWords(int? additions, int? deletions) =>
        additions is null && deletions is null
            ? null
            : $"+{additions ?? 0} −{deletions ?? 0}";

    /// <summary>
    /// Whether a session asks for the person: it is <see cref="IntegrationSessionState.Stalled"/>
    /// — quiet for longer than the host's threshold — or waiting for an answer.
    /// </summary>
    public static bool NeedsYou(WorkSession session) =>
        session.State is IntegrationSessionState.Stalled or IntegrationSessionState.Waiting;

    /// <summary>
    /// Whether a pull request asks for the person: it is still open and a check is
    /// failing, or a reviewer asked for changes. A merged or closed one has nothing
    /// left for anybody to do.
    /// </summary>
    public static bool NeedsYou(WorkPullRequest pull) =>
        pull.State is not (IntegrationArtifactState.Merged or IntegrationArtifactState.Closed)
        && (pull.Checks is IntegrationCheckState.Failing || pull.Review is IntegrationReviewState.ChangesRequested);
}
