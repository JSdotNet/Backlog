using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Modules.Tasks.Services;

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

/// <summary>A task a pull request belongs to, and how that was found.</summary>
internal sealed record PullRequestTaskMatch(TaskLookupCandidate Candidate, PullRequestTaskRelation Relation);

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
/// <para>
/// Pure, over one list of candidates: <see cref="TaskLookup"/> builds one per list the
/// Tasks state hands it, so a screen asking once per drawn cell reads every display
/// key once.
/// </para>
/// </summary>
internal sealed class PullRequestTaskMatching
{
    private readonly IReadOnlyList<(TaskLookupCandidate Candidate, IReadOnlyList<string> ExternalKeys)> _candidates;

    public PullRequestTaskMatching(IEnumerable<TaskLookupCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        _candidates = [.. candidates.Select(candidate => (candidate, ExternalKeysOf(candidate.DisplayKey)))];
    }

    /// <param name="mineOnly">Consider only the tasks that count as mine, for the
    /// "Mine" view: a surer match on somebody else's task must not hide one on
    /// mine.</param>
    public PullRequestTaskMatch? Match(PullRequestTaskQuestion pull, bool mineOnly = false)
    {
        ArgumentNullException.ThrowIfNull(pull);

        var pool = mineOnly ? [.. _candidates.Where(entry => entry.Candidate.IsMine)] : _candidates;

        return First(pool, PullRequestTaskRelation.Recorded, entry => entry.Candidate.PullRequests.Any(recorded =>
                   recorded.Number == pull.Number && SameRepository(recorded.RepositoryFullName, pull.RepositoryFullName)))
            ?? First(pool, PullRequestTaskRelation.ClosesIssue, entry => entry.Candidate.LinkedIssue is { } issue
                   && pull.ClosingIssues.Any(closing =>
                       closing.Number == issue.Number && SameRepository(closing.RepositoryFullName, issue.RepositoryFullName)))
            ?? First(pool, PullRequestTaskRelation.ExternalKey, entry => entry.ExternalKeys.Any(key =>
                   ContainsToken(pull.Title, key) || ContainsToken(pull.HeadRefName, key)))
            ?? First(pool, PullRequestTaskRelation.Session, entry => pull.SessionId is { Length: > 0 } session
                   && entry.Candidate.SessionIds.Contains(session, StringComparer.OrdinalIgnoreCase));
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

    /// <summary>Whether two <c>owner/name</c> spellings name one repository, the way
    /// GitHub compares them.</summary>
    public static bool SameRepository(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static PullRequestTaskMatch? First(
        IReadOnlyList<(TaskLookupCandidate Candidate, IReadOnlyList<string> ExternalKeys)> pool,
        PullRequestTaskRelation relation,
        Func<(TaskLookupCandidate Candidate, IReadOnlyList<string> ExternalKeys), bool> relates)
    {
        foreach (var entry in pool)
        {
            if (relates(entry)) return new PullRequestTaskMatch(entry.Candidate, relation);
        }

        return null;
    }

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
