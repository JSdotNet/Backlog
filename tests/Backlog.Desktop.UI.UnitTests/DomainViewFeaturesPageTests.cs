using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain view's Features and Requirements pages, reached the way a reader
/// reaches them: the New domain view switch on, a context opened, its Features or
/// Requirements row picked (local ADR 0037). Features stand in columns by what
/// they build on with their proved requirements; Requirements reads a feature's
/// requirements and scenarios, and its Always true link opens the aggregate's
/// Always true tab on the Domain page.
/// </summary>
public sealed class DomainViewFeaturesPageTests : IDisposable
{
    private readonly List<string> _roots = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private const string Context = """
        # Tasks

        ```meta
        type: context
        status: draft
        ```

        The tasks context.

        ## Recurrence

        ```meta
        type: feature-flag
        ```

        Turns repeating tasks on.
        """;

    private const string Invariants = """
        # Invariants

        ```meta
        type: invariants
        status: draft
        ```

        ## Task

        ```meta
        type: invariants
        related: [.domain/tasks/domain.md#task]
        ```

        ### Invariant: A new task starts in draft

        ```meta
        type: invariant
        tests: [unit:dotnet:Tasks.TaskTests.A_new_task_is_draft]
        ```

        A task is created in draft.

        Enforced at: constructor
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

        ## Board

        ```meta
        type: aggregate
        status: draft
        ```

        Where tasks are shown.
        """;

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
        related: [.devbook/domain/tasks/domain.md#task]
        ```

        Write a task down.

        ## Steps

        ```meta
        type: feature
        status: proposed
        depends-on: [.devbook/domain/tasks/features.md#capture]
        ```

        Break a task into steps.
        """;

    private const string SplitFeatures = """
        # Recurring tasks

        ```meta
        type: features
        status: draft
        ```

        ## Recurring tasks

        ```meta
        type: feature
        status: draft
        feature-flag: .devbook/domain/tasks/context.md#recurrence
        depends-on: [.devbook/domain/tasks/features.md#steps]
        ```

        Repeat a task on a schedule.
        """;

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
        related: [.devbook/domain/tasks/features.md#capture]
        ```

        > Writing a task down. The rules a task keeps are its
        > [invariants](domain.invariants.md#task).

        ### Requirement: A captured task keeps its words

        ```meta
        type: requirement
        status: draft
        tests: [unit:dotnet:Tasks.Tests.Capture_keeps_the_words]
        ```

        The system SHALL keep a captured task's words as written.

        #### Scenario: Capturing

        - **Given** the capture box is open
        - **When** the person writes *Buy milk* and presses Enter
        - **Then** the task reads *Buy milk*, and the box is empty

        ## Steps

        ```meta
        type: requirements
        status: draft
        related: [.devbook/domain/tasks/features.md#steps]
        ```

        ### Requirement: A step belongs to one task

        ```meta
        type: requirement
        status: draft
        ```

        The system SHALL keep each step on the task it was added to.
        """;

    [Fact]
    public async Task The_Features_row_opens_the_board_in_columns_with_each_cards_proof()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view']")), Patience);
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Features")), Patience);
        await ClickMenuItemAsync(component, "Features");

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='domain-features-column']").Count), Patience);
        Assert.Equal("features", component.Find("[data-testid='domain-view']").GetAttribute("data-page"));
        var capture = component.Find("[data-testid='domain-feature-card'][data-key='capture']");
        Assert.Equal("1 req · 1 tested", capture.QuerySelector("[data-testid='domain-feature-requirements'] [aria-hidden='true']")!.TextContent.Trim());
        Assert.Equal("What Tasks lets a person do", component.Find("[data-testid='domain-features-title']").TextContent.Trim());
        Assert.Single(component.FindAll("[data-testid='domain-features-title']"));
        Assert.Contains("domain-feature-card--proposed", component.Find("[data-testid='domain-feature-card'][data-key='steps']").ClassList);
        Assert.Contains("Task", component.Find("[data-testid='domain-feature-model']").TextContent);
        Assert.Empty(component.FindAll("[data-testid='domain-view-placeholder']"));
    }

    [Fact]
    public async Task A_split_features_file_reads_its_one_feature_in_the_column_the_base_file_gives_it()
    {
        await using var harness = CreateHarness();

        var view = harness.Context.Render<DomainView>(parameters => parameters
            .Add(v => v.RepositoryAlias, harness.RepositoryAlias)
            .Add(v => v.SelectedPath, "tasks/features.recurring.md"));

        view.WaitForAssertion(() => Assert.Single(view.FindAll("[data-testid='domain-feature-card']")), Patience);
        Assert.Equal("2", view.Find("[data-testid='domain-features-column']").GetAttribute("data-depth"));
        var gate = view.Find("[data-testid='domain-feature-gate']");
        Assert.Equal("flag", gate.GetAttribute("data-kind"));
        Assert.Equal("Recurrence", gate.QuerySelector(".domain-feature-card__gate-name")!.TextContent.Trim());
    }

    [Fact]
    public async Task Requirements_read_as_rows_and_a_rule_shown_in_place_opens_its_row_on_Always_true()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view']")), Patience);
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Requirements")), Patience);
        await ClickMenuItemAsync(component, "Requirements");

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-requirement']")), Patience);
        Assert.Equal("true", component.Find("[data-testid='domain-requirement']").GetAttribute("data-proved"));
        Assert.Equal(
            ["the task reads Buy milk", "the box is empty"],
            component.FindAll("[data-keyword='Then'] li[data-testid='domain-scenario-clause']").Select(clause => clause.TextContent.Trim()));
        Assert.DoesNotContain("invariants", component.Find("[data-testid='domain-requirements-intro']").TextContent, StringComparison.OrdinalIgnoreCase);

        // The rule the requirements lean on shows in place, and opens Always true on its row.
        Assert.Equal("A new task starts in draft.", component.Find("[data-testid='domain-requirements-always-true'] [data-testid='domain-rule-list-claim']").TextContent.Trim());
        await component.InvokeAsync(() => component.Find("[data-testid='domain-rule-list-toggle']").Click());
        await component.InvokeAsync(() => component.Find("[data-testid='domain-rule-list-open']").Click());

        component.WaitForAssertion(() => Assert.Equal("domain", component.Find("[data-testid='domain-view']").GetAttribute("data-page")), Patience);
        component.WaitForAssertion(() => Assert.Equal("task", component.Find("[data-testid='domain-unit']").GetAttribute("data-unit")), Patience);
        Assert.Equal("true", component.Find("[data-testid='domain-unit-always-true-tab']").GetAttribute("aria-selected"));
        component.WaitForAssertion(() => Assert.EndsWith("domain.invariants.md#invariant-a-new-task-starts-in-draft", component.Find("[data-testid='domain-rule'][data-focused='true']").GetAttribute("data-rule"), StringComparison.Ordinal), Patience);
    }

    [Fact]
    public async Task A_features_requirements_link_opens_Requirements_on_that_features_group()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view']")), Patience);
        await ClickMenuItemAsync(component, "Tasks");
        component.WaitForAssertion(() => Assert.NotNull(MenuItem(component, "Features")), Patience);
        await ClickMenuItemAsync(component, "Features");
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='domain-feature-card'][data-key='steps']")), Patience);

        await component.InvokeAsync(() => component.Find("[data-testid='domain-feature-card'][data-key='steps']").Click());
        Assert.Equal("None linked to a test yet", component.Find("[data-testid='domain-feature-promise-proof']").TextContent.Trim());
        await component.InvokeAsync(() => component.Find("[data-testid='domain-feature-promises'] [data-testid='domain-feature-detail-requirements'] button").Click());

        component.WaitForAssertion(() => Assert.Equal("requirements", component.Find("[data-testid='domain-view']").GetAttribute("data-page")), Patience);
        component.WaitForAssertion(() => Assert.Equal("steps", component.Find("[data-testid='domain-requirements-group']").GetAttribute("data-key")), Patience);
        Assert.Equal("false", component.Find("[data-testid='domain-requirement']").GetAttribute("data-proved"));
    }

    /// <summary>One menu row, by the label a reader reads; null while it is not drawn.</summary>
    private static IElement? MenuItem(IRenderedComponent<DevbookPane> component, string label) =>
        component.FindAll(".devbook-menu__item")
            .SingleOrDefault(item => string.Equals(item.QuerySelector(".devbook-menu__label")?.TextContent.Trim(), label, StringComparison.Ordinal));

    /// <summary>Presses one menu row, found and clicked in one dispatch so a load
    /// landing in between cannot take the row's handler away — and, should the
    /// menu relist between the wait and the press, found again until it is back.</summary>
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
            await Task.Delay(25);
        }
    }

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-view-features-page", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "context.md"), Context);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), Domain);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.invariants.md"), Invariants);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "features.md"), Features);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "features.recurring.md"), SplitFeatures);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "requirements.md"), Requirements);
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
