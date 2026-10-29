using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Notifications;

/// <summary>
/// The data message the sync service sends this phone through Notification Hubs when one of its
/// owner's notifications is published or changes state (local ADR 0039 §4), read off the FCM
/// data map. It never carries a body: only <c>id</c> and <c>state</c> are certain, and the
/// preview fields are absent when the owner sent none.
/// </summary>
/// <param name="Id">The notification's id.</param>
/// <param name="State">One of <see cref="NotificationStates"/>.</param>
/// <param name="Kind">One of <see cref="NotificationKinds"/>, or null.</param>
/// <param name="Severity">One of <see cref="NotificationSeverities"/>, or null.</param>
/// <param name="Title">The title, or null.</param>
/// <param name="Summary">The summary — already the fixed text for a private notification — or null.</param>
/// <param name="Origin">Where it came from, "System · Place", or null.</param>
public sealed record PushMessage(
    Guid Id,
    string State,
    string? Kind,
    string? Severity,
    string? Title,
    string? Summary,
    string? Origin = null)
{
    /// <summary>Whether the message puts the notification in the tray. Every other state —
    /// answered, acknowledged, closed — takes it out.</summary>
    public bool Shows => State == NotificationStates.Open;

    /// <summary>
    /// The message in <paramref name="data"/>, or null when the map is not one: no id, an id
    /// that is not a GUID, or a state the service does not send. A map like that is passed
    /// over rather than shown, because nothing on the phone could say what it was about.
    /// </summary>
    public static PushMessage? Parse(IReadOnlyDictionary<string, string>? data)
    {
        if (data is null) return null;

        if (!data.TryGetValue(NotificationPushData.Id, out var rawId) || !Guid.TryParse(rawId, out var id)) return null;
        if (!data.TryGetValue(NotificationPushData.State, out var state) || !NotificationStates.IsKnown(state)) return null;

        return new PushMessage(
            id,
            state,
            Read(data, NotificationPushData.Kind),
            Read(data, NotificationPushData.Severity),
            Read(data, NotificationPushData.Title),
            Read(data, NotificationPushData.Summary),
            Read(data, NotificationPushData.Origin));
    }

    private static string? Read(IReadOnlyDictionary<string, string> data, string key) =>
        data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
