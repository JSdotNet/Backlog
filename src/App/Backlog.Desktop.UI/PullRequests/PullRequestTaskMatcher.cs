using System.Globalization;

using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.PullRequests;

/// <summary>How a pull request was found to belong to a task, surest first.</summary>
internal enum PullRequestTaskRelation
{
    /// <summary>The task's own <c>pull-request</c> projection names it — what the
    /// Backlog server's <c>link_change</c> records.</summary>
    Recorded,

    /// <summary>The task is linked to a GitHub issue the pull request closes.</summary>
    ClosesIssue,

    /// <summary>The task's external key — a Jira key such as <c>FIN-8428</c> — is a
    /// token in the pull request's title or head branch.</summary>
    ExternalKey,

    /// <summary>The task linked the session the pull request came out of.</summary>
    Session
}

/// <summary>A pull request a task recorded, by <c>owner/name</c> and number.</summary>
internal sealed record RecordedPullRequest(string RepositoryFullName, int Number);

/// <summary>
/// One task as the matcher sees it: plain values the shell reads off its entry rows,
/// so this stays free of the Tasks module's own types — task data reaches the pull
/// requests pane only through the shell.
/// </summary>
/// <param name="EntryId">The entry to open.</param>
/// <param name="PullRequests">The entry's <c>pull-request</c> projections.</param>
/// <param name="GitHubIssue">The GitHub issue a GitHub-connector linked task follows;
/// <see cref="PullRequestTaskMatcher.GitHubIssueOf"/> builds it.</param>
/// <param name="ExternalKeys">The linked task's keys in another system;
/// <see cref="PullRequestTaskMatcher.ExternalKeysOf"/> reads them from a display
/// key.</param>
/// <param name="SessionIds">The sessions the entry linked.</param>
/// <param name="IsMine">Whether the task counts as one of mine for the "Mine"
/// view.</param>
internal sealed record PullRequestTaskCandidate(
    Guid EntryId,
    IReadOnlyList<RecordedPullRequest> PullRequests,
    GitHubIssueReference? GitHubIssue,
    IReadOnlyList<string> ExternalKeys,
    IReadOnlyList<string> SessionIds,
    bool IsMine);

/// <summary>One pull request as the matcher asks about it, open or merged.</summary>
/// <param name="SessionId">The session the pane matched the pull request to, or
/// null.</param>
public sealed record PullRequestTaskSubject(
    string RepositoryFullName,
    int Number,
    string Title,
    string HeadRefName,
    IReadOnlyList<GitHubIssueReference> ClosingIssues,
    string? SessionId)
{
    public static PullRequestTaskSubject From(GitHubOpenPullRequest pull, string? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(pull);
        return new(pull.RepositoryFullName, pull.Number, pull.Title, pull.HeadRefName, pull.ClosingIssues, sessionId);
    }

    public static PullRequestTaskSubject From(GitHubMergedPullRequest pull, string? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(pull);
        return new(pull.RepositoryFullName, pull.Number, pull.Title, pull.HeadRefName, pull.ClosingIssues, sessionId);
    }
}

/// <summary>A task a pull request belongs to, and how that was found.</summary>
internal sealed record PullRequestTaskMatch(PullRequestTaskCandidate Candidate, PullRequestTaskRelation Relation);

/// <summary>
/// Finds the task a pull request belongs to, in order of how sure each relation is:
/// the task's own record of the pull request; a GitHub issue the task follows that the
/// pull request closes, by repository and number; the task's external key as a whole
/// token in the pull request's title or head branch; and the task that linked the
/// session the pull request came out of. The first relation any task has wins, so a
/// task that recorded the pull request beats one whose key merely appears in its
/// title, whatever order the tasks come in.
/// <para>
/// A key matches as a token, without regard to case: <c>FIN-84</c> is not found in
/// <c>FIN-8428</c>, nor <c>FIN-8428</c> in <c>XFIN-8428</c> or <c>X_FIN-8428</c>, because
/// a letter or digit either side — or an underscore before it, which a project key may
/// hold — means the text is a longer key.
/// </para>
/// </summary>
internal sealed class PullRequestTaskMatcher(IEnumerable<PullRequestTaskCandidate> candidates)
{
    private readonly IReadOnlyList<PullRequestTaskCandidate> _candidates = [.. candidates];

    /// <param name="mineOnly">Consider only the tasks that count as mine, for the
    /// "Mine" view: a surer match on somebody else's task must not hide one on
    /// mine.</param>
    public PullRequestTaskMatch? Match(PullRequestTaskSubject pull, bool mineOnly = false)
    {
        ArgumentNullException.ThrowIfNull(pull);

        var pool = mineOnly ? [.. _candidates.Where(candidate => candidate.IsMine)] : _candidates;

        return First(pool, PullRequestTaskRelation.Recorded, candidate => candidate.PullRequests.Any(recorded =>
                   recorded.Number == pull.Number && SameRepository(recorded.RepositoryFullName, pull.RepositoryFullName)))
            ?? First(pool, PullRequestTaskRelation.ClosesIssue, candidate => candidate.GitHubIssue is { } issue
                   && pull.ClosingIssues.Any(closing =>
                       closing.Number == issue.Number && SameRepository(closing.RepositoryFullName, issue.RepositoryFullName)))
            ?? First(pool, PullRequestTaskRelation.ExternalKey, candidate => candidate.ExternalKeys.Any(key =>
                   ContainsToken(pull.Title, key) || ContainsToken(pull.HeadRefName, key)))
            ?? First(pool, PullRequestTaskRelation.Session, candidate => pull.SessionId is { Length: > 0 } session
                   && candidate.SessionIds.Contains(session, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The keys a linked task's display key names in another system: every part after
    /// the first <c>·</c> that reads as a key — letters, a dash, digits, such as Jira's
    /// <c>FIN-8428</c> in spec-manager's <c>#123 · FIN-8428</c>. A display key that is
    /// only the source's own number names none.
    /// </summary>
    public static IReadOnlyList<string> ExternalKeysOf(string? displayKey)
    {
        if (string.IsNullOrWhiteSpace(displayKey)) return [];

        return [.. displayKey.Split('·').Skip(1).Select(part => part.Trim()).Where(IsKey)];
    }

    /// <summary>
    /// The GitHub issue a linked task follows, when it came through the GitHub
    /// connector: the connected repository, and the number from the <c>#n</c> display
    /// key or else the issue URL. Null for any other connector.
    /// </summary>
    public static GitHubIssueReference? GitHubIssueOf(string? connectorId, string? target, string? displayKey, string? url)
    {
        if (!string.Equals(connectorId, GitHubConnector.ConnectorId, StringComparison.OrdinalIgnoreCase)) return null;
        if (string.IsNullOrWhiteSpace(target)) return null;

        var key = (displayKey ?? string.Empty).Trim();
        if (key.StartsWith('#') && int.TryParse(key[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var fromKey) && fromKey > 0)
        {
            return new GitHubIssueReference(target.Trim(), fromKey);
        }

        var segments = (url ?? string.Empty).TrimEnd('/').Split('/');
        return segments is [.., "issues", var last]
            && int.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var fromUrl)
            && fromUrl > 0
                ? new GitHubIssueReference(target.Trim(), fromUrl)
                : null;
    }

    private static PullRequestTaskMatch? First(
        IReadOnlyList<PullRequestTaskCandidate> pool,
        PullRequestTaskRelation relation,
        Func<PullRequestTaskCandidate, bool> relates) =>
        pool.FirstOrDefault(relates) is { } candidate ? new PullRequestTaskMatch(candidate, relation) : null;

    private static bool SameRepository(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A key: a letter, then letters, digits or underscores, a dash, and
    /// digits.</summary>
    private static bool IsKey(string text)
    {
        var dash = text.IndexOf('-', StringComparison.Ordinal);
        if (dash <= 0 || dash == text.Length - 1) return false;

        var project = text[..dash];
        var number = text[(dash + 1)..];

        return char.IsAsciiLetter(project[0])
            && project.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            && number.All(char.IsAsciiDigit);
    }

    /// <summary>Whether <paramref name="key"/> occurs in <paramref name="text"/> with
    /// no letter or digit either side and no underscore before it. The underscore is a
    /// key character only before: <see cref="IsKey"/> allows one in the project part,
    /// never in the number, so <c>FIN-8428_total</c> still names FIN-8428.</summary>
    private static bool ContainsToken(string? text, string key)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(key)) return false;

        var wanted = key.Trim();
        for (var at = text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase);
             at >= 0;
             at = text.IndexOf(wanted, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var end = at + wanted.Length;
            var openBefore = at == 0 || !(char.IsLetterOrDigit(text[at - 1]) || text[at - 1] == '_');
            var openAfter = end == text.Length || !char.IsLetterOrDigit(text[end]);

            if (openBefore && openAfter) return true;
        }

        return false;
    }
}
