using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The Sessions context telling Notifications that one of its subjects ended
/// (<c>.devbook/domain/notifications/domain.md#subject</c>): a session archived or deleted,
/// a delivery run finished or abandoned. Notifications reads none of this context's data;
/// the end is pushed from here, where it is noticed, and the startup probe
/// (<see cref="SessionSubjectProbe"/>) catches what was missed while the app was closed.
/// <para>
/// A close never fails the reading or the write that noticed the end. Notifications being
/// absent, a viewer instance refusing, the store throwing, or the caller cancelling once
/// the write has landed is logged and dropped: the next start's probe asks again.
/// </para>
/// </summary>
internal static class SessionSubjectEnds
{
    internal static NotificationSubject Session(string id) =>
        new(SessionNotificationSubjects.Context, SessionNotificationSubjects.Session, id);

    internal static NotificationSubject Run(string runId) =>
        new(SessionNotificationSubjects.Context, SessionNotificationSubjects.Run, runId);

    internal static async Task CloseAsync(
        Func<INotifications?>? notifications,
        NotificationSubject subject,
        string reason,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (notifications is null) return;

        try
        {
            if (notifications() is not { } port) return;

            var closed = await port.CloseForSubjectAsync(subject, reason, cancellationToken).ConfigureAwait(false);

            if (closed.IsFailure)
            {
                logger.LogDebug("Notifications about {Type} {Id} were not closed: {Error}", subject.Type, subject.Id, closed.Error.Message);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Closing the notifications about {Type} {Id} failed; the next start's probe asks again.", subject.Type, subject.Id);
        }
    }
}
