using System.Net.Http.Headers;

using Backlog.UI.Components.AppUpdate;

using Microsoft.Extensions.Logging;

namespace Backlog.Mobile.UI.AppUpdate;

/// <summary>What a check found, and — when it found something newer — the APK to install.</summary>
public sealed record ApkUpdateCheck(AppUpdateCheckResult Result, ApkRelease? Release);

/// <summary>
/// Asks GitHub whether the channel has published an APK newer than the one installed.
/// </summary>
/// <remarks>
/// <para>
/// Anonymous: the repository is public, and an on-demand check plus one quiet check
/// every eight hours sits far under GitHub's sixty unauthenticated requests an hour.
/// GitHub refuses a request with no <c>User-Agent</c>, so one is always sent.
/// </para>
/// <para>
/// It never throws. Offline, a refused or rate-limited request, an answer that is
/// not a release list, and no answer within ten seconds — the wait the desktop's
/// update window allows its manifest — all come back as
/// <see cref="AppUpdateAvailability.Failed"/>, logged at Warning as the desktop logs
/// its own failed checks.
/// </para>
/// </remarks>
public sealed class GitHubApkUpdateCheck(
    HttpClient http,
    ApkUpdateChannel channel,
    TimeProvider time,
    ILogger<GitHubApkUpdateCheck> logger)
{
    /// <summary>How long a check waits for GitHub before it gives up.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private const string OfflineMessage = "The update check failed. Check your connection and try again.";

    /// <param name="installedVersionCode">The running app's versionCode; only a strictly higher one is offered.</param>
    public async Task<ApkUpdateCheck> CheckAsync(long installedVersionCode, CancellationToken cancellationToken = default)
    {
        if (!channel.IsConfigured)
        {
            return new(AppUpdateCheckResult.Unsupported(ApkUpdateChannel.DevelopmentBuildMessage), null);
        }

        using var timeout = new CancellationTokenSource(Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, channel.ReleasesUri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Backlog-Mobile", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await http.SendAsync(request, linked.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Update check failed: GitHub answered {StatusCode} for {Uri}.", (int)response.StatusCode, channel.ReleasesUri);
                return Failed("GitHub did not answer the update check. Try again later.");
            }

            var json = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            if (!ApkReleaseFeed.TryRead(json, out var newest))
            {
                logger.LogWarning("Update check failed: the answer from {Uri} was not a release list.", channel.ReleasesUri);
                return Failed("The release list could not be read. Try again later.");
            }

            return newest is not null && newest.Version.VersionCode > installedVersionCode
                ? new(AppUpdateCheckResult.Available(availableVersion: newest.Version.Display), newest)
                : new(AppUpdateCheckResult.UpToDate(), null);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Update check failed: GitHub did not answer within {Timeout}.", Timeout);
            return Failed(OfflineMessage);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Update check failed.");
            return Failed(cancellationToken.IsCancellationRequested ? "The update check was cancelled." : OfflineMessage);
        }
    }

    private static ApkUpdateCheck Failed(string message) => new(AppUpdateCheckResult.Failed(message), null);
}
