using System.Net;

using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// What a host gets for calling each half of the composition, asserted the way
/// a host finds out: by building the provider with the validation the generic
/// host turns on in Development.
/// <para>
/// These build a provider rather than reading the descriptors back, because the
/// failure this covers is not a missing registration — it is a registration
/// whose constructor no host service can satisfy, and only
/// <see cref="ServiceProviderOptions.ValidateOnBuild"/> says so. A mobile head
/// has no <see cref="ITaskRepository"/> and never will; when the task-sync
/// services were registered for every caller, both mobile hosts died inside
/// <c>Build()</c> with a resolution failure no unit test could see.
/// </para>
/// </summary>
public class SyncClientRegistrationTests
{
    private static readonly Uri SyncAddress = new("https://sync.example");

    /// <summary>The validation the generic host applies in Development, which is
    /// where the crash this covers happened.</summary>
    private static ServiceProvider Build(IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

    /// <summary>
    /// The mobile shape: a credential store and nothing else of Tasks. Both
    /// mobile heads compose exactly this, so the pairing surface has to stand on
    /// its own.
    /// </summary>
    [Fact]
    public void A_host_with_no_task_repository_composes_the_pairing_surface()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.NotNull(provider.GetRequiredService<DevicePairingClient>());
        Assert.NotNull(provider.GetRequiredService<SyncTokenProvider>());
    }

    /// <summary>
    /// Task replication is opt-in, and this is what "opt-in" has to mean: a host
    /// that did not ask for it does not carry it. Asserted as an absence rather
    /// than through the test above, because a registration that happens to be
    /// satisfiable in some other host would slip past a validation check.
    /// </summary>
    [Fact]
    public void The_pairing_surface_alone_brings_no_task_sync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.Null(provider.GetService<TaskSyncSession>());
        Assert.Null(provider.GetService<TaskReplicaMerge>());
        Assert.Null(provider.GetService<TaskSyncClient>());

        // The background loop comes with replication and not with pairing. A
        // head with no local task database has nothing for it to exchange, and a
        // timer that woke up every five minutes to find that out would be a
        // timer on a phone's battery.
        Assert.Null(provider.GetService<TaskSyncWorker>());
    }

    /// <summary>
    /// The desktop shape: a repository, a sync-state store and a feature store of
    /// the host's choosing, and the opt-in call on top of the pairing surface.
    /// <para>
    /// The feature store is on the list because the loop reads it. Opting into
    /// replication now means opting into something that asks whether the person
    /// wants it, so a head with no <c>IAppFeatureSettings</c> fails validation
    /// here rather than at its own startup — which is what this whole file is
    /// for.
    /// </para>
    /// </summary>
    [Fact]
    public void A_host_that_opts_in_composes_the_session_and_the_loop()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<ITaskRepository>(new InMemoryTaskStore());
        services.AddSingleton<ITaskSyncStateStore>(new InMemoryTaskSyncStateStore());
        services.AddSyncClient(SyncAddress);
        services.AddTaskSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.NotNull(provider.GetRequiredService<TaskSyncSession>());
        Assert.NotNull(provider.GetRequiredService<DevicePairingClient>());

        using var worker = provider.GetRequiredService<TaskSyncWorker>();
        Assert.NotNull(worker);
    }

    /// <summary>
    /// The My Day preferences port needs no registration of its own in the sync client
    /// (local ADR 0032, §3): a host that registers it has the merge the container builds
    /// hand it the document, and the session and the loop compose with it as well.
    /// </summary>
    [Fact]
    public async Task A_host_that_keeps_my_day_preferences_has_the_merge_hand_them_over()
    {
        var myDay = new RecordingMyDayPreferencesReplication();
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<ITaskRepository>(new InMemoryTaskStore());
        services.AddSingleton<ITaskSyncStateStore>(new InMemoryTaskSyncStateStore());
        services.AddSingleton<Backlog.Modules.Tasks.Abstractions.Services.IMyDayPreferencesReplication>(myDay);
        services.AddSyncClient(SyncAddress);
        services.AddTaskSyncClient(SyncAddress);

        using var provider = Build(services);

        var sent = new Backlog.Modules.Tasks.Abstractions.DataTransferObjects.MyDayPreferencesCopyDto(
            """{"clearsOvernight":false}""", new DateTimeOffset(2026, 10, 15, 8, 0, 0, TimeSpan.Zero));

        var merged = await provider.GetRequiredService<TaskReplicaMerge>().ApplyAsync(
            [TaskChanges.Record(MyDayReplicaDocuments.ToChange(sent), Guid.NewGuid(), 1)],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, merged.Applied);
        Assert.Equal(sent, Assert.Single(myDay.Offered));
        Assert.NotNull(provider.GetRequiredService<TaskSyncSession>());

        using var worker = provider.GetRequiredService<TaskSyncWorker>();
        Assert.True(myDay.HasListeners);
    }

    /// <summary>
    /// The iteration settings port needs no registration of its own in the sync client
    /// either (local ADR 0036, §4): a host that registers it has the merge the container
    /// builds hand it the document, and the session and the loop compose with it.
    /// </summary>
    [Fact]
    public async Task A_host_that_keeps_iteration_settings_has_the_merge_hand_them_over()
    {
        var iteration = new RecordingIterationSettingsReplication();
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<ITaskRepository>(new InMemoryTaskStore());
        services.AddSingleton<ITaskSyncStateStore>(new InMemoryTaskSyncStateStore());
        services.AddSingleton<Backlog.Modules.Progress.Abstractions.Services.IIterationSettingsReplication>(iteration);
        services.AddSyncClient(SyncAddress);
        services.AddTaskSyncClient(SyncAddress);

        using var provider = Build(services);

        var sent = new Backlog.Modules.Progress.Abstractions.DataTransferObjects.IterationSettingsCopyDto(
            """{"length":"two-weeks","twoWeekStart":"2026-10-19"}""", new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

        var merged = await provider.GetRequiredService<TaskReplicaMerge>().ApplyAsync(
            [TaskChanges.Record(IterationReplicaDocuments.ToChange(sent), Guid.NewGuid(), 1)],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, merged.Applied);
        Assert.Equal(sent, Assert.Single(iteration.Offered));
        Assert.NotNull(provider.GetRequiredService<TaskSyncSession>());

        using var worker = provider.GetRequiredService<TaskSyncWorker>();
        Assert.True(iteration.HasListeners);
    }

    /// <summary>
    /// Opting in without choosing a sync-state store leaves both the session and
    /// the loop registered and unconstructable — the case Settings'
    /// <c>ResolveTaskSync</c> guard exists for. It is pinned here so that guard
    /// keeps describing something real: the container throws rather than
    /// answering null.
    /// <para>
    /// The worker is asserted as well as the session because the worker is what
    /// the screen now asks for, and it only shares the session's fate by taking
    /// the same store. If it ever stopped taking one, the screen would start
    /// getting an answer on a head that cannot replicate.
    /// </para>
    /// </summary>
    [Fact]
    public void Opting_in_without_a_sync_state_store_leaves_the_session_unconstructable()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<ITaskRepository>(new InMemoryTaskStore());
        services.AddSyncClient(SyncAddress);
        services.AddTaskSyncClient(SyncAddress);

        // No ValidateOnBuild here: the point is what a host that skipped its
        // store sees at resolution, which is the shape the screen guards.
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetService<TaskSyncSession>());
        Assert.Throws<InvalidOperationException>(() => provider.GetService<TaskSyncWorker>());
    }

    // --- Session replication ---------------------------------------------------

    /// <summary>
    /// Session replication is opt-in on its own terms, and this is what that has to
    /// mean: a host that composed task sync but not session sync carries none of it.
    /// The two are separate calls because the heads differ — a head can have a task
    /// database and no session readers, and the mobile heads have neither.
    /// </summary>
    [Fact]
    public void Task_replication_alone_brings_no_session_replication()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<ITaskRepository>(new InMemoryTaskStore());
        services.AddSingleton<ITaskSyncStateStore>(new InMemoryTaskSyncStateStore());
        services.AddSyncClient(SyncAddress);
        services.AddTaskSyncClient(SyncAddress);

        using var provider = Build(services);

        Assert.Null(provider.GetService<SessionSyncSession>());
        Assert.Null(provider.GetService<SessionSyncClient>());
        Assert.Null(provider.GetService<SessionSyncWorker>());
    }

    /// <summary>
    /// The desktop shape for sessions: the two stores, a feature store, a credential
    /// store and a session source, plus the opt-in call. Built with the validation
    /// the generic host applies in Development, because the failure this covers is a
    /// registration whose constructor no host service can satisfy — which only
    /// <see cref="ServiceProviderOptions.ValidateOnBuild"/> says out loud.
    /// </summary>
    [Fact]
    public void A_host_that_opts_into_session_sync_composes_the_exchange_and_the_loop()
    {
        using var provider = Build(SessionHost());

        Assert.NotNull(provider.GetRequiredService<SessionSyncSession>());

        using var worker = provider.GetRequiredService<SessionSyncWorker>();
        Assert.NotNull(worker);
    }

    /// <summary>
    /// Opting in without choosing the two stores leaves the exchange and the loop
    /// registered and unconstructable, which is the same shape task sync has and the
    /// same shape a settings screen has to guard. Pinned so that guard keeps
    /// describing something real: the container throws rather than answering null.
    /// </summary>
    [Fact]
    public void Opting_in_without_the_session_stores_leaves_the_exchange_unconstructable()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<IAgentSessionSource>(new StubAgentSessionSource());
        services.AddSyncClient(SyncAddress);
        services.AddSessionSyncClient(SyncAddress);

        // No ValidateOnBuild here: the point is what a host that skipped its stores
        // sees at resolution.
        using var provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetService<SessionSyncSession>());
        Assert.Throws<InvalidOperationException>(() => provider.GetService<SessionSyncWorker>());
    }

    /// <summary>
    /// The alias directory is deliberately optional, and this is the host that
    /// proves it: no <c>IRepositoryDirectory</c> anywhere, and the exchange still
    /// composes. A head that has not composed Tasks' adapters is a head where no
    /// repository has an alias yet, which is an arrangement rather than a fault —
    /// the recorded <c>owner/name</c> travels instead.
    /// </summary>
    [Fact]
    public void Session_sync_composes_without_a_repository_directory()
    {
        using var provider = Build(SessionHost());

        Assert.NotNull(provider.GetRequiredService<ISessionRepositoryAliases>());
        Assert.Null(provider.GetRequiredService<ISessionRepositoryAliases>().AliasFor("jsdotnet/backlog"));
    }

    /// <summary>
    /// The replicated source is registered under a key rather than as the plain
    /// <see cref="IAgentSessionSource"/>, so it contributes to the merged source
    /// instead of replacing whatever the host registered. Two unkeyed registrations
    /// would make the last line win, and half the list would vanish with nothing
    /// failing to say so.
    /// </summary>
    [Fact]
    public void The_replicated_source_contributes_rather_than_replacing()
    {
        using var provider = Build(SessionHost());

        // The host's own registration is what an unkeyed ask still answers with.
        Assert.IsType<StubAgentSessionSource>(provider.GetRequiredService<IAgentSessionSource>());

        // And the replicated one is there beside it, for a composite to collect.
        var keyed = provider.GetKeyedServices<IAgentSessionSource>(KeyedService.AnyKey).ToList();
        Assert.Contains(keyed, source => source is ReplicatedAgentSessionSource);
    }

    /// <summary>
    /// The activity twin is registered the same way and for the same reason: a
    /// keyed contributor, never the plain <see cref="IAgentActivitySource"/>. This
    /// host composed no activity source of its own, so the unkeyed ask answers
    /// nothing — which is also what lets the exchange compose without one, since
    /// its activity dependency is optional.
    /// </summary>
    [Fact]
    public void The_replicated_activity_source_contributes_rather_than_replacing()
    {
        using var provider = Build(SessionHost());

        Assert.Null(provider.GetService<IAgentActivitySource>());

        var keyed = provider.GetKeyedServices<IAgentActivitySource>(KeyedService.AnyKey).ToList();
        Assert.Contains(keyed, source => source is ReplicatedAgentActivitySource);
    }

    /// <summary>
    /// And the exchange composes either way — with the activity port registered
    /// and without it. The dependency is optional so a head without transcripts
    /// still pushes its records; what the pusher does with a source once it has
    /// one is <c>SessionSyncSessionTests</c>' subject.
    /// </summary>
    [Fact]
    public void The_exchange_composes_with_an_activity_source_as_well_as_without()
    {
        var services = SessionHost();
        services.AddSingleton<IAgentActivitySource>(new StubAgentActivitySource());

        using var provider = Build(services);

        Assert.NotNull(provider.GetRequiredService<SessionSyncSession>());
    }

    // --- The replication clients' pipeline ---------------------------------------

    /// <summary>
    /// A push that fails is sent once. A 503 on a push does not say the batch did
    /// not land, and a second send of a batch that did reaches a replica already
    /// holding every item, which refuses them all as not later and answers an
    /// accepted count short of what landed. Asserted with the host's default
    /// pipeline applied, because that default is what used to send it again.
    /// </summary>
    [Theory]
    [InlineData(nameof(TaskSyncClient))]
    [InlineData(nameof(SessionSyncClient))]
    [InlineData(nameof(AnnotationSyncClient))]
    public async Task A_push_that_fails_is_sent_once(string client)
    {
        var wire = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var provider = ReplicationHost(client, wire);

        var pushed = await PushAsync(provider, client);

        Assert.True(pushed.IsFailure);
        Assert.Single(wire.Requests);
    }

    /// <summary>A pull is a read and safe to send again, so a transient refusal is
    /// still retried: the pipeline is narrowed for the push, not taken away.</summary>
    [Theory]
    [InlineData(nameof(TaskSyncClient))]
    [InlineData(nameof(SessionSyncClient))]
    [InlineData(nameof(AnnotationSyncClient))]
    public async Task A_pull_that_fails_is_retried(string client)
    {
        var wire = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var provider = ReplicationHost(client, wire);

        var pulled = await PullAsync(provider, client);

        Assert.True(pulled.IsFailure);
        Assert.True(wire.Requests.Count > 1, $"Expected a retry, saw {wire.Requests.Count} request(s).");
    }

    /// <summary>A push of a full batch against a cold replica can run past the
    /// host default's ten seconds an attempt, and a push cancelled there is a
    /// push that may still land. One long attempt is waited out instead.</summary>
    [Theory]
    [InlineData(nameof(TaskSyncClient))]
    [InlineData(nameof(SessionSyncClient))]
    [InlineData(nameof(AnnotationSyncClient))]
    public void A_push_is_given_one_long_attempt(string client)
    {
        using var provider = ReplicationHost(client, new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)));

        var options = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>().Get(PipelineOptions(client));
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(client);

        Assert.True(options.AttemptTimeout.Timeout >= TimeSpan.FromSeconds(100), $"Attempt timeout was {options.AttemptTimeout.Timeout}.");
        Assert.True(options.TotalRequestTimeout.Timeout >= options.AttemptTimeout.Timeout);
        Assert.True(options.CircuitBreaker.SamplingDuration >= options.AttemptTimeout.Timeout * 2);

        // HttpClient.Timeout wraps the whole handler chain, the pipeline and the
        // token fetch included, so it has to outlast the pipeline's own total or
        // it is the budget that applies. Finite, though: guideline ADR 0015 has
        // no infinite timeouts.
        Assert.True(http.Timeout > options.TotalRequestTimeout.Timeout, $"HttpClient.Timeout was {http.Timeout}.");
        Assert.NotEqual(Timeout.InfiniteTimeSpan, http.Timeout);
    }

    /// <summary>A push the pipeline gives up on ends as the clients' existing
    /// unreachable result, the way a refused connection does, rather than as the
    /// pipeline's own exception escaping into the loop's catch-all.</summary>
    [Theory]
    [InlineData(nameof(TaskSyncClient))]
    [InlineData(nameof(SessionSyncClient))]
    [InlineData(nameof(AnnotationSyncClient))]
    public async Task A_push_the_pipeline_times_out_is_an_unreachable_result(string client)
    {
        using var provider = ReplicationHost(client, new HangingHandler(), options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromMilliseconds(100);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMilliseconds(500);
        });

        var pushed = await PushAsync(provider, client);

        Assert.True(pushed.IsFailure);
        Assert.Equal(DevicePairingClient.UnreachableCode, pushed.Error.Code);
    }

    /// <summary>The options name <c>AddStandardResilienceHandler</c> gives a typed
    /// client's pipeline: the client's type name, which is its client name.</summary>
    private static string PipelineOptions(string client) => client + "-standard";

    /// <summary>
    /// The three replication clients as a desktop head composes them, under the
    /// pipeline <c>AddServiceDefaults()</c> puts on every client, with only the
    /// wire under <paramref name="client"/> swapped and its retry delay zeroed so a
    /// retry costs no time. No credential is stored, so the requests go out
    /// unauthenticated and the token endpoint is never asked. A test that needs a
    /// short clock moves the budgets on that pipeline's own options through
    /// <paramref name="adjust"/>.
    /// </summary>
    private static ServiceProvider ReplicationHost(string client, HttpMessageHandler wire, Action<HttpStandardResilienceOptions>? adjust = null)
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSyncClient(SyncAddress);
        services.AddTaskSyncClient(SyncAddress);
        services.AddSessionSyncClient(SyncAddress);
        services.AddAnnotationSyncClient(SyncAddress);
        services.AddHttpClient(client).ConfigurePrimaryHttpMessageHandler(() => wire);
        services.PostConfigure<HttpStandardResilienceOptions>(PipelineOptions(client), options =>
        {
            options.Retry.Delay = TimeSpan.Zero;
            adjust?.Invoke(options);
        });

        return services.BuildServiceProvider();
    }

    private static async Task<Result> PushAsync(IServiceProvider provider, string client)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        return client switch
        {
            nameof(TaskSyncClient) => await provider.GetRequiredService<TaskSyncClient>().PushAsync([], cancellationToken),
            nameof(SessionSyncClient) => await provider.GetRequiredService<SessionSyncClient>().PushAsync([], cancellationToken),
            nameof(AnnotationSyncClient) => await provider.GetRequiredService<AnnotationSyncClient>().PushAsync([], cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(client), client, null)
        };
    }

    private static async Task<Result> PullAsync(IServiceProvider provider, string client)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        return client switch
        {
            nameof(TaskSyncClient) => await provider.GetRequiredService<TaskSyncClient>().PullAsync(null, cancellationToken: cancellationToken),
            nameof(SessionSyncClient) => await provider.GetRequiredService<SessionSyncClient>().PullAsync(null, cancellationToken: cancellationToken),
            nameof(AnnotationSyncClient) => await provider.GetRequiredService<AnnotationSyncClient>().PullAsync(null, cancellationToken: cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(client), client, null)
        };
    }

    /// <summary>The sync service not answering: the request waits until it is
    /// cancelled.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    /// <summary>The composition a desktop head makes, with the two session stores in
    /// memory so nothing here writes a file.</summary>
    private static ServiceCollection SessionHost()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSingleton<IAppFeatureSettings>(new StubFeatureSettings(enabled: true));
        services.AddSingleton<IAgentSessionSource>(new StubAgentSessionSource());
        services.AddSingleton<ISessionSyncStateStore>(new InMemorySessionSyncStateStore());
        services.AddSingleton<IReplicatedSessionStore>(new InMemoryReplicatedSessionStore());
        services.AddSyncClient(SyncAddress);
        services.AddSessionSyncClient(SyncAddress);

        return services;
    }
}
