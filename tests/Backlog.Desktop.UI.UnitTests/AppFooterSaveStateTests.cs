using Backlog.Desktop.UI.AppUpdate;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The footer draws the window's save state, and draws nothing when there is none.
/// <para>
/// The indicator used to be Home's, which meant it existed on one route and the
/// other two routes had no save feedback at all. Moving it into the band
/// MainLayout mounts makes it every route's — and makes the quiet state load
/// bearing, because a band that always said something would be saying it on
/// Settings too.
/// </para>
/// <para>
/// What it reads is <see cref="ISaveStatusSource"/> and not a module's state
/// class. That is the whole point of the interface, so this test registers a stub
/// rather than a backlog: if the footer needed the real thing to be testable it
/// would mean the shell had learned which module matters.
/// </para>
/// </summary>
public sealed class AppFooterSaveStateTests
{
    [Fact]
    public void A_window_with_nothing_in_flight_shows_no_indicator()
    {
        using var context = Context(new StubSaveStatus(SaveState.Idle));

        var footer = context.Render<AppFooter>();

        Assert.Empty(footer.FindAll("[data-testid='save-state-indicator']"));
    }

    [Fact]
    public void A_save_in_flight_is_drawn_in_the_band()
    {
        using var context = Context(new StubSaveStatus(SaveState.Saving));

        var footer = context.Render<AppFooter>();

        Assert.Equal("Saving…", footer.Find("[data-testid='save-state-indicator']").TextContent.Trim());
    }

    /// <summary>The wording is the app's and moved here verbatim from Home's
    /// header, because <c>.design/interaction-guidelines.md#save-state-indicator-vocabulary</c>
    /// fixes it across channels — "Couldn't save", not the library's default.</summary>
    [Fact]
    public void A_failed_save_says_so_in_the_products_own_words()
    {
        using var context = Context(new StubSaveStatus(SaveState.Failed));

        var footer = context.Render<AppFooter>();

        Assert.Equal("Couldn't save", footer.Find("[data-testid='save-state-indicator']").TextContent.Trim());
    }

    /// <summary>
    /// The state changes from a background continuation — a debounce flush, the
    /// settle timer — so the footer has to be listening rather than re-reading on
    /// its next render, which on a route it is not part of may never come.
    /// </summary>
    [Fact]
    public void The_band_redraws_when_the_state_moves_underneath_it()
    {
        var status = new StubSaveStatus(SaveState.Idle);
        using var context = Context(status);

        var footer = context.Render<AppFooter>();

        Assert.Empty(footer.FindAll("[data-testid='save-state-indicator']"));

        status.MoveTo(SaveState.Saved);

        Assert.Equal("Saved", footer.Find("[data-testid='save-state-indicator']").TextContent.Trim());
    }

    /// <summary>A host that registers no backlog still has to be able to draw a
    /// footer — it is every page now, including any future settings-only or
    /// diagnostics head. Hence the resolve rather than an inject.</summary>
    [Fact]
    public void A_host_with_no_save_status_at_all_still_gets_a_footer()
    {
        using var context = Context(status: null);

        var footer = context.Render<AppFooter>();

        Assert.NotNull(footer.Find("[data-testid='app-footer']"));
        Assert.Empty(footer.FindAll("[data-testid='save-state-indicator']"));
    }

    private static BunitContext Context(ISaveStatusSource? status)
    {
        var context = new BunitContext();

        // The update window and the feedback dialog both reach for JS; nothing
        // here is about either.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var root = Path.Combine(Path.GetTempPath(), "backlog-footer", Guid.NewGuid().ToString("n"));

        context.Services.AddSingleton<IAppUpdateService>(new UnsupportedAppUpdateService(currentVersion: "1.2.3"));
        context.Services.AddSingleton<IAppFeatureSettings>(
            new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features.json")));
        context.Services.AddSingleton(new FeedbackReporter(
            new GitHubIntegration(
                new GitHubSettingsStore(Path.Combine(root, "github.json")),
                new SilentGitHubClient(),
                new SilentProbe())));

        if (status is not null) context.Services.AddSingleton(status);

        return context;
    }

    /// <summary>A save status nobody owns, moved by hand. It is the whole of what
    /// the footer is allowed to know.</summary>
    private sealed class StubSaveStatus(SaveState initial) : ISaveStatusSource
    {
        public SaveState Current { get; private set; } = initial;

        public event Action? Changed;

        public void MoveTo(SaveState state)
        {
            Current = state;
            Changed?.Invoke();
        }
    }

    private sealed class SilentGitHubClient : IGitHubClient
    {
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
