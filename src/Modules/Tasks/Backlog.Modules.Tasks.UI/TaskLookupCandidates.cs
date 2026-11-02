using System.Globalization;

using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The window's task rows as <see cref="ITaskLookup"/> matches over them: one
/// <see cref="TaskLookupCandidate"/> per row with an id, each id once (local ADR 0042,
/// V8). <see cref="TasksDesktopState"/> builds them, here because a GitHub-linked task's
/// issue is read with <see cref="GitHubConnector.ConnectorId"/>, which the Tasks core
/// does not see.
/// </summary>
internal static class TaskLookupCandidates
{
    /// <summary>Every row with an id as a candidate, the first row of an id winning.</summary>
    public static IReadOnlyList<TaskLookupCandidate> Of(IEnumerable<EntryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var seen = new HashSet<Guid>();
        var candidates = new List<TaskLookupCandidate>();
        foreach (var row in rows)
        {
            if (row.Id is not { } id || !seen.Add(id)) continue;

            var source = row.SourceRef;
            candidates.Add(new TaskLookupCandidate(
                id,
                string.IsNullOrWhiteSpace(row.PreviewTitle) ? id.ToString()[..8] : row.PreviewTitle,
                [.. row.PullRequestLinks.Select(link => new TaskPullRequestLink(link.Repository, link.Number))],
                source is null ? null : GitHubIssueOf(source.ConnectorId, source.Target, source.DisplayKey, source.Url),
                source?.DisplayKey,
                [.. row.SessionLinks.Select(link => link.SessionId)],
                IsMine: row.PreviewStatus is not (EntryStatus.Done or EntryStatus.Archived)));
        }

        return candidates;
    }

    /// <summary>
    /// The GitHub issue a linked task follows, when it came through the GitHub
    /// connector: the connected repository, and the number from the <c>#n</c> display
    /// key or else the issue URL. Null for any other connector.
    /// </summary>
    public static TaskIssueReference? GitHubIssueOf(string? connectorId, string? target, string? displayKey, string? url)
    {
        if (!string.Equals(connectorId, GitHubConnector.ConnectorId, StringComparison.OrdinalIgnoreCase)) return null;
        if (string.IsNullOrWhiteSpace(target)) return null;

        var key = (displayKey ?? string.Empty).Trim();
        if (key.StartsWith('#') && int.TryParse(key[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var fromKey) && fromKey > 0)
        {
            return new TaskIssueReference(target.Trim(), fromKey);
        }

        var segments = (url ?? string.Empty).TrimEnd('/').Split('/');
        return segments is [.., "issues", var last]
            && int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var fromUrl)
            && fromUrl > 0
                ? new TaskIssueReference(target.Trim(), fromUrl)
                : null;
    }
}
