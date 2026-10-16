using System.Globalization;
using System.Runtime.Versioning;

using Android.App;
using Android.Content;
using Android.Content.PM;

using Backlog.Mobile.UI.AppUpdate;
using Backlog.UI.Components.AppUpdate;

using Microsoft.Extensions.Logging;

namespace Backlog.Mobile.Services;

/// <summary>
/// <see cref="IApkInstaller"/> over Android's own <see cref="PackageInstaller"/>:
/// reads the installed version, downloads the release APK into the app's cache, and
/// commits it through an install session the person confirms.
/// </summary>
/// <remarks>
/// <para>
/// The session API rather than <c>ACTION_INSTALL_PACKAGE</c>, which Android has
/// deprecated since API 29, and which would need a <c>FileProvider</c> to hand the
/// file over. A session takes the bytes directly.
/// </para>
/// <para>
/// Android asks two things before an app may install another package: the
/// <c>REQUEST_INSTALL_PACKAGES</c> permission, declared in the manifest, and the
/// person's own "install unknown apps" allowance for this app, which only the
/// Settings screen grants. When that is missing the person is taken there and the
/// install does not start; they come back and tap Install again.
/// </para>
/// <para>
/// The signature check is Android's: an APK signed with another key than the
/// installed app is refused with <c>STATUS_FAILURE_CONFLICT</c> or
/// <c>INCOMPATIBLE</c>, which <see cref="ApkInstallStatus"/> words as a key mismatch.
/// That is the integrity check; there is no published hash to check against, so none
/// is invented here. The app's data — the outbox, the pairing, the cached inbox —
/// survives because an update keeps the package. Nothing here ever uninstalls.
/// </para>
/// </remarks>
public sealed class AndroidApkInstaller(
    IHttpClientFactory httpClientFactory,
    ILogger<AndroidApkInstaller> logger) : IApkInstaller
{
    /// <summary>The named client the APK download goes through.</summary>
    public const string DownloadClientName = "apk-download";

    /// <summary>How long the install waits for Android's final word once the person
    /// has the prompt in front of them.</summary>
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromMinutes(10);

    public string CurrentVersion => AppInfo.Current.VersionString;

    public long InstalledVersionCode =>
        long.TryParse(AppInfo.Current.BuildString, NumberStyles.None, CultureInfo.InvariantCulture, out var code) ? code : 0;

    public async Task<AppUpdateInstallResult> InstallAsync(ApkRelease release, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        var context = Android.App.Application.Context;
        var packageManager = context.PackageManager
            ?? throw new InvalidOperationException("The package manager is not available.");

        if (OperatingSystem.IsAndroidVersionAtLeast(26) && !packageManager.CanRequestPackageInstalls())
        {
            OpenUnknownAppSourcesSettings(context);
            return AppUpdateInstallResult.Failed(
                "Allow Backlog to install apps in the settings that just opened, then come back and tap Install update again.");
        }

        string apk;
        try
        {
            apk = await DownloadAsync(context, release, cancellationToken).ConfigureAwait(false);
        }
        catch (DownloadRefusedException ex)
        {
            logger.LogWarning("Downloading update {Version} was refused: {Reason}", release.Version.Display, ex.Message);
            return AppUpdateInstallResult.Failed(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Downloading update {Version} failed.", release.Version.Display);
            return AppUpdateInstallResult.Failed("The update could not be downloaded. Check your connection and try again.");
        }

        return await CommitAsync(context, packageManager.PackageInstaller, apk, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads the APK into the cache, bounded by the size GitHub recorded for the
    /// asset: refused up front when the phone lacks room for it twice over (the file,
    /// then the session's copy) or the server announces another size, as soon as more
    /// than that arrives, and at the end when less did. Any failure removes what was
    /// written, so a partial download never stays in the cache.
    /// </summary>
    private async Task<string> DownloadAsync(Context context, ApkRelease release, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(context.CacheDir?.AbsolutePath ?? Path.GetTempPath(), "updates");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);

        var free = new Android.OS.StatFs(folder).AvailableBytes;
        if (free < release.Size * 2)
        {
            throw new DownloadRefusedException(
                $"There is not enough storage on this phone for the update: it needs {Megabytes(release.Size * 2)} MB free and has {Megabytes(free)} MB.");
        }

        var path = Path.Combine(folder, $"Backlog.Mobile_{release.Version.Display}.apk");

        try
        {
            using var client = httpClientFactory.CreateClient(DownloadClientName);
            using var response = await client
                .GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            // GitHub answers the asset URL with a redirect to its download host. Following
            // it is expected; following it off https is not.
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            {
                throw new HttpRequestException("The update download left https.");
            }

            if (response.Content.Headers.ContentLength is { } announced && announced != release.Size)
            {
                throw new DownloadRefusedException(SizeMismatch);
            }

            var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var target = File.Create(path);
                await using (target.ConfigureAwait(false))
                {
                    var buffer = new byte[81920];
                    long written = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        written += read;
                        if (written > release.Size) throw new DownloadRefusedException(SizeMismatch);

                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }

                    if (written != release.Size) throw new DownloadRefusedException(SizeMismatch);
                }
            }

            return path;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private async Task<AppUpdateInstallResult> CommitAsync(
        Context context,
        PackageInstaller installer,
        string apk,
        CancellationToken cancellationToken)
    {
        var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
        parameters.SetAppPackageName(context.PackageName);

        var sessionId = installer.CreateSession(parameters);
        var outcome = ApkInstallStatusReceiver.Expect(sessionId);

        try
        {
            using var session = installer.OpenSession(sessionId);

            try
            {
                var input = File.OpenRead(apk);
                await using (input.ConfigureAwait(false))
                {
                    using var output = session.OpenWrite("Backlog.Mobile.apk", 0, input.Length);
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    session.Fsync(output);
                }
            }
            finally
            {
                // The session holds its own copy now. Deleted here rather than after
                // the install, because a successful install ends this process before
                // any later line runs.
                TryDelete(apk);
            }

            // Mutable because the installer writes the status into the intent it
            // sends back; explicit, to this app's own receiver, so nothing else can.
            var flags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(31)) flags |= PendingIntentFlags.Mutable;

            var status = new Intent(context, typeof(ApkInstallStatusReceiver)).SetPackage(context.PackageName);
            var pending = PendingIntent.GetBroadcast(context, sessionId, status, flags)
                ?? throw new InvalidOperationException("Android did not create the install status intent.");

            session.Commit(pending.IntentSender);
        }
        catch
        {
            ApkInstallStatusReceiver.Forget(sessionId);
            TryAbandon(installer, sessionId);
            throw;
        }

        try
        {
            // On success Android replaces the app and ends this process, so this
            // await only ever returns a refusal, a cancel, or Android's silence.
            var (code, message) = await outcome.WaitAsync(StatusTimeout, cancellationToken).ConfigureAwait(false);
            return ApkInstallStatus.Describe(code, message);
        }
        catch (TimeoutException)
        {
            ApkInstallStatusReceiver.Forget(sessionId);
            return AppUpdateInstallResult.Failed(
                "The Android installer did not report back. If Backlog did not restart, check for updates and try again.");
        }
        catch (OperationCanceledException)
        {
            ApkInstallStatusReceiver.Forget(sessionId);
            throw;
        }
    }

    private const string SizeMismatch =
        "The download did not match the size GitHub recorded for the update, so it was discarded. Try again later.";

    private static long Megabytes(long bytes) => (bytes + (1024 * 1024) - 1) / (1024 * 1024);

    [SupportedOSPlatform("android26.0")]
    private static void OpenUnknownAppSourcesSettings(Context context)
    {
        var intent = new Intent(
            Android.Provider.Settings.ActionManageUnknownAppSources,
            Android.Net.Uri.Parse("package:" + context.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    private void TryAbandon(PackageInstaller installer, int sessionId)
    {
        try
        {
            installer.AbandonSession(sessionId);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Abandoning install session {SessionId} failed.", sessionId);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Removing the downloaded update {Path} failed.", path);
        }
    }

    /// <summary>A download refused for a reason the person can read.</summary>
    private sealed class DownloadRefusedException(string message) : Exception(message);
}
