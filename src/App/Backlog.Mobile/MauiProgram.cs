using Backlog.Mobile.UI.Services;
using Microsoft.Extensions.Logging;

namespace Backlog.Mobile;

public static class MauiProgram
{
	// Ports are dynamic (see src/Backlog.Aspire.AppHost/Properties/launchSettings.json), so the
	// cloud address comes from Aspire's service discovery when launched from the AppHost.
	// Override BACKLOG_CLOUD_URL when running the app standalone against a known host;
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

		var cloudBaseAddress =
			Environment.GetEnvironmentVariable("services__cloud__http__0")
			?? Environment.GetEnvironmentVariable("BACKLOG_CLOUD_URL")
			?? EmulatorHostFallback;

		builder.Services.AddHttpClient<CloudSyncClient>(client =>
			client.BaseAddress = new Uri(cloudBaseAddress));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
