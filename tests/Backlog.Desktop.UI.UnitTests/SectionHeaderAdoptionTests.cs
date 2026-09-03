using AngleSharp.Dom;
using Backlog.Desktop.UI.Inbox;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pane and dialog headers now render the library's <c>SectionHeader</c>
/// instead of a hand-written copy of it, and what these tests hold is that the
/// swap changed nothing a reader or a screen reader can tell: the same elements,
/// the same class names, and the heading id each section's
/// <c>aria-labelledby</c> points at still on the heading itself.
///
/// <para>The class hooks that make that possible are pinned in the library's own
/// <c>ClassHookTests</c>; these are the sites, where a wrong parameter would
/// silently move a class out from under <c>app.css</c>.</para>
/// </summary>
public sealed class SectionHeaderAdoptionTests
{
    [Fact]
    public void The_inbox_pane_header_keeps_the_shape_it_hand_rolled()
    {
        using var context = new BunitContext();

        var pane = context.Render<InboxPane>(parameters => parameters
            .Add(p => p.Items, Array.Empty<InboxItem>()));

        var header = pane.Find(".inbox-pane__header");

        AssertPaneHeader(header, "inbox-pane", "inbox-pane-title");

        // No actions on this pane, so no wrapper for them either.
        Assert.Single(header.Children);
        Assert.Empty(pane.FindAll(".inbox-pane__header-actions"));
    }

    [Fact]
    public void The_update_dialog_header_keeps_its_unclassed_heading_and_its_id()
    {
        using var context = FooterContext();

        var footer = context.Render<AppFooter>();

        footer.Find("[data-testid='app-version']").Click();

        var header = footer.Find(".app-update-dialog__header");

        Assert.Equal("HEADER", header.TagName);
        Assert.Equal("app-update-dialog__header", header.GetAttribute("class"));

        var text = header.Children[0];
        Assert.Null(text.GetAttribute("class"));
        Assert.Equal("app-update-dialog__eyebrow", text.Children[0].GetAttribute("class"));

        // The dialog styles its heading by element, so the heading carries no
        // class at all - not an empty one.
        var heading = text.Children[1];
        Assert.Equal("H2", heading.TagName);
        Assert.Null(heading.GetAttribute("class"));
        Assert.Equal("app-update-title", heading.GetAttribute("id"));

        // The close control is a direct child of the header, and the description
        // aria-describedby points at stays in the dialog body.
        Assert.Equal("BUTTON", header.Children[1].TagName);
        Assert.Empty(header.QuerySelectorAll(".app-update-dialog__description"));
        Assert.NotNull(footer.Find("#app-update-description"));
    }

    [Fact]
    public void The_feedback_dialog_header_keeps_its_unclassed_heading_and_its_id()
    {
        using var context = FooterContext(feedback: true);

        var footer = context.Render<AppFooter>();

        footer.Find("[data-testid='feedback-button']").Click();

        var header = footer.Find(".feedback-dialog__header");

        Assert.Equal("HEADER", header.TagName);
        Assert.Equal("feedback-dialog__header", header.GetAttribute("class"));

        var text = header.Children[0];
        Assert.Null(text.GetAttribute("class"));
        Assert.Equal("feedback-dialog__eyebrow", text.Children[0].GetAttribute("class"));

        var heading = text.Children[1];
        Assert.Equal("H2", heading.TagName);
        Assert.Null(heading.GetAttribute("class"));
        Assert.Equal("feedback-title", heading.GetAttribute("id"));

        Assert.Equal("BUTTON", header.Children[1].TagName);
        Assert.Empty(header.QuerySelectorAll(".feedback-dialog__description"));
        Assert.NotNull(footer.Find("#feedback-description"));
    }

    /// <summary>
    /// The full pane-header shape, in the pane's own names: a bare text block
    /// holding an eyebrow, the heading its landmark points at, and a subtitle.
    /// Every one of those classes is styled in app.css, which the adoption was
    /// not allowed to touch.
    /// </summary>
    internal static void AssertPaneHeader(IElement header, string block, string headingId)
    {
        Assert.Equal("HEADER", header.TagName);
        Assert.Equal($"{block}__header", header.GetAttribute("class"));

        var text = header.Children[0];
        Assert.Equal("DIV", text.TagName);
        Assert.Null(text.GetAttribute("class"));

        Assert.Equal(["P", "H2", "P"], text.Children.Select(child => child.TagName));
        Assert.Equal(
            [$"{block}__eyebrow", $"{block}__title", $"{block}__subtitle"],
            text.Children.Select(child => child.GetAttribute("class")));

        Assert.Equal(headingId, text.Children[1].GetAttribute("id"));
    }

    /// <summary>The actions wrapper, which the panes with controls in the header
    /// keep under their own name.</summary>
    internal static void AssertPaneHeaderActions(IElement header, string block)
    {
        Assert.Equal(2, header.Children.Length);
        Assert.Equal("DIV", header.Children[1].TagName);
        Assert.Equal($"{block}__header-actions", header.Children[1].GetAttribute("class"));
    }

    private static BunitContext FooterContext(bool feedback = false)
    {
        var context = new BunitContext();

        // Both dialogs move focus into themselves when they open, which is a JS call.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var root = Path.Combine(
            Path.GetTempPath(),
            "backlog-section-header-adoption",
            Guid.NewGuid().ToString("n"));

        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features.json"));
        _ = features.SetEnabled(AppFeatures.FeedbackReporting, feedback);

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var gitHub = new GitHubIntegration(gitHubSettings, new StubGitHubClient(), new StubProbe());

        context.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton(new FeedbackReporter(gitHub));

        return context;
    }

    private sealed class StubGitHubClient : IGitHubClient
    {
        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
