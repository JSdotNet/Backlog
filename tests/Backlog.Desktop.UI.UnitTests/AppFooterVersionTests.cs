using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.AppUpdate;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The version control's label and its value read on one baseline.
/// <para>
/// <c>.header-util</c> is an inline flex box that centres its items. When the
/// word and the <c>&lt;code&gt;</c> sat in the button side by side they were two
/// flex items, each centred in its own box, and a monospace face with other
/// metrics than the label's landed the number visibly above the word. One
/// element around both makes them one flex item with one line box, and so one
/// baseline. A stylesheet cannot be measured here; the shape that makes the
/// baseline shared can.
/// </para>
/// </summary>
public sealed class AppFooterVersionTests
{
    [Fact]
    public void The_version_and_its_label_are_one_flex_item()
    {
        using var context = Context(workspace: null);

        var footer = context.Render<AppFooter>();

        var label = SoleChild(footer);
        Assert.StartsWith("Version", label.TextContent.Trim(), StringComparison.Ordinal);
        Assert.Equal("1.2.3", label.QuerySelector("code")?.TextContent);
    }

    [Fact]
    public void The_worktree_and_its_label_are_one_flex_item()
    {
        using var context = Context(new DevelopmentWorkspaceLabel("footer-worktree"));

        var footer = context.Render<AppFooter>();

        var label = SoleChild(footer);
        Assert.StartsWith("Worktree", label.TextContent.Trim(), StringComparison.Ordinal);
        Assert.Equal("footer-worktree", label.QuerySelector("[data-testid='app-workspace']")?.TextContent);
    }

    private static AngleSharp.Dom.IElement SoleChild(IRenderedComponent<AppFooter> footer)
    {
        var control = footer.Find("[data-testid='app-version']");

        var child = Assert.Single(control.Children);
        Assert.Equal("SPAN", child.TagName);
        Assert.Equal("app-version__text", child.GetAttribute("class"));

        return child;
    }

    private static BunitContext Context(DevelopmentWorkspaceLabel? workspace)
    {
        var context = new BunitContext();

        // The update window and the feedback dialog both reach for JS; nothing
        // here is about either.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var root = Path.Combine(Path.GetTempPath(), "backlog-footer-version", Guid.NewGuid().ToString("n"));

        context.Services.AddSingleton<IAppUpdateService>(new UnsupportedAppUpdateService(currentVersion: "1.2.3"));
        context.Services.AddSingleton<IAppFeatureSettings>(
            new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features.json")));
        context.Services.AddSingleton(new FeedbackReporter(
            new GitHubIntegration(
                new GitHubSettingsStore(Path.Combine(root, "github.json")),
                new SilentGitHubClient(),
                new SilentProbe())));
        context.Services.AddSingleton<FeedbackReportChannel>();

        if (workspace is not null) context.Services.AddSingleton(workspace);

        return context;
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
