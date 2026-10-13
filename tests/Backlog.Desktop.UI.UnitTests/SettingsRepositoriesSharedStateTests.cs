using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Repositories page is the Tasks module's section, hosted by the shell in a
/// kept-alive tab through a parameterless <c>DynamicComponent</c> - so a shell
/// re-render does not reach it, and what it shares with the shell has to reach it
/// some other way.
/// <para>
/// Two things cross that line. A feature switched on the Features tab changes what
/// the page draws, so the page listens for the switch itself. And how this machine
/// reaches GitHub - the connection and the logins <c>gh</c> holds - is one answer
/// read by both the page and the shell's AI page, so it is held once and asked for
/// once rather than each page keeping a copy the other cannot refresh.
/// </para>
/// </summary>
public sealed class SettingsRepositoriesSharedStateTests
{
    [Fact]
    public void Switching_github_integration_off_withdraws_the_account_and_access_blocks_from_the_open_page()
    {
        using var settings = RenderSettings(gitHubIntegration: true);
        OpenTab(settings.Component, "Repositories");
        settings.Component.WaitForElement("[data-testid='repo-account']");
        settings.Component.WaitForElement("[data-testid='repo-github-access']");

        OpenTab(settings.Component, "Features");
        FeatureSwitch(settings.Component, "GitHub integration").Change(false);
        OpenTab(settings.Component, "Repositories");

        settings.Component.WaitForAssertion(() =>
        {
            Assert.Empty(settings.Component.FindAll("[data-testid='repo-account']"));
            Assert.Empty(settings.Component.FindAll("[data-testid='repo-github-access']"));
        });
    }

    [Fact]
    public void Switching_additional_repositories_rewords_the_open_pages_status()
    {
        using var settings = RenderSettings(gitHubIntegration: true, additionalRepositories: true);
        OpenTab(settings.Component, "Repositories");
        settings.Component.WaitForAssertion(() =>
            Assert.Contains("@backlog", settings.Component.Find("[data-testid='github-repos-status']").TextContent, StringComparison.Ordinal));

        OpenTab(settings.Component, "Features");
        FeatureSwitch(settings.Component, "Additional repositories").Change(false);
        OpenTab(settings.Component, "Repositories");

        settings.Component.WaitForAssertion(() =>
        {
            var status = settings.Component.Find("[data-testid='github-repos-status']").TextContent;
            Assert.Contains("single-repository backlog experience", status, StringComparison.Ordinal);
            Assert.DoesNotContain("@backlog", status, StringComparison.Ordinal);
        });
    }

    /// <summary>The connection and the <c>gh</c> logins are a network call and a
    /// subprocess. Opening the settings screen and its Repositories page asks each
    /// once, not once per page that shows the answer.</summary>
    [Fact]
    public void The_connection_and_the_cli_logins_are_asked_for_once_when_both_pages_render()
    {
        var probe = new CountingProbe(new GitHubConnection(false, "Not connected."));
        var cli = new CountingCliAccounts();
        using var settings = RenderSettings(gitHubIntegration: true, probe: probe, cli: cli);

        OpenTab(settings.Component, "Repositories");
        settings.Component.WaitForAssertion(() =>
            Assert.Equal("Not connected.", settings.Component.Find("[data-testid='github-connection-status']").TextContent.Trim()));

        Assert.Equal(1, probe.Calls);
        Assert.Equal(1, cli.Calls);
    }

    /// <summary>A repository token is a way this machine reaches GitHub, so storing
    /// one re-asks the connection - and the AI page's GitHub usage checklist, which
    /// ticks its first step on that same answer, ticks with it.</summary>
    [Fact]
    public void A_repository_token_updates_the_connection_the_ai_page_reads()
    {
        var probe = new CountingProbe(new GitHubConnection(false, "Not connected."));
        using var settings = RenderSettings(gitHubIntegration: true, usageMetrics: true, probe: probe);

        OpenTab(settings.Component, "AI");
        settings.Component.WaitForAssertion(() =>
            Assert.Equal("false", FirstUsageStep(settings.Component).GetAttribute("data-done")));

        OpenTab(settings.Component, "Repositories");
        probe.Answer = new GitHubConnection(true, "Connected with the repository token.");
        var token = settings.Component.WaitForElement("[data-testid='github-token-input']");
        token.Input("ghp_example");
        token.Change("ghp_example");

        settings.Component.WaitForAssertion(() =>
            Assert.Equal("Connected with the repository token.", settings.Component.Find("[data-testid='github-connection-status']").TextContent.Trim()));

        OpenTab(settings.Component, "AI");
        settings.Component.WaitForAssertion(() =>
            Assert.Equal("true", FirstUsageStep(settings.Component).GetAttribute("data-done")));
    }

    /// <summary>Forgetting the token re-asks too, and the checklist unticks.</summary>
    [Fact]
    public void Forgetting_a_repository_token_updates_the_connection_the_ai_page_reads()
    {
        var probe = new CountingProbe(new GitHubConnection(true, "Connected with the repository token."));
        using var settings = RenderSettings(
            gitHubIntegration: true,
            usageMetrics: true,
            probe: probe,
            seed: store => Assert.Null(store.SetRepositoryToken("backlog", "ghp_example")));

        OpenTab(settings.Component, "AI");
        settings.Component.WaitForAssertion(() =>
            Assert.Equal("true", FirstUsageStep(settings.Component).GetAttribute("data-done")));

        OpenTab(settings.Component, "Repositories");
        probe.Answer = new GitHubConnection(false, "Not connected.");
        settings.Component.WaitForElement("[data-testid='github-clear-token-button']").Click();

        OpenTab(settings.Component, "AI");
        settings.Component.WaitForAssertion(() =>
            Assert.Equal("false", FirstUsageStep(settings.Component).GetAttribute("data-done")));
    }

    private static void OpenTab(IRenderedComponent<Settings> component, string title) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == title).Click();

    private static AngleSharp.Dom.IElement FeatureSwitch(IRenderedComponent<Settings> component, string name) =>
        component.FindAll(".feature-flag")
            .Single(row => row.QuerySelector(".feature-flag__title")?.TextContent == name)
            .QuerySelector("input")!;

    private static AngleSharp.Dom.IElement FirstUsageStep(IRenderedComponent<Settings> component) =>
        component.FindAll("[data-testid='github-usage-setup-steps'] [data-testid='setup-step']")[0];

    private static SettingsRenderContext RenderSettings(
        bool gitHubIntegration,
        bool additionalRepositories = false,
        bool usageMetrics = false,
        CountingProbe? probe = null,
        CountingCliAccounts? cli = null,
        Action<GitHubSettingsStore>? seed = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-repositories-shared-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, gitHubIntegration);
        _ = features.SetEnabled(TasksFeatures.AdditionalRepositories, additionalRepositories);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, usageMetrics);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("backlog = JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(githubSettings.SetRepositories(repositories));
        seed?.Invoke(githubSettings);

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IWorkingHoursSettings>(
            new WorkingHoursSettingsStore(Path.Combine(root, "working-hours", "working-hours.json")));
        context.Services.AddSingleton<IUsageResetSettings>(
            new UsageResetSettingsStore(Path.Combine(root, "usage-reset", "usage-reset.json")));
        context.Services.AddSingleton<ICaptureSourceSettings>(
            new CaptureSourcesSettingsStore(Path.Combine(root, "capture", "capture-sources.json")));
        context.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        context.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        context.Services.AddSingleton(new GitHubIntegration(
            githubSettings,
            new NoGitHub(),
            probe ?? new CountingProbe(new GitHubConnection(false, "Not connected.")),
            cli ?? new CountingCliAccounts()));
        context.Services.AddSingleton<FeedbackReporter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(githubSettings, store));
        context.Services.AddSingleton(new DevbookSourceSelection(githubSettings, new StubBranchCatalog()));
        context.Services.AddRepositorySettings();
        context.Services.AddRepositoryDevbookSettings();

        return new SettingsRenderContext(root, context, context.Render<Settings>());
    }

    /// <summary>Answers with whatever the test last said, and counts how often it
    /// was asked.</summary>
    private sealed class CountingProbe(GitHubConnection answer) : IGitHubConnectionProbe
    {
        private int _calls;

        public GitHubConnection Answer { get; set; } = answer;

        public int Calls => Volatile.Read(ref _calls);

        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(Answer);
        }

        public void Invalidate()
        {
        }
    }

    /// <summary>The <c>gh</c> CLI without the subprocess, counting how often it was
    /// asked which logins it holds.</summary>
    private sealed class CountingCliAccounts : IGhCliAccountSource
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyList<GhCliAccount>> ListAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<GhCliAccount>>([]);
        }

        public Task<string?> GetTokenAsync(
            string login,
            string? host = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public void Invalidate()
        {
        }
    }

    private sealed class NoGitHub : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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

    private sealed record SettingsRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
