using System.Globalization;
using System.Text.RegularExpressions;

using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// Where an item's text names an issue a task was filed as: <c>#123</c> for an
/// issue in one of the item's repositories, or the issue's GitHub URL for any.
/// Shared by <see cref="DependencyProposal"/>, which reads it as "waits on that
/// task", and <see cref="RelationFinder"/>, which reads it as "is about that
/// task" — one rule, so the two cannot disagree about what names an issue.
/// </summary>
internal static partial class IssueMention
{
    /// <summary>The first of the task's issues the text names, with the text
    /// that named it, or null when it names none.</summary>
    public static (InboxIssueReferenceDto Issue, string Written)? Find(
        string text,
        List<InboxLink> links,
        InboxTaskReferenceDto task,
        IReadOnlyList<string> itemRepos)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(itemRepos);

        foreach (var issue in task.Issues)
        {
            if (itemRepos.Contains(issue.Repo, StringComparer.OrdinalIgnoreCase))
            {
                foreach (Match match in IssueNumberPattern().Matches(text))
                {
                    if (int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                        && number == issue.Number)
                    {
                        return (issue, match.Value);
                    }
                }
            }

            foreach (var link in links)
            {
                var url = IssueUrlPattern().Match(link.Key);
                if (url.Success
                    && string.Equals(url.Groups["repo"].Value, issue.Repo, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(url.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    && number == issue.Number)
                {
                    return (issue, link.Written);
                }
            }
        }

        return null;
    }

    /// <summary>A <c>#123</c> that is not the tail of a word, a link or an
    /// entity — <c>page#12</c> is an anchor and <c>&amp;#12;</c> a character.</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}_&/#])#(?<number>[0-9]+)(?![\p{L}\p{N}_])", RegexOptions.CultureInvariant)]
    private static partial Regex IssueNumberPattern();

    /// <summary>A GitHub issue URL, already compared-form (lower case, no
    /// fragment, no trailing slash, no <c>www.</c>).</summary>
    [GeneratedRegex(@"^https?://(?:www\.)?github\.com/(?<repo>[a-z0-9._-]+/[a-z0-9._-]+)/issues/(?<number>[0-9]+)(?:[/?].*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex IssueUrlPattern();
}
