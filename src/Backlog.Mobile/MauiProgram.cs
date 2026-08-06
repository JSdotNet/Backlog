using Backlog.Mobile.Services;
using Microsoft.Extensions.Logging;

namespace Backlog.Mobile;

public static class MauiProgram
{
	// 10.0.2.2 is the Android emulator's route to the host machine, where the
	// Aspire-hosted cloud service runs.
	private const string DefaultCloudBaseAddress = "http://10.0.2.2:15310";

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
			Environment.GetEnvironmentVariable("services__cloud__http__0") ?? DefaultCloudBaseAddress;

		builder.Services.AddHttpClient<CloudSyncClient>(client =>
			client.BaseAddress = new Uri(cloudBaseAddress));

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
