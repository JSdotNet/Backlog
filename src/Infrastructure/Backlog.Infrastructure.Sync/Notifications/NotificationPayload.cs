using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.Notifications;

/// <summary>
/// One notification as it travels inside <see cref="NotificationChange.Notification"/>: the
/// owner's <see cref="NotificationSnapshot"/>, plus what the owner decided about the replies it
/// rejected. The sync service stores it whole and reads none of it; a viewer — the phone, another
/// desktop — reads it back with <see cref="NotificationPayloads.Read"/>.
/// <para>
/// The rejections ride on the notification because the notification is the one document the
/// owner writes and every viewer reads: a viewer whose reply lost the race, or did not fit,
/// finds its reply id here with the reason, and can reply again. The accepted reply needs no
/// entry — it is <see cref="NotificationSnapshot.Answer"/>.
/// </para>
/// </summary>
/// <param name="Notification">The notification as the owner holds it.</param>
/// <param name="RejectedReplies">The replies the owner rejected, oldest first.</param>
/// <param name="OwnerName">What the owner instance calls itself — the name it paired under,
/// such as its machine name — so a viewer can say which desktop holds the question. Null from
/// an owner that predates the field.</param>
public sealed record NotificationPayload(
    NotificationSnapshot Notification,
    IReadOnlyList<NotificationReplyRejection> RejectedReplies,
    string? OwnerName = null);

/// <summary>A reply the owner instance rejected, and why.</summary>
/// <param name="ReplyId">The reply.</param>
/// <param name="FromDeviceId">The sync device the person replied on.</param>
/// <param name="Reason">One of <see cref="NotificationReplyReasons"/>.</param>
/// <param name="Code">The owner's own error code — <c>reply.owner_only</c>, <c>reply.invalid</c> and so on.</param>
/// <param name="Message">What to tell the person. Never the reply's own text.</param>
/// <param name="At">When the owner rejected it.</param>
public sealed record NotificationReplyRejection(
    Guid ReplyId,
    Guid FromDeviceId,
    string Reason,
    string Code,
    string Message,
    DateTimeOffset At);

/// <summary>The three reasons a reply is rejected for, as the plan names them.</summary>
public static class NotificationReplyReasons
{
    /// <summary>Another reply was accepted first.</summary>
    public const string AlreadyAnswered = "already-answered";

    /// <summary>The question was closed, or the alert acknowledged, before the reply reached
    /// the owner.</summary>
    public const string Closed = "closed";

    /// <summary>The reply does not fit the question, comes from where it may not, or names a
    /// question the owner does not hold.</summary>
    public const string Invalid = "invalid";

    private static readonly string AlreadyAnsweredCode = NotificationsErrors.AlreadyAnswered().Code;

    private static readonly string ClosedCode = NotificationsErrors.ReplyClosed().Code;

    /// <summary>The reason for one of the owner's refusal codes, matched against the codes
    /// <see cref="NotificationsErrors"/> owns rather than retyped here.</summary>
    public static string For(string code) =>
        string.Equals(code, AlreadyAnsweredCode, StringComparison.Ordinal) ? AlreadyAnswered
        : string.Equals(code, ClosedCode, StringComparison.Ordinal) ? Closed
        : Invalid;
}

/// <summary>Between a snapshot and the wire, in both directions.</summary>
public static class NotificationPayloads
{
    /// <summary>How the payload is written: camel-cased, enums as their names, nulls left out.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The wire state for <paramref name="state"/>.</summary>
    public static string StateOf(NotificationState state) => state switch
    {
        NotificationState.Open => NotificationStates.Open,
        NotificationState.Acknowledged => NotificationStates.Acknowledged,
        NotificationState.Answered => NotificationStates.Answered,
        NotificationState.Closed => NotificationStates.Closed,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A notification is open, acknowledged, answered or closed."),
    };

    /// <summary>The change to push for <paramref name="snapshot"/>, stamped
    /// <paramref name="updatedAt"/>, naming its owner as <paramref name="ownerName"/>.</summary>
    public static NotificationChange ToChange(
        NotificationSnapshot snapshot,
        IReadOnlyList<NotificationReplyRejection> rejected,
        DateTimeOffset updatedAt,
        string? ownerName = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(rejected);

        return new NotificationChange(
            snapshot.Id,
            snapshot.OwnerInstanceId,
            StateOf(snapshot.State),
            updatedAt,
            JsonSerializer.SerializeToElement(new NotificationPayload(snapshot, rejected, ownerName), Options));
    }

    /// <summary>The payload a change carries, or null for one this build cannot read.</summary>
    public static NotificationPayload? Read(NotificationChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        try
        {
            return change.Notification.Deserialize<NotificationPayload>(Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
