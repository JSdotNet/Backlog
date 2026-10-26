using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Notifications;

/// <summary>What one button on an Android notification does.</summary>
public enum PushActionKind
{
    /// <summary>Replies with one set answer.</summary>
    Choice,

    /// <summary>Opens a text field in the tray and replies with what is typed.</summary>
    InlineReply
}

/// <summary>
/// One button on an Android notification. <see cref="Key"/> is all the button carries back: the
/// tray hands it to the app's receiver as a string, and <see cref="PushActions.ToReply"/> reads
/// the answer from it.
/// </summary>
/// <param name="Key"><c>choice:&lt;id&gt;</c> for a set answer, <see cref="PushActions.ReplyKey"/> for the inline reply.</param>
/// <param name="Label">What the button says.</param>
/// <param name="Kind">A set answer or the inline reply.</param>
/// <param name="Placeholder">The inline reply's hint, or null.</param>
public sealed record PushAction(string Key, string Label, PushActionKind Kind, string? Placeholder = null);

/// <summary>
/// The answers a question offers from the tray (local ADR 0038 §4,
/// <c>.devbook/domain/notifications/requirements.md#requirement-android-is-the-only-phone-platform</c>), and
/// the reply one of them makes. Pure, so the mapping is tested without a device.
/// <para>
/// Android shows at most three actions, the inline reply included. A question whose whole answer
/// does not fit — several picks, more choices than buttons, an open answer beside three choices —
/// gets no actions, and its tap opens the question in the app instead. So does anything this
/// phone may not answer now: an alert, an owner-only question, a reply already on its way.
/// </para>
/// </summary>
public static class PushActions
{
    /// <summary>The most actions Android draws on one notification.</summary>
    public const int MaxActions = 3;

    /// <summary>The inline reply's key.</summary>
    public const string ReplyKey = "reply";

    /// <summary>What "Reply" reads as when the open answer names no placeholder.</summary>
    public const string ReplyLabel = "Reply";

    private const string ChoicePrefix = "choice:";

    /// <summary>The actions <paramref name="notification"/> gets in the tray; empty when the tap
    /// should open it instead.</summary>
    public static IReadOnlyList<PushAction> For(PhoneNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!notification.CanAnswer || notification.Notification.Kind != NotificationKind.Question) return [];

        if (notification.Notification.AnswerSpec is not { AllowMultiple: false, AnswerOn: AnswerOn.AnyInstance } spec) return [];

        var choices = spec.Choices ?? [];
        var slots = choices.Count + (spec.Open is null ? 0 : 1);
        if (slots == 0 || slots > MaxActions) return [];

        List<PushAction> actions = [.. choices.Select(choice => new PushAction(ChoicePrefix + choice.Id, choice.Label, PushActionKind.Choice))];

        if (spec.Open is { } open)
        {
            actions.Add(new PushAction(ReplyKey, ReplyLabel, PushActionKind.InlineReply, open.Placeholder));
        }

        return actions;
    }

    /// <summary>
    /// The reply the action <paramref name="key"/> makes, with <paramref name="text"/> typed into
    /// the inline reply; null when it names no answer — an empty inline reply, a key this build
    /// did not write. Whether the reply fits the question is
    /// <see cref="NotificationViewProjection.ReplyAsync"/>'s to say.
    /// </summary>
    public static NotificationAnswerPick? ToReply(string? key, string? text)
    {
        if (key == ReplyKey)
        {
            return string.IsNullOrWhiteSpace(text) ? null : new NotificationAnswerPick([], text.Trim());
        }

        if (key is not null && key.StartsWith(ChoicePrefix, StringComparison.Ordinal) && key.Length > ChoicePrefix.Length)
        {
            return new NotificationAnswerPick([key[ChoicePrefix.Length..]], null);
        }

        return null;
    }

    /// <summary>
    /// Answers notification <paramref name="notificationId"/> with the action
    /// <paramref name="key"/> and the inline <paramref name="text"/>: the reply goes through
    /// <see cref="NotificationViewProjection.ReplyAsync"/>, so it is queued in the outbox exactly
    /// as a reply made in the app is. Null when the action names no answer, or the phone may not
    /// answer that notification now — it does not hold it, or it settled since the tray showed it.
    /// </summary>
    public static Task<NotificationReplyPost?> ReplyAsync(
        NotificationViewProjection projection,
        Guid notificationId,
        string? key,
        string? text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projection);

        return ToReply(key, text) is { } pick
            ? projection.ReplyAsync(notificationId, pick.ChoiceIds, pick.Text, cancellationToken)
            : Task.FromResult<NotificationReplyPost?>(null);
    }
}
