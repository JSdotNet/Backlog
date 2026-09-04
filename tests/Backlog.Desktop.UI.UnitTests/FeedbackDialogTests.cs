using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Report issue dialog, driven the way the footer draws it.
///
/// <para>Two things it has to get right, and neither is visible from the
/// reporter's own tests. An image can arrive from the clipboard as well as from
/// a screen capture, and both end in the same preview and the same attachment —
/// so a paste that fails must leave whatever is already attached alone rather
/// than half-clearing it. And the line printed when the issue is filed is the
/// only way back to it: a number a reader has to retype is not a way back.</para>
/// </summary>
public sealed class FeedbackDialogTests
{
    private const string PastedImage = "data:image/png;base64,iVBORw0KGgo=";
    private const string CapturedImage = "data:image/jpeg;base64,/9j/4AAQ";

    [Fact]
    public async Task An_image_on_the_clipboard_fills_the_preview_a_capture_would_have()
    {
        using var host = Open(clipboard: Pasted());

        await host.Dialog.Find("[data-testid='feedback-screenshot-paste']").ClickAsync(new());

        Assert.Equal(PastedImage, host.Dialog.Find("[data-testid='feedback-screenshot-preview']").GetAttribute("src"));
    }

    [Fact]
    public void The_paste_control_is_the_librarys_button_and_is_reachable_by_keyboard()
    {
        using var host = Open(clipboard: Pasted());

        var paste = host.Dialog.Find("[data-testid='feedback-screenshot-paste']");

        // AppButton's own stem, and its type, which is what keeps the control in
        // the tab order without submitting anything.
        Assert.Equal("BUTTON", paste.TagName);
        Assert.Contains("btn", paste.ClassList);
        Assert.Equal("button", paste.GetAttribute("type"));
        Assert.False(paste.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Retake_and_remove_operate_on_a_pasted_image_as_they_do_on_a_captured_one()
    {
        using var host = Open(clipboard: Pasted(), capture: Captured());

        await host.Dialog.Find("[data-testid='feedback-screenshot-paste']").ClickAsync(new());

        await host.Dialog.Find("[data-testid='feedback-screenshot-retake']").ClickAsync(new());
        Assert.Equal(CapturedImage, host.Dialog.Find("[data-testid='feedback-screenshot-preview']").GetAttribute("src"));

        await host.Dialog.Find("[data-testid='feedback-screenshot-remove']").ClickAsync(new());
        Assert.Empty(host.Dialog.FindAll("[data-testid='feedback-screenshot-preview']"));
    }

    [Fact]
    public async Task A_clipboard_with_no_image_says_so_and_keeps_what_is_already_attached()
    {
        // No plan for the clipboard read: the loose runtime answers it with null,
        // which is exactly what the reader returns when the clipboard holds text
        // or nothing at all.
        using var host = Open(capture: Captured());

        await host.Dialog.Find("[data-testid='feedback-screenshot-attach']").ClickAsync(new());
        await host.Dialog.Find("[data-testid='feedback-screenshot-paste']").ClickAsync(new());

        Assert.Equal(CapturedImage, host.Dialog.Find("[data-testid='feedback-screenshot-preview']").GetAttribute("src"));
        Assert.Contains("image", host.Dialog.Find(".feedback-status").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_refused_clipboard_is_reported_and_the_issue_can_still_be_filed_without_one()
    {
        using var host = Open(clipboardFailure: new JSException("Clipboard access was denied."));

        await host.Dialog.Find("[data-testid='feedback-screenshot-paste']").ClickAsync(new());

        Assert.Contains("Clipboard access was denied.", host.Dialog.Find(".feedback-status").TextContent);
        Assert.Empty(host.Dialog.FindAll("[data-testid='feedback-screenshot-preview']"));

        await Fill(host, "Paste refused");
        await host.Dialog.Find("[data-testid='feedback-submit']").ClickAsync(new());

        Assert.Equal("[Feedback][Desktop app] Paste refused", host.Client.CreatedTitle);
        Assert.NotNull(host.Dialog.Find("[data-testid='feedback-issue-link']"));
    }

    [Fact]
    public async Task The_filed_issue_is_a_link_and_not_only_a_number()
    {
        using var host = Open();

        await Fill(host, "Broken view");
        await host.Dialog.Find("[data-testid='feedback-submit']").ClickAsync(new());

        var link = host.Dialog.Find("[data-testid='feedback-issue-link']");

        Assert.Equal("A", link.TagName);
        Assert.Equal("https://github.com/JSdotNet/Backlog/issues/101", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener", link.GetAttribute("rel"));
        Assert.Equal("JSdotNet/Backlog#101", link.TextContent);

        // A bare "#101" tells a screen reader nothing about where it goes.
        var name = link.GetAttribute("aria-label");
        Assert.NotNull(name);
        Assert.Contains("JSdotNet/Backlog", name);
        Assert.Contains("101", name);
    }

    /// <summary>
    /// The line the link sits in is still the one that announces politely. A
    /// success that interrupts is the failure's announcement wearing the
    /// success's colour.
    /// </summary>
    [Fact]
    public async Task The_success_line_announces_without_interrupting_and_a_failure_still_does()
    {
        using var host = Open();

        await Fill(host, "Broken view");
        await host.Dialog.Find("[data-testid='feedback-submit']").ClickAsync(new());

        var status = host.Dialog.Find(".feedback-status");
        Assert.Equal("status", status.GetAttribute("role"));
        Assert.Contains("feedback-status--ok", status.ClassList);
        Assert.Contains("Created", status.TextContent);

        using var refused = Open(clipboardFailure: new JSException("Clipboard access was denied."));
        await refused.Dialog.Find("[data-testid='feedback-screenshot-paste']").ClickAsync(new());

        Assert.Equal("alert", refused.Dialog.Find(".feedback-status").GetAttribute("role"));
    }

    private static async Task Fill(DialogHost host, string title)
    {
        var input = host.Dialog.Find("[data-testid='feedback-title-input']");
        await input.InputAsync(new() { Value = title });
    }

    private static GitHubFeedbackScreenshot Pasted() =>
        new(PastedImage, "image/png", 640, 480, 24);

    private static GitHubFeedbackScreenshot Captured() =>
        new(CapturedImage, "image/jpeg", 900, 600, 42);

    private static DialogHost Open(
        GitHubFeedbackScreenshot? clipboard = null,
        GitHubFeedbackScreenshot? capture = null,
        Exception? clipboardFailure = null)
    {
        var context = new BunitContext();

        // The dialog moves focus into itself when it opens, and the clipboard
        // read is answered with null when a test does not plan it.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        if (clipboard is not null)
        {
            context.JSInterop.Setup<GitHubFeedbackScreenshot>("backlogReadClipboardImage").SetResult(clipboard);
        }

        if (clipboardFailure is not null)
        {
            context.JSInterop.Setup<GitHubFeedbackScreenshot>("backlogReadClipboardImage").SetException(clipboardFailure);
        }

        if (capture is not null)
        {
            context.JSInterop.Setup<GitHubFeedbackScreenshot>("backlogCaptureScreenshot").SetResult(capture);
        }

        var root = Path.Combine(Path.GetTempPath(), "backlog-feedback-dialog", Guid.NewGuid().ToString("n"));

        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features.json"));
        _ = features.SetEnabled(AppFeatures.FeedbackReporting, true);

        var client = new RecordingGitHubClient();
        var gitHub = new GitHubIntegration(
            new GitHubSettingsStore(Path.Combine(root, "github.json")),
            client,
            new StubProbe());

        context.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton(new FeedbackReporter(gitHub));

        var footer = context.Render<AppFooter>();
        footer.Find("[data-testid='feedback-button']").Click();

        return new DialogHost(context, footer, client, root);
    }

    private sealed record DialogHost(
        BunitContext Context,
        IRenderedComponent<AppFooter> Dialog,
        RecordingGitHubClient Client,
        string Root) : IDisposable
    {
        public void Dispose()
        {
            Context.Dispose();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class RecordingGitHubClient : IGitHubClient
    {
        public string? CreatedTitle { get; private set; }
        public string? CreatedBody { get; private set; }
        public string? UploadedPath { get; private set; }

        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default)
        {
            CreatedTitle = title;
            CreatedBody = body ?? string.Empty;

            return Task.FromResult(new GitHubIssue(
                101,
                $"https://github.com/{repository.FullName}/issues/101",
                title,
                GitHubItemState.Open,
                DateTimeOffset.UtcNow));
        }

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
            CancellationToken cancellationToken = default)
        {
            UploadedPath = path;

            return Task.FromResult(new GitHubUploadedFile(
                path,
                $"https://raw.githubusercontent.com/{repository.FullName}/{branch}/{path}"));
        }
    }

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }
}
