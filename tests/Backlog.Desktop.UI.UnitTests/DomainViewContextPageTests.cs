using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain view's context page, reached the way a reader reaches it: the New
/// domain view switch on, a context's row picked. The row opens one page with no
/// tabs (local ADR 0040): the neighbours, read from <c>dependencies.md</c> or the
/// <c>## Dependencies</c> chapter of a <c>context.md</c> that keeps its tables
/// inline, then the Overview — the boundary prose, the chapters per kind, the proof
/// coverage, how settled they are and the flags and settings.
/// </summary>
public sealed class DomainViewContextPageTests : IDisposable
{
    private readonly List<string> _roots = [];

    /// <summary>The page reads the folder off the renderer's thread, and under the
    /// whole solution's concurrent run one second was not always enough.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private const string Context = """
        # Tasks

        ```meta
        status: draft
        index: root
        type: context
        ```

        Tasks maintains a personal backlog of prompts and tasks.

        ## GitHub integration

        ```meta
        status: draft
        type: feature-flag
        key: github-integration
        default: on
        ```

        Turning it on offers GitHub access in settings. More words.
        """;

    private const string Dependencies = """
        # Tasks

        ```meta
        type: dependencies
        status: draft
        ```

        ## Outbound dependencies

        | Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
        |---|---|---|---|---|
        | GitHub (external) | ACL | REST | `x` | Tasks project to GitHub issues. Status syncs. |
        | [Devbook](../devbook/domain.md#knowledge-note) | Partnership | Link | `x` | Entries embed notes. |

        ## Inbound dependents (known)

        | Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
        |---|---|---|---|---|
        | [Sync](../sync/context.md) | OHS + Published Language | Feed | `x` | Relies on a task being one document. |
        """;

    private const string Requirements = """
        # Requirements: Tasks

        ```meta
        type: requirements
        status: draft
        ```

        ## Task creation

        ```meta
        type: requirements
        status: draft
        ```

        ### Requirement: A task has a title

        ```meta
        type: requirement
        status: active
        tests: unit:dotnet:Backlog.Tests.TaskTests
        ```

        The system SHALL refuse a task without a title.

        ### Requirement: A task has a status

        ```meta
        type: requirement
        status: draft
        ```

        The system SHALL give every task a status.
        """;

    private const string SyncContext = """
        # Sync

        ```meta
        status: draft
        type: context
        ```

        Sync carries documents between devices.

        ## Dependencies

        ### Inbound dependents (known)

        | Consumer | DDD pattern | What it relies on |
        |---|---|---|
        | [Tasks](../tasks/domain.md#task) | OHS | A task document is stored whole. |
        """;

    [Fact]
    public async Task A_context_row_opens_on_its_neighbours_with_the_boundary_counts_proof_and_flags_below()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view']")), Patience);
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Tasks")), Patience);
        await ClickMenuItemAsync(component, "Tasks");

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-context-page']")), Patience);
        Assert.Equal("context", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Empty(component.FindAll("[data-testid='domain-context-page'] [role='tab']"));
        Assert.Single(component.FindAll("[data-testid='domain-context-page'] > [data-testid='domain-neighbours']"));
        Assert.Single(component.FindAll("[data-testid='domain-context-page'] > [data-testid='domain-context-page-overview']"));
        Assert.Equal("Tasks", component.Find("[data-testid='domain-context-page-title']").TextContent);
        Assert.Contains("personal backlog", component.Find("[data-testid='domain-context-page-lead']").TextContent);
        var requirements = component.Find("[data-testid='domain-context-page-proof-requirements']");
        Assert.Contains("2 requirements", requirements.TextContent);
        Assert.Contains("1 of 2 name a test", requirements.TextContent);
        Assert.Single(component.FindAll("[data-testid='domain-context-page-lens']"), lens => lens.TextContent.Contains("Requirements", StringComparison.Ordinal));
        var flag = component.Find("[data-testid='domain-context-page-switch']");
        Assert.Equal("github-integration", flag.GetAttribute("data-key"));
        Assert.Contains("Turning it on offers GitHub access in settings.", flag.TextContent);
        Assert.DoesNotContain("More words", flag.TextContent);
        Assert.Empty(component.FindAll("[data-testid='domain-view-placeholder']"));
    }

    [Fact]
    public async Task Neighbours_reads_dependencies_md_into_listens_to_listen_to_and_hand_in_hand()
    {
        await using var harness = CreateHarness();

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, "tasks"));
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-context-page']")), Patience);

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-neighbours-legend']")), Patience);
        var gitHub = view.Find("[data-testid='domain-neighbours-listens-to'] [data-testid='domain-neighbour']");
        Assert.Equal("GitHub", gitHub.GetAttribute("data-name"));
        Assert.Equal("true", gitHub.GetAttribute("data-external"));
        Assert.Equal("Tasks project to GitHub issues", gitHub.QuerySelector("[data-testid='domain-neighbour-line']")!.TextContent.Trim());
        Assert.Equal("Sync", view.Find("[data-testid='domain-neighbours-listen-to'] [data-testid='domain-neighbour']").GetAttribute("data-name"));
        Assert.Equal("Devbook", view.Find("[data-testid='domain-neighbours-hand-in-hand'] [data-testid='domain-neighbour']").GetAttribute("data-name"));
        Assert.DoesNotContain("Partnership", view.Find("[data-testid='domain-neighbours']").TextContent);
        Assert.DoesNotContain("OHS", view.Find("[data-testid='domain-neighbours']").TextContent);
    }

    [Theory]
    [InlineData("core", "draft", "Core · draft")]
    [InlineData("supporting", "none", "Supporting")]
    [InlineData(null, "accepted", "accepted")]
    [InlineData(null, "none", null)]
    public void The_middle_cards_summary_is_the_subdomain_type_and_the_status(string? kind, string? status, string? summary)
    {
        Assert.Equal(summary, DomainViewContextPage.Summary(kind, status));
    }

    [Fact]
    public async Task A_dependencies_path_opens_the_same_one_page()
    {
        await using var harness = CreateHarness();

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, "tasks/dependencies.md"));

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-neighbours-legend']")), Patience);
        Assert.Empty(view.FindAll("[role='tab']"));
        Assert.Equal("Tasks", view.Find("[data-testid='domain-context-page-title']").TextContent);
    }

    [Fact]
    public async Task A_context_without_dependencies_md_reads_its_own_Dependencies_chapter()
    {
        await using var harness = CreateHarness();

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, "sync/context.md"));
        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-context-page']")), Patience);

        view.WaitForAssertion(() => Assert.Equal(
            "Tasks",
            view.Find("[data-testid='domain-neighbours-listen-to'] [data-testid='domain-neighbour']").GetAttribute("data-name")), Patience);
        Assert.Single(view.FindAll("[data-testid='domain-neighbours-listens-to'] [data-testid='domain-neighbours-none']"));
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
        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-view-context-page", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "context.md"), Context);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "dependencies.md"), Dependencies);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "requirements.md"), Requirements);
        Directory.CreateDirectory(Path.Combine(root, ".domain", "sync"));
        File.WriteAllText(Path.Combine(root, ".domain", "sync", "context.md"), SyncContext);
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
