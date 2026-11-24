using AngleSharp.Dom;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A reference that names a heading — a search hit, an atlas link, a Brain chip, a
/// task's Devbook reference — followed into the Domain view (local ADR 0047). The
/// view draws a file's units, features, groups and tabs one at a time, so landing
/// on the file is not enough: the page has to open on the part the anchor names.
/// Each test hands the pane the request from outside, the way the shell does, and
/// asks the page what it opened rather than whether the request reached the view.
/// </summary>
public sealed class DevbookPaneDomainAnchorTests : IDisposable
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

        ## Overview

        What a task is for.
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

        ### Column

        ```meta
        type: entity
        ```

        One lane of the board.
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
        ```

        A task is created in draft.

        ## Board

        ```meta
        type: invariants
        related: [.domain/tasks/domain.md#board]
        ```

        ### Invariant: A board keeps one backlog column

        ```meta
        type: invariant
        ```

        A board always has its backlog column.
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
        ```

        Write a task down.

        ## Steps

        ```meta
        type: feature
        status: draft
        depends-on: [.devbook/domain/tasks/features.md#capture]
        ```

        Break a task into steps.
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
        related: [.devbook/domain/tasks/features.md#capture]
        ```

        ### Requirement: A captured task keeps its words

        ```meta
        type: requirement
        ```

        The system SHALL keep a captured task's words as written.

        ## Steps

        ```meta
        type: requirements
        related: [.devbook/domain/tasks/features.md#steps]
        ```

        ### Requirement: A step belongs to one task

        ```meta
        type: requirement
        ```

        The system SHALL keep each step on the task it was added to.
        """;

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
            }
        ```

        ## Fields

        ### Identity

        - `id` — the task's own id.
        """;

    private const string Dependencies = """
        # Tasks

        ## Inbound dependents (known)

        | Consumer | Why |
        |---|---|
        | Inbox | Relies on Tasks creating a draft task. |
        """;

    private const string Notes = """
        # Notes

        ```meta
        type: notes
        ```

        Loose notes on the context.

        ## First thought

        The first one.

        ## Second thought

        The second one.
        """;

    [Fact]
    public async Task A_request_naming_a_unit_opens_the_Domain_page_on_that_unit()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        Open(component, ".devbook/domain/tasks/domain.md#board", 1);

        component.WaitForAssertion(() => Assert.Equal("domain", Page(component)), Patience);
        component.WaitForAssertion(() => Assert.Equal("board", component.Find("[data-testid='domain-unit']").GetAttribute("data-unit")), Patience);
    }

    [Fact]
    public async Task A_request_naming_a_heading_inside_a_unit_opens_the_unit_that_holds_it()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        Open(component, ".devbook/domain/tasks/domain.md#column", 1);

        component.WaitForAssertion(() => Assert.Equal("board", component.Find("[data-testid='domain-unit']").GetAttribute("data-unit")), Patience);
        Assert.Equal("true", component.Find("[data-testid='domain-unit-about-tab']").GetAttribute("aria-selected"));
    }

    /// <summary>The unit asked for again, after the reader picked another one by
    /// hand, is a new request: the page goes back to it.</summary>
    [Fact]
    public async Task The_same_unit_asked_for_again_opens_it_again()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        Open(component, ".devbook/domain/tasks/domain.md#board", 1);
        component.WaitForAssertion(() => Assert.Equal("board", component.Find("[data-testid='domain-unit']").GetAttribute("data-unit")), Patience);

        await component.InvokeAsync(() => component.Find("[data-testid='domain-unit-tab-task']").Click());
        component.WaitForAssertion(() => Assert.Equal("task", component.Find("[data-testid='domain-unit']").GetAttribute("data-unit")), Patience);

        Open(component, ".devbook/domain/tasks/domain.md#board", 2);
        component.WaitForAssertion(() => Assert.Equal("board", component.Find("[data-testid='domain-unit']").GetAttribute("data-unit")), Patience);
    }

    /// <summary>An invariants file draws no row of its own: a reference into it
    /// opens the unit whose Always true lists the rule, on that tab and that rule —
    /// not the file's first unit, and not the tab's top.</summary>
    [Fact]
    public async Task A_request_naming_a_rule_opens_its_unit_on_Always_true_at_that_rule()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        Open(component, ".devbook/domain/tasks/domain.invariants.md#invariant-a-board-keeps-one-backlog-column", 1);

        component.WaitForAssertion(() => Assert.Equal("domain", Page(component)), Patience);
        component.WaitForAssertion(() =>
        {
            Assert.Equal("board", component.Find("[data-testid='domain-unit']").GetAttribute("data-unit"));
            Assert.Equal("true", component.Find("[data-testid='domain-unit-always-true-tab']").GetAttribute("aria-selected"));
            Assert.EndsWith(
                "domain.invariants.md#invariant-a-board-keeps-one-backlog-column",
                component.Find("[data-testid='domain-rule'][data-focused='true']").GetAttribute("data-rule"),
                StringComparison.Ordinal);
        }, Patience);
    }

    [Fact]
    public async Task A_request_naming_a_feature_opens_Features_on_that_feature()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        Open(component, ".devbook/domain/tasks/features.md#steps", 1);

        component.WaitForAssertion(() => Assert.Equal("features", Page(component)), Patience);
        component.WaitForAssertion(() => Assert.Equal("steps", component.Find("[data-testid='domain-feature-detail']").GetAttribute("data-key")), Patience);
    }

    [Fact]
    public async Task A_request_naming_a_requirement_group_opens_Requirements_on_that_group()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        Open(component, ".devbook/domain/tasks/requirements.md#steps", 1);

        component.WaitForAssertion(() => Assert.Equal("requirements", Page(component)), Patience);
        component.WaitForAssertion(() => Assert.Equal("steps", component.Find("[data-testid='domain-requirements-group']").GetAttribute("data-key")), Patience);
    }

    [Fact]
    public async Task A_request_naming_a_Model_tab_opens_Model_on_that_tab()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        Open(component, ".devbook/domain/tasks/model.md#fields", 1);

        component.WaitForAssertion(() => Assert.Equal("model", Page(component)), Patience);
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("[data-testid='domain-model-tab-fields']").GetAttribute("aria-selected")), Patience);
    }

    /// <summary>A file opened as written draws its chapters one under another, so
    /// the heading is brought into view the way an arc42 chapter's is: the pane
    /// asks the script once, and the page draws a heading the script can find by
    /// its text.</summary>
    [Fact]
    public async Task A_request_naming_a_heading_of_a_file_opened_as_written_brings_that_heading_into_view()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        Open(component, ".devbook/domain/tasks/notes.md#second-thought", 1);

        component.WaitForAssertion(() => Assert.Equal("file", Page(component)), Patience);
        component.WaitForAssertion(() => Assert.Equal(["second-thought"], RevealedHeadings(harness)), Patience);
        component.WaitForAssertion(() =>
        {
            var heading = component.FindAll("[data-testid='domain-file-chapter-title']")
                .Single(title => title.TextContent.Trim() == "Second thought");
            Assert.Equal("H3", heading.TagName);
        }, Patience);
    }

    /// <summary><c>dependencies.md</c> has no row: it is the neighbours atop its
    /// context's page. A reference into it opens that page and keeps its heading,
    /// rather than dropping it on the way.</summary>
    [Fact]
    public async Task A_request_into_a_file_shown_on_another_page_keeps_its_heading()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        Open(component, ".devbook/domain/tasks/dependencies.md#inbound-dependents-known", 1);

        component.WaitForAssertion(() => Assert.Equal("context", Page(component)), Patience);
        component.WaitForAssertion(() => Assert.Equal(["inbound-dependents-known"], RevealedHeadings(harness)), Patience);
    }

    [Fact]
    public async Task A_request_naming_a_heading_of_the_context_page_brings_it_into_view()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();

        Open(component, ".devbook/domain/tasks/context.md#overview", 1);

        component.WaitForAssertion(() => Assert.Equal("context", Page(component)), Patience);
        component.WaitForAssertion(() => Assert.Equal(["overview"], RevealedHeadings(harness)), Patience);
    }

    [Fact]
    public async Task A_request_naming_a_heading_of_the_context_map_brings_it_into_view()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();
        component.WaitForAssertion(() => Assert.Equal("context-map", Page(component)), Patience);

        Open(component, ".devbook/domain/context-map.md#tasks", 1);

        component.WaitForAssertion(() => Assert.Equal(["tasks"], RevealedHeadings(harness)), Patience);
        Assert.Equal("context-map", Page(component));
    }

    private static void Open(IRenderedComponent<DevbookPane> component, string reference, long sequence) =>
        component.Render(parameters => parameters.Add(pane => pane.OpenReference, new DevbookOpenRequest(reference, sequence)));

    /// <summary>The page the Domain view is drawing, by its key; null while none is.</summary>
    private static string? Page(IRenderedComponent<DevbookPane> component) =>
        component.FindAll("[data-testid='domain-view']").SingleOrDefault()?.GetAttribute("data-page");

    private static List<string?> RevealedHeadings(Harness harness) =>
        [.. harness.Context.JSInterop.Invocations
            .Where(invocation => invocation.Identifier == DevbookPane.RevealHeadingFunction)
            .Select(invocation => invocation.Arguments[1] as string)];

    private Harness CreateHarness()
    {
        var temp = new TemporaryDirectory("backlog-devbook-pane-domain-anchor");
        _temps.Add(temp);
        var root = temp.Path;
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n\n## Tasks\n\nThe work a person does.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "context.md"), Context);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), Domain);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.invariants.md"), Invariants);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "features.md"), Features);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "requirements.md"), Requirements);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "model.md"), Model);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "dependencies.md"), Dependencies);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "notes.md"), Notes);
        File.WriteAllText(Path.Combine(root, ".arc42", "03-context-and-scope.md"), "# Context and scope\n\nThe system in its surroundings.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

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
        foreach (var temp in _temps) temp.Dispose();
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
