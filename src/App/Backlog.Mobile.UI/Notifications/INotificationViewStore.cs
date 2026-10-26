using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Notifications;

/// <summary>
/// The phone's <c>notification_view</c>: the notifications the feed brought, as the feed
/// carried them, the cursor the next pull resumes from, and the replies this phone made — the
/// one thing the phone writes about a notification, kept so it can tell its own answer from one
/// made elsewhere after the reply has left the outbox.
/// </summary>
public interface INotificationViewStore
{
    /// <summary>Every notification kept, one record per id. Synchronous, for the reason the
    /// task view's read is: a first render cannot await.</summary>
    IReadOnlyList<NotificationChangeRecord> ReadRows();

    /// <summary>Where the next pull starts, or null for the beginning.</summary>
    string? ReadCursor();

    /// <summary>Writes <paramref name="rows"/> over whatever is kept for their ids and, when
    /// <paramref name="cursor"/> is given, moves the cursor, in one transaction.</summary>
    Task SaveAsync(IReadOnlyList<NotificationChangeRecord> rows, string? cursor = null, CancellationToken cancellationToken = default);

    /// <summary>Forgets the cursor, so the next pull starts from the beginning.</summary>
    Task ResetCursorAsync(CancellationToken cancellationToken = default);

    /// <summary>Every reply this phone made, oldest first.</summary>
    IReadOnlyList<NotificationReplyPost> ReadSentReplies();

    /// <summary>Keeps a reply this phone is about to send.</summary>
    Task AddSentReplyAsync(NotificationReplyPost reply, CancellationToken cancellationToken = default);
}
