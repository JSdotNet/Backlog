using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The legend under the Devbook tree that says what each scenario dot means. It is
/// drawn where a dot can be — a repository whose domain folder has a scenario page
/// — and nowhere else, so a repository without one, and a Brain, carry no key to
/// marks they never show.
/// </summary>
public sealed class DevbookPaneScenarioLegendTests : IDisposable
{
    private const string ScenarioPage = """
        # Scenarios

        ## Place an order

        ```meta
        type: scenario
        ```

        ### The cart is filled
        - **Given** an empty cart
        """;

    private readonly List<string> _roots = [];

    [Fact]
    public async Task A_repository_with_a_scenario_page_shows_the_four_dots_under_the_tree()
    {
        await using var harness = CreateHarness(withScenarioPage: true);

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='devbook-scenario-legend']")));
        var legend = component.Find("[data-testid='devbook-scenario-legend']");
        Assert.Equal(
            [("passed", "Passed"), ("failed", "Failed"), ("stale", "Stale"), ("never-run", "Never run")],
            legend.QuerySelectorAll("[data-testid='devbook-scenario-legend-item']")
                .Select(item => (item.QuerySelector(".devbook-scenario-dot")!.GetAttribute("data-state"), item.TextContent.Trim())));

        // Each dot is the tree's own shape, hidden from a screen reader: the word
        // beside it already names the state, and saying it twice is noise.
        Assert.All(legend.QuerySelectorAll(".devbook-scenario-dot"), dot =>
        {
            Assert.Equal("true", dot.GetAttribute("aria-hidden"));
            Assert.Null(dot.GetAttribute("role"));
            Assert.Null(dot.GetAttribute("aria-label"));
        });
    }

    [Fact]
    public async Task A_repository_without_a_scenario_page_shows_no_legend()
    {
        await using var harness = CreateHarness(withScenarioPage: false);

        var component = harness.Render();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".devbook-menu__item--file")));
        Assert.Empty(component.FindAll("[data-testid='devbook-scenario-legend']"));
    }

    private Harness CreateHarness(bool withScenarioPage)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-devbook-pane-legend", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "orders"));
        _roots.Add(root);
        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts.\n");
        if (withScenarioPage) File.WriteAllText(Path.Combine(root, ".domain", "orders", "scenarios.md"), ScenarioPage);

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(DevbookFeatures.RepositoryDevbook, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        // The domain folder alone: the legend belongs to it, and the test is not also
        // a test of which folders this machine happens to have.
        List<DevbookFolderSetting> folders = [.. DevbookFolderSetting.Defaults().Select(folder => folder with { Enabled = folder.Key is ".domain" })];
        var repository = repositories[0] with { CloneDirectory = root, DevbookFolders = folders };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var source = new DevbookFolderSource(gitHub, settings);
        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IDevbookFolderSource>(source);
        context.Services.AddSingleton(new DevbookScenarioStore(source));
        context.Services.AddSingleton(sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));
        context.Services.AddSingleton<Arc42DevbookStore>();
        context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<DevbookChapterWriter>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<DevbookScope>();
        context.Services.AddScoped<DevbookOpenChapter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<DevbookUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());

        return new Harness(context, repository.Alias);
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
