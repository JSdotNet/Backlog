using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Infrastructure.Sync.Extensions;

/// <summary>
/// What a host registers to be able to talk to the sync service as a device.
/// </summary>
public static class SyncClientRegistration
{
    /// <summary>
    /// Registers the token pipeline and the pairing client against
    /// <paramref name="baseAddress"/> — the surface every head can compose,
    /// whatever else it does or does not have.
    /// <para>
    /// Task replication is not here. It is
    /// <see cref="AddTaskSyncClient(IServiceCollection, Uri)"/>, because it needs
    /// an <see cref="Backlog.Modules.Tasks.ITaskRepository"/> and the mobile
    /// heads have none: registering it for every caller took both of them down
    /// inside <c>Build()</c>, on a provider validation neither the build nor the
    /// unit suite can see.
    /// </para>
    /// <para>
    /// It deliberately does not register an <see cref="IDeviceCredentialStore"/>
    /// or an <see cref="ITaskSyncStateStore"/>. Which store is right is the one
    /// thing only the host knows — DPAPI on the Windows desktop, its own file
    /// under the content root in each browser harness so the two are two distinct
    /// devices, and in memory on the Android head until its secure-storage
    /// adapter lands. Registering a default here would make the wrong one
    /// silently work, and for the sync state that failure is invisible: two
    /// harnesses sharing one watermark would each skip what the other had
    /// pushed, with nothing failing to say so.
    /// </para>
    /// <para>
    /// A host with data clients of its own — mobile's <c>CloudSyncClient</c> —
    /// chains <c>.AddHttpMessageHandler&lt;SyncAuthenticationHandler&gt;()</c>
    /// onto them so they leave carrying the same token. The handler is transient
    /// because <c>IHttpClientFactory</c> owns handler lifetimes; the provider
    /// behind it is the singleton that actually holds the cache.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSyncClient(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        // The token endpoint's own client, with no authentication handler on it.
        // See SyncTokenProvider for why it cannot be the same one.
        services.AddHttpClient(SyncTokenProvider.HttpClientName, client => client.BaseAddress = baseAddress);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SyncTokenProvider>();
        services.TryAddTransient<SyncAuthenticationHandler>();

        // devices/register, devices/pair and devices/token are anonymous, and
        // devices/codes and devices/me are bearer — one client covers both,
        // because sending no Authorization header when this device has no token
        // is exactly what an unpaired device should do.
        services.AddHttpClient<DevicePairingClient>(client => client.BaseAddress = baseAddress)
            .AddHttpMessageHandler<SyncAuthenticationHandler>();

        return services;
    }

    /// <summary>
    /// Adds task replication on top of <see cref="AddSyncClient"/>: the replica
    /// client, the merge, the session and the background loop that runs it.
    /// <para>
    /// Call it after <see cref="AddSyncClient"/> and against the same address —
    /// the typed client here chains onto the token pipeline that call registers,
    /// and on its own it would leave every replication request anonymous.
    /// </para>
    /// <para>
    /// <strong>Opt-in, and only a host may opt in.</strong> All three take
    /// <see cref="Backlog.Modules.Tasks.ITaskRepository"/> and the session also
    /// takes <see cref="ITaskSyncStateStore"/>, and a head that has neither is
    /// not a head that forgot them: the mobile heads carry the Inbox and no local
    /// task database at all. Registering these for every caller is what took both
    /// of them down at <c>Build()</c>.
    /// </para>
    /// <para>
    /// It is a separate call rather than a check inside
    /// <see cref="AddSyncClient"/> for whether the collection already holds a
    /// repository. That check would read as safer and be worse: a host that
    /// composes its repository <em>after</em> its sync client — which is a matter
    /// of line order, not of intent — would get no task sync, no session, and
    /// nothing at all to say so. A missing call is visible in the host's own
    /// composition; a sync client that silently declined to sync is visible
    /// nowhere.
    /// </para>
    /// <para>
    /// <strong>The loop is part of replication, and a head has to start it.</strong>
    /// <see cref="TaskSyncWorker"/> is registered here because a device that only
    /// replicates when somebody opens Settings and presses a button is not
    /// replicating. It is a singleton with a timer inside it rather than an
    /// <c>IHostedService</c> — see that type for why the MAUI head leaves no
    /// place for one — which means nothing starts it except the first resolve.
    /// A head that calls this must therefore ask for it once after
    /// <c>Build()</c>: a singleton nobody resolves is a singleton that never
    /// runs, and there is nothing about that failure to notice.
    /// </para>
    /// <para>
    /// The loop also adds one thing to what a caller owes this method: an
    /// <see cref="Backlog.SharedKernel.IAppFeatureSettings"/>, on top of the
    /// <see cref="ITaskSyncStateStore"/> and <see cref="IDeviceCredentialStore"/>
    /// the session and the token pipeline already need. It gates on the
    /// <c>task-sync</c> feature and on this device being paired, because a person
    /// can have paired devices and still not want their tasks leaving the
    /// machine, and an unpaired device has no owner to replicate under. A head
    /// that leaves the feature store out fails provider validation rather than
    /// silently syncing whatever the person switched off.
    /// </para>
    /// </summary>
    public static IServiceCollection AddTaskSyncClient(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        // Both halves of task replication are bearer, so this one always carries
        // the token. Its own typed client rather than the pairing client's: the
        // two have different retry and timeout profiles waiting to be set, and
        // sharing one would mean choosing between them.
        services.AddHttpClient<TaskSyncClient>(client => client.BaseAddress = baseAddress)
            .AddHttpMessageHandler<SyncAuthenticationHandler>();

        // Transient rather than singleton, because both take the typed client
        // above and IHttpClientFactory owns its lifetime — a singleton here would
        // pin one handler chain for the life of the process. The state they share
        // is the host's ITaskSyncStateStore, which is where the singleton belongs.
        services.TryAddTransient<TaskReplicaMerge>();
        services.TryAddTransient<TaskSyncSession>();

        // And a singleton, because the schedule is the thing it holds: a second
        // worker would be a second timer, and two timers are the overlapping
        // cycles its own guard exists to prevent. It takes the provider rather
        // than the session for the reason the two lines above are transient.
        services.TryAddSingleton<TaskSyncWorker>();

        return services;
    }
}
