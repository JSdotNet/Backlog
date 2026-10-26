using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.Features.PushNotifications;

/// <summary>
/// Builds the Android push for a notification that was published or changed state (local ADR
/// 0038 §4) from its envelope alone — the id, the state, and the owner's push preview — so the
/// service never opens the document to send one, and the body cannot travel.
/// </summary>
public static class NotificationPushMessages
{
    /// <summary>The push for <paramref name="change"/>. A private notification's summary reads
    /// <see cref="NotificationPushData.PrivateSummary"/> whatever the owner sent; a change
    /// without a preview carries its id and state only.</summary>
    public static NotificationPushMessage For(NotificationChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NotificationPushData.Id] = change.Id.ToString("D"),
            [NotificationPushData.State] = change.State,
        };

        if (change.Preview is { } preview)
        {
            data[NotificationPushData.Kind] = preview.Kind;
            data[NotificationPushData.Severity] = preview.Severity;
            data[NotificationPushData.Title] = preview.Title;
            data[NotificationPushData.Summary] = preview.IsPrivate
                ? NotificationPushData.PrivateSummary
                : preview.Summary ?? string.Empty;
        }

        return new NotificationPushMessage(change.Id, data);
    }
}
