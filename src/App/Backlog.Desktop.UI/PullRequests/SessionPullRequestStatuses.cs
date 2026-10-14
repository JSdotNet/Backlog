using System.Text.RegularExpressions;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;

namespace Backlog.Desktop.UI.PullRequests;

/// <summary>
/// How a pull request the session list shows stands, answered the way the pull requests
/// pane answers it: read from GitHub by number and given its readiness verdict
/// (<see cref="PullRequestVerdict"/>), so a pull request wears the same chip on both
/// screens.
/// <para>
/// Read by number — the pinned read — rather than from the open list, because a
/// session's pull request may since have merged or closed, and the open list would not
/// hold it. One pull request per call: the panel asks for the row it shows. An open one
/// is then placed among its repository's open pull requests, so a stacked one names the
/// pull request it waits on, as the pane does.
/// </para>
/// </summary>
internal static partial class SessionPullRequestStatuses
{
    /// <summary>The verdict on one pull request, or null where it cannot be placed on
    /// GitHub, GitHub is not configured, or the read failed.</summary>
    public static async Task<SessionPullRequestStatus?> ReadAsync(GitHubIntegration? gitHub, DeliveryRunReference pull)
    {
        if (gitHub is not { IsConfigured: true } || PinOf(pull) is not { } pin) return null;

        var listing = await gitHub.ListPinnedPullRequestsAsync([pin]);
        var found = listing.PullRequests.FirstOrDefault(candidate =>
            candidate.Number == pin.Number
            && string.Equals(candidate.RepositoryFullName, pin.RepositoryFullName, StringComparison.OrdinalIgnoreCase));

        if (found is null) return null;

        return Of(found, await StackParentOfAsync(gitHub, found));
    }

    /// <summary>
    /// The open pull request <paramref name="pull"/> is stacked on, found the way the
    /// pane finds it — <see cref="PullRequestStacks"/> over its repository's open pull
    /// requests — so a stacked pull request's reason names its parent on the card as
    /// it does in the pane. Null for one merged or closed, one in a repository that is
    /// not configured, and one at the bottom of a stack or in none.
    /// </summary>
    private static async Task<GitHubOpenPullRequest?> StackParentOfAsync(GitHubIntegration gitHub, GitHubOpenPullRequest pull)
    {
        if (!pull.IsOpen) return null;

        var repository = gitHub.Repositories.FirstOrDefault(candidate =>
            string.Equals(candidate.FullName, pull.RepositoryFullName, StringComparison.OrdinalIgnoreCase));
        if (repository is null) return null;

        var open = await gitHub.ListOpenPullRequestsAsync([repository]);
        var others = open.PullRequests.Where(candidate => candidate.Number != pull.Number
            || !string.Equals(candidate.RepositoryFullName, pull.RepositoryFullName, StringComparison.OrdinalIgnoreCase));

        return PullRequestStacks.Of([.. others, pull]).ParentOf(pull);
    }

    /// <summary>A pull request as the session list's card draws it: its verdict's label
    /// and tone, its title, and the verdict's reason. Closed without merging is no
    /// verdict, so it reads as closed in the archived tone.</summary>
    /// <param name="pull">The pull request.</param>
    /// <param name="waitsOn">The open pull request it is stacked on, as
    /// <see cref="PullRequestVerdict.Of(GitHubOpenPullRequest, GitHubOpenPullRequest?)"/>
    /// takes it, so the card's reason is the pane's.</param>
    public static SessionPullRequestStatus Of(GitHubOpenPullRequest pull, GitHubOpenPullRequest? waitsOn = null)
    {
        ArgumentNullException.ThrowIfNull(pull);

        return PullRequestVerdict.Of(pull, waitsOn) is { } verdict
            ? new SessionPullRequestStatus(verdict.Label, verdict.Tone.ToString().ToLowerInvariant(), pull.Title, verdict.Reason)
            : new SessionPullRequestStatus("Closed", "archived", pull.Title, "Closed without merging.");
    }

    /// <summary>
    /// Where a reference points on GitHub: its repository as <c>owner/name</c> — from
    /// the reference where it carries one, else from its URL — and its number, from the
    /// URL or the <c>PR #n</c> label. Null where either cannot be read.
    /// </summary>
    public static PullRequestPin? PinOf(DeliveryRunReference pull)
    {
        ArgumentNullException.ThrowIfNull(pull);

        var url = string.IsNullOrWhiteSpace(pull.Url) ? null : GitHubPullUrl().Match(pull.Url.Trim());
        var repository = pull.Repository is { } recorded && recorded.Contains('/')
            ? recorded.Trim()
            : url is { Success: true } ? url.Groups["repository"].Value : null;
        var number = url is { Success: true }
            ? int.Parse(url.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture)
            : PullLabel().Match(pull.Label) is { Success: true } label
                ? int.Parse(label.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture)
                : 0;

        return repository is null || number <= 0 ? null : new PullRequestPin(repository, number);
    }

    [GeneratedRegex(@"^https?://github\.com/(?<repository>[^/\s]+/[^/\s]+)/pull/(?<number>\d{1,9})", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubPullUrl();

    [GeneratedRegex(@"#(?<number>\d{1,9})\b")]
    private static partial Regex PullLabel();
}
