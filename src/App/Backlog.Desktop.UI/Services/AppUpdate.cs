namespace Backlog.Desktop.UI.Services;

/// <summary>
/// How an update check turned out. Kept deliberately small and platform-neutral
/// so the shared UI can render an outcome without knowing anything about MSIX,
/// App Installer, or which head it is running in.
/// </summary>
public enum AppUpdateAvailability
{
    /// <summary>No check has run yet, or the result could not be determined.</summary>
    Unknown,

    /// <summary>A check ran and the installed version is the latest.</summary>
    UpToDate,

    /// <summary>A newer version is available and can be installed.</summary>
    Available,

    /// <summary>A newer version is available and the platform considers it mandatory.</summary>
    Required,

    /// <summary>
    /// Updates are not managed by the app on this build (unpackaged, non-Windows,
    /// or installed from a bare package with no update source).
    /// </summary>
    Unsupported,

    /// <summary>A check was attempted but failed.</summary>
    Failed
}

/// <summary>
/// The result of asking "is there an update?". Pure data: an availability plus a
/// human-readable message the UI can show verbatim.
/// </summary>
/// <param name="Availability">What the check found.</param>
/// <param name="Message">A short, user-facing explanation of the outcome.</param>
public sealed record AppUpdateCheckResult(AppUpdateAvailability Availability, string Message)
{
    /// <summary>True when an update exists and can be installed (available or required).</summary>
    public bool UpdateReady =>
        Availability is AppUpdateAvailability.Available or AppUpdateAvailability.Required;

    public static AppUpdateCheckResult UpToDate(string? message = null) =>
        new(AppUpdateAvailability.UpToDate, message ?? "You are on the latest version.");

    public static AppUpdateCheckResult Available(string? message = null) =>
        new(AppUpdateAvailability.Available, message ?? "An update is available.");

    public static AppUpdateCheckResult Required(string? message = null) =>
        new(AppUpdateAvailability.Required, message ?? "A required update is available.");

    public static AppUpdateCheckResult Unsupported(string message) =>
        new(AppUpdateAvailability.Unsupported, message);

    public static AppUpdateCheckResult Failed(string message) =>
        new(AppUpdateAvailability.Failed, message);
}

/// <summary>
/// How the header renders an update check. The version itself is the control a
/// person clicks, so the label and the status colour are derived here rather than
/// inline in the markup — that keeps the wording testable without a head.
/// </summary>
public static class AppUpdatePresentation
{
    /// <summary>The label on the version control while idle or mid-check.</summary>
    public static string CheckLabel(bool isChecking) =>
        isChecking ? "Checking..." : "Check for updates";

    /// <summary>
    /// The accessible name for the version control: it has to say both which build
    /// this is and what clicking it does.
    /// </summary>
    public static string VersionActionLabel(string? currentVersion, bool isChecking)
    {
        var version = string.IsNullOrWhiteSpace(currentVersion) ? "unknown" : currentVersion.Trim();
        return isChecking
            ? $"Version {version}. Checking for updates."
            : $"Version {version}. Check for updates.";
    }

    /// <summary>The CSS classes for the status message next to the version.</summary>
    public static string StatusClass(AppUpdateAvailability availability) => availability switch
    {
        AppUpdateAvailability.UpToDate => "app-version__status app-version__status--ok",
        AppUpdateAvailability.Available or AppUpdateAvailability.Required =>
            "app-version__status app-version__status--available",
        AppUpdateAvailability.Failed => "app-version__status app-version__status--error",
        _ => "app-version__status"
    };
}

/// <summary>
/// The result of asking to apply an update.
/// </summary>
/// <param name="Started">True when the install/restart was successfully kicked off.</param>
/// <param name="Message">A short, user-facing explanation of the outcome.</param>
public sealed record AppUpdateInstallResult(bool Started, string Message)
{
    public static AppUpdateInstallResult InProgress(string? message = null) =>
        new(true, message ?? "Installing the update. The app will restart to finish.");

    public static AppUpdateInstallResult Unsupported(string message) =>
        new(false, message);

    public static AppUpdateInstallResult Failed(string message) =>
        new(false, message);
}
