namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>A task as another screen hands it back: the entry's id to open it by, and
/// the title the Tasks list gives it — the first block of its id where it has none.</summary>
public sealed record TaskReference(Guid EntryId, string Title);

/// <summary>A pull request a task recorded, by <c>owner/name</c> and number.</summary>
public sealed record TaskPullRequestLink(string RepositoryFullName, int Number);

/// <summary>An issue by its repository's <c>owner/name</c> and its number: the GitHub
/// issue a linked task follows, or one a pull request closes.</summary>
public sealed record TaskIssueReference(string RepositoryFullName, int Number);

/// <summary>
/// One pull request, open or merged, as the lookup asks about it: what a screen that
/// lists pull requests knows of one, in Tasks' own words, so Tasks never names
/// Repository Management's types (local ADR 0042 §4).
/// </summary>
/// <param name="ClosingIssues">The issues the pull request says it closes.</param>
/// <param name="SessionId">The session the asking screen matched the pull request to,
/// or null.</param>
public sealed record PullRequestTaskQuestion(
    string RepositoryFullName,
    int Number,
    string Title,
    string HeadRefName,
    IReadOnlyList<TaskIssueReference> ClosingIssues,
    string? SessionId);

/// <summary>
/// Which task a session or a pull request belongs to, and which pull requests the
/// tasks link — asked by the shell's other screens, which list sessions and pull
/// requests but hold no task rows of their own (local ADR 0042, V8).
/// <para>
/// Answered synchronously over the window's Tasks rows: the screens ask once per cell
/// they draw, so the lookup matches over plain values the Tasks state already holds and
/// never reads the store.
/// </para>
/// </summary>
public interface ITaskLookup
{
    /// <summary>The task that linked the session — the entry's own record of the session
    /// it was worked in — or null.</summary>
    TaskReference? TaskForSession(string sessionId);

    /// <summary>
    /// The task the pull request belongs to, in order of how sure each relation is: the
    /// task's own record of the pull request; a GitHub issue the task follows that the
    /// pull request closes; the task's external key as a whole token in the pull
    /// request's title or head branch; and the task that linked the session the pull
    /// request came out of. Null when nothing relates.
    /// </summary>
    /// <param name="mineOnly">Consider only the tasks that count as mine, for a "Mine"
    /// view: a surer match on somebody else's task must not hide one on mine.</param>
    TaskReference? TaskForPullRequest(PullRequestTaskQuestion pullRequest, bool mineOnly = false);

    /// <summary>Every pull request the tasks link, each once — the repository compared
    /// without regard to case — in the order the tasks name them.</summary>
    IReadOnlyList<TaskPullRequestLink> LinkedPullRequests();
}

/// <summary>
/// One task as the lookup matches over it: plain values read off the window's task
/// rows.
/// </summary>
/// <param name="EntryId">The entry to open.</param>
/// <param name="Title">What the answer calls the task.</param>
/// <param name="PullRequests">The entry's <c>pull-request</c> projections.</param>
/// <param name="LinkedIssue">The GitHub issue a GitHub-connector linked task follows,
/// or null.</param>
/// <param name="DisplayKey">The linked task's display key, whose parts after the first
/// <c>·</c> name its keys in another system (Jira's <c>FIN-8428</c> in
/// <c>#123 · FIN-8428</c>), or null.</param>
/// <param name="SessionIds">The sessions the entry linked.</param>
/// <param name="IsMine">Whether the task counts as one of mine for a "Mine" view.</param>
public sealed record TaskLookupCandidate(
    Guid EntryId,
    string Title,
    IReadOnlyList<TaskPullRequestLink> PullRequests,
    TaskIssueReference? LinkedIssue,
    string? DisplayKey,
    IReadOnlyList<string> SessionIds,
    bool IsMine);

/// <summary>
/// What <see cref="ITaskLookup"/> matches over: the window's task rows as plain values,
/// answered by the window's Tasks state.
/// </summary>
public interface ITaskLookupCandidates
{
    /// <summary>Every task, each once. The same list instance until the rows change, so
    /// a lookup asked once per drawn cell matches over a list it built once.</summary>
    IReadOnlyList<TaskLookupCandidate> Candidates { get; }
}
