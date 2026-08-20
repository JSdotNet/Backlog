using Backlog.Infrastructure.GitHub;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Design knowledge is the one area whose reading view is worth more than its
/// editing view, and these tests are mostly about not trading the first for the
/// second.
/// <para>
/// With nothing selected the panel is a folder overview: every file, its token
/// strips, and a status badge per section. That is what a designer opens the
/// section for, so the editing surface is offered for a selected chapter only —
/// and the overview is asserted part by part rather than by its presence,
/// because the way to lose it is one branch too wide, not a deleted block.
/// </para>
/// </summary>
public sealed class DesignKnowledgeViewTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task A_selected_design_chapter_renders_the_editing_surface()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("colors.md");

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));
        Assert.Single(component.FindAll("[data-testid='knowledge-chapter-edit']"));
    }

    [Fact]
    public async Task The_selected_chapter_is_shown_through_the_shared_file_view()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("colors.md");

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));

        // Inside the file view's body rather than merely somewhere on the panel.
        // The header is what keeps the identity on screen while the chapter
        // scrolls, and a body that landed beside the file view instead of in it
        // would take that away while still looking right in a screenshot.
        Assert.Single(component.FindAll("[data-testid='design-chapter-file-body'] [data-testid='knowledge-chapter-surface']"));

        component.AssertTheFileIsNamedOnce("Colors", ".design-document");

        // Nothing above the file view introduces it any more: no header holding a
        // lone status badge, no folder path, no summary reprinting the file's
        // first paragraph. The file view's own header is the only thing that says
        // which file this is.
        Assert.Empty(component.FindAll(".design-document__header"));
        Assert.Empty(component.FindAll(".design-document__file"));
        Assert.Empty(component.FindAll(".design-document__summary"));
        Assert.Empty(component.FindAll(".design-knowledge__source"));

        // The token strip goes with them. Colors carries a token table, so this
        // is a strip that would have rendered — and its values are in the file
        // the view below is showing.
        Assert.Empty(component.FindAll(".design-token-strip"));
    }

    [Fact]
    public async Task A_selected_chapter_hands_its_scrollbar_to_the_file_view()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("colors.md");

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='design-chapter-file']")));

        // The modifier is what caps the pane at the section's height, and Fill is
        // what makes the file view take that height and scroll its own body. A
        // body with a max-height instead would grow the pane past the section
        // already scrolling it, which is the second scrollbar this removed.
        Assert.Single(component.FindAll(".design-knowledge--chapter"));
        Assert.Contains("file-view--fill", component.Find("[data-testid='design-chapter-file']").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_selected_chapter_is_offered_whole_rather_than_reassembled_from_its_sections()
    {
        await using var harness = CreateHarness();

        var component = harness.Render("colors.md");

        // The meta fence is the tell. It is the part a parsed-and-restitched
        // buffer would drop, and dropping it would write the file back without
        // its status on the first debounce.
        component.WaitForAssertion(() => Assert.Contains(
            "status: accepted",
            component.Find("[data-testid='knowledge-chapter-surface']").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_typed_design_chapter_reaches_the_file()
    {
        await using var harness = CreateHarness();
        var component = harness.Render("colors.md");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-edit']")));

        component.Find("[data-testid='knowledge-chapter-edit']").Click();

        // Waited for rather than found: the click is dispatched onto a renderer
        // that may still be finishing the view's own load, and the render that
        // puts the textarea there is then not the one the click returned from.
        // Kept afterwards, because the blur belongs to the element the text was
        // typed into.
        var editor = component.WaitForElement("textarea");
        editor.Input("# Colors\n\nTyped into the design chapter.\n");
        editor.Blur();

        // The gestures are dispatched without awaiting the write, so the file is
        // polled rather than read once.
        component.WaitForAssertion(
            () => Assert.Contains(
                "Typed into the design chapter.",
                File.ReadAllText(Path.Combine(harness.DesignFolder, "colors.md")),
                StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_chapter_that_left_the_folder_offers_no_way_in()
    {
        await using var harness = CreateHarness();
        var component = harness.Render("colors.md");
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='knowledge-chapter-surface']")));

        // Listed and no longer readable. The panel keeps rendering what it
        // parsed before the file went away, and offers no edit that would fail
        // on the first keystroke.
        File.Delete(Path.Combine(harness.DesignFolder, "colors.md"));
        harness.Rerender(component, "colors.md");

        // Waited away rather than asserted away: the view re-reads the file before
        // it can conclude there is nothing to edit, so the surface leaves on a
        // later render than the parameter set returned from. The wait is not
        // vacuous — the surface was pinned on screen above, so what is waited for
        // is a disappearance and not an absence that was always true.
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-surface']")));
        Assert.Contains("Palette", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_nothing_selected_the_folder_overview_is_still_the_whole_folder()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(selectedPath: null);

        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll(".design-document").Count));

        // Every part of the overview, one assertion each: the nav across files,
        // the token strip, the per-section blocks and the per-section status
        // badge. An editing surface here would have replaced the last two.
        Assert.Equal(2, component.FindAll(".design-knowledge__nav-link").Count);
        Assert.NotEmpty(component.FindAll(".design-token"));
        Assert.NotEmpty(component.FindAll(".design-section"));
        Assert.NotEmpty(component.FindAll(".design-section__header .knowledge-status"));
        Assert.Empty(component.FindAll("[data-testid='knowledge-chapter-surface']"));
    }

    /// <summary>
    /// Deletes the temp folders once every test has awaited its harness away, so
    /// nothing this class rendered can still be writing into one of them. The
    /// catch stays as a courtesy for a lock this class does not own — a scanner
    /// or an indexer holding a file open — rather than as the thing keeping the
    /// suite green.
    /// </summary>
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
        var root = Path.Combine(Path.GetTempPath(), "backlog-design-view-tests", Guid.NewGuid().ToString("n"));
        var repository = Path.Combine(root, "repo");
        var design = Path.Combine(repository, ".design");
        Directory.CreateDirectory(design);
        _roots.Add(root);

        File.WriteAllText(Path.Combine(design, "colors.md"), Colors);
        File.WriteAllText(Path.Combine(design, "interaction-guidelines.md"), InteractionGuidelines);

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(gitHubSettings.SetRepositories(repositories));
        gitHubSettings.SetCloneDirectory("backlog", repository);

        var context = new BunitContext();

        // The markdown editor watches its textarea through interop for the
        // highlight layer. None of that is what these tests are about.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHubSettings));
        context.Services.AddSingleton<DesignKnowledgeProvider>();
        context.Services.AddSingleton<KnowledgeChapterWriter>();

        return new Harness(context, design);
    }

    private const string Colors = """
        # Colors
        ```meta
        status: accepted
        order: ["interaction-guidelines.md"]
        ```

        The palette and how it is applied.

        ## Palette
        ```meta
        status: draft
        ```

        | Token | Value | Usage |
        | --- | --- | --- |
        | --color-primary | #1f6feb | Primary actions |
        """;

    private const string InteractionGuidelines = """
        # Interaction guidelines
        ```meta
        status: draft
        ```

        How the app behaves under the hand.

        ## Auto-save
        ```meta
        status: accepted
        ```

        Bodies persist on a debounce.
        """;

    private sealed record Harness(BunitContext Context, string DesignFolder) : IAsyncDisposable
    {
        public IRenderedComponent<DesignKnowledgeView> Render(string? selectedPath) =>
            Context.Render<DesignKnowledgeView>(parameters => parameters
                .Add(view => view.RepositoryAlias, "backlog")
                .Add(view => view.SelectedPath, selectedPath));

        public void Rerender(IRenderedComponent<DesignKnowledgeView> component, string? selectedPath) =>
            component.Render(parameters => parameters
                .Add(view => view.RepositoryAlias, "backlog")
                .Add(view => view.SelectedPath, selectedPath));

        /// <summary>
        /// Awaited disposal, because the editing surface this harness renders
        /// writes its last pending save on the way out. A synchronous
        /// <c>Dispose</c> hands that save to the renderer's dispatcher and returns
        /// before it lands, so the folder delete that follows could arrive while
        /// the file was still being replaced — a locked temp file on a slow
        /// machine and a green suite on a fast one.
        /// </summary>
        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
