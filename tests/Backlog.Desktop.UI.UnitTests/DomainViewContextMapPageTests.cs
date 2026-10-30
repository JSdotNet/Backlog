using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain view's Context map page, reached the way a reader reaches it: the
/// New domain view switch on, the Context map row picked (local ADR 0044). Map
/// draws the contexts with a line per pair worded from each context's dependency
/// tables; List names them in reading order; a card opens its context's page.
/// </summary>
public sealed class DomainViewContextMapPageTests : IDisposable
{
    private readonly List<string> _roots = [];

    /// <summary>The page reads the folder off the renderer's thread, and under the
    /// whole solution's concurrent run one second was not always enough.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private const string ContextMap = """
        # Backlog

        ```meta
        type: context-map
        status: draft
        ```

        ## Subdomain landscape

        | Bounded context | Subdomain type | Why |
        |---|---|---|
        | Tasks | Core | It owns the durable work model. |
        | Sync | Supporting | It keeps one person's data the same across devices. |

        ## Context map

        ```mermaid
        flowchart LR
            Tasks[Tasks]
            Sync[Sync]
            Sync -->|OHS + Published Language<br/>Task replica feed| Tasks
        ```

        ## Strategic rules

        - Sync carries other contexts' documents without interpreting them.
        """;

    private const string TasksContext = """
        # Tasks

        ```meta
        status: draft
        index: root
        type: context
        ```

        Tasks maintains a personal backlog of prompts and tasks.
        """;

    private const string TasksDependencies = """
        # Tasks

        ```meta
        type: dependencies
        status: draft
        ```

        ## Outbound dependencies

        | Depends on (context/module) | DDD pattern | Why |
        |---|---|---|
        | [Sync](../sync/context.md) | OHS + Published Language (Sync = supplier) | A task reaches the person's other devices as one whole document. |
        """;

    private const string SyncContext = """
        # Sync

        ```meta
        status: draft
        type: context
        ```

        Sync carries documents between devices.
        """;

    [Fact]
    public async Task The_Context_map_row_draws_the_map_with_each_line_worded_from_the_dependency_tables()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='context-map-page']")), Patience);
        Assert.Equal("context-map", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='context-map-card']").Count), Patience);
        var line = component.Find("[data-testid='context-map-line']");
        Assert.Equal("Sync to Tasks: A task reaches the person's other devices as one whole document.", line.GetAttribute("aria-label"));
        Assert.DoesNotContain("OHS", component.Find("[data-testid='context-map-canvas']").InnerHtml, StringComparison.Ordinal);
        Assert.Empty(component.FindAll("[data-testid='domain-view-placeholder']"));

        await component.InvokeAsync(() => component.Find("[data-testid='context-map-page-tab-list']").Click());
        Assert.Equal(["Tasks", "Sync"], component.FindAll("[data-testid='context-map-list-card']").Select(card => card.GetAttribute("data-context")));
        Assert.Contains("without interpreting them", component.Find("[data-testid='context-map-list-rules']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>The Zoom: a card follows its context's dependencies.md, which has no
    /// row of its own, so the pane selects the context's row — the one page that
    /// opens on its neighbours, the same page the reader's own pick of the row opens.</summary>
    [Theory]
    [InlineData("Tasks", "tasks")]
    [InlineData("Sync", "sync")]
    public async Task Picking_a_card_opens_that_contexts_one_page_as_the_menu_does(string id, string slug)
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='context-map-card']").Count), Patience);

        await component.InvokeAsync(() => component.Find($"[data-testid='context-map-card'][data-context='{id}']").Click());

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-context-page']")), Patience);
        Assert.Equal("context", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Empty(component.FindAll("[data-testid='domain-context-page'] [role='tab']"));
        Assert.Single(component.FindAll("[data-testid='domain-context-page'] > [data-testid='domain-neighbours']"));
        Assert.Equal($"{slug}/context.md", component.FindComponent<DomainView>().Instance.SelectedPath);

        // The reader's own pick of the same row afterwards opens the same page.
        await ClickMenuItemAsync(component, "Context Map");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='context-map-page']")), Patience);
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, id)), Patience);
        await ClickMenuItemAsync(component, id);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-context-page']")), Patience);
        Assert.Single(component.FindAll("[data-testid='domain-context-page'] > [data-testid='domain-neighbours']"));
    }

    /// <summary>The repository's own map, read the way the page reads it: every
    /// context it names is a card with its folder, and every line says why in the
    /// words of a dependency table's row: the source context's, or the other's.</summary>
    [Fact]
    public async Task The_repositorys_own_map_names_a_folder_for_every_context_and_words_its_lines()
    {
        await using var harness = CreateHarness(RepositoryRoot.Root.FullName);
        var view = await harness.Context.Services.GetRequiredService<DomainDevbookStore>().LoadAsync(harness.RepositoryAlias, TestContext.Current.CancellationToken);
        Assert.Null(view.Error);

        var read = DomainViewContextMapPage.Read(view);

        Assert.Null(read.Problem);
        Assert.Equal(15, read.Map.Contexts.Count);
        Assert.All(read.Map.Contexts, context => Assert.NotNull(context.Slug));
        var unworded = read.Map.Lines.Where(line => line.Wording.Length == 0).Select(read.Map.Describe).ToList();
        Assert.True(unworded.Count == 0, "Lines no dependency row words: " + string.Join("; ", unworded));
    }

    /// <summary>One menu row, by the label a reader reads; null while it is not drawn.</summary>
    private static IElement? MenuItem(IRenderedComponent<DevbookPane> component, string label) =>
        component.FindAll(".devbook-menu__item")
            .SingleOrDefault(item => string.Equals(item.QuerySelector(".devbook-menu__label")?.TextContent.Trim(), label, StringComparison.Ordinal));

    /// <summary>Presses one menu row, found and clicked in one dispatch so a load
    /// landing in between cannot take the row's handler away.</summary>
    private static Task ClickMenuItemAsync(IRenderedComponent<DevbookPane> component, string label) =>
        component.InvokeAsync(() => MenuItem(component, label)!.Click());

    private Harness CreateHarness(string? cloneDirectory = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-view-context-map-page", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "sync"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), ContextMap);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "context.md"), TasksContext);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "dependencies.md"), TasksDependencies);
        File.WriteAllText(Path.Combine(root, ".domain", "sync", "context.md"), SyncContext);

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(DevbookFeatures.RepositoryDevbook, true);
        _ = features.SetEnabled(DevbookFeatures.DomainView, true);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = cloneDirectory ?? root,
            DevbookFolders = [.. DevbookFolderSetting.Defaults().Select(folder => folder with { Enabled = folder.Key is ".domain" })]
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
