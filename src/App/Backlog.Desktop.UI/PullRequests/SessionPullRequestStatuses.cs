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
/// hold it. One pull request per call: the panel asks for the row it shows, not for the
/// whole list.
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

        return found is null ? null : Of(found);
    }

    /// <summary>A pull request as the session list's card draws it: its verdict's label
    /// and tone, its title, and the verdict's reason. Closed without merging is no
    /// verdict, so it reads as closed in the archived tone.</summary>
    public static SessionPullRequestStatus Of(GitHubOpenPullRequest pull)
    {
        ArgumentNullException.ThrowIfNull(pull);

        return PullRequestVerdict.Of(pull) is { } verdict
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
