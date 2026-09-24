using Backlog.Mobile.Services;
using Backlog.Mobile.UI.Services;
using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace Backlog.Mobile;

public static class MauiProgram
{
	// Ports are dynamic (see src/Backlog.Aspire.AppHost/Properties/launchSettings.json), so the
	// sync address comes from Aspire's service discovery when launched from the AppHost.
	// Override BACKLOG_SYNC_URL when running the app standalone against a known host;
	// 10.0.2.2 is the Android emulator's route to the host machine.
	private const string EmulatorHostFallback = "http://10.0.2.2:5000";

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		var syncBaseAddress =
			Environment.GetEnvironmentVariable("services__sync__http__0")
			?? Environment.GetEnvironmentVariable("BACKLOG_SYNC_URL")
			?? EmulatorHostFallback;

		// The device half of cloud sync: the registration credential, kept in
		// Android's Keystore-backed secure storage so a force-stop does not
		// un-pair the phone. DPAPI is Windows; this is the platform's own answer.
		// ISecureStorage is registered explicitly because MAUI does not put its
		// Essentials interfaces in the container on its own, and the store takes
		// it through the constructor so its logic is testable off a device.
		builder.Services.AddSingleton<ISecureStorage>(SecureStorage.Default);
		builder.Services.AddSingleton<IDeviceCredentialStore, SecureStorageDeviceCredentialStore>();

		// Pairing and tokens only. Task replication is AddTaskSyncClient, and it is
		// not called here on purpose: it needs an ITaskRepository, and this head has
		// none - the phone carries the Inbox, not a local task database. Registering
		// it anyway is not a dormant feature, it is a head that cannot start.
		builder.Services.AddSyncClient(new Uri(syncBaseAddress));

		// The inbox endpoints are bearer-only, so the data client leaves carrying
		// the same short-lived token the pairing client mints. The handler is
		// chained here rather than inside AddSyncClient because which of a host's
		// clients are sync clients is the host's to know.
		builder.Services.AddHttpClient<CloudSyncClient>(client =>
			client.BaseAddress = new Uri(syncBaseAddress))
			.AddHttpMessageHandler<SyncAuthenticationHandler>();

		// The Android recogniser, not the Web Speech one: the System WebView
		// generally has no webkitSpeechRecognition, so the browser implementation
		// would look wired up here and then do nothing on a device. The browser
		// harness registers WebSpeechTranscriber against the same abstraction.
		builder.Services.AddScoped<ISpeechTranscriber, AndroidSpeechTranscriber>();

		// The Android share target, not the harness's query-string reader: an
		// ACTION_SEND intent cannot reach a browser and a WebView URL is not how a
		// share reaches a MAUI app, which is why there are two registrations rather
		// than one. Singleton, and registered twice on purpose: MainActivity resolves
		// the concrete type to hand it an intent, the Inbox screen takes the
		// abstraction, and both have to be the same instance or the buffered share is
		// left in an object nothing is listening to.
		builder.Services.AddSingleton<AndroidShareTargetReceiver>();
		builder.Services.AddSingleton<ISharedContentReceiver>(
			services => services.GetRequiredService<AndroidShareTargetReceiver>());

		// The shell's own state: the capture draft that outlives a tab switch, and
		// the sync status the app bar reads. The browser harness calls the same method.
		builder.Services.AddMobileShell();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		// The credential store reads secure storage once, in its constructor,
		// and answers from memory afterwards. Resolving it here puts that one
		// blocking read at startup, where the head is already doing startup
		// work, rather than under the Inbox's first render — see the seam note
		// on SecureValueDeviceCredentialStore.
		_ = app.Services.GetRequiredService<IDeviceCredentialStore>();

		return app;
	}
}
