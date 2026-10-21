using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain view's Model page, reached the way a reader reaches it: the New
/// domain view switch on, a context opened, its Model row picked. The page opens on
/// Diagram with a Diagram | Fields | Events bar and draws the file's classDiagram as
/// the aggregate's consistency-boundary band (local ADR 0037); a split
/// <c>model.&lt;name&gt;.md</c> opens the same page, and a fence the reader cannot
/// arrange is drawn with mermaid.
/// </summary>
public sealed class DomainViewModelPageTests : IDisposable
{
    private readonly List<string> _roots = [];

    /// <summary>The model page reads the folder off the renderer's thread, and under
    /// the whole solution's concurrent run one second was not always enough.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private const string Model = """
        # Model: Tasks

        ```meta
        type: model
        status: draft
        ```

        The structure.

        ## Model diagram

        ```mermaid
        classDiagram
            class Task {
                <<aggregate root>>
                +Id id
                +List~Id~ depends_on
                +List~String~ repo_ids
            }
            class SubItem {
                <<entity>>
            }
            Task "1" *-- "0..*" SubItem : contains
        ```

        ## Relationship notes

        - `Task` is the only consistency boundary.
        """;

    private const string Domain = """
        # Domain: Tasks

        ```meta
        status: draft
        ```

        What the tasks context is.

        ## Task

        ```meta
        type: aggregate
        status: draft
        ```

        A refined, actionable item.

        ## Occurrence Spawning

        ```meta
        type: domain-service
        status: proposed
        ```

        Creates the next occurrence of a repeating task. Invocation semantics:
        called synchronously by the use case that completes the task.

        ## StatusChanged

        ```meta
        type: domain-event
        status: draft
        related: [.devbook/domain/tasks/domain.md#task]
        ```

        Published when a `Task` changes status.

        ### Payload

        - `task_id` - task identifier.

        ### Consumers

        - Monitoring & Dashboard.
        """;

    private const string Repositories = """
        # Domain: Repository Management

        ```meta
        status: draft
        ```

        The repositories.

        ## Repository

        ```meta
        type: entity
        status: draft
        aliases: [repo_id, repo_ids]
        ```

        One registered repository.
        """;

    private const string Split = """
        # Model: Scheduling

        ```meta
        type: model
        status: draft
        ```

        The scheduling part.

        ## Schedule

        ```mermaid
        classDiagram
            class Schedule {
                <<value object>>
            }
        ```
        """;

    [Fact]
    public async Task The_Model_row_opens_on_Diagram_with_the_aggregate_in_its_band()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view']")), Patience);
        // The menu draws after the view frame, and under the whole suite's load it lags.
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Tasks")), Patience);
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Model")), Patience);
        await ClickMenuItemAsync(component, "Model");

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-model-band']")), Patience);
        Assert.Equal("model", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Equal(["Diagram", "Fields", "Events"], component.FindAll("[data-testid='domain-model-tabs'] [role='tab']").Select(tab => tab.TextContent.Trim()));
        Assert.Equal("true", component.Find("[data-testid='domain-model-tab-diagram']").GetAttribute("aria-selected"));
        Assert.Equal("SubItem", component.Find("[data-testid='domain-model-part']").GetAttribute("data-part"));
        Assert.Equal("Task model", component.Find("[data-testid='domain-model-title']").TextContent);

        // repo_ids names another context's entity by its alias; depends_on, with no
        // line and no stem, stays as written.
        Assert.Equal(
            ["depends_on", "Repository"],
            component.FindAll("[data-testid='domain-model-reference']").Select(r => r.GetAttribute("data-target")));
        Assert.Equal("Occurrence Spawning", component.Find("[data-testid='domain-model-service']").GetAttribute("data-name"));
        Assert.Empty(component.FindAll("[data-testid='domain-view-placeholder']"));
    }

    [Fact]
    public async Task Fields_and_Events_read_the_model_notes_and_the_contexts_domain_chapters()
    {
        await using var harness = CreateHarness();

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, "tasks/model.md"));

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-model-band']")), Patience);
        await view.InvokeAsync(() => view.Find("[data-testid='domain-model-tab-fields']").Click());

        view.WaitForAssertion(() => Assert.Equal(["Task", "SubItem"], view.FindAll("[data-testid='domain-fields-class']").Select(c => c.GetAttribute("data-name"))), Patience);
        Assert.Contains("only consistency boundary", view.Find("[data-testid='domain-model-relationship-notes']").TextContent);
        var service = view.Find("[data-testid='domain-service-invocation']");
        Assert.Equal("Occurrence Spawning", service.GetAttribute("data-name"));
        Assert.Contains("Called synchronously", service.TextContent);

        await view.InvokeAsync(() => view.Find("[data-testid='domain-model-tab-events']").Click());

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-event']")), Patience);
        var statusChanged = view.Find("[data-testid='domain-event']");
        Assert.Equal("StatusChanged", statusChanged.GetAttribute("data-name"));
        Assert.Equal("documented", statusChanged.GetAttribute("data-delivery"));
        Assert.Contains("Task", statusChanged.QuerySelector("[data-testid='domain-event-raised-by']")!.TextContent);
        Assert.Contains("task_id", statusChanged.QuerySelector("[data-testid='domain-event-payload']")!.TextContent);
        Assert.Contains("Monitoring", statusChanged.QuerySelector("[data-testid='domain-event-consumers']")!.TextContent);
    }

    [Fact]
    public async Task A_split_model_file_opens_the_model_page_and_falls_back_to_mermaid_without_a_root()
    {
        await using var harness = CreateHarness();

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, "tasks/model.scheduling.md"));

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-model-diagram']")), Patience);
        Assert.Equal("model", view.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Equal("mermaid", view.Find("[data-testid='domain-model-diagram']").GetAttribute("data-rendered"));
        Assert.Single(view.FindAll("[data-testid='diagram-view']"));
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
        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-view-model-page", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), Domain);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "model.md"), Model);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "model.scheduling.md"), Split);
        Directory.CreateDirectory(Path.Combine(root, ".domain", "repository-management"));
        File.WriteAllText(Path.Combine(root, ".domain", "repository-management", "domain.md"), Repositories);
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
