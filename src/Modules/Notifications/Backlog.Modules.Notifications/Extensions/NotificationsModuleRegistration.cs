using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.Services;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Backlog.Modules.Notifications.Extensions;

/// <summary>Which part an instance plays for the notifications it sees
/// (<c>.devbook/domain/notifications/domain.md#owner-instance</c>).</summary>
public enum NotificationsRole
{
    /// <summary>A desktop: owns and writes the notifications it publishes.</summary>
    Owner,

    /// <summary>The phone: shows notifications and never publishes or changes one.</summary>
    Viewer
}

/// <summary>
/// The module's composition root. A host calls <see cref="AddNotificationsModule"/> once and gets
/// <see cref="INotifications"/>, <see cref="INotificationReplies"/> and
/// <see cref="INotificationStatuses"/> — and, as an owner,
/// <see cref="INotificationCentre"/> for its screens; each context that asks
/// questions adds its handler with <see cref="AddNotificationAnswerHandler{THandler}"/>, and each
/// context that owns subjects its probe with <see cref="AddNotificationSubjectProbe{TProbe}"/>.
/// <para>
/// As an <see cref="NotificationsRole.Owner"/>, the module needs the host's
/// <see cref="INotificationRepository"/> — the <c>notifications</c> table in <c>backlog.db</c> —
/// and <see cref="IDeviceIdentitySource"/>, and the host starts <see cref="NotificationsStartup"/>
/// once the container is built. As a <see cref="NotificationsRole.Viewer"/> it needs neither, and
/// every call is refused with <c>notifications.viewer_only</c>.
/// </para>
/// </summary>
public static class NotificationsModuleRegistration
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, NotificationsRole role = NotificationsRole.Owner)
    {
        ArgumentNullException.ThrowIfNull(services);

        switch (role)
        {
            case NotificationsRole.Viewer:
                services.TryAddSingleton<ViewerNotifications>();
                services.TryAddSingleton<INotifications>(sp => sp.GetRequiredService<ViewerNotifications>());
                services.TryAddSingleton<INotificationReplies>(sp => sp.GetRequiredService<ViewerNotifications>());
                services.TryAddSingleton<INotificationStatuses, ViewerNotificationStatuses>();
                return services;

            case NotificationsRole.Owner:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(role), role, "An instance is an owner or a viewer.");
        }

        services.TryAddSingleton(TimeProvider.System);

        // The host's store, observed: every write raises the change the centre and the
        // operating system's notifications redraw on, whichever channel made it.
        services.TryAddSingleton(sp => new ObservedNotificationRepository(sp.GetRequiredService<INotificationRepository>()));

        services.TryAddSingleton(sp => new AnswerDelivery(
            sp.GetServices<INotificationAnswerHandler>(),
            sp.GetRequiredService<ObservedNotificationRepository>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<AnswerDelivery>>()));
        // The same delivery, for a source that knows when its handler can take an answer again.
        services.TryAddSingleton<INotificationAnswerReplay>(sp => sp.GetRequiredService<AnswerDelivery>());

        // One instance behind both ports: the gate that makes the first valid reply win has to
        // be the one every change passes through.
        services.TryAddSingleton(sp => new OwnerNotifications(
            sp.GetRequiredService<ObservedNotificationRepository>(),
            sp.GetRequiredService<IDeviceIdentitySource>(),
            sp.GetRequiredService<AnswerDelivery>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IAppFeatureSettings>()));
        services.TryAddSingleton<INotifications>(sp => sp.GetRequiredService<OwnerNotifications>());
        services.TryAddSingleton<INotificationReplies>(sp => sp.GetRequiredService<OwnerNotifications>());
        services.TryAddSingleton<INotificationStatuses>(sp => new OwnerNotificationStatuses(sp.GetRequiredService<INotificationRepository>()));

        services.TryAddSingleton(sp => new NotificationCentre(
            sp.GetRequiredService<ObservedNotificationRepository>(),
            sp.GetRequiredService<INotificationReplies>(),
            sp.GetRequiredService<IDeviceIdentitySource>(),
            sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<INotificationCentre>(sp => sp.GetRequiredService<NotificationCentre>());

        // What the sync exchange carries to the other devices: read-only, over the same
        // store. Owner only — a viewer has nothing of its own to carry.
        services.TryAddSingleton<INotificationReplication>(sp => new NotificationReplication(
            sp.GetRequiredService<INotificationRepository>()));

        services.TryAddSingleton(sp => new NotificationsStartup(
            sp.GetRequiredService<ObservedNotificationRepository>(),
            sp.GetRequiredService<INotifications>(),
            sp.GetRequiredService<IDeviceIdentitySource>(),
            sp.GetRequiredService<AnswerDelivery>(),
            sp.GetServices<INotificationSubjectProbe>(),
            sp.GetService<ILogger<NotificationsStartup>>()));

        return services;
    }

    /// <summary>Registers <typeparamref name="THandler"/> to receive the accepted answers to its
    /// source's questions. A singleton; registering the same type twice adds it once.</summary>
    public static IServiceCollection AddNotificationAnswerHandler<THandler>(this IServiceCollection services)
        where THandler : class, INotificationAnswerHandler
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<INotificationAnswerHandler, THandler>());
        return services;
    }

    /// <summary>Registers <typeparamref name="TProbe"/> to say, at startup, whether a subject of
    /// its context still lives. A singleton; registering the same type twice adds it once.</summary>
    public static IServiceCollection AddNotificationSubjectProbe<TProbe>(this IServiceCollection services)
        where TProbe : class, INotificationSubjectProbe
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<INotificationSubjectProbe, TProbe>());
        return services;
    }
}
