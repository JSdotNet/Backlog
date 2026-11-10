using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain view's page for a file it has no page of its own for — actors.md,
/// skills.md, a page a context adds such as import.md — reached as a reader
/// reaches it: the New domain view switch on and the file's row picked. The file
/// opens as written, in the view's page style; the placeholder is left for no
/// selection (domain/devbook requirement "Any other file opens as written").
/// </summary>
public sealed class DomainViewFilePageTests : IDisposable
{
    private readonly List<string> _roots = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // Shaped as Fincent's financieel/actors.md: a typed title block naming the
    // file's related pages, a quoted lead with an annotation under it, then one
    // typed chapter per actor.
    private const string Actors = """
        # Who works with tasks

        ```meta
        type: actors
        status: review
        related: [.devbook/domain/tasks/domain.md, .devbook/domain/tasks/skills.md]
        ```

        > Who works with **tasks**: the people who plan and the people who do.

        ```annotation
        kind: question
        body: Is a reviewer an actor of its own?
        ```

        ## Planner

        ```meta
        type: user
        related: [.devbook/domain/tasks/domain.md#task]
        ```

        The planner orders the work for the week.

        ## Doer

        ```meta
        type: user
        ```

        The doer ticks a task off once it is finished.
        """;

    private const string Skills = """
        # Skills

        ```meta
        type: skills
        ```

        What an agent working on tasks can be asked to do.

        ## Triage

        An agent sorts the inbox into tasks.
        """;

    // Shaped as Fincent's financieel/import.md: a page of a kind the convention
    // does not name, its lead quoted, its chapters untyped.
    private const string Import = """
        # Import

        ```meta
        type: import
        related: [.devbook/domain/tasks/domain.md#task]
        ```

        > Everything tasks records about importing an old to-do list.

        ## What import is

        A one-off move of a list into tasks, without its history.

        ## Starting points

        - Done items are not imported.
        """;

    [Fact]
    public async Task Actors_opens_as_written_with_its_title_lead_chapters_and_related_links()
    {
        await using var harness = CreateHarness();
        var component = await OpenAsync(harness, "Actors");

        component.WaitForAssertion(() => Assert.Equal("Who works with tasks", component.Find("[data-testid='domain-file-title']").TextContent.Trim()), Patience);
        Assert.Equal("file", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Empty(component.FindAll("[data-testid='domain-view-placeholder']"));

        Assert.Equal("actors · review", component.Find("[data-testid='domain-file-kicker']").TextContent.Trim());
        var lead = component.Find("[data-testid='domain-file-lead']");
        Assert.Contains("the people who plan and the people who do", lead.TextContent);
        Assert.DoesNotContain("reviewer", lead.TextContent);

        Assert.Equal(
            ["Planner", "Doer"],
            component.FindAll("[data-testid='domain-file-chapter-title']").Select(title => title.TextContent.Trim()));
        Assert.Contains("orders the work for the week", component.Find("[data-testid='domain-file-chapter'][data-chapter='planner']").TextContent);
        Assert.Contains("ticks a task off", component.Find("[data-testid='domain-file-chapter'][data-chapter='doer']").TextContent);

        Assert.Equal(2, component.FindAll("[data-testid='domain-file-related'] [data-testid='domain-file-related-link']").Count);
        Assert.Single(component.FindAll("[data-testid='domain-file-chapter'][data-chapter='planner'] [data-testid='domain-file-chapter-related']"));
        Assert.Empty(component.FindAll("[data-testid='domain-file-chapter'][data-chapter='doer'] [data-testid='domain-file-chapter-related']"));
    }

    [Theory]
    [InlineData("Skills", "Skills", "Triage", "sorts the inbox")]
    [InlineData("Import", "Import", "What import is", "without its history")]
    public async Task Skills_and_a_page_the_convention_does_not_name_open_as_written(string row, string title, string chapter, string body)
    {
        await using var harness = CreateHarness();
        var component = await OpenAsync(harness, row);

        component.WaitForAssertion(() => Assert.Equal(title, component.Find("[data-testid='domain-file-title']").TextContent.Trim()), Patience);
        Assert.Equal("file", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Empty(component.FindAll("[data-testid='domain-view-placeholder']"));
        Assert.Single(component.FindAll("[data-testid='domain-file-lead']"));
        Assert.Contains(chapter, component.FindAll("[data-testid='domain-file-chapter-title']").Select(heading => heading.TextContent.Trim()));
        Assert.Contains(body, component.Find("[data-testid='domain-file-chapter']").TextContent);
    }

    [Fact]
    public async Task Following_a_related_link_opens_its_page()
    {
        await using var harness = CreateHarness();
        var component = await OpenAsync(harness, "Actors");

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='domain-file-related'] [data-testid='domain-file-related-link']")), Patience);
        await component.InvokeAsync(() => component.FindAll("[data-testid='domain-file-related'] [data-testid='domain-file-related-link']")[0].Click());

        component.WaitForAssertion(() => Assert.Equal("tasks/domain.md", component.FindComponent<DomainView>().Instance.SelectedPath), Patience);
        Assert.Equal("domain", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
    }

    [Fact]
    public async Task No_selection_still_shows_the_placeholder()
    {
        await using var harness = CreateHarness();

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, (string?)null));

        Assert.Single(view.FindAll("[data-testid='domain-view-placeholder']"));
        Assert.Equal("none", view.Find("[data-testid='domain-view']").GetAttribute("data-page"));
    }

    [Fact]
    public async Task A_path_that_names_no_file_says_so()
    {
        await using var harness = CreateHarness();

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, "tasks/missing.md"));

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-view-file-problem']")), Patience);
        Assert.Equal("file", view.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        Assert.Empty(view.FindAll("[data-testid='domain-view-placeholder']"));
    }

    [Theory]
    [InlineData("../secret.md")]
    [InlineData("tasks/../../secret.md")]
    [InlineData("{root}/secret.md")]
    public async Task A_path_out_of_the_folder_says_so_and_reads_nothing(string selected)
    {
        await using var harness = CreateHarness();
        var secret = SecretPath(harness);

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, selected.Replace("{root}", Path.GetDirectoryName(secret)!.Replace('\\', '/'), StringComparison.Ordinal)));

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-view-file-problem']")), Patience);
        Assert.Empty(view.FindAll("[data-testid='domain-file-title']"));
        Assert.DoesNotContain(SecretText, view.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_store_reads_no_file_outside_the_domain_folder()
    {
        await using var harness = CreateHarness();
        var secret = SecretPath(harness);
        var domain = await harness.Context.Services.GetRequiredService<DomainDevbookStore>().LoadAsync(harness.RepositoryAlias, TestContext.Current.CancellationToken);

        Assert.NotNull(DomainDevbookStore.ReadText(domain, ".domain/tasks/actors.md"));
        Assert.Null(DomainDevbookStore.ReadText(domain, ".domain/../secret.md"));
        Assert.Null(DomainDevbookStore.ReadText(domain, ".domain/tasks/../../secret.md"));
        Assert.Null(DomainDevbookStore.ReadText(domain, ".domain/" + secret.Replace('\\', '/')));
        Assert.Null(DomainDevbookStore.ReadText(domain, ".domain/" + secret));
    }

    [Fact]
    public void Within_root_keeps_a_path_inside_and_refuses_one_that_leaves()
    {
        var root = Path.Combine(Path.GetTempPath(), "within-root", ".domain");
        var outside = Path.Combine(Path.GetTempPath(), "within-root", "secret.md");

        Assert.Equal(Path.Combine(root, "tasks", "actors.md"), DomainDevbookStore.WithinRoot(root, "tasks/actors.md"));
        Assert.Null(DomainDevbookStore.WithinRoot(root, "../secret.md"));
        Assert.Null(DomainDevbookStore.WithinRoot(root, outside));
        Assert.Null(DomainDevbookStore.WithinRoot(root, outside.Replace('\\', '/')));
        // A sibling folder that only shares the root's name as a prefix is outside too.
        Assert.Null(DomainDevbookStore.WithinRoot(root, "../.domain-other/x.md"));
    }

    private const string SecretText = "Nobody outside the folder reads this.";

    /// <summary>A file beside the Domain folder, outside it, that no Domain page
    /// may read.</summary>
    private static string SecretPath(Harness harness)
    {
        var secret = Path.Combine(harness.Root, "secret.md");
        File.WriteAllText(secret, $"# Secret\n\n{SecretText}\n");
        return secret;
    }

    /// <summary>Renders the pane, opens the Tasks context and picks one of its rows.</summary>
    private static async Task<IRenderedComponent<DevbookPane>> OpenAsync(Harness harness, string row)
    {
        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view']")), Patience);
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Tasks")), Patience);
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, row)), Patience);
        await ClickMenuItemAsync(component, row);
        return component;
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
        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-view-file-page", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), "# Domain: Tasks\n\n```meta\nstatus: draft\n```\n\nWhat the tasks context is.\n\n## Task\n\n```meta\ntype: aggregate\n```\n\nA piece of work.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "actors.md"), Actors);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "skills.md"), Skills);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "import.md"), Import);
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

        return new Harness(context, repository.Alias, root);
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias, string Root) : IAsyncDisposable
    {
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
