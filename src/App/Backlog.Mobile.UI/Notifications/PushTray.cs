using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Notifications;

/// <summary>
/// One notification as the Android tray shows it: the words, whether it is urgent, and the
/// answers it offers from the tray. Its <see cref="Id"/> is the tray entry's tag, so a later
/// push replaces or removes the same entry.
/// </summary>
/// <param name="Id">The notification's id.</param>
/// <param name="Title">The first line.</param>
/// <param name="Text">The second line, or null.</param>
/// <param name="Urgent">Whether the notification is critical.</param>
/// <param name="Actions">The tray's buttons; empty when the tap opens the question instead.</param>
/// <param name="Subtitle">Where it came from, "System · Place", shown as the entry's subtitle; or null.</param>
public sealed record PushTrayEntry(Guid Id, string Title, string? Text, bool Urgent, IReadOnlyList<PushAction> Actions, string? Subtitle = null);

/// <summary>
/// Decides what the tray shows for a push (local ADR 0039 §4), from the message and the copy the
/// phone pulled on receiving it. Pure, so the tray's rules are tested without a device.
/// <para>
/// The message's own words win: the service already put a private notification's fixed text in
/// them. Where the owner sent no preview, the pulled copy fills in — with the same fixed text
/// for a private one, because the tray is outside the app. A settled notification, by the
/// message or by the pull that followed it, shows nothing: its tray entry is removed.
/// </para>
/// </summary>
public static class PushTray
{
    /// <summary>The first line when neither the message nor the phone names a title.</summary>
    public const string FallbackTitle = "Backlog";

    /// <summary>The entry to show, or null when the tray entry for <paramref name="message"/>'s
    /// notification should be removed.</summary>
    /// <param name="message">The push.</param>
    /// <param name="held">The notification as the phone holds it after pulling, or null when the
    /// pull did not bring it — then the tray shows the message's words, with no actions.</param>
    public static PushTrayEntry? Entry(PushMessage message, PhoneNotification? held)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!message.Shows || held is { IsOpen: false }) return null;

        var notification = held?.Notification;

        var title = message.Title ?? notification?.Title ?? FallbackTitle;
        var text = message.Summary ?? Summary(notification);
        var urgent = message.Severity is { } severity
            ? severity == NotificationSeverities.Critical
            : notification?.Severity == NotificationSeverity.Critical;

        var subtitle = message.Origin
            ?? notification?.Origin?.LineWithin(NotificationRecordLimits.PreviewOriginLength);

        return new PushTrayEntry(message.Id, title, text, urgent, held is null ? [] : PushActions.For(held), subtitle);
    }

    private static string? Summary(NotificationSnapshot? notification) => notification switch
    {
        null => null,
        { IsPrivate: true } => NotificationPushData.PrivateSummary,
        _ => string.IsNullOrWhiteSpace(notification.Summary) ? null : notification.Summary
    };
}
