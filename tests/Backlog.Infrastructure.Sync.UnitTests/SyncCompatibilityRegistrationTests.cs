using System.Net;

using Backlog.Infrastructure.Sync.Annotations;
using Backlog.Infrastructure.Sync.DevbookSubjects;
using Backlog.Infrastructure.Sync.Extensions;
using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// Every sync client a head composes holds its requests while the service is
/// newer than the app — the token request and a host's own data client
/// included. Asserted through the registrations rather than one handler, because
/// the failure this guards is a client that was registered without it and so
/// goes on syncing against a contract that moved.
/// </summary>
public sealed class SyncCompatibilityRegistrationTests
{
    private static readonly Uri SyncAddress = new("https://sync.example");

    private static readonly DeviceCredential Paired =
        new(Guid.NewGuid(), Guid.NewGuid(), "Workshop PC", "a-registration-credential");

    private const string HostDataClient = "host-data";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(nameof(DevicePairingClient))]
    [InlineData(nameof(TaskSyncClient))]
    [InlineData(nameof(InboxAttachmentSyncSource))]
    [InlineData(nameof(SessionSyncClient))]
    [InlineData(nameof(AnnotationSyncClient))]
    [InlineData(nameof(DevbookSubjectSyncClient))]
    public async Task A_sync_client_sends_nothing_to_a_newer_service(string client)
    {
        using var host = Host.Create(service: "9.0.0");

        var answer = await host.CallAsync(client);

        Assert.True(answer.IsFailure);
        Assert.Equal(SyncErrorCodes.ClientOutdated, answer.Error.Code);
        Assert.Equal(SyncCompatibility.UpdateRequiredMessage, answer.Error.Message);
        Assert.All(host.Wire.Requests, request => Assert.Equal("/", request.Path));
    }

    [Fact]
    public async Task The_token_request_is_held_too()
    {
        using var host = Host.Create(service: "9.0.0");

        var token = await host.Provider.GetRequiredService<SyncTokenProvider>().GetAccessTokenAsync(Cancellation);

        Assert.Null(token);
        Assert.All(host.Wire.Requests, request => Assert.Equal("/", request.Path));
    }

    /// <summary>The phone's data client is the host's own, chained with the same
    /// call the shared clients use.</summary>
    [Fact]
    public async Task A_host_client_on_the_sync_pipeline_is_held_too()
    {
        using var host = Host.Create(service: "9.0.0");

        using var response = await host.Provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(HostDataClient)
            .GetAsync("/api/sync/inbox", Cancellation);

        Assert.Equal((HttpStatusCode)426, response.StatusCode);
        Assert.All(host.Wire.Requests, request => Assert.Equal("/", request.Path));
    }

    [Fact]
    public async Task A_service_no_newer_than_the_app_is_synced_with()
    {
        using var host = Host.Create(service: "0.1.0");

        var answer = await host.CallAsync(nameof(TaskSyncClient));

        Assert.True(answer.IsSuccess, answer.IsFailure ? answer.Error.Message : null);
        Assert.Contains(host.Wire.Requests, request => request.Path == SyncRoutes.Absolute(SyncRoutes.Tasks));
    }

    /// <summary>A head's own version is the one compared, whichever order it
    /// registered it in.</summary>
    [Fact]
    public void The_head_names_the_version_compared()
    {
        var services = new ServiceCollection();
        services.AddSyncAppVersion(_ => "0.1.122.0");
        services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore());
        services.AddSyncClient(SyncAddress);

        using var provider = services.BuildServiceProvider();

        Assert.Equal("0.1.122.0", provider.GetRequiredService<SyncCompatibility>().AppVersion);
    }

    private sealed class Host : IDisposable
    {
        private Host(ServiceProvider provider, StubHttpMessageHandler wire)
        {
            Provider = provider;
            Wire = wire;
        }

        public ServiceProvider Provider { get; }

        /// <summary>Every client's wire: the root reports the version given,
        /// the token endpoint mints a token, and every data route answers an
        /// empty success.</summary>
        public StubHttpMessageHandler Wire { get; }

        public static Host Create(string service)
        {
            var wire = new StubHttpMessageHandler((request, _) => request.RequestUri?.AbsolutePath switch
            {
                "/" => StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"service":"Backlog Sync","version":"{{service}}","commit":""}"""),
                var path when path == SyncRoutes.Absolute(SyncRoutes.DeviceToken) => StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"accessToken":"token-0","expiresAt":"2099-01-01T00:00:00Z","tokenType":"Bearer"}"""),
                _ => StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}""")
            });

            var services = new ServiceCollection();
            services.AddSingleton<IDeviceCredentialStore>(new InMemoryDeviceCredentialStore(Paired));
            services.AddSyncAppVersion(_ => "0.1.122");
            services.AddSyncClient(SyncAddress);
            services.AddTaskSyncClient(SyncAddress);
            services.AddSessionSyncClient(SyncAddress);
            services.AddAnnotationSyncClient(SyncAddress);
            services.AddDevbookSubjectSyncClient(SyncAddress);
            services.AddHttpClient(HostDataClient, http => http.BaseAddress = SyncAddress).AddSyncPipeline();

            foreach (var name in new[]
            {
                SyncTokenProvider.HttpClientName,
                nameof(DevicePairingClient),
                nameof(TaskSyncClient),
                nameof(InboxAttachmentSyncSource),
                nameof(SessionSyncClient),
                nameof(AnnotationSyncClient),
                nameof(DevbookSubjectSyncClient),
                HostDataClient
            })
            {
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => wire);
            }

            return new Host(services.BuildServiceProvider(), wire);
        }

        public async Task<Result> CallAsync(string client) => client switch
        {
            nameof(DevicePairingClient) => (Result)await Provider.GetRequiredService<DevicePairingClient>().GetStatusAsync(Cancellation),
            nameof(TaskSyncClient) => (Result)await Provider.GetRequiredService<TaskSyncClient>().PushAsync([], Cancellation),
            nameof(InboxAttachmentSyncSource) => (Result)await Provider.GetRequiredService<InboxAttachmentSyncSource>().FetchAsync(Guid.NewGuid(), Cancellation),
            nameof(SessionSyncClient) => (Result)await Provider.GetRequiredService<SessionSyncClient>().PushAsync([], Cancellation),
            nameof(AnnotationSyncClient) => (Result)await Provider.GetRequiredService<AnnotationSyncClient>().PushAsync([], Cancellation),
            nameof(DevbookSubjectSyncClient) => (Result)await Provider.GetRequiredService<DevbookSubjectSyncClient>().PushAsync([], Cancellation),
            _ => throw new ArgumentOutOfRangeException(nameof(client), client, null)
        };

        public void Dispose() => Provider.Dispose();
    }
}
