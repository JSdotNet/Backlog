using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The repository identity mark on the header's scope filter.
/// <para>
/// Classes rather than colours, because the shell says <em>which</em> repository a chip
/// is for and the stylesheet says which hue that is. What matters here is that the
/// shell reads the same answer the entry list and the roadmap read — which is the whole
/// reason the choice lives in Settings rather than on any one of them.
/// </para>
/// </summary>
public sealed class HomeRepositoryScopeTests
{
    [Fact]
    public void Each_scope_chip_carries_its_repositorys_mark()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");

        // Position, because neither repository has been given a colour of its own.
        Assert.Contains("repo-mark--1", chips[0].ClassName);
        Assert.Contains("repo-mark--2", chips[1].ClassName);
    }

    [Fact]
    public void A_chosen_colour_reaches_the_chip()
    {
        using var harness = CreateHarness(settings => Assert.Null(settings.SetRepositoryColour("backlog", 5)));
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");

        Assert.Contains("repo-mark--5", chips[0].ClassName);
    }

    [Fact]
    public void The_mark_is_never_the_only_thing_saying_which_repository()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");

        // .design/color-scheme.md#band-identity-tokens requires it: the alias is written
        // on the chip, so a reader who never sees a hue loses nothing.
        Assert.Equal("backlog", chips[0].TextContent.Trim());
        Assert.Equal("docs", chips[1].TextContent.Trim());
    }

    [Fact]
    public void Selecting_a_chip_keeps_its_mark()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        var chips = component.WaitForElements("[data-testid='repository-filter-option']");
        chips[0].Click();

        // The mark is identity and the fill is selection. They are two different facts
        // and the chip has to be able to carry both at once, which is why the identity
        // rule is an edge rather than a surface.
        component.WaitForAssertion(() =>
        {
            var selected = component.FindAll("[data-testid='repository-filter-option']")[0];
            Assert.Contains("chip--active", selected.ClassName);
            Assert.Contains("repo-mark--1", selected.ClassName);
        });
    }

    private static IRenderedComponent<Home> Render(Harness harness)
    {
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;
        return harness.Context.Render<Home>();
    }

    private static Harness CreateHarness(Action<GitHubSettingsStore>? configureRepositories = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-repository-scope-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        // The three surfaces under test are on; everything that would put extra
        // chrome or a network call in the way is off.
        _ = featureSettings.SetEnabled(RoadmapFeatures.Roadmap, true);
        _ = featureSettings.SetEnabled(DashboardFeatures.Dashboard, true);
        _ = featureSettings.SetEnabled(DevPcFeatures.SystemTools, true);
        _ = featureSettings.SetEnabled(SessionFeatures.Sessions, true);
        _ = featureSettings.SetEnabled(KnowledgeFeatures.KnowledgeSections, true);
        _ = featureSettings.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true);
        _ = featureSettings.SetEnabled(AppFeatures.InboxPane, false);
        _ = featureSettings.SetEnabled(AppFeatures.AiAssistant, false);
        _ = featureSettings.SetEnabled(AppFeatures.FeedbackReporting, false);
        _ = featureSettings.SetEnabled(BacklogFeatures.GitHubIntegration, false);

        var (repositories, errors) = GitHubSettings.ParseText(
            "backlog = JSdotNet/Backlog" + Environment.NewLine + "docs = JSdotNet/Docs");
        Assert.Empty(errors);

        Assert.Null(gitHubSettings.SetRepositories(
        [
            .. repositories.Select(repository => repository with
            {
                CloneDirectory = RepositoryRoot.Root.FullName,
                KnowledgeFolders = KnowledgeFolderSetting.Defaults()
            })
        ]));

        configureRepositories?.Invoke(gitHubSettings);

        var gitHub = new GitHubIntegration(gitHubSettings, new StubGitHubClient(), new StubProbe());
        var knowledgeFolderSource = new KnowledgeFolderSource(gitHubSettings, store);

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton(new FeedbackReporter(gitHub));
        context.Services.AddSingleton<IAzureFoundryChatClient, StubAzureFoundryChatClient>();
        context.Services.AddSingleton<ICopilotToolService, UnsupportedCopilotToolService>();

        // The sessions takeover, with nothing on the machine behind it. A shell test
        // asking whether the takeover replaced the panes should not also be reading
        // whatever the person running it had been doing that morning — and a pane that
        // only worked with rows in it would fail here, which is the point.
        context.Services.AddSingleton<IAgentSessionSource>(new EmptySessionSource());
        context.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(knowledgeFolderSource);
        // The Roadmap module the way a host wires it: a real plan document under the
        // same storage root, so the band draws what was stored rather than a fixture.
        context.Services.AddSingleton<IRoadmapPlanning>(sp =>
            BacklogTestHost.PlanningFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
        // The band gathers an item's linked and tagged work through this port before it
        // opens the editor, so a host that composes the band composes the rollup with it.
        context.Services.AddSingleton<IRoadmapItemRollup>(sp =>
            new Backlog.Infrastructure.FileSystem.Roadmap.RoadmapItemRollupService(
                BacklogTestHost.EntriesFor(sp.GetRequiredService<WorkspaceSettingsStore>()),
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        context.Services.AddSingleton<DesignKnowledgeProvider>();
        context.Services.AddSingleton<TechnologyKnowledgeService>();
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<KnowledgeMenu>();
        context.Services.AddSingleton<Arc42KnowledgeStore>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();
        context.Services.AddSingleton<KnowledgeScope>();
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        // The dashboard takeover, with no provider behind it — see DashboardTestHost.
        _ = context.Services.AddUnavailableDashboard("backlog", "backlog-ide");
        context.Services.AddScoped(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddScoped(sp => BacklogTestHost.StateFor(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<GitHubIntegration>(),
            BacklogCopilotCli.Unavailable));

        return new Harness(root, context);
    }

    private sealed record Harness(string Root, BunitContext Context) : IDisposable
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

    private sealed class EmptySessionSource : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentSessionCatalog.Empty);
    }

    private sealed class StubAzureFoundryChatClient : IAzureFoundryChatClient
    {
        public Task<AzureFoundryChatResponse> AskAsync(AzureFoundryChatRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AzureFoundryChatResponse("Not used in this test."));
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
