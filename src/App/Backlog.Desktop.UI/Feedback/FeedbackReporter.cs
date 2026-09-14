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
    /// <param name="screenshotError">Why no screenshot is attached, as a whole
    /// sentence. The caller writes it, not this class: only the caller knows
    /// which origin failed, and a capture, a clipboard read and an upload are
    /// three different sentences. Anything passed here reaches the issue body
    /// verbatim.</param>
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
                    ScreenshotPath(screenshot.MediaType),
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
            // The failure is printed in the words it was reported in. The sentence
            // used to be written here — "Screenshot capture failed:" in front of
            // whatever arrived — which was true for as long as a screen capture
            // was the only way an image reached a report. It stopped being true
            // twice over: a clipboard the WebView refuses read as a capture
            // nobody attempted, and the upload failure above, which already says
            // what it is, came out doubled. Each origin says its own sentence now.
            return string.IsNullOrWhiteSpace(screenshotError)
                ? "No screenshot was attached."
                : screenshotError.Trim();
        }

        // "Attached" rather than "Captured": nothing here can tell a pasted image
        // from a captured one — the screenshot carries no origin — so the line
        // says only what is true of both.
        return $"""
        Attached from the app as {screenshot.MediaType}, {screenshot.Width} x {screenshot.Height}, {screenshot.SizeBytes} bytes.

        ![Screenshot]({screenshotUrl})
        """;
    }

    private static string ScreenshotPath(string mediaType) =>
        $"feedback-screenshots/{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{ExtensionFor(mediaType)}";

    /// <summary>The extension for the media type the reader reports. It used to
    /// be a literal <c>.jpg</c>, which was true for as long as a screen capture
    /// was the only origin — a file committed under the wrong extension is one
    /// GitHub serves under the wrong type, and the embed in the issue body then
    /// does not render.
    /// <para>
    /// In practice today that type is <c>image/jpeg</c> or <c>image/webp</c>:
    /// <c>backlogScreenshotMediaType</c> in <c>app.js</c> re-encodes anything
    /// else, because <c>toDataURL</c> takes a quality only for those two and
    /// quality is how the size budget is met. The other arms are what keeps this
    /// honest if that ever changes, and the committed test pins all four.
    /// </para></summary>
    private static string ExtensionFor(string mediaType) => mediaType?.Trim().ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        // JPEG included: it is what the capture path produces, and it is the
        // fallback the reader re-encodes an unknown type into.
        _ => ".jpg"
    };

    /// <summary>The bytes behind a <c>data:image/...;base64,...</c> URL — the
    /// shape <c>backlogCaptureScreenshot</c> and <c>backlogReadClipboardImage</c>
    /// in <c>app.js</c> both always return.</summary>
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
