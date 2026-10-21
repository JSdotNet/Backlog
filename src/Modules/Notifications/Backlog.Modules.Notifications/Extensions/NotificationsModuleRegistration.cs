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
/// <see cref="INotifications"/> and <see cref="INotificationReplies"/>; each context that asks
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
                return services;

            case NotificationsRole.Owner:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(role), role, "An instance is an owner or a viewer.");
        }

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton(sp => new AnswerDelivery(
            sp.GetServices<INotificationAnswerHandler>(),
            sp.GetRequiredService<INotificationRepository>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<ILogger<AnswerDelivery>>()));

        // One instance behind both ports: the gate that makes the first valid reply win has to
        // be the one every change passes through.
        services.TryAddSingleton(sp => new OwnerNotifications(
            sp.GetRequiredService<INotificationRepository>(),
            sp.GetRequiredService<IDeviceIdentitySource>(),
            sp.GetRequiredService<AnswerDelivery>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IAppFeatureSettings>()));
        services.TryAddSingleton<INotifications>(sp => sp.GetRequiredService<OwnerNotifications>());
        services.TryAddSingleton<INotificationReplies>(sp => sp.GetRequiredService<OwnerNotifications>());

        services.TryAddSingleton(sp => new NotificationsStartup(
            sp.GetRequiredService<INotificationRepository>(),
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
