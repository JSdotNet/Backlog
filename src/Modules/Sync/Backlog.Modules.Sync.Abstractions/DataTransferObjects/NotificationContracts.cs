using System.Text.Json;

namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// One notification as its owner instance pushes it (local ADR 0039): the envelope the
/// sync service reads, and the notification itself as an opaque document it stores and
/// hands back unread.
/// <para>
/// The service reads only <see cref="Id"/>, <see cref="OwnerInstanceId"/>,
/// <see cref="State"/> and <see cref="Preview"/>. The state is there because retention is a
/// store setting: a notification that has left Open is stamped to expire 30 days later. The
/// preview is there because the Android push says what a notification is about before the
/// phone has pulled it, and the owner writes it beside the document rather than the service
/// reading it out of one. Everything a viewer
/// shows — title, summary, body, the answer spec, the answer, which replies were rejected —
/// travels inside <see cref="Notification"/>, whose shape belongs to the Notifications
/// context and not to Sync (<c>.devbook/domain/notifications/context.md</c>: Sync stores the
/// documents and reads none of their fields).
/// </para>
/// <para>
/// No owner and no device id, for the reason <see cref="TaskChange"/> gives: both come from
/// the caller's validated token. <see cref="OwnerInstanceId"/> is the publishing desktop's
/// installation identity (local ADR 0039 §1): a notification already stored is written again
/// only by a push naming the same instance, and a reply names it to say which desktop it is
/// addressed to. The instance, not the pairing, is the owner, so a desktop that pairs again
/// keeps its notifications and the replies to them.
/// </para>
/// </summary>
/// <param name="Id">The notification's own id.</param>
/// <param name="OwnerInstanceId">The installation that published it, its only writer.</param>
/// <param name="State">Where it stands — one of <see cref="NotificationStates"/>.</param>
/// <param name="UpdatedAt">When the owner last changed it, on the owner's clock.</param>
/// <param name="Notification">The notification itself, stored whole and never read here.</param>
/// <param name="Preview">What the Android push may say about it (ADR 0039 §4) — the
/// envelope's own copy of kind, severity, title and summary, so the service never reads the
/// document to send one. Null from an owner that predates it; the push then carries the id and
/// the state only. Used for the send and not stored.</param>
public sealed record NotificationChange(
    Guid Id,
    Guid OwnerInstanceId,
    string State,
    DateTimeOffset UpdatedAt,
    JsonElement Notification,
    NotificationPushPreview? Preview = null);

/// <summary>The states a <see cref="NotificationChange"/> may name. Only
/// <see cref="Open"/> means something to the service: every other state starts the
/// 30-day expiry.</summary>
public static class NotificationStates
{
    public const string Open = "open";
    public const string Acknowledged = "acknowledged";
    public const string Answered = "answered";
    public const string Closed = "closed";

    /// <summary>Every state a push may carry.</summary>
    public static IReadOnlyList<string> All { get; } = [Open, Acknowledged, Answered, Closed];

    /// <summary>Whether <paramref name="state"/> is one of <see cref="All"/>, compared ordinally.</summary>
    public static bool IsKnown(string? state) => state is not null && All.Contains(state, StringComparer.Ordinal);
}

/// <summary>A notification as it comes back out of the feed: the change, the sync device
/// that wrote it, and the store's ordering stamp — <see cref="TaskChangeRecord"/>'s shape.</summary>
public sealed record NotificationChangeRecord(NotificationChange Change, Guid DeviceId, long ServerTimestamp);

/// <summary>A batch of notifications from their owner instance.</summary>
public sealed record PushNotificationsRequest(IReadOnlyList<NotificationChange> Notifications);

/// <summary>How many notifications the replica took, and which it refused because they
/// belong to another owner instance — one writer per notification (ADR 0039 §1). The rest of
/// the batch is stored either way, so a refused id never holds the others back.</summary>
public sealed record PushNotificationsResponse(int Accepted, IReadOnlyList<Guid> NotOwned);

/// <summary>One page of the owner's notification feed, and where to resume — the contract
/// of <see cref="PullTasksResponse"/>.</summary>
public sealed record PullNotificationsResponse(IReadOnlyList<NotificationChangeRecord> Notifications, string Since, bool HasMore);

/// <summary>
/// A reply a viewer posts to a question (<c>.devbook/domain/notifications/domain.md#reply</c>):
/// written once, never edited, and a proposal until the owner instance accepts it.
/// <para>
/// No owner and no sending device: the service stamps both from the token, which is what
/// makes "a reply comes from a device under the same owner" true by construction.
/// </para>
/// </summary>
/// <param name="Id">The reply's own id, minted by the device. Posting the same id twice
/// stores it once.</param>
/// <param name="NotificationId">The question it answers.</param>
/// <param name="OwnerInstanceId">The owner instance it is addressed to — the
/// notification's <see cref="NotificationChange.OwnerInstanceId"/>.</param>
/// <param name="ChoiceIds">The chosen choice ids; empty for an open answer.</param>
/// <param name="Text">The open answer, or null. Plain text, never logged.</param>
/// <param name="RepliedAt">When the person replied, on the replying device's clock.</param>
/// <param name="Note">The note written beside the answer, or null. Plain text, never logged.</param>
public sealed record NotificationReplyPost(
    Guid Id,
    Guid NotificationId,
    Guid OwnerInstanceId,
    IReadOnlyList<string> ChoiceIds,
    string? Text,
    DateTimeOffset RepliedAt,
    string? Note = null);

/// <summary>
/// A stored reply as the owner instance pulls it: what the viewer posted, plus the owner
/// and the sync device the service stamped from the poster's token.
/// </summary>
/// <param name="Reply">What the viewer posted.</param>
/// <param name="OwnerId">The account the reply was posted under — the owner checks it
/// against its own before it accepts (<c>reply.foreign_owner</c>).</param>
/// <param name="FromDeviceId">The sync device the person replied on.</param>
/// <param name="ServerTimestamp">The store's own write stamp: the order replies are
/// handed out in, which is the order the owner processes them.</param>
public sealed record NotificationReplyRecord(
    NotificationReplyPost Reply,
    Guid OwnerId,
    Guid FromDeviceId,
    long ServerTimestamp);

/// <summary>Whether the reply was stored now, or had been already.</summary>
public sealed record PostNotificationReplyResponse(Guid Id, bool Stored);

/// <summary>The replies waiting for one owner instance, oldest first.</summary>
public sealed record PullNotificationRepliesResponse(IReadOnlyList<NotificationReplyRecord> Replies);

/// <summary>
/// The bounds a notification and a reply are held to on the wire, in one place so the
/// desktop can run the service's rule before it pushes.
/// </summary>
public static class NotificationRecordLimits
{
    /// <summary>The most a notification document may weigh as serialized JSON: the 32 KB
    /// body bound (<c>domain.invariants.md</c>) with room for the title, the summary, the
    /// answer spec and the reply outcomes around it.</summary>
    public const int DocumentBytes = 64 * 1024;

    /// <summary>The most choices one reply may pick — the most a question offers.</summary>
    public const int ReplyChoices = 8;

    /// <summary>How long one choice id may be.</summary>
    public const int ChoiceIdLength = 100;

    /// <summary>How long an open answer may be on the wire. A question's own limit is
    /// checked by the owner; this bounds what reaches the store.</summary>
    public const int ReplyTextLength = 8_000;

    /// <summary>How long a note beside an answer may be on the wire. A question's own note
    /// limit is checked by the owner; this bounds what reaches the store.</summary>
    public const int ReplyNoteLength = 8_000;

    /// <summary>How long a push preview's title may be — the notification's own title bound
    /// (<c>domain.md</c>).</summary>
    public const int PreviewTitleLength = 80;

    /// <summary>How long a push preview's summary may be — the notification's own summary
    /// bound.</summary>
    public const int PreviewSummaryLength = 180;

    /// <summary>How long a push preview's origin may be: "System · Place", cut to fit a tray
    /// subtitle.</summary>
    public const int PreviewOriginLength = 60;

    /// <summary>Why <paramref name="change"/> would be refused, or null when it would be
    /// stored.</summary>
    public static string? WhyRefused(NotificationChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Id == Guid.Empty) return "A notification needs an id.";
        if (change.OwnerInstanceId == Guid.Empty) return "A notification names its owner instance.";
        if (!NotificationStates.IsKnown(change.State)) return $"'{change.State}' is not a notification state.";
        if (change.Notification.ValueKind is not JsonValueKind.Object) return "A notification document is a JSON object.";
        if (change.Notification.GetRawText().Length > DocumentBytes)
            return $"A notification document holds at most {DocumentBytes} characters.";

        return change.Preview is { } preview ? WhyRefused(preview) : null;
    }

    /// <summary>Why <paramref name="preview"/> would be refused, or null when it would be
    /// sent.</summary>
    public static string? WhyRefused(NotificationPushPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (!NotificationKinds.IsKnown(preview.Kind)) return $"'{preview.Kind}' is not a notification kind.";
        if (!NotificationSeverities.IsKnown(preview.Severity)) return $"'{preview.Severity}' is not a notification severity.";
        if (string.IsNullOrWhiteSpace(preview.Title) || preview.Title.Length > PreviewTitleLength)
            return $"A push preview's title is between 1 and {PreviewTitleLength} characters.";
        if (preview.Summary is { Length: > PreviewSummaryLength })
            return $"A push preview's summary holds at most {PreviewSummaryLength} characters.";
        if (preview.Origin is { Length: > PreviewOriginLength })
            return $"A push preview's origin holds at most {PreviewOriginLength} characters.";

        return null;
    }

    /// <summary>Why <paramref name="reply"/> would be refused, or null when it would be
    /// stored.</summary>
    public static string? WhyRefused(NotificationReplyPost reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        if (reply.Id == Guid.Empty) return "A reply needs an id.";
        if (reply.NotificationId == Guid.Empty) return "A reply names the notification it answers.";
        if (reply.OwnerInstanceId == Guid.Empty) return "A reply names the owner instance it is addressed to.";

        var choices = reply.ChoiceIds ?? [];
        if (choices.Count > ReplyChoices) return $"A reply picks at most {ReplyChoices} choices.";
        if (choices.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > ChoiceIdLength))
            return $"A choice id is between 1 and {ChoiceIdLength} characters.";
        if (reply.Text is { Length: > ReplyTextLength }) return $"An open answer holds at most {ReplyTextLength} characters.";
        if (reply.Note is { Length: > ReplyNoteLength }) return $"A note holds at most {ReplyNoteLength} characters.";

        // An empty reply is not refused here: a multiple-choice question with no minimum
        // takes one, and whether it fits is the owner's call against the answer spec.
        return null;
    }
}
