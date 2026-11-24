using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain view's two readings of the selected row (local ADR 0047): Unit view,
/// the pages it draws by unit, and As written, the file itself in the shared file
/// view — where a chapter is edited, compared, remarked on and given a sync
/// direction. A page built from more than one file opens its primary file, with
/// a chip to its companion. Rendered in the pane, the way a reader reaches them.
/// </summary>
public sealed class DomainViewModesTests : IDisposable
{
    private readonly List<TemporaryDirectory> _temps = [];

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private const string Domain = """
        # Domain: Tasks

        ```meta
        type: domain
        status: draft
        ```

        What the tasks context is.

        ## Board

        ```meta
        type: aggregate
        ```

        Where tasks are shown.
        """;

    private const string DomainInvariants = """
        # Invariants

        ```meta
        type: invariants
        ```

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

    private const string SplitDomain = """
        # Task

        ```meta
        type: domain
        ```

        ## Task

        ```meta
        type: aggregate
        ```

        A refined, actionable item.
        """;

    private const string SplitInvariants = """
        # Task invariants

        ```meta
        type: invariants
        ```

        ## Task

        ```meta
        type: invariants
        related: [.domain/tasks/domain.task.md#task]
        ```

        ### Invariant: A new task starts in draft

        ```meta
        type: invariant
        ```

        A task is created in draft.
        """;

    private const string Features = """
        # Features

        ```meta
        type: features
        ```

        ## Capture

        ```meta
        type: feature
        ```

        Write a task down.
        """;

    [Fact]
    public async Task As_written_shows_the_selected_file_in_the_file_view_and_Unit_view_brings_the_page_back()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();
        Open(component, ".devbook/domain/tasks/domain.md", 1);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-unit']")), Patience);
        Assert.Equal("true", component.Find("[data-testid='domain-view-mode-units']").GetAttribute("aria-pressed"));

        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-mode-written']").Click());

        component.WaitForAssertion(() =>
        {
            Assert.Equal(".domain/tasks/domain.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent.Trim());
            Assert.Contains("What the tasks context is.", component.Find("[data-testid='domain-chapter-file'] .file-view__body").TextContent, StringComparison.Ordinal);
        }, Patience);
        Assert.Empty(component.FindAll("[data-testid='domain-unit']"));
        Assert.Equal("true", component.Find("[data-testid='domain-view-mode-written']").GetAttribute("aria-pressed"));

        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-mode-units']").Click());

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-unit']")), Patience);
        Assert.Empty(component.FindAll("[data-testid='domain-chapter-file']"));
    }

    /// <summary>The reading is the reader's choice, not the row's: picking another
    /// row keeps it.</summary>
    [Fact]
    public async Task As_written_stays_while_the_reader_moves_to_another_row()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();
        Open(component, ".devbook/domain/tasks/domain.md", 1);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view-mode-written']")), Patience);
        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-mode-written']").Click());

        Open(component, ".devbook/domain/tasks/features.md", 2);

        component.WaitForAssertion(() => Assert.Equal(".domain/tasks/features.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent.Trim()), Patience);
    }

    /// <summary>A split aggregate's page reads two files: As written opens the
    /// unit's own file, and a chip opens its Always true file.</summary>
    [Fact]
    public async Task A_page_of_two_files_opens_its_primary_file_with_a_chip_to_its_companion()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();
        Open(component, ".devbook/domain/tasks/domain.task.md", 1);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view-mode-written']")), Patience);

        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-mode-written']").Click());

        component.WaitForAssertion(() => Assert.Equal(".domain/tasks/domain.task.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent.Trim()), Patience);
        component.WaitForAssertion(() => Assert.Equal(
            ["domain.task.md", "domain.task.invariants.md"],
            component.FindAll("[data-testid='domain-view-written-file']").Select(chip => chip.TextContent.Trim())), Patience);

        await component.InvokeAsync(() => component.FindAll("[data-testid='domain-view-written-file']")[1].Click());

        component.WaitForAssertion(() =>
        {
            Assert.Equal(".domain/tasks/domain.task.invariants.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent.Trim());
            Assert.Contains("A task is created in draft.", component.Find("[data-testid='domain-chapter-file'] .file-view__body").TextContent, StringComparison.Ordinal);
        }, Patience);
        Assert.Equal("true", component.FindAll("[data-testid='domain-view-written-file']")[1].GetAttribute("aria-pressed"));
    }

    /// <summary>A file read alone has no companion, and no chip row.</summary>
    [Fact]
    public async Task A_page_of_one_file_offers_no_companion()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();
        Open(component, ".devbook/domain/tasks/features.md", 1);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view-mode-written']")), Patience);

        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-mode-written']").Click());

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-chapter-file']")), Patience);
        Assert.Empty(component.FindAll("[data-testid='domain-view-written-file']"));
    }

    /// <summary>A remark is kept by the file and the block it was left on, so the
    /// ones left on a Domain chapter before the unit view are back beside their
    /// block in As written.</summary>
    [Fact]
    public async Task A_remark_left_on_a_Domain_chapter_reads_again_beside_its_block()
    {
        await using var harness = CreateHarness();
        var remarks = new SessionDevbookAnnotationStore();
        var kept = remarks.Add(harness.RepositoryAlias, ".domain/tasks/domain.md", 0, "someone");
        remarks.Edit(kept.Id, "Left before the unit view.");
        harness.Context.Services.AddSingleton<IDevbookAnnotationStore>(remarks);

        var component = harness.Render();
        Open(component, ".devbook/domain/tasks/domain.md", 1);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view-mode-written']")), Patience);
        Assert.DoesNotContain("Left before the unit view.", component.Markup, StringComparison.Ordinal);

        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-mode-written']").Click());

        // Beside the block it was left on, the file's first, and no other.
        component.WaitForAssertion(() => Assert.Contains(
            "Left before the unit view.",
            component.Find("[data-testid='domain-chapter-file'] .md-block-row[data-block='0']").TextContent,
            StringComparison.Ordinal), Patience);
        Assert.Single(
            component.FindAll("[data-testid='domain-chapter-file'] .md-block-row"),
            row => row.TextContent.Contains("Left before the unit view.", StringComparison.Ordinal));
    }

    /// <summary>A reference into a page's companion file — a rule in the Always
    /// true file of a split aggregate — read as written opens that file, and the
    /// pane looks for the heading in it.</summary>
    [Fact]
    public async Task As_written_opens_the_companion_file_a_reference_names_and_reveals_its_heading()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();
        Open(component, ".devbook/domain/tasks/domain.task.md", 1);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view-mode-written']")), Patience);
        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-mode-written']").Click());
        component.WaitForAssertion(() => Assert.Equal(".domain/tasks/domain.task.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent.Trim()), Patience);

        Open(component, ".devbook/domain/tasks/domain.task.invariants.md#invariant-a-new-task-starts-in-draft", 2);

        component.WaitForAssertion(() => Assert.Equal(".domain/tasks/domain.task.invariants.md", component.Find("[data-testid='domain-chapter-file'] .file-view__path").TextContent.Trim()), Patience);
        var asked = harness.Context.JSInterop.Invocations
            .Where(invocation => invocation.Identifier == DevbookPane.RevealHeadingFunction)
            .Select(invocation => (Anchor: invocation.Arguments[1] as string, Path: invocation.Arguments[2] as string))
            .ToList();
        Assert.Contains(("invariant-a-new-task-starts-in-draft", "tasks/domain.task.invariants.md"), asked);
    }

    /// <summary>The mirror of the companion case: on the companion's chip, a
    /// reference into the primary file — a search hit for one of its headings —
    /// brings the primary file back and reveals the heading in it.</summary>
    [Fact]
    public async Task A_reference_into_the_primary_file_brings_it_back_from_its_companion()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();
        Open(component, ".devbook/domain/tasks/domain.md", 1);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view-mode-written']")), Patience);
        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-mode-written']").Click());
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='domain-view-written-file']").Count), Patience);
        await component.InvokeAsync(() => component.FindAll("[data-testid='domain-view-written-file']")[1].Click());
        component.WaitForAssertion(() => Assert.Equal(".domain/tasks/domain.invariants.md", ShownPath(component)), Patience);

        Open(component, ".devbook/domain/tasks/domain.md#board", 2);

        component.WaitForAssertion(() => Assert.Equal(".domain/tasks/domain.md", ShownPath(component)), Patience);
        component.WaitForAssertion(() => Assert.Contains(("board", "tasks/domain.md"), Revealed(harness)), Patience);
    }

    /// <summary>A request is followed once. The reading drawn again — Unit view and
    /// back — does not follow the request standing in the pane a second time, so
    /// the file the reader went back to stays the one shown.</summary>
    [Fact]
    public async Task Reading_as_written_again_does_not_follow_the_standing_request_again()
    {
        await using var harness = CreateHarness();
        var component = harness.Render();
        Open(component, ".devbook/domain/tasks/domain.task.md", 1);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-view-mode-written']")), Patience);
        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-mode-written']").Click());
        Open(component, ".devbook/domain/tasks/domain.task.invariants.md#invariant-a-new-task-starts-in-draft", 2);
        component.WaitForAssertion(() => Assert.Equal(".domain/tasks/domain.task.invariants.md", ShownPath(component)), Patience);

        await component.InvokeAsync(() => component.FindAll("[data-testid='domain-view-written-file']")[0].Click());
        component.WaitForAssertion(() => Assert.Equal(".domain/tasks/domain.task.md", ShownPath(component)), Patience);

        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-mode-units']").Click());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='domain-unit']")), Patience);
        await component.InvokeAsync(() => component.Find("[data-testid='domain-view-mode-written']").Click());

        component.WaitForAssertion(() => Assert.Equal(".domain/tasks/domain.task.md", ShownPath(component)), Patience);
        Assert.Equal("true", component.FindAll("[data-testid='domain-view-written-file']")[0].GetAttribute("aria-pressed"));
    }

    private static string? ShownPath(IRenderedComponent<DevbookPane> component) =>
        component.FindAll("[data-testid='domain-chapter-file'] .file-view__path").SingleOrDefault()?.TextContent.Trim();

    private static List<(string? Anchor, string? Path)> Revealed(Harness harness) =>
        [.. harness.Context.JSInterop.Invocations
            .Where(invocation => invocation.Identifier == DevbookPane.RevealHeadingFunction)
            .Select(invocation => (invocation.Arguments[1] as string, invocation.Arguments[2] as string))];

    private static void Open(IRenderedComponent<DevbookPane> component, string reference, long sequence) =>
        component.Render(parameters => parameters.Add(pane => pane.OpenReference, new DevbookOpenRequest(reference, sequence)));

    private Harness CreateHarness()
    {
        var temp = new TemporaryDirectory("backlog-domain-view-modes");
        _temps.Add(temp);
        var root = temp.Path;
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts and what joins them.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "context.md"), "# Tasks\n\n```meta\ntype: context\n```\n\nThe tasks context.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), Domain);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.invariants.md"), DomainInvariants);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.task.md"), SplitDomain);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.task.invariants.md"), SplitInvariants);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "features.md"), Features);
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
