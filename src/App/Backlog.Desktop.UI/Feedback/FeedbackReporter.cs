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

    /// <summary>Creates an issue on this app's repository from an in-app
    /// feedback report.</summary>
    public Task<GitHubIssueLink> ReportAsync(
        string title,
        string? details,
        string? screenArea,
        GitHubFeedbackScreenshot? screenshot,
        string? screenshotError = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new GitHubException("A feedback report needs a short title.");
        }

        return gitHub.CreateIssueAsync(
            gitHub.RepositoryFor(FeedbackOwner, FeedbackRepository),
            BuildFeedbackTitle(title),
            BuildFeedbackBody(details, screenArea, screenshot, screenshotError),
            cancellationToken: cancellationToken);
    }

    internal static string BuildFeedbackTitle(string title) => $"[Feedback][Desktop app] {title.Trim()}";

    internal static string BuildFeedbackBody(string? details, string? screenArea, GitHubFeedbackScreenshot? screenshot, string? screenshotError) =>
        $"""
        ## Desktop app screen area

        {(string.IsNullOrWhiteSpace(screenArea) ? "Unspecified" : screenArea.Trim())}

        ## Report

        {(string.IsNullOrWhiteSpace(details) ? "_No details provided._" : details.Trim())}

        ## Screenshot

        {BuildScreenshotSection(screenshot, screenshotError)}
        """;

    private static string BuildScreenshotSection(GitHubFeedbackScreenshot? screenshot, string? screenshotError)
    {
        if (screenshot is null)
        {
            return string.IsNullOrWhiteSpace(screenshotError)
                ? "No screenshot was captured."
                : $"Screenshot capture failed: {screenshotError.Trim()}";
        }

        return $"""
        Captured from the app as {screenshot.MediaType}, {screenshot.Width} x {screenshot.Height}, {screenshot.SizeBytes} bytes.

        ![Screenshot]({screenshot.DataUrl})
        """;
    }
}
