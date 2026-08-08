using System.Threading;
using System.Threading.Tasks;

namespace Backlog.UI.Services;

/// <summary>
/// Lets the shared UI show the current version and drive an update check/install
/// without knowing how the app was distributed. Only the Windows/MSIX head
/// implements this for real; every other host uses
/// <see cref="UnsupportedAppUpdateService"/>.
/// </summary>
public interface IAppUpdateService
{
    /// <summary>The version currently running, formatted for display.</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// True when this build can actually manage its own updates (packaged MSIX on
    /// Windows). False for unpackaged runs, the web host, and other platforms.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>Ask the update source whether a newer version exists.</summary>
    Task<AppUpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default);

    /// <summary>Apply an available update and restart the app to finish.</summary>
    Task<AppUpdateInstallResult> StartUpdateAsync(CancellationToken ct = default);
}
