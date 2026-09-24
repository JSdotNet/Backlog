using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Mobile.UI.Services;

public static class MobileShellServiceCollectionExtensions
{
    /// <summary>
    /// What the shell itself needs, the same in both hosts: the draft that outlives
    /// a tab switch and the status the app bar reads. The MAUI head and the browser
    /// harness both call this, so the two cannot disagree about what the shell is.
    /// </summary>
    public static IServiceCollection AddMobileShell(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CaptureDraft>();
        services.AddScoped<SyncStatusTracker>();

        // One instance behind both names: the Inbox records against the tracker,
        // and the status line reads the same object through the abstraction.
        services.AddScoped<ISyncStatusSource>(provider => provider.GetRequiredService<SyncStatusTracker>());

        return services;
    }
}
