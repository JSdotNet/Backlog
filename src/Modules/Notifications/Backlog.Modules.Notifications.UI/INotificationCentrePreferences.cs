namespace Backlog.Modules.Notifications.UI;

/// <summary>
/// How this machine last had the notification centre drawn: its Group by, its filters and
/// whether it lists the settled notifications.
/// Choices about the view, not about any notification, so they are kept per device and never
/// synced — the footing the tools pane's tab and the Board's columns are on — and answered by
/// the host over its per-device settings file.
/// <para>
/// The centre resolves it optionally: a host that composes none holds the choices for the life
/// of the pane and opens on the defaults next time.
/// </para>
/// </summary>
public interface INotificationCentrePreferences
{
    /// <summary>The choices last made on this device; every part null until made.</summary>
    NotificationCentreChoices Choices { get; }

    /// <summary>Remembers <paramref name="choices"/> on this device.</summary>
    void Remember(NotificationCentreChoices choices);
}

/// <summary>
/// The centre's choices, each by the option's own slug, null for the default: Group by
/// (<c>repository</c>, <c>system</c>, <c>asked-by</c>, <c>subject</c>, <c>none</c>; null follows
/// the scope), Asked by (<c>ai-agent</c>, <c>person</c>), Kind (<c>questions</c>,
/// <c>alerts</c>) and From (a system's name). A slug this build does not know reads as the
/// default — <c>open</c>, the Kind option Show settled replaced, among them.
/// <para>
/// <see cref="ShowSettled"/> is the Show settled switch: <c>true</c> lists the answered,
/// acknowledged and closed notifications beside the open ones; null, the default, hides them.
/// </para>
/// </summary>
public sealed record NotificationCentreChoices(
    string? GroupBy = null,
    string? AskedBy = null,
    string? Kind = null,
    string? From = null,
    bool? ShowSettled = null);
