using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

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
        ArgumentNullException.ThrowIfNull(baseAddress);
        return services.AddSyncClient(_ => baseAddress);
    }

    /// <summary>
    /// The same registration with the address asked for per client rather than
    /// fixed at composition — the shape a head takes when the answer can change
    /// while it runs, which is <see cref="SyncServiceEndpoint"/>'s: a URL saved
    /// on the Settings page reaches the next client the factory creates. The
    /// callback runs inside <c>IHttpClientFactory</c>'s configure step, so it
    /// must not throw; see that type for how it declines a bad value instead.
    /// </summary>
    public static IServiceCollection AddSyncClient(this IServiceCollection services, Func<IServiceProvider, Uri> baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        // The token endpoint's own client, with no authentication handler on it.
        // See SyncTokenProvider for why it cannot be the same one.
        services.AddHttpClient(SyncTokenProvider.HttpClientName, (sp, client) => client.BaseAddress = baseAddress(sp));

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SyncTokenProvider>();

        // One log for everything that replicates, registered with the client
        // both task and session sync sit on rather than with either of them:
        // the footer that reads it wants one list, in time order, of what moved
        // — not one per kind. Its logger and clock are optional parameters, so
        // a host that composes neither still gets a log that works.
        services.TryAddSingleton<SyncActivityLog>();
        services.TryAddTransient<SyncAuthenticationHandler>();

        // devices/register, devices/pair and devices/token are anonymous, and
        // devices/codes and devices/me are bearer — one client covers both,
        // because sending no Authorization header when this device has no token
        // is exactly what an unpaired device should do.
        services.AddHttpClient<DevicePairingClient>((sp, client) => client.BaseAddress = baseAddress(sp))
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
        ArgumentNullException.ThrowIfNull(baseAddress);
        return services.AddTaskSyncClient(_ => baseAddress);
    }

    /// <summary>The per-client-address form of <see cref="AddTaskSyncClient(IServiceCollection, Uri)"/>;
    /// pass the same callback <see cref="AddSyncClient(IServiceCollection, Func{IServiceProvider, Uri})"/> got.</summary>
    public static IServiceCollection AddTaskSyncClient(this IServiceCollection services, Func<IServiceProvider, Uri> baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        // Both halves of task replication are bearer, so this one always carries
        // the token. Its own typed client rather than the pairing client's: the
        // two have different retry and timeout profiles — this one's is the
        // replication pipeline, the pairing client's is still the host default —
        // and sharing one would mean choosing between them.
        services.AddHttpClient<TaskSyncClient>((sp, client) => client.BaseAddress = baseAddress(sp))
            .AddHttpMessageHandler<SyncAuthenticationHandler>()
            .WithReplicationPipeline();

        // Where a pulled capture's files come from (local ADR 0014). Here and
        // not in AddSyncClient because the intake that asks for them is part of
        // task replication — a capture arrives on the task feed — and a head
        // without that feed has no capture to fetch files for. Bearer, like the
        // feed, and transient over its typed client for the reason below.
        services.AddHttpClient<InboxAttachmentSyncSource>((sp, client) => client.BaseAddress = baseAddress(sp))
            .AddHttpMessageHandler<SyncAuthenticationHandler>();
        services.TryAddTransient<IInboxAttachmentSource>(sp => sp.GetRequiredService<InboxAttachmentSyncSource>());

        // Transient rather than singleton, because both take the typed client
        // above and IHttpClientFactory owns its lifetime — a singleton here would
        // pin one handler chain for the life of the process. The state they share
        // is the host's ITaskSyncStateStore, which is where the singleton belongs.
        // The impediment edits a pull dealt with, waiting for the push that retires them
        // (local ADR 0035 §8). A singleton: the merge that adds and the session that sends
        // are transient, and must meet in one queue.
        services.TryAddSingleton<ImpedimentEditRetirements>();
        services.TryAddTransient<TaskReplicaMerge>();
        services.TryAddTransient<TaskSyncSession>();

        // And a singleton, because the schedule is the thing it holds: a second
        // worker would be a second timer, and two timers are the overlapping
        // cycles its own guard exists to prevent. It takes the provider rather
        // than the session for the reason the two lines above are transient.
        services.TryAddSingleton<TaskSyncWorker>();

        // The roadmap's first read waits for this loop's first pull (local ADR 0018).
        // Here because the loop is: a head without task replication has no pull to
        // wait for and registers none, and the roadmap reads at once. The replication
        // port the merge and the session take is Roadmap's own, registered by
        // AddRoadmapModule, and both take it as optional.
        services.TryAddSingleton<IRoadmapCatchUp, TaskSyncRoadmapCatchUp>();

        return services;
    }

    /// <summary>
    /// The key the replicated session source is registered under.
    /// <para>
    /// Its value never has to agree with anything: the composite that merges the
    /// sources asks for <c>KeyedService.AnyKey</c>, so every contributor is
    /// collected whatever it called itself and the two projects need share no
    /// constant. What the key <em>does</em> is keep this registration out of the
    /// unkeyed <see cref="IAgentSessionSource"/> slot the composite occupies —
    /// two unkeyed registrations would make the last one win and one of the two
    /// halves of the list vanish silently, and a composite that enumerated the
    /// unkeyed registrations would enumerate itself.
    /// </para>
    /// </summary>
    private const string ReplicatedSessionSourceKey = "sync.replicated";

    /// <summary>The key the replicated activity source is registered under, on
    /// the same terms as <see cref="ReplicatedSessionSourceKey"/>: its own slot
    /// beside the unkeyed <see cref="IAgentActivitySource"/> the composite
    /// occupies, and a value nothing else has to agree with.</summary>
    private const string ReplicatedActivitySourceKey = "sync.replicated-activity";

    /// <summary>Where <see cref="AddSessionSyncStores"/> puts this device's
    /// session-replication progress inside the folder it is given.</summary>
    private const string SessionSyncStateFileName = "session-sync-state.json";

    /// <summary>Where <see cref="AddSessionSyncStores"/> puts the records pulled
    /// from the owner's feed.</summary>
    private const string ReplicatedSessionsFileName = "replicated-sessions.json";

    /// <summary>
    /// The two files session replication keeps, both inside
    /// <paramref name="folderPath"/>.
    /// <para>
    /// Two stores and one call. They are separate stores because one file
    /// carrying both would make the exchanges fail together — see
    /// <see cref="ISessionSyncStateStore"/> — and one call because the thing only
    /// the host knows is <em>where</em>, not how many files that turns into. A
    /// host naming one folder cannot put half of this in the right place and half
    /// somewhere else.
    /// </para>
    /// <para>
    /// A file on every platform and no branch, for the reason
    /// <see cref="TaskSyncStateStoreFactory"/> gives: nothing here is a secret, so
    /// there is no DPAPI decision to take, and a store that forgot its progress
    /// when the process ended would re-push the whole machine on every run — and
    /// lose every record the cursor had already moved past.
    /// </para>
    /// <para>
    /// <c>TryAdd</c>, so a host or a test that has already chosen its own stores
    /// keeps them.
    /// </para>
    /// </summary>
    /// <param name="folderPath">A per-user, per-installation folder. Never the
    /// workspace root: that root can be pointed at a folder a file-sync product
    /// carries, and one device adopting another's session watermark would make it
    /// skip its own unpushed records with nothing on screen to say so.</param>
    public static IServiceCollection AddSessionSyncStores(this IServiceCollection services, string folderPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        services.TryAddSingleton<ISessionSyncStateStore>(
            _ => new FileSessionSyncStateStore(Path.Combine(folderPath, SessionSyncStateFileName)));
        // The store measures its retention against a clock; a host that composed no
        // other clock gets the system one, and one that did keeps its own.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IReplicatedSessionStore>(
            provider => new FileReplicatedSessionStore(
                Path.Combine(folderPath, ReplicatedSessionsFileName),
                provider.GetRequiredService<TimeProvider>()));

        return services;
    }

    /// <summary>
    /// Adds session replication on top of <see cref="AddSyncClient"/>: the record
    /// client, the alias lookup, the exchange, the source that answers with what
    /// other environments reported, and the loop that runs it.
    /// <para>
    /// Call it after <see cref="AddSyncClient"/> and against the same address —
    /// the typed client here chains onto the token pipeline that call registers,
    /// and on its own it would leave every session request anonymous, which the
    /// service answers 401 to.
    /// </para>
    /// <para>
    /// <strong>Opt-in, and only a head that has sessions may opt in.</strong> It
    /// is a separate call from <see cref="AddTaskSyncClient"/> and not a part of
    /// it, because the two answer different questions: a head can have a task
    /// database and no session readers, and the mobile heads have neither. Both
    /// loops answer to the one <c>sync</c> feature key: the split in composition
    /// is about what a head has, not about what a person wants replicated.
    /// </para>
    /// <para>
    /// <strong>What a caller owes it.</strong> An
    /// <see cref="IAgentSessionSource"/> — <c>AddAgentSessionSource()</c> — because
    /// the push reads this machine's sessions through the same port every screen
    /// does and filters to the local ones. An
    /// <see cref="ISessionSyncStateStore"/> and an
    /// <see cref="IReplicatedSessionStore"/>, which
    /// <see cref="AddSessionSyncStores"/> supplies. An
    /// <see cref="Backlog.SharedKernel.IAppFeatureSettings"/> and an
    /// <see cref="IDeviceCredentialStore"/>, which the loop reads as its two
    /// gates. A head missing any of them fails provider validation rather than
    /// silently syncing nothing, which is the failure
    /// <c>SyncClientRegistrationTests</c> exists to keep visible.
    /// </para>
    /// <para>
    /// <see cref="IRepositoryDirectory"/> is deliberately <em>not</em> on that
    /// list. It is asked for with <c>GetService</c> and its absence is an ordinary
    /// arrangement: a head that has not composed Tasks' adapters is a head where no
    /// repository has an alias yet, and a session record then carries the
    /// <c>owner/name</c> the agent recorded — which is true, where an exception at
    /// startup would only be inconvenient.
    /// </para>
    /// <para>
    /// <strong>The loop is part of replication, and a head has to start it.</strong>
    /// <see cref="SessionSyncWorker"/> is a singleton with a timer inside it rather
    /// than an <c>IHostedService</c>, so nothing starts it except the first
    /// resolve. A head that calls this must ask for it once after <c>Build()</c>:
    /// a singleton nobody resolves is a singleton that never runs, and there is
    /// nothing about that failure to notice.
    /// </para>
    /// </summary>
    public static IServiceCollection AddSessionSyncClient(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        return services.AddSessionSyncClient(_ => baseAddress);
    }

    /// <summary>The per-client-address form of <see cref="AddSessionSyncClient(IServiceCollection, Uri)"/>;
    /// pass the same callback <see cref="AddSyncClient(IServiceCollection, Func{IServiceProvider, Uri})"/> got.</summary>
    public static IServiceCollection AddSessionSyncClient(this IServiceCollection services, Func<IServiceProvider, Uri> baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        // Both halves of session replication are bearer, so this one always
        // carries the token. Its own typed client rather than the task one's: they
        // have different batch sizes and different failure codes, so a client can
        // move to its own pipeline without the other. Today both carry the same
        // replication pipeline.
        services.AddHttpClient<SessionSyncClient>((sp, client) => client.BaseAddress = baseAddress(sp))
            .AddHttpMessageHandler<SyncAuthenticationHandler>()
            .WithReplicationPipeline();

        // Optional by construction. GetService rather than GetRequiredService is
        // the whole of the arrangement described above: no directory means no
        // aliases, and no aliases means the recorded owner/name travels.
        services.TryAddSingleton<ISessionRepositoryAliases>(
            sp => new RepositoryDirectorySessionAliases(sp.GetService<IRepositoryDirectory>()));

        // Transient, because it takes the typed client above and
        // IHttpClientFactory owns its lifetime — a singleton here would pin one
        // handler chain for the life of the process. The state it shares with the
        // next cycle is the host's two stores, which is where the singletons
        // belong.
        //
        // Its IAgentActivitySource is an optional constructor parameter, which the
        // container fills from the unkeyed registration when a host made one —
        // AddAgentActivitySource()'s composite — and leaves null when it did not,
        // so a head without transcripts still pushes its records. The composite
        // is acceptable there even though it also answers with what other machines
        // reported: the push filters activity on Origin == Local, the same rule it
        // applies to the sessions themselves, so a record that arrived over the
        // wire never goes back out under this machine's id. Injecting the local
        // reader alone would need a key shared between two projects that
        // deliberately share none.
        services.TryAddTransient<SessionSyncSession>();

        // The read side, contributed to the merged source rather than registered
        // as the source. See ReplicatedSessionSourceKey for why it is keyed at
        // all; the composite that collects it is AddAgentSessionSource()'s, and
        // the two calls may be made in either order because both resolve lazily.
        services.TryAddKeyedSingleton<IAgentSessionSource, ReplicatedAgentSessionSource>(ReplicatedSessionSourceKey);

        // And its twin for the activity port, on identical terms: keyed, so it
        // contributes to AddAgentActivitySource()'s composite rather than
        // replacing the local reader, and the Dashboard's adapter sees the other
        // machines' runs and waits without a line of its own.
        services.TryAddKeyedSingleton<IAgentActivitySource, ReplicatedAgentActivitySource>(ReplicatedActivitySourceKey);

        // And a singleton, because the schedule is the thing it holds: a second
        // worker would be a second timer, and two timers are the overlapping
        // cycles its own guard exists to prevent.
        services.TryAddSingleton<SessionSyncWorker>();

        // What the Sessions pane's update button asks once this machine's records are
        // amended: send them all again. Contributed only where sync is composed, so the
        // records themselves never depend on it.
        services.TryAddSingleton<ISessionRecordPublisher, SessionSyncRecordPublisher>();

        return services;
    }

    /// <summary>Where <see cref="AddAnnotationSyncStore"/> puts this device's
    /// annotation-replication progress inside the folder it is given.</summary>
    private const string AnnotationSyncStateFileName = "annotation-sync-state.json";

    /// <summary>
    /// The one file annotation replication keeps, inside <paramref name="folderPath"/>
    /// — the same per-user, per-installation folder the task and session
    /// progress files sit in, and never the workspace root, for the reason
    /// <see cref="AddSessionSyncStores"/> gives. One file rather than two because
    /// the records themselves live in the Devbook annotation store the host
    /// composes; only the progress is replication's own. <c>TryAdd</c>, so a
    /// host or a test that has already chosen its store keeps it.
    /// </summary>
    public static IServiceCollection AddAnnotationSyncStore(this IServiceCollection services, string folderPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        services.TryAddSingleton<IAnnotationSyncStateStore>(
            _ => new FileAnnotationSyncStateStore(Path.Combine(folderPath, AnnotationSyncStateFileName)));

        return services;
    }

    /// <summary>
    /// Adds annotation replication on top of <see cref="AddSyncClient"/>: the
    /// replica client, the merge, the exchange and the loop that runs it.
    /// <para>
    /// Call it after <see cref="AddSyncClient"/> and against the same address.
    /// Opt-in for the reason the other two are: it takes an
    /// <see cref="IDevbookAnnotationStore"/>, which is the desktop's to compose,
    /// and an <see cref="IAnnotationSyncStateStore"/>, which
    /// <see cref="AddAnnotationSyncStore"/> supplies; a head with neither is not
    /// a head that forgot them. The loop is a singleton with a timer inside it,
    /// so a head that calls this must resolve <see cref="AnnotationSyncWorker"/>
    /// once after <c>Build()</c> or nothing ever runs.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAnnotationSyncClient(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        return services.AddAnnotationSyncClient(_ => baseAddress);
    }

    /// <summary>The per-client-address form of <see cref="AddAnnotationSyncClient(IServiceCollection, Uri)"/>;
    /// pass the same callback <see cref="AddSyncClient(IServiceCollection, Func{IServiceProvider, Uri})"/> got.</summary>
    public static IServiceCollection AddAnnotationSyncClient(this IServiceCollection services, Func<IServiceProvider, Uri> baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        services.AddHttpClient<AnnotationSyncClient>((sp, client) => client.BaseAddress = baseAddress(sp))
            .AddHttpMessageHandler<SyncAuthenticationHandler>()
            .WithReplicationPipeline();

        // Transient over the typed client above, for the reason the task merge
        // and session are; the state they share is the host's store.
        services.TryAddTransient<AnnotationReplicaMerge>();
        services.TryAddTransient<AnnotationSyncSession>();

        services.TryAddSingleton<AnnotationSyncWorker>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="LocalDevelopmentPairing"/> and the client it sends
    /// on, for the development harnesses the AppHost asks to pair themselves.
    /// Development only; the shipped heads never call it.
    /// <para>
    /// The client, <see cref="LocalDevelopmentPairing.HttpClientName"/>, has the
    /// host's default pipeline taken off and none put back. The auto-pair's own
    /// loop is the readiness poll and decides what may be sent again; a
    /// pipeline underneath it would retry a register or a redeem on a timeout,
    /// which is the one thing that loop refuses to do (guideline ADR 0015: no
    /// nested retries, never retry a non-idempotent command). No
    /// <see cref="SyncAuthenticationHandler"/> either: register, redeem and
    /// token are anonymous, and the one bearer call — the desktop harness
    /// minting a pairing code for the phone (<c>POST devices/codes</c>) — carries
    /// a token the loop has just traded the desktop's own credential for, set on
    /// that one request.
    /// </para>
    /// </summary>
    public static IServiceCollection AddLocalDevelopmentPairing(this IServiceCollection services, Func<IServiceProvider, Uri> baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);

        var client = services.AddHttpClient(LocalDevelopmentPairing.HttpClientName, (sp, http) =>
        {
            http.BaseAddress = baseAddress(sp);
            http.Timeout = LocalPairingCallBudget;
        });

#pragma warning disable EXTEXP0001 // See WithReplicationPipeline.
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<LocalDevelopmentPairing>();

        return services;
    }

    /// <summary>How long one auto-pair call is waited for. With no pipeline
    /// this is the call's only bound; a call that runs past it is one the loop
    /// treats as possibly landed.</summary>
    internal static readonly TimeSpan LocalPairingCallBudget = TimeSpan.FromSeconds(30);

    /// <summary>How long one replication attempt is waited for: the hundred
    /// seconds a plain <see cref="HttpClient"/> allows, sized for a full batch
    /// against a cold replica rather than for the typical call.</summary>
    internal static readonly TimeSpan ReplicationAttemptBudget = TimeSpan.FromSeconds(100);

    /// <summary>How long one replication call is waited for across its retries.
    /// Only pulls retry, so a push never sees more than one
    /// <see cref="ReplicationAttemptBudget"/>.</summary>
    internal static readonly TimeSpan ReplicationTotalBudget = TimeSpan.FromMinutes(5);

    /// <summary>How far past <see cref="ReplicationTotalBudget"/> a replication
    /// client's own <see cref="HttpClient.Timeout"/> sits: room for the token
    /// fetch in front of the pipeline, so the pipeline's total is what ends a slow
    /// call and the client's timeout only bounds what the pipeline cannot.</summary>
    internal static readonly TimeSpan ReplicationClientMargin = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Takes the host's default pipeline off a replication client and puts its own
    /// on.
    /// <para>
    /// The host's <c>AddServiceDefaults()</c> puts the standard resilience pipeline
    /// on every client with defaults meant for a service talking to a service:
    /// retries for every method, ten seconds an attempt, thirty in all. Neither
    /// half fits a push. A push that runs past ten seconds is cancelled and sent
    /// again, and a 503 on one does not say the batch did not land. A second send
    /// of a batch that landed reaches a replica already holding every item, which
    /// refuses each as not later and answers an accepted count short of what
    /// landed, so the session reports as refused what the service kept. Guideline
    /// ADR 0015: never retry a non-idempotent command.
    /// </para>
    /// </summary>
    private static IHttpClientBuilder WithReplicationPipeline(this IHttpClientBuilder client)
    {
        // The experimental attribute on RemoveAllResilienceHandlers is a warning
        // about the API's shape, not its behaviour; it is the one published way
        // to take the host's default pipeline off a client.
#pragma warning disable EXTEXP0001
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        client.AddStandardResilienceHandler(ConfigureReplicationPipeline);

        // HttpClient.Timeout wraps the whole chain, the token fetch in
        // SyncAuthenticationHandler included. The resilience handler leaves it
        // infinite so the pipeline's total is the budget; this puts a finite
        // outer bound back (guideline ADR 0015: no infinite timeouts), past the
        // pipeline's total so it never clips it. After the handler, so it is the
        // setting that stands.
        client.ConfigureHttpClient(http => http.Timeout = ReplicationTotalBudget + ReplicationClientMargin);

        return client;
    }

    /// <summary>The options <see cref="WithReplicationPipeline"/> applies, apart
    /// so a test can hold the pipeline to them without composing a host.</summary>
    internal static void ConfigureReplicationPipeline(HttpStandardResilienceOptions options)
    {
        // The push goes out once; the pull is a read and still retries.
        options.Retry.DisableForUnsafeHttpMethods();
        options.AttemptTimeout.Timeout = ReplicationAttemptBudget;
        options.TotalRequestTimeout.Timeout = ReplicationTotalBudget;
        // The circuit breaker must sample over at least twice the attempt or the
        // options fail validation. Its minimum throughput is a hundred calls,
        // which one device's sync loop does not reach in that window, so the
        // window is set for the validator, not the breaker.
        options.CircuitBreaker.SamplingDuration = ReplicationAttemptBudget * 2;
    }
}
