using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Which repository each notification is about, worked out at read time from what its producer
/// left on it — the notification carries no repository of its own (no change to the
/// Notifications model, <c>.devbook/domain/notifications/domain.md#origin</c>). The centre's
/// scope and its Group by Repository read the answer through <c>NotificationsSurfaceWiring</c>.
/// <list type="bullet">
/// <item>A run's Personal Validation: the worktree its source key names, placed in a
/// registered clone; else the repository at the head of its origin's place.</item>
/// <item>A question about a session — the relay's, an MCP <c>ask</c> or <c>notify</c> from a
/// session: the session's resolved, else recorded, repository.</item>
/// <item>A pull request's: the repository its subject names.</item>
/// <item>A spec-manager question on a linked task: the task's repository. Any other
/// spec-manager notification: its product's connected target's repository, the product read
/// from the source key or the head of the place, matched against the target either way.</item>
/// <item>A Devbook remark: the repository its chapter link names.</item>
/// <item>Anything else — pairing, the settings test, an MCP call from no session — names no
/// repository and is the Brain's.</item>
/// </list>
/// Each answer is the configured alias the header scope is written in, or null for the Brain's —
/// a repository this workspace does not know included. A notification whose source could not be
/// read (the session catalog failed, or no longer holds its session) is left out of the answer,
/// so the centre shows it under every scope rather than hiding it.
/// </summary>
/// <param name="aliasOf">The configured alias for a repository named as <c>owner/name</c> or by
/// its alias, archived ones included; null for one this workspace does not know.</param>
/// <param name="sessions">The session catalog; null leaves session questions unresolved.</param>
/// <param name="worktrees">Which registered clone a working folder lies in.</param>
/// <param name="taskRepository">Where a Backlog entry is filed, by its id — see
/// <see cref="TaskFiling"/>. Called on the caller's thread only.</param>
/// <param name="targets">The connected targets, for a spec-manager product's repository.</param>
public sealed class NotificationRepositories(
    Func<string?, string?> aliasOf,
    IAgentSessionSource? sessions = null,
    ISessionRepositoryResolver? worktrees = null,
    Func<Guid, TaskFiling>? taskRepository = null,
    IConnectedTargets? targets = null)
{
    // The connector id and the notification source context spec-manager uses are both the
    // system's own name, which the Notifications vocabulary already spells.
    private const string SpecManager = NotificationOrigins.SpecManager;

    private readonly Func<string?, string?> _aliasOf = aliasOf ?? throw new ArgumentNullException(nameof(aliasOf));

    /// <summary>The repository of each of <paramref name="notifications"/> that could be worked
    /// out, by id: its alias, or null for the Brain's. The session catalog is read at most once,
    /// and only when a notification is about a session.</summary>
    public async Task<IReadOnlyDictionary<Guid, string?>> ResolveAsync(
        IReadOnlyList<NotificationView> notifications,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        // Everything that is not about a session is worked out before the first await, on the
        // caller's thread: the task lookup reads the window's live rows, which only that thread
        // may touch.
        var resolved = new Dictionary<Guid, string?>();
        var aboutSessions = new List<NotificationView>();
        foreach (var notification in notifications)
        {
            if (IsAboutSession(notification)) aboutSessions.Add(notification);
            else if (Resolve(notification, catalog: null) is { } answer) resolved[notification.Id] = answer.Alias;
        }

        if (aboutSessions.Count == 0) return resolved;

        var catalog = await SessionsAsync(cancellationToken);
        foreach (var notification in aboutSessions)
        {
            if (Resolve(notification, catalog) is { } answer) resolved[notification.Id] = answer.Alias;
        }

        return resolved;
    }

    /// <summary>The answer for one notification, or null when it could not be worked out.</summary>
    private Answer? Resolve(NotificationView notification, IReadOnlyDictionary<string, AgentSession>? catalog)
    {
        var subject = notification.Subject;

        if (subject is { Context: SessionNotificationSubjects.Context })
        {
            switch (subject.Type)
            {
                case SessionNotificationSubjects.Run:
                    return new Answer(OfRun(notification));
                case SessionNotificationSubjects.PullRequest:
                    return new Answer(_aliasOf(SessionNotificationSubjects.ParsePullRequestId(subject.Id)?.Repository));
                case SessionNotificationSubjects.Session:
                    return catalog is not null && catalog.TryGetValue(subject.Id, out var session)
                        ? new Answer(_aliasOf(RepositoryOf(session)))
                        : null;
            }
        }

        if (subject is { Context: TaskNotificationSubjects.Context, Type: TaskNotificationSubjects.Task }
            && Guid.TryParse(subject.Id, out var taskId)
            && taskRepository is not null)
        {
            var filing = taskRepository(taskId);

            // Before the task list is read no answer is kept, so the next load asks again.
            if (!filing.ListRead) return null;

            // A task filed under no repository is the Brain's, whatever its product says.
            if (filing.Held) return new Answer(_aliasOf(filing.Repository));
        }

        if (string.Equals(notification.Source.Context, SpecManager, StringComparison.Ordinal))
        {
            return new Answer(_aliasOf(ProductRepository(notification)));
        }

        if (DevbookNotificationSubjects.ParseChapterLink(notification.DeepLink) is { } chapter)
        {
            return new Answer(_aliasOf(chapter.RepositoryAlias));
        }

        return new Answer(null);
    }

    /// <summary>The worktree's clone, else the repository the run's place starts with.</summary>
    private string? OfRun(NotificationView notification)
    {
        if (SessionNotificationSubjects.ParseValidationSourceKey(notification.Source.Key) is { } run
            && worktrees?.RepositoryOf(run.Worktree) is { } cloned
            && _aliasOf(cloned) is { } alias)
        {
            return alias;
        }

        return _aliasOf(PlaceHead(notification));
    }

    /// <summary>
    /// The repository a spec-manager product's connected target is filed under. The product is
    /// read from the source key (<c>{product}/{question}</c>) and from the head of the place,
    /// which carries the slug for a question and the product's name for its other
    /// notifications, so either one matching a target's slug finds it.
    /// </summary>
    private string? ProductRepository(NotificationView notification)
    {
        if (targets is null) return null;

        var key = notification.Source.Key;
        var slash = key.LastIndexOf('/');
        string?[] products = [slash > 0 ? key[..slash] : null, PlaceHead(notification)];

        var connected = targets.List().Where(target => string.Equals(target.ConnectorId, SpecManager, StringComparison.Ordinal)).ToList();

        return products
            .Where(product => !string.IsNullOrWhiteSpace(product))
            .Select(product => connected.FirstOrDefault(target => string.Equals(target.Target.Trim(), product!.Trim(), StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(target => target is not null)
            ?.RepositoryId;
    }

    private static string? PlaceHead(NotificationView notification)
    {
        var place = notification.Origin?.Place;
        if (string.IsNullOrWhiteSpace(place)) return null;

        var separator = place.IndexOf(NotificationOrigin.Separator, StringComparison.Ordinal);
        return (separator < 0 ? place : place[..separator]).Trim();
    }

    /// <summary>The session's resolved repository, else the one it recorded.</summary>
    private static string? RepositoryOf(AgentSession session) =>
        string.IsNullOrWhiteSpace(session.ResolvedRepository) ? session.Repository : session.ResolvedRepository;

    private static bool IsAboutSession(NotificationView notification) =>
        notification.Subject is { Context: SessionNotificationSubjects.Context, Type: SessionNotificationSubjects.Session };

    /// <summary>The catalog by session id, or null when there is none or it could not be read —
    /// its notifications are then left unresolved, never guessed into the Brain.</summary>
    private async Task<IReadOnlyDictionary<string, AgentSession>?> SessionsAsync(CancellationToken cancellationToken)
    {
        if (sessions is null) return null;

        try
        {
            var catalog = await sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
            var byId = new Dictionary<string, AgentSession>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in catalog.Sessions) byId.TryAdd(session.Id, session);
            return byId;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>A worked-out answer: the alias, or null for the Brain's.</summary>
    private readonly record struct Answer(string? Alias);
}
