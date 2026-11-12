using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>What a review link in a run's Personal Validation question is to the person
/// reading it, possibly on another device.</summary>
internal enum ReviewLinkKind
{
    /// <summary>A page any device can open.</summary>
    Web,

    /// <summary>An address only the machine that runs the session can open: a loopback host
    /// or a file. It stays a link, because the desktop's own centre shows the same body and
    /// the link works there.</summary>
    DesktopOnly,

    /// <summary>A GitHub pull request, which the question shows first.</summary>
    PullRequest
}

/// <summary>
/// Sorts the review links a run's gate passes
/// (<c>.devbook/domain/notifications/requirements.md#requirement-a-run-at-personal-validation-asks-to-be-approved</c>):
/// the phone opens none of the addresses the running app and its storybook listen on, so
/// those are marked desktop only, and the pull request is lifted out to be read first.
/// </summary>
internal static partial class ReviewLinks
{
    internal static ReviewLinkKind Classify(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return ReviewLinkKind.Web;
        if (PullRequestOf(url) is not null) return ReviewLinkKind.PullRequest;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var address)) return ReviewLinkKind.Web;
        if (address.IsFile) return ReviewLinkKind.DesktopOnly;

        // DnsSafeHost drops the brackets an IPv6 host is written in.
        var host = address.DnsSafeHost;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return ReviewLinkKind.DesktopOnly;
        }

        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip)
            ? ReviewLinkKind.DesktopOnly
            : ReviewLinkKind.Web;
    }

    /// <summary>The repository, as the address spells it, and the number of the GitHub pull
    /// request <paramref name="url"/> addresses — with any path, query or fragment after
    /// it — or null for any other address.</summary>
    internal static (string Repository, int Number)? PullRequestOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var match = PullRequestAddress().Match(url.Trim());

        return match.Success && int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0
            ? ($"{match.Groups["owner"].Value}/{match.Groups["name"].Value}", number)
            : null;
    }

    [GeneratedRegex(@"^https?://(?:www\.)?github\.com/(?<owner>[\w.-]+)/(?<name>[\w.-]+)/pull/(?<number>\d{1,9})(?:[/?#].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PullRequestAddress();
}
