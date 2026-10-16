using Backlog.UI.Components.AppUpdate;

namespace Backlog.Mobile.UI.AppUpdate;

/// <summary>
/// Android's package-installer outcomes, and what the update window says for each.
/// </summary>
/// <remarks>
/// The values are <c>PackageInstaller.STATUS_*</c>, which Android documents as
/// constants. They are restated here so the wording can be tested without a device;
/// the Android head reads the integer from the status intent and passes it in.
/// </remarks>
public static class ApkInstallStatus
{
    /// <summary>The installer needs the person to confirm; not an outcome yet.</summary>
    public const int PendingUserAction = -1;
    public const int Success = 0;
    public const int Failure = 1;
    public const int FailureBlocked = 2;
    public const int FailureAborted = 3;
    public const int FailureInvalid = 4;
    public const int FailureConflict = 5;
    public const int FailureStorage = 6;
    public const int FailureIncompatible = 7;
    public const int FailureTimeout = 8;

    // CONFLICT also covers a downgrade and a clash with another package, so the key
    // is named as the likely cause, not the certain one, and Android's own words follow.
    private const string Conflict =
        "Android refused the update: it conflicts with the installed app, which may be signed with a different key. " +
        "A build installed from a development machine cannot be updated by a release.";

    /// <param name="status">The <c>EXTRA_STATUS</c> the installer reported.</param>
    /// <param name="detail">Its <c>EXTRA_STATUS_MESSAGE</c>, when it sent one.</param>
    public static AppUpdateInstallResult Describe(int status, string? detail) => status switch
    {
        PendingUserAction => AppUpdateInstallResult.InProgress("Confirm the update in the Android installer."),
        Success => AppUpdateInstallResult.InProgress("Installed. Backlog restarts with the new version."),
        FailureAborted => AppUpdateInstallResult.Failed("The update was cancelled."),
        FailureConflict or FailureIncompatible => AppUpdateInstallResult.Failed(WithDetail(Conflict, detail)),
        FailureStorage => AppUpdateInstallResult.Failed("There is not enough storage on this phone to install the update."),
        FailureInvalid => AppUpdateInstallResult.Failed("The downloaded package is damaged. Check for updates and try again."),
        FailureBlocked => AppUpdateInstallResult.Failed("Android blocked the update on this phone."),
        FailureTimeout => AppUpdateInstallResult.Failed("The Android installer timed out. Try again."),
        _ => AppUpdateInstallResult.Failed(string.IsNullOrWhiteSpace(detail)
            ? "The update could not be installed."
            : $"The update could not be installed ({detail.Trim()}).")
    };

    private static string WithDetail(string message, string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? message : $"{message} Android said: {detail.Trim()}";
}
