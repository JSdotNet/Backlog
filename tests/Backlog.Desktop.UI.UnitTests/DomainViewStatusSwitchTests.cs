using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devbook status switch (<see cref="DevbookFeatures.Status"/>) on the Domain
/// view's unit pages. Off, a page shows no status the chapters state: not in a
/// unit's or a file's kicker, not as an entity's proposed mark or a Glossary
/// term's badge, not as the context's badge, its summary line or its How settled
/// bar, and not as a feature's Proposed mark or the legend entry explaining it.
/// The words around them stay. Flipped while
/// the page is open, the page redraws without being reopened.
/// </summary>
public sealed class DomainViewStatusSwitchTests : IDisposable
{
    private readonly List<TemporaryDirectory> _temps = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private const string Context = """
        # Tasks

        ```meta
        type: context
        status: draft
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
        status: proposed
        ```

        A refined, actionable item.

        ### Step

        ```meta
        type: entity
        status: proposed
        ```

        One step of a task.

        ## Ubiquitous Language

        ```meta
        type: ubiquitous-language
        ```

        The words the tasks team uses.

        ### Chore

        ```meta
        type: term
        status: proposed
        ```

        A task nobody wants.
        """;

    // One neighbour, so the context page draws its middle card and the summary line.
    private const string Dependencies = """
        # Tasks

        ## Inbound dependents (known)

        | Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
        |---|---|---|---|---|
        | [Inbox](../inbox/domain.md#inbox-item) | OHS + Published Language (Inbox = supplier) | Publishes `ItemTriaged` | `.domain/inbox/domain.md#itemtriaged` | Relies on Tasks creating a draft task from a triaged item. |
        """;

    private const string Features = """
        # Features

        ```meta
        type: features
        ```

        ## Capture

        ```meta
        type: feature
        status: proposed
        ```

        Write a task down.
        """;

    private const string Notes = """
        # Notes

        ```meta
        type: notes
        status: review
        ```

        Loose notes.
        """;

    [Fact]
    public async Task With_status_on_the_unit_pages_show_what_the_chapters_state()
    {
        await using var harness = CreateHarness(status: true);
        var component = harness.Render();

        Open(component, ".devbook/domain/tasks/domain.md", 1);
        component.WaitForAssertion(() => Assert.Equal("aggregate · proposed", component.Find("[data-testid='domain-unit-kicker']").TextContent.Trim()), Patience);
        Assert.Single(component.FindAll("[data-testid='domain-unit-member-proposed']"));
        await component.InvokeAsync(() => component.Find("[data-testid='domain-unit-tab-ubiquitous-language']").Click());
        component.WaitForAssertion(() => Assert.Equal("proposed", component.Find("[data-testid='domain-glossary-status']").TextContent.Trim()), Patience);

        Open(component, ".devbook/domain/tasks/context.md", 2);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-context-page-settled']")), Patience);
        Assert.NotEmpty(component.FindAll("[data-testid='domain-context-page-header'] .badge--status"));
        Assert.Equal("draft", component.Find("[data-testid='domain-neighbours-summary']").TextContent.Trim());

        Open(component, ".devbook/domain/tasks/features.md", 3);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='domain-feature-status']")), Patience);
        Assert.Contains("domain-feature-card--proposed", component.Find("[data-testid='domain-feature-card']").ClassList);
        Assert.Single(component.FindAll("[data-testid='domain-features-legend-proposed']"));

        Open(component, ".devbook/domain/tasks/notes.md", 4);
        component.WaitForAssertion(() => Assert.Equal("notes · review", component.Find("[data-testid='domain-file-kicker']").TextContent.Trim()), Patience);
    }

    [Fact]
    public async Task With_status_off_the_unit_pages_show_none()
    {
        await using var harness = CreateHarness(status: false);
        var component = harness.Render();

        Open(component, ".devbook/domain/tasks/domain.md", 1);
        component.WaitForAssertion(() => Assert.Equal("aggregate", component.Find("[data-testid='domain-unit-kicker']").TextContent.Trim()), Patience);
        Assert.Single(component.FindAll("[data-testid='domain-unit-member']"));
        Assert.Empty(component.FindAll("[data-testid='domain-unit-member-proposed']"));
        await component.InvokeAsync(() => component.Find("[data-testid='domain-unit-tab-ubiquitous-language']").Click());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-glossary-term'][data-term='Chore']")), Patience);
        Assert.Empty(component.FindAll("[data-testid='domain-glossary-status']"));

        Open(component, ".devbook/domain/tasks/context.md", 2);
        component.WaitForAssertion(() => Assert.Equal("Tasks", component.Find("[data-testid='domain-context-page-title']").TextContent.Trim()), Patience);
        Assert.Empty(component.FindAll("[data-testid='domain-context-page-settled']"));
        Assert.Empty(component.FindAll("[data-testid='domain-context-page-header'] .badge--status"));
        Assert.Single(component.FindAll("[data-testid='domain-neighbours-self']"));
        Assert.Empty(component.FindAll("[data-testid='domain-neighbours-summary']"));

        Open(component, ".devbook/domain/tasks/features.md", 3);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-feature-card']")), Patience);
        Assert.Empty(component.FindAll("[data-testid='domain-feature-status']"));
        Assert.DoesNotContain("domain-feature-card--proposed", component.Find("[data-testid='domain-feature-card']").ClassList);
        Assert.Empty(component.FindAll("[data-testid='domain-features-legend-proposed']"));
        Assert.Single(component.FindAll("[data-testid='domain-features-legend-flag']"));

        Open(component, ".devbook/domain/tasks/notes.md", 4);
        component.WaitForAssertion(() => Assert.Equal("notes", component.Find("[data-testid='domain-file-kicker']").TextContent.Trim()), Patience);
    }

    [Fact]
    public async Task Switching_status_on_shows_it_without_reopening_the_page()
    {
        await using var harness = CreateHarness(status: false);
        var component = harness.Render();
        Open(component, ".devbook/domain/tasks/domain.md", 1);
        component.WaitForAssertion(() => Assert.Equal("aggregate", component.Find("[data-testid='domain-unit-kicker']").TextContent.Trim()), Patience);

        await component.InvokeAsync(() => Assert.Null(harness.Features.SetEnabled(DevbookFeatures.Status, true)));

        component.WaitForAssertion(() => Assert.Equal("aggregate · proposed", component.Find("[data-testid='domain-unit-kicker']").TextContent.Trim()), Patience);
    }

    private static void Open(IRenderedComponent<DevbookPane> component, string reference, long sequence) =>
        component.Render(parameters => parameters.Add(pane => pane.OpenReference, new DevbookOpenRequest(reference, sequence)));

    private Harness CreateHarness(bool status)
    {
        var temp = new TemporaryDirectory("backlog-domain-view-status-switch");
        _temps.Add(temp);
        var root = temp.Path;
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "context.md"), Context);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), Domain);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "features.md"), Features);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "dependencies.md"), Dependencies);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "notes.md"), Notes);
        File.WriteAllText(Path.Combine(root, ".arc42", "03-context-and-scope.md"), "# Context and scope\n\nThe system in its surroundings.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        Assert.Null(features.SetEnabled(DevbookFeatures.Status, status));

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
        foreach (var temp in _temps) temp.Dispose();
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias, AppFeatureSettingsStore Features) : IAsyncDisposable
    {
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
