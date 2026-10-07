using System.Globalization;
using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Integrations;
using Backlog.UI.Components.Tasks;

namespace Backlog.Desktop.UI.InProgress;

/// <summary>
/// What the In progress view and a task's side panel are asked to draw, worked out
/// from the task list, the session record and GitHub's open pull requests.
/// <para>
/// Apart from any rendering so the rules have one home: which entries are in the
/// view, which session and pull request belong to which, what is left over as not
/// linked to a task, and every sentence the cards carry. The library's
/// <see cref="InProgressSections"/> then decides the sections.
/// </para>
/// </summary>
public sealed class InProgressProjection
{
    private readonly IReadOnlyList<EntryRow> _rows;
    private readonly IReadOnlyList<AgentSession> _sessions;
    private readonly IReadOnlyList<GitHubOpenPullRequest> _pulls;
    private readonly Func<string?, string?> _alias;
    private readonly Func<string?, int?> _colour;
    private readonly Func<string, LinkedSessionState?> _linkedState;
    private readonly DateTimeOffset _now;
    private readonly DateOnly _today;
    private readonly bool _sessionsRead;
    private readonly Dictionary<string, AgentSession> _sessionsById;
    private readonly Dictionary<string, GitHubOpenPullRequest> _pullsByKey;
    private readonly HashSet<string> _linkedSessions;
    private readonly HashSet<string> _linkedPulls;

    /// <param name="rows">Every entry the workspace holds — what decides whether a
    /// session or pull request is linked at all.</param>
    /// <param name="sessions">The session record.</param>
    /// <param name="pulls">The open pull requests of every registered repository.</param>
    /// <param name="alias">The configured alias of a repository named <c>owner/name</c>,
    /// or null.</param>
    /// <param name="colour">The repository identity hue, null while colours are hidden.</param>
    /// <param name="linkedState">What the task list last read about a linked session's
    /// state, for a session the record no longer lists.</param>
    /// <param name="now">The moment the sentences are measured against, local.</param>
    /// <param name="sessionsRead">Whether the session record has been read yet. Until
    /// it has, a linked session it does not list is not said to be missing from it.</param>
    public InProgressProjection(
        IReadOnlyList<EntryRow> rows,
        IReadOnlyList<AgentSession> sessions,
        IReadOnlyList<GitHubOpenPullRequest> pulls,
        Func<string?, string?> alias,
        Func<string?, int?> colour,
        Func<string, LinkedSessionState?> linkedState,
        DateTimeOffset now,
        bool sessionsRead = true)
    {
        _sessionsRead = sessionsRead;
        _rows = rows;
        _sessions = sessions;
        _pulls = pulls;
        _alias = alias;
        _colour = colour;
        _linkedState = linkedState;
        _now = now;
        _today = DateOnly.FromDateTime(now.DateTime);

        _sessionsById = new(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions) _sessionsById.TryAdd(session.Id, session);

        _pullsByKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (var pull in pulls) _pullsByKey.TryAdd(Key(pull.RepositoryFullName, pull.Number), pull);

        _linkedSessions = rows.SelectMany(row => row.SessionLinks).Select(link => link.SessionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _linkedPulls = rows.SelectMany(row => row.PullRequestLinks).Select(link => Key(link.Repository, link.Number)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A pull request's key here and on the pull requests list: <c>owner/name#number</c>.</summary>
    public static string Key(string repositoryFullName, int number) =>
        $"{repositoryFullName}#{number.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The entries the view shows: of <paramref name="scopedRows"/> — the task list's
    /// rows under the header's repository scope, in its order — every saved one in
    /// progress, and every one finished today, marked <see cref="InProgressTask.Finished"/>.
    /// </summary>
    public IReadOnlyList<InProgressTask> Tasks(IEnumerable<EntryRow> scopedRows) =>
        [.. scopedRows
            .Where(row => row.IsPersisted && (IsInProgress(row) || IsFinishedToday(row)))
            .Select(Task)];

    /// <summary>One entry as a card: its facts, and the sessions and pull requests it links.</summary>
    public InProgressTask Task(EntryRow row)
    {
        var repository = row.PreviewRepoIds.FirstOrDefault();
        var finished = !IsInProgress(row) && IsFinishedToday(row);

        return new InProgressTask(
            row.Id!.Value.ToString(),
            string.IsNullOrWhiteSpace(row.PreviewTitle) ? "Untitled" : row.PreviewTitle,
            SessionsOf(row),
            PullRequestsOf(row),
            Status: finished ? "Done" : "In progress",
            Plan: PlanOf(row),
            Repository: repository is null ? null : _alias(repository) ?? repository,
            Colour: repository is null ? null : _colour(repository),
            SubItems: row.SubItemCount == 0 ? null : $"{row.CompletedSubItemCount} of {row.SubItemCount} sub-items",
            Started: row.PreviewStartedOn is { } started ? $"Started {Day(started)}" : null,
            Due: !finished && row.PreviewDueOn is { } due ? $"Due {Day(due)}" : null,
            Overdue: !finished && row.PreviewDueOn is { } dueOn && dueOn < _today,
            Effort: row.PreviewEffort is { } points ? (points == 1 ? "1 pt" : $"{points} pts") : null,
            Finished: finished);
    }

    /// <summary>The sessions an entry links, in the order they were linked. A session
    /// the record no longer lists is still drawn, with what the task list last read
    /// about it, so a link never silently disappears.</summary>
    public IReadOnlyList<WorkSession> SessionsOf(EntryRow row) =>
        [.. row.SessionLinks.Select(link => _sessionsById.TryGetValue(link.SessionId, out var session)
            ? Session(session, withRepository: false)
            : new WorkSession(
                link.SessionId,
                $"Session {link.ShortId}",
                _linkedState(link.SessionId) is { } state ? Map(state) : IntegrationSessionState.Unknown,
                _sessionsRead ? "Not in the session record on this machine" : null))];

    /// <summary>The pull requests an entry links. One no longer open is drawn from what
    /// the task list last read about it — merged, closed, or not read yet.</summary>
    public IReadOnlyList<WorkPullRequest> PullRequestsOf(EntryRow row) =>
        [.. row.PullRequestLinks.Select(link => _pullsByKey.TryGetValue(Key(link.Repository, link.Number), out var pull)
            ? PullRequest(pull)
            : Recorded(row, link))];

    /// <summary>
    /// What no entry links: sessions still live — running or gone quiet — and the
    /// reader's own open pull requests, under the header's repository scope
    /// (<paramref name="scope"/> by alias, empty for every repository). Everyone's
    /// pull requests would be every bot's bump in every repository; a person links
    /// what they opened.
    /// </summary>
    public IReadOnlyList<InProgressLoose> Loose(IReadOnlyCollection<string> scope)
    {
        var loose = new List<InProgressLoose>();

        foreach (var session in _sessions)
        {
            if (session.State is AgentSessionState.Finished || _linkedSessions.Contains(session.Id)) continue;

            var repository = RepositoryOf(session);
            if (!InScope(repository, scope)) continue;

            loose.Add(new InProgressLoose(
                Session: Session(session, withRepository: true),
                Repository: repository is null ? null : _alias(repository) ?? repository,
                Colour: repository is null ? null : _colour(repository)));
        }

        foreach (var pull in _pulls)
        {
            if (!pull.IsOpen || !pull.ViewerDidAuthor || _linkedPulls.Contains(Key(pull.RepositoryFullName, pull.Number))) continue;
            if (!InScope(pull.RepositoryFullName, scope)) continue;

            loose.Add(new InProgressLoose(
                PullRequest: PullRequest(pull),
                Repository: _alias(pull.RepositoryFullName) ?? pull.RepositoryFullName,
                Colour: _colour(pull.RepositoryFullName)));
        }

        return loose;
    }

    /// <summary>The session behind a loose card's key, or null.</summary>
    public AgentSession? SessionById(string id) => _sessionsById.GetValueOrDefault(id);

    /// <summary>The pull request behind a loose card's key, or null.</summary>
    public GitHubOpenPullRequest? PullRequestByKey(string key) => _pullsByKey.GetValueOrDefault(key);

    /// <summary>The repository a session is placed in: the one this product resolved
    /// from its folder, else the one it recorded.</summary>
    public static string? RepositoryOf(AgentSession session) =>
        string.IsNullOrWhiteSpace(session.ResolvedRepository)
            ? string.IsNullOrWhiteSpace(session.Repository) ? null : session.Repository
            : session.ResolvedRepository;

    private bool InScope(string? repository, IReadOnlyCollection<string> scope)
    {
        if (scope.Count == 0) return true;
        if (repository is null) return false;

        var alias = _alias(repository);
        return alias is not null && scope.Contains(alias, StringComparer.Ordinal);
    }

    private static bool IsInProgress(EntryRow row) => row.PreviewStatus is EntryStatus.InProgress;

    private bool IsFinishedToday(EntryRow row) =>
        row.PreviewStatus is EntryStatus.Done && row.PreviewCompletedOn == _today;

    private static string? PlanOf(EntryRow row) =>
        row.PreviewTags.FirstOrDefault(tag => tag.StartsWith('+'))
        ?? (string.IsNullOrWhiteSpace(row.ImportPlanId) ? null : "+" + row.ImportPlanId.TrimStart('+'));

    private string Day(DateOnly day) =>
        day == _today ? "today"
        : day == _today.AddDays(-1) ? "yesterday"
        : day == _today.AddDays(1) ? "tomorrow"
        : day.ToString("MMM d", CultureInfo.InvariantCulture);

    private WorkSession Session(AgentSession session, bool withRepository)
    {
        var parts = new List<string>(3);

        if (withRepository && RepositoryOf(session) is { } repository) parts.Add(_alias(repository) ?? repository);
        if (session.TurnCount is { } turns) parts.Add(turns == 1 ? "1 prompt" : $"{turns} prompts");
        parts.Add(session.State is AgentSessionState.Finished ? $"Ended {Ago(session.LastActivityAt)}" : $"Last activity {Ago(session.LastActivityAt)}");

        return new WorkSession(
            session.Id,
            string.IsNullOrWhiteSpace(session.Title) ? $"Session {Short(session.Id)}" : session.Title,
            Map(session.State),
            string.Join(" · ", parts),
            session.Kind is AgentSessionKind.Copilot ? IntegrationProvider.Copilot : IntegrationProvider.Claude);
    }

    private static WorkPullRequest PullRequest(GitHubOpenPullRequest pull) =>
        new(
            Key(pull.RepositoryFullName, pull.Number),
            pull.Number,
            pull.Title,
            pull.IsDraft ? IntegrationArtifactState.Draft : IntegrationArtifactState.Open,
            Map(pull.ReviewState),
            Map(pull.Checks),
            ChecksText(pull.CheckCounts),
            pull.Additions,
            pull.Deletions);

    /// <summary>A linked pull request the open read did not list: drawn from the task
    /// list's own read of it, or as unknown when it has not read one.</summary>
    private static WorkPullRequest Recorded(EntryRow row, EntryPullRequestLink link)
    {
        var key = Key(link.Repository, link.Number);
        var title = $"Pull request in {link.Repository}";

        if (row.PullRequestStatuses.TryGetValue(link, out var status))
        {
            return new WorkPullRequest(key, link.Number, title, Map(status.State), Map(status.ReviewState), Map(status.Checks));
        }

        return row.PullRequestStates.TryGetValue(link, out var state)
            ? new WorkPullRequest(key, link.Number, title, Map(state))
            : new WorkPullRequest(key, link.Number, title, IntegrationArtifactState.Unknown);
    }

    /// <summary>"Checks running 4/7", "1 check failing", or null to let the card word
    /// the state itself.</summary>
    private static string? ChecksText(GitHubCheckCounts? counts)
    {
        if (counts is not { Total: > 0 }) return null;
        if (counts.Failed > 0) return counts.Failed == 1 ? "1 check failing" : $"{counts.Failed} checks failing";
        if (counts.Pending > 0) return $"Checks running {counts.Passed}/{counts.Total}";
        return null;
    }

    private string Ago(DateTimeOffset moment)
    {
        var elapsed = _now - moment;

        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes} min ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{(int)elapsed.TotalHours} h ago";

        var days = (int)elapsed.TotalDays;
        return days == 1 ? "yesterday" : $"{days} days ago";
    }

    private static string Short(string id) => id.Length <= 8 ? id : id[..8];

    private static IntegrationSessionState Map(AgentSessionState state) => state switch
    {
        AgentSessionState.Running => IntegrationSessionState.Running,
        AgentSessionState.Stalled => IntegrationSessionState.Stalled,
        _ => IntegrationSessionState.Finished
    };

    private static IntegrationSessionState Map(LinkedSessionState state) => state switch
    {
        LinkedSessionState.Running => IntegrationSessionState.Running,
        LinkedSessionState.Stalled => IntegrationSessionState.Stalled,
        _ => IntegrationSessionState.Finished
    };

    private static IntegrationArtifactState Map(GitHubItemState state) => state switch
    {
        GitHubItemState.Draft => IntegrationArtifactState.Draft,
        GitHubItemState.Open => IntegrationArtifactState.Open,
        GitHubItemState.Merged => IntegrationArtifactState.Merged,
        GitHubItemState.Closed => IntegrationArtifactState.Closed,
        _ => IntegrationArtifactState.Unknown
    };

    private static IntegrationReviewState Map(GitHubReviewState review) => review switch
    {
        GitHubReviewState.Approved => IntegrationReviewState.Approved,
        GitHubReviewState.ChangesRequested => IntegrationReviewState.ChangesRequested,
        GitHubReviewState.ReviewRequired => IntegrationReviewState.ReviewRequired,
        _ => IntegrationReviewState.None
    };

    private static IntegrationCheckState Map(GitHubCheckState checks) => checks switch
    {
        GitHubCheckState.Passing => IntegrationCheckState.Passing,
        GitHubCheckState.Failing => IntegrationCheckState.Failing,
        GitHubCheckState.Pending => IntegrationCheckState.Pending,
        _ => IntegrationCheckState.None
    };
}
