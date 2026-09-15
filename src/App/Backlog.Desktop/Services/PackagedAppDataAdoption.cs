using Backlog.Infrastructure.FileSystem;
using Microsoft.Extensions.Logging;

#if WINDOWS
using Windows.Storage;
#endif

namespace Backlog.Desktop.Services;

/// <summary>
/// The host's half of <see cref="PackagedAppData"/>: knows where a packaged
/// install's redirected AppData is, and runs the adoption before any store is
/// constructed over the real folder.
/// <para>
/// Only a packaged process has a redirected folder to adopt from, and only a
/// packaged process can name it — <see cref="ApplicationData.Current"/> throws
/// in an unpackaged one, which is what every Debug run is
/// (<c>WindowsPackageType=None</c>). That throw is the whole of how "not
/// packaged" is detected here, the same way <see cref="MsixAppUpdateService"/>
/// detects it.
/// </para>
/// </summary>
internal static class PackagedAppDataAdoption
{
    /// <summary>Runs before the service provider exists, so the result is
    /// handed back for the host to log once it has a logger.</summary>
    public static AppDataAdoption? Run()
    {
#if WINDOWS
        string redirected;
        try
        {
            // The redirected %LocalAppData% of a packaged app: LocalCache\Local,
            // with the same folder name under it that the real one has.
            redirected = Path.Combine(
                ApplicationData.Current.LocalCacheFolder.Path,
                "Local",
                WorkspaceSettingsStore.DefaultAppDataFolderName);
        }
        catch (Exception)
        {
            return null;
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            WorkspaceSettingsStore.DefaultAppDataFolderName);

        return PackagedAppData.Adopt(redirected, appData);
#else
        return null;
#endif
    }

    public static void Log(this AppDataAdoption? adoption, ILogger logger)
    {
        switch (adoption?.Outcome)
        {
            case AppDataAdoptionOutcome.Adopted:
                logger.LogInformation("Adopted the app's state from the packaged install's redirected AppData folder.");
                break;
            case AppDataAdoptionOutcome.Failed:
                logger.LogWarning("Couldn't adopt the app's state from the packaged install's redirected AppData folder: {Error}. It will be tried again next start.", adoption.Error);
                break;
        }
    }
}
