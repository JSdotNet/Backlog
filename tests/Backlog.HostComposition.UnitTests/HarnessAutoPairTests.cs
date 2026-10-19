extern alias DesktopHarness;
extern alias MobileHarness;

using System.Collections.Concurrent;
using System.Net;
using System.Runtime.Versioning;
using System.Text;

using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The development auto-pair, as each browser harness composes it: off unless
/// the AppHost turns <c>Harness:AutoPair</c> on, and when it is on, the desktop
/// registering itself and the phone pairing under the desktop's owner through
/// the harness's own credential store.
/// <para>
/// The wire is replaced under every client the host's factory builds, so nothing
/// leaves the test; what reaches it is what the harness would have sent to
/// <c>sync</c>. Every store the auto-pair writes is the test's own — a credential
/// or a feature switch written into the process's shared isolation folder would
/// change what every other harness test in the run starts from.
/// </para>
/// </summary>
public sealed class HarnessAutoPairTests
{
    private static readonly Guid Owner = Guid.Parse("6f1a2b3c-0000-4000-8000-0000000000a1");
    private static readonly Guid DesktopDevice = Guid.Parse("6f1a2b3c-0000-4000-8000-0000000000d1");
    private static readonly Guid PhoneDevice = Guid.Parse("6f1a2b3c-0000-4000-8000-0000000000f1");

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private sealed class Harness<TEntryPoint>(string? autoPair, SyncWire wire, Action<IServiceCollection>? services = null)
        : IsolatedHarnessFactory<TEntryPoint>
        where TEntryPoint : class
    {
        protected override void ConfigureHarness(IWebHostBuilder builder)
        {
            if (autoPair is not null) builder.UseSetting("Harness:AutoPair", autoPair);

            builder.ConfigureTestServices(collection =>
            {
                collection.ConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(handlers => handlers.PrimaryHandler = wire));
                services?.Invoke(collection);
            });
        }
    }

    /// <summary>
    /// Criterion 6: a harness the AppHost did not ask — every host test, which
    /// runs in Development, and any run that opted out — composes no auto-pair at
    /// all, so nothing about it can reach <c>sync</c>.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void The_desktop_harness_registers_nothing_unless_the_apphost_asks(string? autoPair)
    {
        var wire = new SyncWire(_ => SyncWire.Unavailable());
        using var harness = new Harness<DesktopHarness::Program>(autoPair, wire);

        Assert.Null(harness.Services.GetService<LocalDevelopmentPairing>());
        Assert.DoesNotContain(harness.Services.GetServices<IHostedService>(), service => service is DesktopHarness::Backlog.Desktop.WebHarness.DesktopAutoRegistration);
        Assert.Empty(wire.DeviceRequests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void The_phone_harness_pairs_nothing_unless_the_apphost_asks(string? autoPair)
    {
        var wire = new SyncWire(_ => SyncWire.Unavailable());
        using var harness = new Harness<MobileHarness::Program>(autoPair, wire);

        Assert.Null(harness.Services.GetService<LocalDevelopmentPairing>());
        Assert.DoesNotContain(harness.Services.GetServices<IHostedService>(), service => service is MobileHarness::Backlog.Mobile.WebHarness.PhoneAutoPairing);
        Assert.Empty(wire.DeviceRequests);
    }

    /// <summary>
    /// Asked, the desktop registers once through the real register route, keeps
    /// the credential in its own store, and turns its own <c>sync</c> switch on so
    /// it pushes what the phone is paired to see (runner decision Q3).
    /// </summary>
    [Fact]
    public async Task The_desktop_harness_registers_itself_and_turns_sync_on_when_the_apphost_asks()
    {
        using var folder = new TemporaryFolder();
        var store = new InMemoryDeviceCredentialStore();
        // A file of the test's own: the store's one-argument constructor is the
        // per-user features.json every Debug head on this machine reads.
        var features = new AppFeatureSettingsStore(Backlog.Desktop.UI.Shell.AppFeatures.All, folder.File("desktop", "feature.settings.json"));
        var saved = SavedOnce(store);
        var wire = new SyncWire(request =>
            request.Path.EndsWith(SyncRoutes.DeviceToken, StringComparison.Ordinal) ? SyncWire.CredentialInvalid()
            : request.Path.EndsWith(SyncRoutes.RegisterDevice, StringComparison.Ordinal) ? SyncWire.Registration(DesktopDevice)
            : SyncWire.Unavailable());

        using var harness = new Harness<DesktopHarness::Program>("true", wire, services =>
        {
            services.AddSingleton<IDeviceCredentialStore>(store);
            services.AddSingleton<IAppFeatureSettings>(features);
        });

        harness.StartServer();

        var credential = await saved.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal((Owner, DesktopDevice, LocalDevelopmentPairing.DesktopDeviceName), (credential.OwnerId, credential.DeviceId, credential.DeviceName));
        Assert.Single(wire.DeviceRequests, request => request.Path.EndsWith(SyncRoutes.RegisterDevice, StringComparison.Ordinal));
        Assert.True(features.IsEnabled(SyncFeatures.Sync));
    }

    /// <summary>
    /// Asked, the phone asks the desktop for a pairing code through the handoff
    /// folder the AppHost passed — the test answers as the desktop — and redeems
    /// it as a device of its own under the desktop's owner. No credential crosses:
    /// the phone sends nothing to the token or code routes. Its stand-up seed is
    /// forgotten before the credential lands, so only synced data shows (R2).
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task The_phone_harness_pairs_with_a_code_the_desktop_handed_over_when_the_apphost_asks()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The handoff is a DPAPI envelope.");

        using var folder = new TemporaryFolder();
        var handoff = folder.Folder("handoff");
        var phone = new DpapiDeviceCredentialStore(folder.File("phone", "device-credential.json"));
        var views = folder.File("phone", "views.db");
        var saved = SavedOnce(phone);
        var wire = new SyncWire(request =>
            request.Path.EndsWith(SyncRoutes.RedeemPairingCode, StringComparison.Ordinal) ? SyncWire.Registration(PhoneDevice)
            : SyncWire.Unavailable());

        using var harness = new Harness<MobileHarness::Program>("true", wire, services =>
        {
            services.AddSingleton<IDeviceCredentialStore>(phone);
            services.AddSingleton(new MobileHarness::Backlog.Mobile.WebHarness.PhoneAutoPairingOptions(handoff));
            // Views of the test's own, which the Development stand-up seed fills.
            services.AddSingleton<Backlog.Mobile.UI.Progress.IProgressViewStore>(new Backlog.Mobile.UI.Progress.SqliteProgressViewStore(views));
            services.AddSingleton<Backlog.Mobile.UI.Tasks.ITaskViewStore>(new Backlog.Mobile.UI.Tasks.SqliteTaskViewStore(views));
        });
        harness.StartServer();
        var progress = harness.Services.GetRequiredService<Backlog.Mobile.UI.Progress.ProgressViewProjection>();
        var tasks = harness.Services.GetRequiredService<Backlog.Mobile.UI.Tasks.TaskViewProjection>();
        Assert.NotEmpty(progress.Goals());

        await HandOverWhenAskedAsync(handoff, "K7MN9PQR");
        var credential = await saved.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal((Owner, PhoneDevice, LocalDevelopmentPairing.PhoneDeviceName), (credential.OwnerId, credential.DeviceId, credential.DeviceName));
        var redeem = Assert.Single(wire.DeviceRequests, request => request.Path.EndsWith(SyncRoutes.RedeemPairingCode, StringComparison.Ordinal));
        Assert.Contains("K7MN9PQR", redeem.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(wire.DeviceRequests, request => request.Path.EndsWith(SyncRoutes.PairingCodes, StringComparison.Ordinal));
        Assert.Empty(progress.Goals());
        Assert.Empty(tasks.Live());
    }

    /// <summary>
    /// Asked, the desktop — once registered — mints a pairing code with its own
    /// credential when the phone asks for one, and hands only that code over, in
    /// the OS-protected envelope.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task The_desktop_harness_hands_the_phone_a_code_it_minted_itself()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The handoff is a DPAPI envelope.");

        using var folder = new TemporaryFolder();
        var handoff = folder.Folder("handoff");
        var store = new InMemoryDeviceCredentialStore();
        var features = new AppFeatureSettingsStore(Backlog.Desktop.UI.Shell.AppFeatures.All, folder.File("desktop", "feature.settings.json"));
        var wire = new SyncWire(request =>
            request.Path.EndsWith(SyncRoutes.DeviceToken, StringComparison.Ordinal)
                ? request.Body.Contains("issued-secret", StringComparison.Ordinal) ? SyncWire.Token() : SyncWire.CredentialInvalid()
            : request.Path.EndsWith(SyncRoutes.RegisterDevice, StringComparison.Ordinal) ? SyncWire.Registration(DesktopDevice)
            : request.Path.EndsWith(SyncRoutes.PairingCodes, StringComparison.Ordinal) ? SyncWire.Code()
            : SyncWire.Unavailable());

        using var harness = new Harness<DesktopHarness::Program>("true", wire, services =>
        {
            services.AddSingleton<IDeviceCredentialStore>(store);
            services.AddSingleton<IAppFeatureSettings>(features);
            services.AddSingleton(new DesktopHarness::Backlog.Desktop.WebHarness.DesktopAutoRegistrationOptions(handoff));
        });
        harness.StartServer();

        var phoneSide = new DpapiPairingCodeHandoff(handoff);
        phoneSide.RequestCode();
        var code = await TakeWhenHandedAsync(phoneSide);

        Assert.Equal("K7MN9PQR", code);
        Assert.Single(wire.DeviceRequests, request => request.Path.EndsWith(SyncRoutes.PairingCodes, StringComparison.Ordinal));
        Assert.DoesNotContain(Directory.EnumerateFiles(handoff), file => File.ReadAllText(file).Contains("issued-secret", StringComparison.Ordinal));
    }

    /// <summary>The desktop's side, played by the test: once the phone asks, a
    /// code is handed over.</summary>
    [SupportedOSPlatform("windows")]
    private static async Task HandOverWhenAskedAsync(string folder, string code)
    {
        var desktopSide = new DpapiPairingCodeHandoff(folder);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(Patience);
        while (!desktopSide.CodeRequested) await Task.Delay(50, deadline.Token);
        desktopSide.HandOver(code);
    }

    /// <summary>The phone's side, played by the test: the code, once one is
    /// handed over.</summary>
    [SupportedOSPlatform("windows")]
    private static async Task<string> TakeWhenHandedAsync(DpapiPairingCodeHandoff phoneSide)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(Patience);
        while (true)
        {
            if (phoneSide.TakeCode() is { } code) return code;
            await Task.Delay(50, deadline.Token);
        }
    }

    /// <summary>
    /// A phone made "never paired" on purpose — the manual-pairing marker beside
    /// its credential, which the end-to-end reset writes — is left for a person,
    /// even with the switch on: it does not even ask the desktop for a code.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task The_phone_harness_leaves_a_phone_marked_for_pairing_by_hand_unpaired()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The handoff is a DPAPI envelope.");

        using var folder = new TemporaryFolder();
        var handoff = folder.Folder("handoff");
        var phone = new DpapiDeviceCredentialStore(folder.File("phone", "device-credential.json"));
        File.WriteAllText(folder.File("phone", LocalDevelopmentPairing.ManualPairingMarkerFileName), string.Empty);
        var wire = new SyncWire(_ => SyncWire.Unavailable());

        using var harness = new Harness<MobileHarness::Program>("true", wire, services =>
        {
            services.AddSingleton<IDeviceCredentialStore>(phone);
            services.AddSingleton(new MobileHarness::Backlog.Mobile.WebHarness.PhoneAutoPairingOptions(handoff));
        });

        var autoPair = harness.Services.GetServices<IHostedService>().OfType<MobileHarness::Backlog.Mobile.WebHarness.PhoneAutoPairing>().Single();
        await autoPair.ExecuteTask!.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.ManualPairingRequested, autoPair.Outcome);
        Assert.Empty(wire.DeviceRequests);
        Assert.Null(phone.Current);
        Assert.False(new DpapiPairingCodeHandoff(handoff).CodeRequested);
    }

    /// <summary>
    /// Criterion 9 through the harness: a desktop credential the service no
    /// longer knows — the emulator's registry came back empty — is discarded
    /// through the harness's own store and replaced by one fresh registration.
    /// </summary>
    [Fact]
    public async Task The_desktop_harness_registers_again_when_the_service_has_forgotten_its_credential()
    {
        using var folder = new TemporaryFolder();
        var stale = new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), LocalDevelopmentPairing.DesktopDeviceName, "forgotten-secret");
        var store = new InMemoryDeviceCredentialStore(stale);
        var features = new AppFeatureSettingsStore(Backlog.Desktop.UI.Shell.AppFeatures.All, folder.File("desktop", "feature.settings.json"));
        var saved = SavedOnce(store);
        var wire = new SyncWire(request =>
            request.Path.EndsWith(SyncRoutes.DeviceToken, StringComparison.Ordinal) ? SyncWire.CredentialInvalid()
            : request.Path.EndsWith(SyncRoutes.RegisterDevice, StringComparison.Ordinal) ? SyncWire.Registration(DesktopDevice)
            : SyncWire.Unavailable());

        using var harness = new Harness<DesktopHarness::Program>("true", wire, services =>
        {
            services.AddSingleton<IDeviceCredentialStore>(store);
            services.AddSingleton<IAppFeatureSettings>(features);
        });
        harness.StartServer();

        var credential = await saved.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal((Owner, DesktopDevice), (credential.OwnerId, credential.DeviceId));
        Assert.Single(wire.DeviceRequests, request => request.Body.Contains("forgotten-secret", StringComparison.Ordinal));
        Assert.Single(wire.DeviceRequests, request => request.Path.EndsWith(SyncRoutes.RegisterDevice, StringComparison.Ordinal));
    }

    /// <summary>
    /// Opted out, a held credential is neither checked nor discarded, however
    /// stale: the run pairs by hand, as before the auto-pair existed.
    /// </summary>
    [Fact]
    public void The_desktop_harness_leaves_a_held_credential_alone_when_the_apphost_opts_out()
    {
        var stale = new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), LocalDevelopmentPairing.DesktopDeviceName, "forgotten-secret");
        var store = new InMemoryDeviceCredentialStore(stale);
        var wire = new SyncWire(_ => SyncWire.CredentialInvalid());

        using var harness = new Harness<DesktopHarness::Program>("false", wire, services => services.AddSingleton<IDeviceCredentialStore>(store));
        harness.StartServer();

        Assert.Null(harness.Services.GetService<LocalDevelopmentPairing>());
        Assert.DoesNotContain(wire.DeviceRequests, request => request.Body.Contains("forgotten-secret", StringComparison.Ordinal));
        Assert.Equal(stale, store.Current);
    }

    /// <summary>
    /// A phone marked for pairing by hand keeps the credential it holds,
    /// unchecked, even with the switch on: a person or the end-to-end suite owns
    /// that pairing.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task The_phone_harness_keeps_a_marked_phones_credential_untouched()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The phone harness's store is DPAPI.");

        using var folder = new TemporaryFolder();
        var held = new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), LocalDevelopmentPairing.PhoneDeviceName, "held-by-hand");
        var phone = new DpapiDeviceCredentialStore(folder.File("phone", "device-credential.json"));
        phone.Save(held);
        File.WriteAllText(folder.File("phone", LocalDevelopmentPairing.ManualPairingMarkerFileName), string.Empty);
        var wire = new SyncWire(_ => SyncWire.CredentialInvalid());

        using var harness = new Harness<MobileHarness::Program>("true", wire, services =>
        {
            services.AddSingleton<IDeviceCredentialStore>(phone);
            services.AddSingleton(new MobileHarness::Backlog.Mobile.WebHarness.PhoneAutoPairingOptions(folder.Folder("handoff")));
        });

        var autoPair = harness.Services.GetServices<IHostedService>().OfType<MobileHarness::Backlog.Mobile.WebHarness.PhoneAutoPairing>().Single();
        await autoPair.ExecuteTask!.WaitAsync(Patience, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.AlreadyPaired, autoPair.Outcome);
        Assert.Empty(wire.DeviceRequests);
        Assert.Equal(held, phone.Current);
    }

    /// <summary>The first credential the store saves, heard the way the pairing
    /// gate hears it.</summary>
    private static Task<DeviceCredential> SavedOnce(IDeviceCredentialStore store)
    {
        var saved = new TaskCompletionSource<DeviceCredential>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Changed += () =>
        {
            if (store.Current is { } credential) saved.TrySetResult(credential);
        };
        return saved.Task;
    }

    /// <summary>The sync service as these tests script it, recording what each
    /// harness sent to the device routes.</summary>
    private sealed class SyncWire(Func<SyncWire.Request, HttpResponseMessage> answer) : HttpMessageHandler
    {
        private const string DeviceRoutes = SyncRoutes.Base + "/devices/";

        private readonly ConcurrentQueue<Request> _deviceRequests = new();

        public IReadOnlyCollection<Request> DeviceRequests => _deviceRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
        {
            var body = message.Content is null ? string.Empty : await message.Content.ReadAsStringAsync(cancellationToken);
            var request = new Request(message.RequestUri!.AbsolutePath, body);
            if (request.Path.StartsWith(DeviceRoutes, StringComparison.Ordinal)) _deviceRequests.Enqueue(request);
            return answer(request);
        }

        public sealed record Request(string Path, string Body);

        public static HttpResponseMessage Unavailable() =>
            Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable);

        public static HttpResponseMessage CredentialInvalid() =>
            Problem(HttpStatusCode.Unauthorized, SyncErrorCodes.DeviceCredentialInvalid);

        public static HttpResponseMessage Token() => Json(
            HttpStatusCode.OK,
            """{"accessToken":"borrowed-token","expiresAt":"2099-01-01T00:00:00+00:00","tokenType":"Bearer"}""");

        public static HttpResponseMessage Code() => Json(
            HttpStatusCode.Created,
            """{"code":"K7MN9PQR","expiresAt":"2099-01-01T00:00:00+00:00"}""");

        public static HttpResponseMessage Registration(Guid device) => Json(
            HttpStatusCode.Created,
            $$"""{"ownerId":"{{Owner}}","deviceId":"{{device}}","credential":"issued-secret"}""");

        private static HttpResponseMessage Problem(HttpStatusCode status, string code) => new(status)
        {
            Content = new StringContent(
                $$"""{"title":"Scripted","status":{{(int)status}},"detail":"Scripted by the test.","code":"{{code}}"}""",
                Encoding.UTF8,
                "application/problem+json")
        };

        private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-auto-pair-" + Guid.NewGuid().ToString("N"));

        public string Folder(string folder)
        {
            var directory = Path.Combine(_root, folder);
            Directory.CreateDirectory(directory);
            return directory;
        }

        public string File(string folder, string name)
        {
            var directory = Path.Combine(_root, folder);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, name);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
