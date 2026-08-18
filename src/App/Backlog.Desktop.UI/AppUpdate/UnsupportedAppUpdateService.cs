using System.Reflection;
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

/// <summary>
/// Formats a version for display, favouring the informational version (which
/// carries the semantic version stamped at build time) and falling back to the
/// assembly version. Pure and unit-testable so the desktop head and the web host
/// present versions identically.
/// </summary>
public static class AppVersion
{
    /// <summary>The display version of the entry (or this) assembly.</summary>
    public static string OfEntryAssembly() =>
        Of(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly());

    /// <summary>The display version of a specific assembly.</summary>
    public static string Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return Normalize(informational)
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    /// <summary>
    /// Trims the source-revision suffix the SDK appends to the informational
    /// version (e.g. <c>1.2.3+abc1234</c> becomes <c>1.2.3</c>) and rejects
    /// blank values.
    /// </summary>
    public static string? Normalize(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var plus = informationalVersion.IndexOf('+');
        var trimmed = plus >= 0 ? informationalVersion[..plus] : informationalVersion;
        trimmed = trimmed.Trim();

        return trimmed.Length == 0 ? null : trimmed;
    }
}
