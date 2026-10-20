using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain view's Flow page, reached as a reader reaches it: the New domain view
/// switch on and a context's Flow row picked. A lifecycle's stateDiagram-v2 is
/// drawn as one row of states with the chapter's prose beneath it, a sequence in the
/// same style, and a Mermaid diagram the page cannot read keeps the DiagramView
/// rendering (local ADR 0036).
/// </summary>
public sealed class DomainViewFlowPageTests : IDisposable
{
    private readonly List<string> _roots = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private const string Flow = """
        # Tasks

        ```meta
        type: flow
        status: draft
        ```

        ## Task lifecycle

        ```mermaid
        stateDiagram-v2
            [*] --> Draft : Created
            Draft --> Ready : Refined
            Ready --> Done : Completed
            Done --> Draft : Restored
        ```

        - Restoring a done task returns it to `Draft`.

        ## Capture intake

        ```mermaid
        sequenceDiagram
            participant P as Phone
            participant I as Inbox
            P->>I: capture
        ```

        The intake parses no token.

        ## Task model

        ```mermaid
        classDiagram
            class Task
        ```
        """;

    [Fact]
    public async Task The_Flow_row_draws_the_lifecycle_as_one_row_and_keeps_every_other_diagram()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view']")), Patience);
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Tasks")), Patience);
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Flow")), Patience);
        await ClickMenuItemAsync(component, "Flow");

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-flow-row']")), Patience);
        Assert.Equal("flow", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Equal(
            ["[*]start", "Draft", "Ready", "Done"],
            component.FindAll("[data-testid='domain-flow-state']").Select(state => state.GetAttribute("data-state")));
        Assert.Equal("restore", component.Find("[data-testid='domain-flow-transition'][data-from='Done']").GetAttribute("data-direction"));

        var lifecycle = component.Find("[data-testid='domain-flow-section'][data-flow='task-lifecycle']");
        Assert.Contains("Restoring a done task", lifecycle.TextContent);
        Assert.Empty(lifecycle.QuerySelectorAll("[data-testid='diagram-view']"));

        var intake = component.Find("[data-testid='domain-flow-section'][data-flow='capture-intake']");
        Assert.Equal(["P", "I"], intake.QuerySelectorAll("[data-testid='domain-sequence-participant']").Select(card => card.GetAttribute("data-participant")));
        Assert.Empty(intake.QuerySelectorAll("[data-testid='diagram-view']"));

        var model = component.Find("[data-testid='domain-flow-section'][data-flow='task-model']");
        Assert.Single(model.QuerySelectorAll("[data-testid='diagram-view']"));
        Assert.Empty(component.FindAll("[data-testid='domain-view-placeholder']"));
    }

    [Fact]
    public async Task A_path_that_names_no_flow_file_says_so()
    {
        await using var harness = CreateHarness();

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, "tasks/flow.missing.md"));

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-view-flow-problem']")), Patience);
        Assert.Equal("flow", view.Find("[data-testid='domain-view']").GetAttribute("data-page"));
    }

    /// <summary>One menu row, by the label a reader reads; null while it is not drawn.</summary>
    private static IElement? MenuItem(IRenderedComponent<DevbookPane> component, string label) =>
        component.FindAll(".devbook-menu__item")
            .SingleOrDefault(item => string.Equals(item.QuerySelector(".devbook-menu__label")?.TextContent.Trim(), label, StringComparison.Ordinal));

    /// <summary>Presses one menu row, found and clicked in one dispatch so a load
    /// landing in between cannot take the row's handler away.</summary>
    private static Task ClickMenuItemAsync(IRenderedComponent<DevbookPane> component, string label) =>
        component.InvokeAsync(() => MenuItem(component, label)!.Click());

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-view-flow-page", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), "# Domain: Tasks\n\n```meta\nstatus: draft\n```\n\nWhat the tasks context is.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "flow.md"), Flow);
        File.WriteAllText(Path.Combine(root, ".arc42", "03-context-and-scope.md"), "# Context and scope\n\nThe system in its surroundings.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(DevbookFeatures.RepositoryDevbook, true);
        _ = features.SetEnabled(DevbookFeatures.DomainView, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            DevbookFolders = [.. DevbookFolderSetting.Defaults().Select(folder => folder with { Enabled = folder.Key is ".domain" or ".arc42" })]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHub, settings));
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

        return new Harness(context, repository.Alias, features);
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias, IAppFeatureSettings Features) : IAsyncDisposable
    {
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
