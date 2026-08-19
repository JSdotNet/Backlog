using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Opening the app has to show the backlog that is already there.
/// <para>
/// The list is a parameterless child of the shell, so Blazor does not re-render
/// it just because the shell finished loading — the state has to say it changed.
/// Reading an empty store finishes before the first render and hides that, which
/// is why this test seeds an entry first: with anything on disk the read is
/// genuinely asynchronous, and a shell that stays quiet leaves a populated
/// backlog showing "Nothing here yet."
/// </para>
/// </summary>
public sealed class HomeInitialLoadTests
{
    [Fact]
    public async Task Entries_already_in_the_store_are_on_screen_at_first_render()
    {
        using var harness = await CreateHarnessAsync("# Seeded entry");
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;

        var component = harness.Context.Render<Home>();

        // Nothing else touches the list, so this only ever completes because the
        // load announced itself. Without that there is no second render and this
        // waits until it gives up.
        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='empty-state']"));
            Assert.Contains("Seeded entry", component.Find("[data-testid='entry-list']").TextContent);
        });
    }

    private static async Task<Harness> CreateHarnessAsync(string entryText)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-home-initial-load", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));

        // Written through the real use case, so the store is left exactly as the
        // app leaves it — index and all.
        var saved = await BacklogTestHost.EntriesFor(store).SaveFromTextAsync(null, entryText, 0);
        Assert.True(saved.IsSuccess);

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        foreach (var feature in new[]
                 {
                     AppFeatures.InboxPane,
                     KnowledgeFeatures.KnowledgeSections,
                     KnowledgeFeatures.RepositoryKnowledge,
                     DevPcFeatures.SystemTools,
                     AppFeatures.AiAssistant,
                     AppFeatures.FeedbackReporting,
                     BacklogFeatures.GitHubIntegration
                 })
        {
            _ = featureSettings.SetEnabled(feature, false);
        }

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
        context.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
        context.Services.AddSingleton<IKnowledgeFolderSource>(knowledgeFolderSource);
        // The Roadmap module the way a host wires it: a real plan document under the
        // same storage root, so the band draws what was stored rather than a fixture.
        context.Services.AddSingleton<IRoadmapPlanning>(sp =>
            BacklogTestHost.PlanningFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
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
