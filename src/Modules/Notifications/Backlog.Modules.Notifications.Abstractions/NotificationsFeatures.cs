namespace Backlog.Modules.Notifications.Abstractions;

/// <summary>
/// The feature key Notifications owns (<c>.devbook/arc42/08-crosscutting-concepts.md#feature-enablement</c>).
/// <para>
/// One key, per device like every flag: off on a desktop, <see cref="INotifications.PublishAsync"/>
/// refuses with <c>notifications.not_available</c> and a source falls back to a toast; off on
/// the phone, it shows no list. Its status — Dev — is authored in the settings catalog with
/// every other feature's.
/// </para>
/// </summary>
public static class NotificationsFeatures
{
    /// <summary>Publish alerts and questions on this desktop, and show and answer them on
    /// every paired device.</summary>
    public const string Notifications = "notifications";
}
