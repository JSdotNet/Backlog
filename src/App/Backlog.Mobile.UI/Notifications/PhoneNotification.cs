using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Notifications;

/// <summary>
/// Where one notification stands as this phone sees it — the owner's state, read against the
/// replies this phone made (<c>.devbook/domain/notifications/features.md#answering-from-another-device</c>).
/// </summary>
public enum NotificationStanding
{
    /// <summary>An open question this phone may answer.</summary>
    Answerable,

    /// <summary>An open question that says <c>answerOn: owner</c>: shown, never answered here.</summary>
    OwnerOnly,

    /// <summary>An open alert: there is nothing to answer.</summary>
    Alert,

    /// <summary>A reply made here is still in the outbox.</summary>
    Sending,

    /// <summary>A reply made here has reached the sync service and waits for the owner to
    /// accept or reject it.</summary>
    AwaitingOwner,

    /// <summary>The owner rejected the last reply made here, with a reason, or cloud sync
    /// refused to take it; the question is still open, so the phone may reply again.</summary>
    Rejected,

    /// <summary>The owner accepted a reply made on this phone.</summary>
    AnsweredHere,

    /// <summary>The owner accepted a reply made on another device.</summary>
    AnsweredElsewhere,

    /// <summary>An alert someone acknowledged.</summary>
    Acknowledged,

    /// <summary>The publisher closed it, or its subject ended.</summary>
    Closed
}

/// <summary>Whether the desktop that owns a notification is running, as far as the phone can
/// tell from the stamps of its open questions.</summary>
public enum OwnerPresence
{
    /// <summary>Nothing to read it from: the notification is settled or an alert.</summary>
    Unknown,

    /// <summary>The owner pushed an open question within
    /// <see cref="NotificationSyncSession.PresenceWindow"/>.</summary>
    Online,

    /// <summary>It has not, so a reply waits until the owner wakes.</summary>
    Offline
}

/// <summary>
/// One notification as the phone shows it: the owner's notification, who owns it and whether
/// that desktop is running, where it stands for this phone, and the words that go with that —
/// the winning answer, or why a reply was rejected or the notification closed.
/// </summary>
/// <param name="Notification">The notification as its owner pushed it.</param>
/// <param name="OwnerName">The owner desktop's name, or null from an owner that sends none.</param>
/// <param name="Presence">Whether the owner is running.</param>
/// <param name="OwnerSeenAt">The newest stamp of the owner's open questions, or null.</param>
/// <param name="Standing">Where it stands for this phone.</param>
/// <param name="Verdict">The winning answer, the rejection's reason, or the close reason, in words.</param>
/// <param name="Waiting">The outbox entry still carrying this phone's reply, or null.</param>
public sealed record PhoneNotification(
    NotificationSnapshot Notification,
    string? OwnerName,
    OwnerPresence Presence,
    DateTimeOffset? OwnerSeenAt,
    NotificationStanding Standing,
    string? Verdict,
    OutboxEntry? Waiting)
{
    public Guid Id => Notification.Id;

    /// <summary>Whether the answer controls are drawn: an open question nobody has to wait on.</summary>
    public bool CanAnswer => Standing is NotificationStanding.Answerable or NotificationStanding.Rejected;

    public bool IsOpen => Notification.State == NotificationState.Open;
}

/// <summary>The rules that turn the owner's notification and this phone's replies into a
/// <see cref="NotificationStanding"/>. Pure, so the card states are tested without a screen.</summary>
public static class NotificationStandings
{
    /// <summary>
    /// Where <paramref name="payload"/> stands for the phone whose sync device is
    /// <paramref name="deviceId"/> and which made <paramref name="mine"/> (oldest first), with
    /// <paramref name="queued"/> answering the outbox entry still carrying one of those replies,
    /// or null once it has left.
    /// </summary>
    public static (NotificationStanding Standing, string? Verdict) Of(
        NotificationPayload payload,
        Guid? deviceId,
        IReadOnlyList<NotificationReplyPost> mine,
        Func<Guid, OutboxEntry?> queued)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(queued);

        var notification = payload.Notification;

        switch (notification.State)
        {
            case NotificationState.Answered:
                var answer = notification.Answer;
                var here = answer is not null
                    && (mine.Any(reply => reply.Id == answer.ReplyId) || (deviceId is { } me && answer.FromDeviceId == me));
                return (here ? NotificationStanding.AnsweredHere : NotificationStanding.AnsweredElsewhere,
                    answer is null ? null : AnswerText(notification.AnswerSpec, answer.ChoiceIds, answer.Text));

            case NotificationState.Acknowledged:
                return (NotificationStanding.Acknowledged, null);

            case NotificationState.Closed:
                return (NotificationStanding.Closed, string.IsNullOrWhiteSpace(notification.CloseReason) ? null : notification.CloseReason);
        }

        if (notification.Kind == NotificationKind.Alert) return (NotificationStanding.Alert, null);

        if (notification.AnswerSpec?.AnswerOn == AnswerOn.Owner) return (NotificationStanding.OwnerOnly, null);

        if (mine.LastOrDefault() is not { } last) return (NotificationStanding.Answerable, null);

        if (queued(last.Id) is { } entry)
        {
            // Refused is the service's answer and will not change on a retry: the reply is set
            // aside, and the person answers again instead.
            return entry.Refused
                ? (NotificationStanding.Rejected, entry.LastError ?? "Cloud sync refused it.")
                : (NotificationStanding.Sending, null);
        }

        var rejection = payload.RejectedReplies?.LastOrDefault(rejected => rejected.ReplyId == last.Id);

        return rejection is not null
            ? (NotificationStanding.Rejected, rejection.Message)
            : (NotificationStanding.AwaitingOwner, null);
    }

    /// <summary>An answer as a person reads it: the chosen labels, then the text.</summary>
    public static string AnswerText(AnswerSpec? spec, IReadOnlyList<string>? choiceIds, string? text)
    {
        var labels = (choiceIds ?? [])
            .Select(id => spec?.Choices.FirstOrDefault(choice => choice.Id == id)?.Label ?? id)
            .ToList();

        if (!string.IsNullOrWhiteSpace(text)) labels.Add($"“{text.Trim()}”");

        return labels.Count == 0 ? "No choice" : string.Join(", ", labels);
    }

    /// <summary>Whether a reply of <paramref name="choiceIds"/> and <paramref name="text"/>
    /// fits <paramref name="spec"/> — what enables the send. The owner checks again; this only
    /// keeps the phone from sending what it already knows will be rejected. It mirrors the
    /// owner's <c>ReplyValidation</c>: text is the open answer instead of a choice, or the words
    /// of a picked choice that <see cref="AnswerChoice.TakesText"/>, which then must have them.</summary>
    public static bool Fits(AnswerSpec? spec, IReadOnlyCollection<string> choiceIds, string? text)
    {
        if (spec is null) return false;

        ArgumentNullException.ThrowIfNull(choiceIds);

        var choices = spec.Choices ?? [];
        var words = string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        if (choiceIds.Count == 0 && words is not null)
        {
            return spec.Open is { } open && words.Length <= open.MaxLength;
        }

        if (choiceIds.Any(id => choices.All(choice => choice.Id != id))) return false;

        if (choiceIds.Count > 0)
        {
            var takesText = choices.Any(choice => choice.TakesText && choiceIds.Contains(choice.Id));
            if (takesText != (words is not null)) return false;
            if (words is not null && words.Length > (spec.Open?.MaxLength ?? OpenAnswer.DefaultMaxLength)) return false;
        }

        if (!spec.AllowMultiple) return choiceIds.Count == 1;

        // The owner's own bounds (ReplyValidation): one at least unless the question says
        // fewer, every choice at most unless it says fewer — so a minimum of zero takes an
        // empty reply.
        var least = spec.MinChoices ?? 1;
        var most = spec.MaxChoices ?? choices.Count;
        return choiceIds.Count >= least && choiceIds.Count <= most;
    }
}
