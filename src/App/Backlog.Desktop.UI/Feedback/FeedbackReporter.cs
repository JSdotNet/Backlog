using Backlog.Infrastructure.GitHub;

// The namespace deliberately does not match the folder, for the reason
// Settings.razor sets out in src/App/Backlog.Desktop.UI/Settings: a sibling
// namespace under Backlog.Desktop.UI shadows same-named types for everything
// beneath it, and the Shell's published namespace is what the tests import.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Turns an in-app bug report into an issue on this app's own repository.
/// <para>
/// Split out of the GitHub adapter rather than left inside it. The adapter
/// answers "file this issue in that repository"; this answers "what does a good
/// Desktop-app bug report look like, and which repository does it go to" — a
/// question about this product, hard-coded to <c>JSdotNet/Backlog</c> and
/// formatted around a screen area and a screenshot. That is app chrome, and the
/// chrome is the Shell. Keeping it in the adapter would have put one product's
/// repository name in a cross-cutting adapter that is meant to know nothing in
/// particular, and would have left the feedback half sitting in a class Backlog
/// Management injects for an entirely different reason.
/// </para>
/// </summary>
public sealed class FeedbackReporter(GitHubIntegration gitHub)
{
    private const string FeedbackOwner = "JSdotNet";
    private const string FeedbackRepository = "Backlog";

    // A dedicated branch rather than the default branch: a screenshot is
    // evidence for an issue, not a change worth reviewing, and committing it
    // straight onto the default branch would either fight branch protection or
    // put binary noise directly into the branch other work builds on.
    private const string ScreenshotBranch = "feedback-screenshots";

    /// <summary>Creates an issue on this app's repository from an in-app
    /// feedback report. A screenshot, if there is one, is committed to the
    /// repository first — GitHub's markdown sanitizer strips a screenshot
    /// embedded as a <c>data:</c> URL straight in the issue body, so the only
    /// way to make it actually render is to link a real hosted file.</summary>
    public async Task<GitHubIssueLink> ReportAsync(
        string title,
        string? details,
        GitHubFeedbackScreenshot? screenshot,
        string? screenshotError = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new GitHubException("A feedback report needs a short title.");
        }

        var repository = gitHub.RepositoryFor(FeedbackOwner, FeedbackRepository);
        string? screenshotUrl = null;

        if (screenshot is not null)
        {
            try
            {
                var uploaded = await gitHub.UploadFileAsync(
                    repository,
                    ScreenshotPath(),
                    ScreenshotBranch,
                    DecodeDataUrl(screenshot.DataUrl),
                    $"Add feedback screenshot for \"{title.Trim()}\"",
                    cancellationToken);
                screenshotUrl = uploaded.DownloadUrl;
            }
            catch (GitHubException ex)
            {
                screenshot = null;
                screenshotError = $"Screenshot upload failed: {ex.Message}";
            }
        }

        return await gitHub.CreateIssueAsync(
            repository,
            BuildFeedbackTitle(title),
            BuildFeedbackBody(details, screenshot, screenshotUrl, screenshotError),
            cancellationToken: cancellationToken);
    }

    internal static string BuildFeedbackTitle(string title) => $"[Feedback][Desktop app] {title.Trim()}";

    internal static string BuildFeedbackBody(string? details, GitHubFeedbackScreenshot? screenshot, string? screenshotUrl, string? screenshotError) =>
        $"""
        ## Report

        {(string.IsNullOrWhiteSpace(details) ? "_No details provided._" : details.Trim())}

        ## Screenshot

        {BuildScreenshotSection(screenshot, screenshotUrl, screenshotError)}
        """;

    private static string BuildScreenshotSection(GitHubFeedbackScreenshot? screenshot, string? screenshotUrl, string? screenshotError)
    {
        if (screenshot is null || string.IsNullOrWhiteSpace(screenshotUrl))
        {
            return string.IsNullOrWhiteSpace(screenshotError)
                ? "No screenshot was attached."
                : $"Screenshot capture failed: {screenshotError.Trim()}";
        }

        return $"""
        Captured from the app as {screenshot.MediaType}, {screenshot.Width} x {screenshot.Height}, {screenshot.SizeBytes} bytes.

        ![Screenshot]({screenshotUrl})
        """;
    }

    private static string ScreenshotPath() =>
        $"feedback-screenshots/{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jpg";

    /// <summary>The bytes behind a <c>data:image/...;base64,...</c> URL — the
    /// shape <c>backlogCaptureScreenshot</c> in <c>app.js</c> always returns.</summary>
    private static byte[] DecodeDataUrl(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0)
        {
            throw new GitHubException("The captured screenshot wasn't a data URL.");
        }

        return Convert.FromBase64String(dataUrl[(comma + 1)..]);
    }
}
