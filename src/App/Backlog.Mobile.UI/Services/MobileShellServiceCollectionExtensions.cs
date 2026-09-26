using Backlog.Mobile.UI.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Mobile.UI.Services;

public static class MobileShellServiceCollectionExtensions
{
    /// <summary>
    /// What the shell itself needs, the same in both hosts: the draft that outlives
    /// a tab switch, the status the app bar reads, and the lifecycle the hosts report
    /// resumes through. The MAUI head and the browser harness both call this, so the
    /// two cannot disagree about what the shell is.
    /// </summary>
    /// <remarks>The shell needs <see cref="AddDeviceOutbox"/> too; that one takes
    /// the file, which is the host's to place.</remarks>
    public static IServiceCollection AddMobileShell(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CaptureDraft>();
        services.AddScoped<SyncStatusTracker>();

        // One instance behind both names: the Inbox records against the tracker,
        // and the status line reads the same object through the abstraction.
        services.AddScoped<ISyncStatusSource>(provider => provider.GetRequiredService<SyncStatusTracker>());

        // Scoped unless the host already registered it: the MAUI head makes it a
        // singleton, because its window events arrive from outside any scope.
        services.TryAddScoped<AppLifecycle>();

        return services;
    }

    /// <summary>
    /// The device store in <paramref name="databasePath"/>, the outbox over it, and
    /// the kinds it sends. Singletons: one file per device, and one flush over it —
    /// two would send the same entry twice.
    /// </summary>
    public static IServiceCollection AddDeviceOutbox(this IServiceCollection services, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IDeviceStore>(_ => new SqliteDeviceStore(databasePath));
        services.AddSingleton<DeviceOutbox>();

        // One registration per kind. The capture holds a typed sync client for the
        // life of the app, which on a phone is the life of the process anyway.
        services.AddSingleton<IOutboxKind, CaptureOutboxKind>();

        return services;
    }
}
