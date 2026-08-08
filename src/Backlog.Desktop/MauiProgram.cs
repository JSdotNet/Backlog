using Backlog.Desktop.Services;
using Backlog.UI.Services;
using Backlog.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backlog.Desktop;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        ConfigureWebView2RemoteDebugging();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.AddServiceDefaults();
        builder.Services.AddSingleton<BacklogStore>();
        builder.Services.AddSingleton<BacklogDesktopState>();

        // The MSIX head can manage its own updates when packaged; it degrades to
        // an "unsupported" report when running unpackaged (e.g. Debug), so this is
        // safe to register unconditionally.
        builder.Services.AddSingleton<IAppUpdateService, MsixAppUpdateService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void ConfigureWebView2RemoteDebugging()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string argument = "--remote-debugging-port=9222";
        var current = Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", EnvironmentVariableTarget.Process);

        if (string.IsNullOrWhiteSpace(current))
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", argument, EnvironmentVariableTarget.Process);
            return;
        }

        if (!current.Contains("--remote-debugging-port", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable(
                "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS",
                $"{current} {argument}",
                EnvironmentVariableTarget.Process);
        }
    }
}
