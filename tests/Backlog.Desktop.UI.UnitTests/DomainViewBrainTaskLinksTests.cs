using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Domain view's Brain and task links (local ADR 0026 §3, ADR 0027 §6,
/// <c>.devbook/domain/devbook/features.md#domain-view-by-unit</c>): under each unit,
/// feature and term the From your Brain box for the notes linking to it, a
/// "◆ n note" badge on a model part and a glossary term, and on a feature and a
/// requirement the gold chips of the tasks naming it — dashed for a task this
/// backlog does not hold.
/// </summary>
public sealed class DomainViewBrainTaskLinksTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private const string Held = "11111111-1111-1111-1111-111111111111";
    private const string Elsewhere = "22222222-2222-2222-2222-222222222222";

    private readonly List<string> _roots = [];

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

        ### Sub Item

        ```meta
        type: entity
        aliases: [SubItem]
        ```

        One step of a task.

        ## Board

        ```meta
        type: aggregate
        status: draft
        ```

        Where tasks are shown.

        ## Ubiquitous Language

        ```meta
        type: ubiquitous-language
        ```

        The words of the tasks team.

        ### Inbox

        ```meta
        type: term
        ```

        Where a captured thought waits.

        ### Today

        ```meta
        type: term
        ```

        What is planned for the day.
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
        status: draft
        related: [.devbook/domain/tasks/features.md#capture]
        ```

        ### Requirement: A captured task keeps its words

        ```meta
        type: requirement
        status: draft
        ```

        The system SHALL keep a captured task's words as written.

        ### Requirement: A capture is never lost

        ```meta
        type: requirement
        status: draft
        ```

        The system SHALL keep every capture.
        """;

    private const string Model = """
        # Model: Tasks

        ```meta
        type: model
        status: draft
        ```

        ## Model diagram

        ```mermaid
        classDiagram
            class Task {
                <<aggregate root>>
                +Id id
            }
            class SubItem {
                <<entity>>
            }
            Task "1" *-- "0..*" SubItem : contains
        ```
        """;

    /// <summary>The owner's Brain: links to the Task unit, to an entity inside it,
    /// to a term, to a feature, and one to the whole domain file — written in the
    /// other devbook layout's spelling, which still names the same chapters.</summary>
    private static readonly IReadOnlyList<BrainChapterBacklink> BrainLinks =
    [
        new(".devbook/domain/tasks/domain.md", "task", new BrainBacklink("work/plan.md", "Plan", null)),
        new(".devbook/domain/tasks/domain.md", "sub-item", new BrainBacklink("work/steps.md", "Steps idea", "Ideas")),
        new(".devbook/domain/tasks/domain.md", "sub-item", new BrainBacklink("work/plan.md", "Plan", null)),
        new(".devbook/domain/tasks/domain.md", "inbox", new BrainBacklink("work/inbox.md", "Inbox zero", null)),
        new(".devbook/domain/tasks/domain.md", null, new BrainBacklink("work/whole.md", "Whole file", null)),
        new(".devbook/domain/tasks/features.md", "capture", new BrainBacklink("work/capture.md", "Capture notes", null))
    ];

    private static readonly IReadOnlyList<DevbookTaskReferences> TaskReferences =
    [
        new(Held, [".devbook/domain/tasks/features.md#capture", ".devbook/domain/tasks/requirements.md#requirement-a-captured-task-keeps-its-words"]),
        new(Elsewhere, [".domain/tasks/features.md#capture"]),
        new("33333333-3333-3333-3333-333333333333", [".devbook/domain/tasks/features.md"])
    ];

    [Fact]
    public async Task A_unit_lists_the_notes_linking_to_it_or_to_a_chapter_inside_it()
    {
        await using var harness = CreateHarness();

        var view = harness.Render("tasks/domain.md");

        view.WaitForAssertion(() => Assert.Equal("task", view.Find("[data-testid='domain-unit']").GetAttribute("data-unit")), Patience);
        var box = view.Find("[data-testid='domain-unit'] [data-testid='brain-backlinks']");
        Assert.Equal(
            ["work/plan.md", "work/steps.md"],
            box.QuerySelectorAll("[data-testid='brain-backlink']").Select(item => item.GetAttribute("data-note")));
        Assert.DoesNotContain("Whole file", box.TextContent, StringComparison.Ordinal);

        await view.InvokeAsync(() => view.Find("[data-testid='domain-unit-tab-board']").Click());

        view.WaitForAssertion(() => Assert.Equal("board", view.Find("[data-testid='domain-unit']").GetAttribute("data-unit")), Patience);
        Assert.Empty(view.FindAll("[data-testid='domain-unit'] [data-testid='brain-backlinks']"));
    }

    [Fact]
    public async Task Another_repository_shows_none_of_the_first_ones_notes()
    {
        await using var harness = CreateHarness();

        var view = harness.Render("tasks/domain.md");
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[data-testid='domain-unit'] [data-testid='brain-backlinks']")), Patience);

        view.Render(parameters => parameters.Add(v => v.RepositoryAlias, "elsewhere"));

        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-testid='brain-backlinks']")), Patience);
    }

    [Fact]
    public async Task A_glossary_term_carries_its_notes_badge_and_box_and_a_term_with_none_carries_neither()
    {
        await using var harness = CreateHarness();

        var view = harness.Render("tasks/domain.md");
        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-testid='domain-unit-tab-ubiquitous-language']")), Patience);
        await view.InvokeAsync(() => view.Find("[data-testid='domain-unit-tab-ubiquitous-language']").Click());

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-testid='domain-glossary-term'][data-term='Inbox']")), Patience);
        var inbox = view.Find("[data-testid='domain-glossary-term'][data-term='Inbox']");
        Assert.Equal("◆ 1 note", inbox.QuerySelector("[data-testid='domain-glossary-notes-button']")!.TextContent.Trim());
        Assert.Equal("work/inbox.md", inbox.QuerySelector("[data-testid='brain-backlink']")!.GetAttribute("data-note"));

        var today = view.Find("[data-testid='domain-glossary-term'][data-term='Today']");
        Assert.Null(today.QuerySelector("[data-testid='domain-glossary-notes']"));
        Assert.Null(today.QuerySelector("[data-testid='brain-backlinks']"));
    }

    [Fact]
    public async Task A_model_part_counts_the_notes_linking_to_its_chapter()
    {
        await using var harness = CreateHarness();

        var view = harness.Render("tasks/model.md");

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-testid='domain-model-element'][data-name='SubItem']")), Patience);
        Assert.Equal("◆ 2 notes", view.Find("[data-testid='domain-model-element'][data-name='SubItem'] [data-testid='domain-model-notes-button']").TextContent.Trim());
        Assert.Equal("◆ 1 note", view.Find("[data-testid='domain-model-element'][data-name='Task'] [data-testid='domain-model-notes-button']").TextContent.Trim());
    }

    [Fact]
    public async Task A_feature_shows_the_tasks_naming_it_gold_or_dashed_and_the_notes_linking_to_it()
    {
        await using var harness = CreateHarness();

        var view = harness.Render("tasks/features.md");

        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-testid='domain-feature-detail'][data-key='capture']")), Patience);
        var chips = view.FindAll("[data-testid='domain-feature-tasks'] [data-testid='domain-chapter-task']");
        Assert.Equal([Held, Elsewhere], chips.Select(chip => chip.GetAttribute("data-task")));
        Assert.Equal(["true", "false"], chips.Select(chip => chip.GetAttribute("data-resolved")));
        Assert.Contains("Write the capture box", chips[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("not in this backlog", chips[1].TextContent, StringComparison.Ordinal);
        Assert.Equal("work/capture.md", view.Find("[data-testid='domain-feature-detail'] [data-testid='brain-backlink']").GetAttribute("data-note"));

        await view.InvokeAsync(() => chips[0].QuerySelector("button")!.Click());
        Assert.Equal([Held], harness.Opened);

        await view.InvokeAsync(() => view.Find("[data-testid='domain-feature-card'][data-key='steps']").Click());
        view.WaitForAssertion(() => Assert.Empty(view.FindAll("[data-testid='domain-feature-tasks']")), Patience);
        Assert.Empty(view.FindAll("[data-testid='domain-feature-detail'] [data-testid='brain-backlinks']"));
    }

    [Fact]
    public async Task A_requirement_shows_the_tasks_naming_it_and_nothing_on_the_view_is_teal_but_the_Brains()
    {
        await using var harness = CreateHarness();

        var view = harness.Render("tasks/requirements.md");

        view.WaitForAssertion(() => Assert.Equal(2, view.FindAll("[data-testid='domain-requirement']").Count), Patience);
        var requirements = view.FindAll("[data-testid='domain-requirement']");
        Assert.Equal([Held], requirements[0].QuerySelectorAll("[data-testid='domain-chapter-task']").Select(chip => chip.GetAttribute("data-task")));
        Assert.Empty(requirements[1].QuerySelectorAll("[data-testid='domain-chapter-task']"));
        Assert.DoesNotContain("brain", view.Find("[data-testid='domain-requirements']").InnerHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Links_to_a_whole_file_name_no_chapter_and_a_note_is_listed_once_per_chapter()
    {
        var notes = DomainViewLinks.NotesByChapter(BrainLinks);

        Assert.Equal(["tasks/domain.md#task", "tasks/domain.md#sub-item", "tasks/domain.md#inbox", "tasks/features.md#capture"], notes.Keys);
        Assert.Equal(["work/steps.md", "work/plan.md"], notes["tasks/domain.md#sub-item"].Select(note => note.NotePath));

        var tasks = DomainViewLinks.TasksByChapter(TaskReferences, id => id == Held ? "Held" : null);
        Assert.Equal([Held, Elsewhere], tasks["tasks/features.md#capture"].Select(task => task.TaskId));
        Assert.DoesNotContain("tasks/features.md", tasks.Keys);
        Assert.Null(tasks["tasks/features.md#capture"][1].Title);
    }

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-view-brain-task-links", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "tasks"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "context.md"), "# Tasks\n\n```meta\ntype: context\nstatus: draft\n```\n\nThe tasks context.\n");
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "domain.md"), Domain);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "features.md"), Features);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "requirements.md"), Requirements);
        File.WriteAllText(Path.Combine(root, ".domain", "tasks", "model.md"), Model);

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            DevbookFolders = [.. DevbookFolderSetting.Defaults().Select(folder => folder with { Enabled = folder.Key is ".domain" })]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHub, settings));
        context.Services.AddSingleton(sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));
        context.Services.AddSingleton<IBrainBacklinks>(new FixedChapterLinks(repository.Alias, BrainLinks));

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

    private sealed class FixedChapterLinks(string alias, IReadOnlyList<BrainChapterBacklink> links) : IBrainBacklinks
    {
        public Task<IReadOnlyList<BrainBacklink>> LinksToAsync(string repositoryAlias, string chapterPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BrainBacklink>>([]);

        public Task<IReadOnlyList<BrainChapterBacklink>> ChapterLinksAsync(string repositoryAlias, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Equals(repositoryAlias, alias, StringComparison.OrdinalIgnoreCase) ? links : []);
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public List<string> Opened { get; } = [];

        public IRenderedComponent<DomainView> Render(string selectedPath) =>
            Context.Render<DomainView>(parameters => parameters
                .Add(v => v.RepositoryAlias, RepositoryAlias)
                .Add(v => v.SelectedPath, selectedPath)
                .Add(v => v.TaskReferencesFor, (Func<string, IReadOnlyList<DevbookTaskReferences>>)(alias => alias == RepositoryAlias ? TaskReferences : []))
                .Add(v => v.TaskTitleFor, (Func<string, string?>)(id => id == Held ? "Write the capture box" : null))
                .Add(v => v.OnOpenTask, (string id) => Opened.Add(id)));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
