using Backlog.Desktop.UI.AppUpdate;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What happens to a page that throws, driven through the layout that mounts
/// the boundary — because the point is the composition: the page goes, the
/// footer stays, and the footer is where the Report issue dialog lives.
///
/// <para>Before this boundary existed, an exception here reached the renderer's
/// own handler, which in the WebView draws the framework's banner and then stops
/// listening to the page altogether. Nothing below could be tested because
/// nothing below could run. Every assertion here is therefore on state the
/// boundary keeps alive.</para>
/// </summary>
public sealed class AppErrorBoundaryTests
{
    [Fact]
    public void A_page_that_throws_is_replaced_by_the_error_state_and_the_footer_stays()
    {
        using var host = Open();

        host.Layout.Find("[data-testid='page-throw']").Click();

        var state = host.Layout.Find("[data-testid='app-error-state']");
        Assert.Equal("alert", state.GetAttribute("role"));
        Assert.Contains("Something went wrong", state.TextContent);
        Assert.Contains("InvalidOperationException: The page threw.", host.Layout.Find("[data-testid='app-error-message']").TextContent);

        Assert.Empty(host.Layout.FindAll("[data-testid='page']"));
        Assert.NotNull(host.Layout.Find("[data-testid='app-footer']"));
        Assert.NotNull(host.Layout.Find("[data-testid='feedback-button']"));
    }

    [Fact]
    public void Try_again_renders_the_page_once_more()
    {
        using var host = Open();

        host.Layout.Find("[data-testid='page-throw']").Click();
        host.Layout.Find("[data-testid='app-error-retry']").Click();

        Assert.NotNull(host.Layout.Find("[data-testid='page']"));
        Assert.Empty(host.Layout.FindAll("[data-testid='app-error-state']"));
    }

    /// <summary>
    /// The case an error boundary has to be careful with: a page that fails
    /// while rendering fails again on every attempt. Trying again is allowed to
    /// land back here; it is not allowed to take the app down.
    /// </summary>
    [Fact]
    public void A_page_that_throws_while_rendering_lands_back_on_the_error_state_after_trying_again()
    {
        using var host = Open(throwOnRender: true);

        Assert.NotNull(host.Layout.Find("[data-testid='app-error-state']"));

        host.Layout.Find("[data-testid='app-error-retry']").Click();

        Assert.NotNull(host.Layout.Find("[data-testid='app-error-state']"));
        Assert.NotNull(host.Layout.Find("[data-testid='app-footer']"));
    }

    [Fact]
    public void Report_issue_opens_the_feedback_dialog_written_up_from_the_exception()
    {
        using var host = Open();

        host.Layout.Find("[data-testid='page-throw']").Click();
        host.Layout.Find("[data-testid='app-error-report']").Click();

        var dialog = host.Layout.Find("[data-testid='feedback-dialog']");
        Assert.NotNull(dialog);

        Assert.Equal(
            "Unhandled error: InvalidOperationException: The page threw.",
            host.Layout.Find("[data-testid='feedback-title-input']").GetAttribute("value"));

        // The library's TextArea writes its value as the element's content, the
        // way a textarea carries one; there is no value attribute to read.
        var details = host.Layout.Find("[data-testid='feedback-details-input']").TextContent;
        Assert.Contains("System.InvalidOperationException: The page threw.", details);
        Assert.Contains("- Version: `1.2.3`", details);

        // The error state stays behind the dialog: closing the dialog without
        // filing must not lose the way back to the page.
        Assert.NotNull(host.Layout.Find("[data-testid='app-error-state']"));
    }

    [Fact]
    public void Report_issue_is_withheld_when_feedback_reporting_is_off()
    {
        using var host = Open(feedbackReporting: false);

        host.Layout.Find("[data-testid='page-throw']").Click();

        Assert.NotNull(host.Layout.Find("[data-testid='app-error-retry']"));
        Assert.Empty(host.Layout.FindAll("[data-testid='app-error-report']"));
    }

    [Fact]
    public void Go_to_Home_leaves_the_failed_route_and_renders_the_page_again()
    {
        using var host = Open();
        var navigation = host.Context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/settings");

        host.Layout.Find("[data-testid='page-throw']").Click();
        host.Layout.Find("[data-testid='app-error-home']").Click();

        Assert.Equal(navigation.BaseUri, navigation.Uri);
        Assert.NotNull(host.Layout.Find("[data-testid='page']"));
        Assert.Empty(host.Layout.FindAll("[data-testid='app-error-state']"));
    }

    private static LayoutHost Open(bool throwOnRender = false, bool feedbackReporting = true)
    {
        var context = new BunitContext();

        // The feedback dialog moves focus into itself when it opens; nothing here
        // is about that.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var root = Path.Combine(Path.GetTempPath(), "backlog-error-boundary", Guid.NewGuid().ToString("n"));

        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features.json"));
        _ = features.SetEnabled(AppFeatures.FeedbackReporting, feedbackReporting);

        context.Services.AddSingleton<IAppUpdateService>(new UnsupportedAppUpdateService(currentVersion: "1.2.3"));
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton(new FeedbackReporter(
            new GitHubIntegration(
                new GitHubSettingsStore(Path.Combine(root, "github.json")),
                new SilentGitHubClient(),
                new SilentProbe())));
        context.Services.AddSingleton<FeedbackReportChannel>();

        var layout = context.Render<MainLayout>(parameters => parameters.Add(
            l => l.Body,
            builder =>
            {
                builder.OpenComponent<ThrowingPage>(0);
                builder.AddComponentParameter(1, nameof(ThrowingPage.ThrowOnRender), throwOnRender);
                builder.CloseComponent();
            }));

        return new LayoutHost(context, layout, root);
    }

    private sealed record LayoutHost(BunitContext Context, IRenderedComponent<MainLayout> Layout, string Root) : IDisposable
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

    /// <summary>A page with one button, which throws — or a page that throws
    /// before it has a button at all.</summary>
    private sealed class ThrowingPage : ComponentBase
    {
        [Parameter]
        public bool ThrowOnRender { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (ThrowOnRender)
            {
                throw new InvalidOperationException("The page threw while rendering.");
            }

            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "data-testid", "page");
            builder.OpenElement(2, "button");
            builder.AddAttribute(3, "type", "button");
            builder.AddAttribute(4, "data-testid", "page-throw");
            builder.AddAttribute(5, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, Throw));
            builder.AddContent(6, "Throw");
            builder.CloseElement();
            builder.CloseElement();
        }

        private static void Throw() => throw new InvalidOperationException("The page threw.");
    }

    private sealed class SilentGitHubClient : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SilentProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not configured."));

        public void Invalidate()
        {
        }
    }
}
