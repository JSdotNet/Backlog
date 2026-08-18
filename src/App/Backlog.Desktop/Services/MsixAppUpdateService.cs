using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Backlog.Desktop.UI.BacklogManagement;
using Backlog.Desktop.UI.Knowledge;
using Backlog.Desktop.UI.AppUpdate;
using Microsoft.Extensions.Logging;

#if WINDOWS
using Windows.ApplicationModel;
using Windows.Management.Deployment;
#endif

namespace Backlog.Desktop.Services;

/// <summary>
/// The real update path for the packaged Windows client. It talks to the MSIX
/// deployment APIs to check the app's <c>.appinstaller</c> source and, when a
/// newer version exists, applies it and restarts the app.
/// <para>
/// Everything here is defensive about package identity: calling
/// <see cref="Package.Current"/> in an unpackaged process throws, and Debug runs
/// this app unpackaged (<c>WindowsPackageType=None</c>) so Playwright can attach
/// over WebView2 CDP. When identity is unavailable — unpackaged, non-Windows, or
/// installed from a bare <c>.msix</c> with no update source — the service reports
/// <see cref="AppUpdateAvailability.Unsupported"/> rather than crashing.
/// </para>
/// </summary>
public sealed class MsixAppUpdateService : IAppUpdateService
{
    private const string UnpackagedMessage =
        "Updates are managed by however you started this build. This unpackaged run updates when you rebuild or reinstall it.";

    private readonly ILogger<MsixAppUpdateService>? _logger;
    private readonly bool _isPackaged;

    public MsixAppUpdateService(ILogger<MsixAppUpdateService>? logger = null)
    {
        _logger = logger;
        _isPackaged = DetectPackaged();
        CurrentVersion = ReadCurrentVersion();
    }

    public string CurrentVersion { get; }

    public bool IsSupported => _isPackaged;

#if WINDOWS
    public async Task<AppUpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        if (!_isPackaged)
        {
            return AppUpdateCheckResult.Unsupported(UnpackagedMessage);
        }

        try
        {
            // The documented gotcha: calling CheckUpdateAvailabilityAsync directly
            // on Package.Current fails with "Access denied". Going through
            // PackageManager.FindPackageForUser returns a package we are allowed to
            // query.
            var manager = new PackageManager();
            var package = manager.FindPackageForUser(string.Empty, Package.Current.Id.FullName);
            if (package is null)
            {
                return AppUpdateCheckResult.Failed("Could not locate the installed package to check for updates.");
            }

            var result = await package.CheckUpdateAvailabilityAsync().AsTask(ct).ConfigureAwait(false);

            return result.Availability switch
            {
                PackageUpdateAvailability.Available =>
                    AppUpdateCheckResult.Available(),
                PackageUpdateAvailability.Required =>
                    AppUpdateCheckResult.Required("A required update is available. Install it to keep the app working."),
                PackageUpdateAvailability.NoUpdates =>
                    AppUpdateCheckResult.UpToDate(),
                PackageUpdateAvailability.Error =>
                    AppUpdateCheckResult.Failed("The update check failed. Check your connection and try again."),
                _ => new AppUpdateCheckResult(AppUpdateAvailability.Unknown, "The update state could not be determined.")
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Update check failed.");
            return AppUpdateCheckResult.Failed("The update check failed. Check your connection and try again.");
        }
    }

    public async Task<AppUpdateInstallResult> StartUpdateAsync(CancellationToken ct = default)
    {
        if (!_isPackaged)
        {
            return AppUpdateInstallResult.Unsupported(UnpackagedMessage);
        }

        try
        {
            // The .appinstaller URI the app was installed from is the stable update
            // source. A bare .msix install has none — treat that as unsupported.
            var appInstaller = Package.Current.GetAppInstallerInfo();
            if (appInstaller?.Uri is null)
            {
                return AppUpdateInstallResult.Unsupported(
                    "This build has no App Installer update source, so it cannot update itself. Reinstall from the latest release to enable updates.");
            }

            var manager = new PackageManager();

            // AddPackageByAppInstallerOptions is a [Flags] enum. ForceTargetAppShutdown
            // shuts this app down so the update can replace the running package.
            await manager.AddPackageByAppInstallerFileAsync(
                appInstaller.Uri,
                AddPackageByAppInstallerOptions.ForceTargetAppShutdown,
                targetVolume: null).AsTask(ct).ConfigureAwait(false);

            return AppUpdateInstallResult.InProgress();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Update install failed.");
            return AppUpdateInstallResult.Failed("Installing the update failed. Try again, or reinstall from the latest release.");
        }
    }
#else
    public Task<AppUpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default) =>
        Task.FromResult(AppUpdateCheckResult.Unsupported(UnpackagedMessage));

    public Task<AppUpdateInstallResult> StartUpdateAsync(CancellationToken ct = default) =>
        Task.FromResult(AppUpdateInstallResult.Unsupported(UnpackagedMessage));
#endif

    private static bool DetectPackaged()
    {
#if WINDOWS
        try
        {
            // Touching Package.Current in an unpackaged process throws; if it
            // yields a full name, we are packaged.
            _ = Package.Current.Id.FullName;
            return true;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    private string ReadCurrentVersion()
    {
#if WINDOWS
        if (_isPackaged)
        {
            try
            {
                var v = Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            catch
            {
                // Fall through to the assembly version.
            }
        }
#endif
        return AppVersion.Of(Assembly.GetExecutingAssembly());
    }
}
