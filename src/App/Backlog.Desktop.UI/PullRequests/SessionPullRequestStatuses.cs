using System.Text.RegularExpressions;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;

namespace Backlog.Desktop.UI.PullRequests;

/// <summary>
/// How a pull request the session list shows stands, answered the way the pull requests
/// pane answers it: read by number through Repository Management's contract and given
/// its one verdict (<see cref="PullRequestVerdict"/>, local ADR 0042), so a pull request
/// wears the same chip on both screens.
/// <para>
/// Read by number rather than from the open list, because a session's pull request may
/// since have merged or closed, and the open list would not hold it. One pull request
/// per call: the panel asks for the row it shows. An open one then takes the parent it
/// is stacked on from its repository's open list, which the contract places
/// (<see cref="PullRequestStatus.StackParent"/>), so a stacked one names the pull
/// request it waits on, as the pane does. Both reads go through the contract's one
/// cache, so the card costs nothing the pane or a task's row read within the minute.
/// </para>
/// </summary>
internal static partial class SessionPullRequestStatuses
{
    /// <summary>The verdict on one pull request, or null where it cannot be placed on
    /// GitHub, the contract is not composed or has no repository, or the read failed. A
    /// reading that finds it merged or closed is reported to <paramref name="subjects"/>,
    /// so the notifications about it close.</summary>
    public static async Task<SessionPullRequestStatus?> ReadAsync(
        IPullRequests? pullRequests,
        DeliveryRunReference pull,
        PullRequestSubjects? subjects = null)
    {
        if (pullRequests is not { IsConfigured: true } || PinOf(pull) is not { } reference) return null;

        var listing = await pullRequests.ReadAsync([reference]);
        var found = listing.PullRequests.FirstOrDefault(candidate => reference.Names(candidate.RepositoryFullName, candidate.Number));

        if (found is null) return null;

        if (subjects is not null) await subjects.ObserveAsync(found);

        return Of(await PlacedAsync(pullRequests, found));
    }

    /// <summary>
    /// The pull request with the parent it is stacked on, as its repository's open list
    /// places it — the pane's own placing — so a stacked pull request's reason names its
    /// parent on the card as it does in the pane. Unchanged for one merged or closed, and
    /// for one its repository's list does not hold.
    /// </summary>
    private static async Task<PullRequestStatus> PlacedAsync(IPullRequests pullRequests, PullRequestStatus pull)
    {
        if (!pull.IsOpen) return pull;

        var open = await pullRequests.ListOpenAsync([pull.RepositoryFullName]);
        var listed = open.PullRequests.FirstOrDefault(candidate => candidate.Reference.Names(pull.RepositoryFullName, pull.Number));

        return listed is null ? pull : pull with { StackParent = listed.StackParent };
    }

    /// <summary>A pull request as the session list's card draws it: its verdict's label
    /// and tone, its title, and the verdict's reason — the reason naming the pull request
    /// it waits on where <see cref="PullRequestStatus.StackParent"/> is set, so the card's
    /// reason is the pane's. Closed without merging is no verdict, so it reads as closed
    /// in the archived tone.</summary>
    public static SessionPullRequestStatus Of(PullRequestStatus pull)
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
    public static PullRequestReference? PinOf(DeliveryRunReference pull)
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

        return repository is null || number <= 0 ? null : new PullRequestReference(repository, number);
    }

    [GeneratedRegex(@"^https?://github\.com/(?<repository>[^/\s]+/[^/\s]+)/pull/(?<number>\d{1,9})", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubPullUrl();

    [GeneratedRegex(@"#(?<number>\d{1,9})\b")]
    private static partial Regex PullLabel();
}
