using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain view's Demo strip (local ADR 0047): with the New domain view switch
/// on, a page that owns a click demo shows a Demo button above it, and pressing the
/// button shows the demo in a DemoView the way the old panel draws it. Which demos
/// a page owns is the old panel's answer — the demo beside the file by name, and
/// those its chapters' <c>demo</c> fields name, once each, with their places.
/// </summary>
public sealed class DomainViewDemoTests : IDisposable
{
    private readonly List<string> _roots = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private const string ContextDemo = "<!doctype html><html><body>tasks context demo</body></html>";
    private const string FeaturesDemo = "<!doctype html><html><body>tasks features demo</body></html>";
    private const string RequirementsDemo = "<!doctype html><html><body>tasks requirements demo</body></html>";

    private const string Context = """
        # Tasks

        ```meta
        type: context
        status: draft
        index: root
        ```

        The tasks context.
        """;

    private const string Domain = """
        # Domain: Tasks

        ```meta
        type: domain
        status: draft
        ```

        What the tasks context is.

        ## Task

        ```meta
        type: aggregate
        status: draft
        ```

        A refined, actionable item.
        """;

    // The Capture chapter names a place in the features demo under the other
    // spelling of the same folder: still one demo, with that place as its chip.
    private const string Features = """
        # Features

        ```meta
        type: features
        status: draft
        ```

        ## Capture

        ```meta
        type: feature
        status: draft
        demo: [.devbook/domain/tasks/features.demo.html#walkthrough/capture]
        ```

        Write a task down.
        """;

    // Owns requirements.demo.html by name and names the features demo by field:
    // two demos, so each button says which.
    private const string Requirements = """
        # Requirements

        ```meta
        type: requirements
        status: draft
        ```

        ## Capture

        ```meta
        type: requirements
        status: draft
        demo: [.devbook/domain/tasks/features.demo.html#walkthrough/capture]
        ```

        ### Requirement: A captured task keeps its words

        ```meta
        type: requirement
        status: draft
        ```

        The system SHALL keep a captured task's words as written.
        """;

    [Fact]
    public async Task The_Features_page_shows_a_Demo_button_that_shows_its_demo_with_the_places_its_chapters_name()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view']")), Patience);
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Features")), Patience);
        await ClickMenuItemAsync(component, "Features");

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view'] [data-testid='domain-view-demos']")), Patience);
        var button = Assert.Single(component.FindAll("[data-testid='domain-view-demo']"));
        Assert.Equal("Demo", button.TextContent.Trim());
        Assert.Equal("false", button.GetAttribute("aria-pressed"));
        Assert.Empty(component.FindAll("[data-testid='demo-view']"));

        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-demo']").Click());

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='demo-view']")), Patience);
        Assert.Equal("true", component.Find("[data-testid='domain-view-demo']").GetAttribute("aria-pressed"));
        Assert.Equal(".domain/tasks/features.demo.html", component.Find(".demo-view__path").TextContent.Trim());
        Assert.Equal(["walkthrough/capture"], component.FindAll("[data-testid='demo-view-place']").Select(place => place.TextContent.Trim()));
        component.WaitForAssertion(() => Assert.Single(harness.Context.JSInterop.Invocations["backlogDemos.load"]), Patience);
        Assert.Equal(FeaturesDemo, Assert.Single(harness.Context.JSInterop.Invocations["backlogDemos.load"]).Arguments[2]);

        // Pressed again, the demo goes.
        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-demo']").Click());
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='demo-view']")), Patience);
    }

    [Fact]
    public async Task A_contexts_folder_shows_the_contexts_own_demo()
    {
        await using var harness = CreateHarness();

        var view = RenderView(harness, "tasks");

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-view-demo']")), Patience);
        Assert.Equal("context", view.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Equal("Demo", view.Find("[data-testid='domain-view-demo']").TextContent.Trim());

        await view.InvokeAsync(() => view.Find("[data-testid='domain-view-demo']").Click());

        view.WaitForAssertion(() => Assert.Equal(".domain/tasks/demo.html", view.Find(".demo-view__path").TextContent.Trim()), Patience);
        view.WaitForAssertion(() => Assert.Equal(ContextDemo, Assert.Single(harness.Context.JSInterop.Invocations["backlogDemos.load"]).Arguments[2]), Patience);
    }

    [Fact]
    public async Task A_page_owning_several_demos_shows_a_button_for_each_named_by_its_file()
    {
        await using var harness = CreateHarness();

        var view = RenderView(harness, "tasks/requirements.md");

        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("[data-testid='domain-view-demo']").Count), Patience);
        Assert.Equal(
            ["Demo · requirements", "Demo · features"],
            view.FindAll("[data-testid='domain-view-demo']").Select(button => button.TextContent.Trim()));

        await view.InvokeAsync(() => view.FindAll("[data-testid='domain-view-demo']")[1].Click());

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='demo-view']")), Patience);
        Assert.Equal(".devbook/domain/tasks/features.demo.html", view.Find(".demo-view__path").TextContent.Trim());
        Assert.Equal(["walkthrough/capture"], view.FindAll("[data-testid='demo-view-place']").Select(place => place.TextContent.Trim()));
        Assert.Equal(["false", "true"], view.FindAll("[data-testid='domain-view-demo']").Select(button => button.GetAttribute("aria-pressed")));
    }

    /// <summary>Moved from a page with a demo to one without, the strip goes with
    /// the page it belonged to rather than staying above the next.</summary>
    [Theory]
    [InlineData("tasks/domain.md", "domain")]
    [InlineData("context-map.md", "context-map")]
    public async Task A_page_without_a_demo_shows_no_strip(string selectedPath, string page)
    {
        await using var harness = CreateHarness();

        var view = RenderView(harness, "tasks/features.md");
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-view-demos']")), Patience);

        view.Render(parameters => parameters.Add(v => v.SelectedPath, selectedPath));

        view.WaitForAssertion(() => Assert.Equal(page, view.Find("[data-testid='domain-view']").GetAttribute("data-page")), Patience);
        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-testid='domain-view-demos']")), Patience);
        Assert.Empty(view.FindAll("[data-testid='domain-view-demo']"));
    }

    private static IRenderedComponent<DomainView> RenderView(Harness harness, string selectedPath) =>
        harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, selectedPath));

    /// <summary>One menu row, by the label a reader reads; null while it is not drawn.</summary>
    private static IElement? MenuItem(IRenderedComponent<DevbookPane> component, string label) =>
        component.FindAll(".devbook-menu__item")
            .SingleOrDefault(item => string.Equals(item.QuerySelector(".devbook-menu__label")?.TextContent.Trim(), label, StringComparison.Ordinal));

    /// <summary>Presses one menu row, found and clicked in one dispatch so a load
    /// landing in between cannot take the row's handler away.</summary>
    private static async Task ClickMenuItemAsync(IRenderedComponent<DevbookPane> component, string label)
    {
        var until = DateTime.UtcNow + Patience;
        while (true)
        {
            var pressed = await component.InvokeAsync(() =>
            {
                if (MenuItem(component, label) is not { } item) return false;
                item.Click();
                return true;
            });
            if (pressed) return;
            Assert.True(DateTime.UtcNow < until, $"The menu row {label} never came back.");
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
    }

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-view-demos", Guid.NewGuid().ToString("N"));
        var tasks = Path.Combine(root, ".domain", "tasks");
        Directory.CreateDirectory(tasks);
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n");
        File.WriteAllText(Path.Combine(tasks, "context.md"), Context);
        File.WriteAllText(Path.Combine(tasks, "domain.md"), Domain);
        File.WriteAllText(Path.Combine(tasks, "features.md"), Features);
        File.WriteAllText(Path.Combine(tasks, "requirements.md"), Requirements);
        File.WriteAllText(Path.Combine(tasks, "demo.html"), ContextDemo);
        File.WriteAllText(Path.Combine(tasks, "features.demo.html"), FeaturesDemo);
        File.WriteAllText(Path.Combine(tasks, "requirements.demo.html"), RequirementsDemo);
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
