using Backlog.UI.Components;
using System.Threading;
using System.Threading.Tasks;

namespace Backlog.Desktop.UI.AppUpdate;

/// <summary>
/// The update service used wherever the app cannot manage its own updates: the
/// Blazor Server web host, non-Windows platforms, and the desktop app when it
/// detects it is running unpackaged. It reports the running version and answers
/// every update request with a clear "not my job" message rather than throwing.
/// </summary>
public sealed class UnsupportedAppUpdateService : IAppUpdateService
{
    private readonly string _message;

    /// <param name="message">
    /// The explanation shown in the UI. Defaults to wording that fits any host
    /// where updates are handled by whatever installed or launched the build.
    /// </param>
    /// <param name="currentVersion">
    /// The version to report. Defaults to this assembly's informational version.
    /// </param>
    public UnsupportedAppUpdateService(
        string? message = null,
        string? currentVersion = null)
    {
        _message = message ?? "Updates are managed by however you started this build.";
        CurrentVersion = currentVersion ?? AppVersion.OfEntryAssembly();
    }

    public string CurrentVersion { get; }

    public bool IsSupported => false;

    public Task<AppUpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default) =>
        Task.FromResult(AppUpdateCheckResult.Unsupported(_message));

    public Task<AppUpdateInstallResult> StartUpdateAsync(CancellationToken ct = default) =>
        Task.FromResult(AppUpdateInstallResult.Unsupported(_message));
}
