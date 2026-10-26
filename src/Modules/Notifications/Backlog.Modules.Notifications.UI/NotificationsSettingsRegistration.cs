using global::Backlog.Modules.Notifications.Abstractions;
using global::Backlog.SharedKernel;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Notifications.UI;

/// <summary>
/// Puts the Notifications page on the settings screen, on the precedent of
/// <c>AddSyncSettings</c>: whether a host's settings screen carries it is the host's decision, and
/// the shell knows the page only as the <see cref="SettingsSection"/> this adds.
/// </summary>
/// <remarks>
/// Offered behind <see cref="NotificationsFeatures.Notifications"/>, so while notifications are off
/// the menu marks the page Off and it shows only the note pointing at the Features page. The page
/// reads <see cref="INotifications"/> and <see cref="INotificationCentre"/>, which only an owner
/// instance registers (<c>AddNotificationsModule()</c>): a desktop host adds it, the phone does not.
/// The host also registers <see cref="TestNotificationAnswers"/> with
/// <c>AddNotificationAnswerHandler&lt;T&gt;()</c>, so a test question's answer is taken once.
/// </remarks>
public static class NotificationsSettingsRegistration
{
    /// <summary>The Notifications page's place among the Product areas pages: after the Devices
    /// page (50), whose pairing it tries out.</summary>
    public const int Order = 60;

    public static IServiceCollection AddNotificationsSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SettingsSection(
            "notifications",
            "Notifications",
            Order,
            typeof(NotificationsSettings),
            NotificationsFeatures.Notifications,
            SettingsGroup.ProductAreas,
            CardTitles: NotificationsSettings.CardTitles));

        return services;
    }
}
