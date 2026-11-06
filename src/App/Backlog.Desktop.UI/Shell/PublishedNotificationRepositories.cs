using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The repository each notification this desktop pushes is about (local ADR 0039 §6), worked out
/// by <see cref="NotificationRepositories"/> — the rule the centre's header scope keeps — for the
/// background exchange rather than for a window.
/// <para>
/// The centre's resolution reads the window's live task rows on the window's thread; the push
/// runs on no window's thread, so this reads a linked task's filing through
/// <paramref name="taskFiling"/> first, ahead of the resolution, and hands it the answers. A
/// task that could not be read leaves its notification not known, never guessed into the Brain.
/// </para>
/// </summary>
/// <param name="aliasOf">The configured alias for a repository named as <c>owner/name</c> or by
/// its alias; null for one this workspace does not know.</param>
/// <param name="sessions">The session catalog, asked for on each call; null leaves session
/// questions not known.</param>
/// <param name="worktrees">Which registered clone a working folder lies in, asked for on each call.</param>
/// <param name="taskFiling">Where a Backlog entry is filed, by its id; null leaves a task's
/// notification to the rules that need no task.</param>
/// <param name="targets">The connected targets, asked for on each call.</param>
public sealed class PublishedNotificationRepositories(
    Func<string?, string?> aliasOf,
    Func<IAgentSessionSource?>? sessions = null,
    Func<ISessionRepositoryResolver?>? worktrees = null,
    Func<Guid, CancellationToken, Task<TaskFiling>>? taskFiling = null,
    Func<IConnectedTargets?>? targets = null) : INotificationRepositorySource
{
    private readonly Func<string?, string?> _aliasOf = aliasOf ?? throw new ArgumentNullException(nameof(aliasOf));

    /// <summary>
    /// The resolution as the desktop composes it over its container, a singleton: the alias
    /// from the repository directory, the session catalog, the clones and the connected targets
    /// asked for on each push, and a linked task read through a scope of its own, since the
    /// task module is scoped. No task module leaves the task not read, so its notification is
    /// not known; a task the backlog does not hold goes to the other rules; a held one is filed
    /// under its first repository.
    /// </summary>
    public static PublishedNotificationRepositories Compose(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new PublishedNotificationRepositories(
            repository => string.IsNullOrWhiteSpace(repository)
                ? null
                : services.GetService<IRepositoryDirectory>()?.Resolve(repository)?.Alias,
            services.GetService<IAgentSessionSource>,
            services.GetService<ISessionRepositoryResolver>,
            async (taskId, cancellationToken) =>
            {
                await using var scope = services.CreateAsyncScope();
                if (scope.ServiceProvider.GetService<ITaskItems>() is not { } tasks) return TaskFiling.NotRead;

                return await tasks.GetAsync(taskId, cancellationToken).ConfigureAwait(false) is { } entry
                    ? TaskFiling.Under(entry.RepoIds?.FirstOrDefault())
                    : TaskFiling.NotHeld;
            },
            services.GetService<IConnectedTargets>);
    }

    public async Task<IReadOnlyDictionary<Guid, string?>> ResolveAsync(
        IReadOnlyList<NotificationSnapshot> notifications,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        var filings = await FilingsAsync(notifications, cancellationToken).ConfigureAwait(false);

        var resolution = new NotificationRepositories(
            _aliasOf,
            sessions?.Invoke(),
            worktrees?.Invoke(),
            taskFiling is null ? null : id => filings.GetValueOrDefault(id, TaskFiling.NotRead),
            targets?.Invoke());

        return await resolution.ResolveAsync([.. notifications.Select(ViewOf)], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The filing of each task a notification is about, read before the resolution
    /// runs; a read that fails leaves its task not read.</summary>
    private async Task<Dictionary<Guid, TaskFiling>> FilingsAsync(
        IReadOnlyList<NotificationSnapshot> notifications,
        CancellationToken cancellationToken)
    {
        var filings = new Dictionary<Guid, TaskFiling>();
        if (taskFiling is null) return filings;

        foreach (var notification in notifications)
        {
            if (notification.Subject is not { Context: TaskNotificationSubjects.Context, Type: TaskNotificationSubjects.Task } subject
                || !Guid.TryParse(subject.Id, out var taskId)
                || filings.ContainsKey(taskId))
            {
                continue;
            }

            try
            {
                filings[taskId] = await taskFiling(taskId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                filings[taskId] = TaskFiling.NotRead;
            }
        }

        return filings;
    }

    private static NotificationView ViewOf(NotificationSnapshot snapshot) =>
        new(
            snapshot.Id,
            snapshot.OwnerInstanceId,
            snapshot.Source,
            snapshot.Subject,
            snapshot.Kind,
            snapshot.Severity,
            snapshot.Title,
            snapshot.Summary,
            snapshot.BodyMarkdown,
            snapshot.AnswerSpec,
            snapshot.DeepLink,
            snapshot.IsPrivate,
            snapshot.CreatedAt,
            snapshot.State,
            snapshot.SettledAt,
            snapshot.CloseReason,
            snapshot.Answer,
            snapshot.Origin);
}
