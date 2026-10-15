using Backlog.UI.Components.AppUpdate;

using Microsoft.Extensions.Logging;

namespace Backlog.Mobile.UI.AppUpdate;

/// <summary>
/// The platform half of the phone's self-update: what is installed, and putting a
/// downloaded APK in its place. The Android head implements it over
/// <c>PackageInstaller</c>; everything that decides <em>whether</em> to install sits
/// in <see cref="ApkAppUpdateService"/>, where it can be tested off a device.
/// </summary>
public interface IApkInstaller
{
    /// <summary>The installed version as a person reads it — Android's versionName.</summary>
    string CurrentVersion { get; }

    /// <summary>The installed versionCode, which a newer APK must exceed.</summary>
    long InstalledVersionCode { get; }

    /// <summary>
    /// Downloads <paramref name="release"/> and hands it to the platform installer,
    /// which asks the person to confirm. Never called unless they asked to install.
    /// </summary>
    Task<AppUpdateInstallResult> InstallAsync(ApkRelease release, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IAppUpdateService"/> for the Android app: GitHub Releases is the update
/// source, and the platform installer applies the update.
/// </summary>
/// <remarks>
/// <para>
/// A build without an update channel — every build <c>release-mobile.yml</c> did not
/// publish — reports <see cref="IsSupported"/> false and answers every request with
/// <see cref="ApkUpdateChannel.DevelopmentBuildMessage"/>, as the desktop answers an
/// unpackaged run. It asks GitHub for nothing.
/// </para>
/// <para>
/// Installing checks again rather than reusing the last answer: the download URL is
/// then the one GitHub returned moments ago, and a release withdrawn since the check
/// is not installed. Nothing here installs on its own; only
/// <see cref="StartUpdateAsync"/> does, and only the update window calls it.
/// </para>
/// </remarks>
public sealed class ApkAppUpdateService(
    ApkUpdateChannel channel,
    IApkInstaller installer,
    GitHubApkUpdateCheck check,
    ILogger<ApkAppUpdateService> logger) : IAppUpdateService
{
    public string CurrentVersion => installer.CurrentVersion;

    public bool IsSupported => channel.IsConfigured;

    public async Task<AppUpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default) =>
        channel.IsConfigured
            ? (await check.CheckAsync(installer.InstalledVersionCode, ct).ConfigureAwait(false)).Result
            : AppUpdateCheckResult.Unsupported(ApkUpdateChannel.DevelopmentBuildMessage);

    public async Task<AppUpdateInstallResult> StartUpdateAsync(CancellationToken ct = default)
    {
        if (!channel.IsConfigured)
        {
            return AppUpdateInstallResult.Unsupported(ApkUpdateChannel.DevelopmentBuildMessage);
        }

        var found = await check.CheckAsync(installer.InstalledVersionCode, ct).ConfigureAwait(false);
        if (found.Release is null)
        {
            return AppUpdateInstallResult.Failed(found.Result.Availability == AppUpdateAvailability.UpToDate
                ? "There is no newer version to install."
                : found.Result.Message);
        }

        try
        {
            return await installer.InstallAsync(found.Release, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Installing update {Version} failed.", found.Release.Version.Display);
            return AppUpdateInstallResult.Failed("The update could not be installed. Try again later.");
        }
    }
}
