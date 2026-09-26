using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The AI adoption record is the second folder drawn by the document-folder
/// view, and these tests are about the two things that differ from Design
/// rather than about the view — <see cref="DesignDevbookViewTests"/> owns that.
/// <para>
/// The first is the shape of the folder: no README, an adoption map as the
/// root, stage files numbered so the flow orders itself, and a
/// <c>concepts.md</c> after them. The second is the vocabulary: a rating on the
/// <c>.tech</c> ladder that is required, so the record never offers "No status"
/// and a word from the editorial folders is a typo here. Everything else — the
/// file view for a selected chapter, the status written to the addressed fence,
/// the reference callback — is the same code path Design already proves.
/// </para>
/// </summary>
public sealed class AiDevbookViewTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task The_overview_reads_the_folder_in_flow_order_with_the_map_first()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(selectedPath: null);

        component.WaitForAssertion(() => Assert.Equal(3, component.FindAll(".folder-document").Count));

        // Alphabetically `adoption-map.md` sorts after every numbered stage and
        // `concepts.md` after that. The map is the root and opens the folder; the
        // rest is the numbering doing its job with no declaration behind it.
        Assert.Equal(
            ["AI adoption map", "Specify", "Concepts"],
            component.FindAll(".folder-devbook__nav-link").Select(link => link.QuerySelector("span")!.TextContent));

        Assert.Single(component.FindAll("[data-testid='ai-devbook-view']"));
        Assert.Empty(component.FindAll("[data-testid='design-devbook-view']"));
    }

    [Fact]
    public async Task A_chapter_rated_on_the_ladder_wears_the_ladder_s_badge()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(selectedPath: null);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".folder-section .devbook-record select")));

        // `trial` is Provisional on the shared scale, exactly as it is in `.tech`,
        // and the control offers the five words of the ladder and nothing else:
        // a required rating has no "No status" row to clear it with.
        var select = component.FindAll(".folder-section .devbook-record select")[0];
        Assert.Equal(
            ["candidate", "trial", "adopted", "hold", "retired"],
            select.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        Assert.Equal("trial", select.GetAttribute("value"));
    }

    [Fact]
    public async Task A_status_picked_on_an_ai_chapter_is_written_to_that_chapter_s_fence()
    {
        await using var harness = CreateHarness();
        var component = harness.Render(selectedPath: null);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".folder-section .devbook-record select")));

        component.FindAll(".folder-section .devbook-record select")[0].Change("adopted");

        // The map carries a file-level block and no chapters, so the first
        // chapter control on the page is "Devbook skills" in 01-specify.md, and it
        // is that fence that changes. The file's own block staying `adopted` is
        // half the assertion: the writer addressed
        // `.ai/01-specify.md#devbook-skills`, not the file.
        component.WaitForAssertion(
            () =>
            {
                var text = File.ReadAllText(Path.Combine(harness.AiFolder, "01-specify.md")).ReplaceLineEndings();
                Assert.Contains("## Devbook skills\n```meta\nstatus: adopted".ReplaceLineEndings(), text, StringComparison.Ordinal);
                Assert.Contains("# Specify\n```meta\nstatus: adopted\ntype: stage".ReplaceLineEndings(), text, StringComparison.Ordinal);
                Assert.DoesNotContain("status: trial", text, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_selected_stage_file_is_read_through_the_file_view_with_the_ai_vocabulary()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("01-specify.md");

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='ai-chapter-file']")));
        Assert.Single(component.FindAll(".folder-devbook--chapter"));

        // The chapter's fence is folded into its heading, and the control it
        // draws is the ladder's — the same select the overview offered.
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-chapter-file-body'] .devbook-record select")));
        var select = component.FindAll("[data-testid='ai-chapter-file-body'] .devbook-record select")[0];
        Assert.Contains("adopted", select.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        Assert.DoesNotContain(string.Empty, select.QuerySelectorAll("option").Select(option => option.GetAttribute("value") ?? string.Empty));
    }

    /// <summary>
    /// The one place the AI view asks for a word Design does not: a chapter that
    /// says <c>draft</c> is a typo in <c>.ai</c>, and the record has to show it as
    /// one — a pill rather than a control — instead of drawing it as if the
    /// folder had a <c>draft</c>.
    /// </summary>
    [Fact]
    public async Task An_editorial_status_is_a_typo_in_the_ai_folder()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(selectedPath: null);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".folder-section")));

        var sections = component.FindAll(".folder-section");
        var contextEngineering = Assert.Single(sections, section => section.TextContent.Contains("Context engineering", StringComparison.Ordinal));
        Assert.Empty(contextEngineering.QuerySelectorAll(".devbook-record select"));
        Assert.Contains("draft", contextEngineering.TextContent, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-ai-view-tests", Guid.NewGuid().ToString("n"));
        var repository = Path.Combine(root, "repo");
        var ai = Path.Combine(repository, ".ai");
        Directory.CreateDirectory(ai);
        _roots.Add(root);

        // Written out of flow order on purpose: the view has to order them, and
        // a fixture written in order would pass on a view that did not.
        File.WriteAllText(Path.Combine(ai, "concepts.md"), Concepts);
        File.WriteAllText(Path.Combine(ai, "adoption-map.md"), AdoptionMap);
        File.WriteAllText(Path.Combine(ai, "01-specify.md"), Specify);

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(gitHubSettings.SetRepositories(repositories));
        gitHubSettings.SetCloneDirectory("backlog", repository);

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHubSettings));
        context.Services.AddSingleton<AiDevbookProvider>();
        context.Services.AddSingleton<DevbookChapterWriter>();

        return new Harness(context, ai);
    }

    /// <summary>The root: a file-level block and no per-chapter ones, as
    /// <c>devbook-ai.md</c> has it — the same rule as <c>.devbook/domain/context-map.md</c>.</summary>
    private const string AdoptionMap = """
        # AI adoption map
        ```meta
        status: adopted
        type: adoption-map
        ```

        How this project develops with AI, stage by stage.

        | Stage | File |
        | --- | --- |
        | Specify | 01-specify.md |
        """;

    /// <summary>A stage file: one chapter per thing used at the stage, each
    /// rated on the ladder. No <c>stage</c> field — the file is the stage.</summary>
    private const string Specify = """
        # Specify
        ```meta
        status: adopted
        type: stage
        ```

        Turning an intent into an agreed chapter.

        ## Devbook skills
        ```meta
        status: trial
        type: skill
        depends-on: [".tech/tooling.md#claude-code"]
        ```

        - **Used for** — drafting devbook chapters from a request.
        - **Adopted by** — one maintainer, on feature branches.
        - **Evidence** — none yet.
        """;

    /// <summary>Concepts span the flow, so they carry <c>stage</c>. The status
    /// here is deliberately a word from the editorial folders: it is what the
    /// typo case looks like in this folder.</summary>
    private const string Concepts = """
        # Concepts
        ```meta
        status: adopted
        type: concepts
        ```

        The ideas the practices rest on.

        ## Context engineering
        ```meta
        status: draft
        type: concept
        stage: [specify]
        ```

        Load only the chapters the task needs.
        """;

    private sealed record Harness(BunitContext Context, string AiFolder) : IAsyncDisposable
    {
        public IRenderedComponent<AiDevbookView> Render(string? selectedPath) =>
            Context.Render<AiDevbookView>(parameters => parameters
                .Add(view => view.RepositoryAlias, "backlog")
                .Add(view => view.SelectedPath, selectedPath));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
