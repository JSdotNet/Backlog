using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Diagrams.C4;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// An arc42 chapter drawing the C4 views it documents, under the sections
/// <c>_c4/references.json</c> names, and flagging where its own C4 fence and the model
/// part ways (plan c4-zoom-navigation, entry chapter-views).
/// <para>
/// Pinned for both states of <c>c4-zoom</c>: on, the view is drawn with its Model /
/// Chapter fence toggle and the way into the zoom tab; off, the chapter is the chapter
/// it was. A harness of its own rather than a share of <c>C4DevbookPanelTests</c>',
/// which sibling entries of the same plan are editing at the same time.
/// </para>
/// </summary>
public sealed class C4ChapterViewsTests : IDisposable
{
    private const string Workspace = """
        workspace "Test Backlog" "A workspace for the chapter view tests" {
            !identifiers hierarchical
            model {
                me = person "ME" "The owner"
                backlog = softwareSystem "Prompt Backlog" "The system" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI"
                    store = container "Local Task Store" "Canonical" "SQLite" "Database"
                }
                github = softwareSystem "GitHub" "Issues" "External"
                me -> backlog.desktop "Captures work"
                backlog.desktop -> backlog.store "Reads and writes"
                backlog.desktop -> github "Syncs issues" "HTTPS"
            }
            views {
                systemContext backlog "context-backlog" "System Context" { include * }
                container backlog "containers-backlog" "Containers — Prompt Backlog" { include * }
            }
        }
        """;

    private const string ChapterPath = ".arc42/05-building-block-view.md";

    private const string Documented = """
        { "views": { "containers-backlog": [".arc42/05-building-block-view.md#container-view"] } }
        """;

    /// <summary>The fence draws what the model's container view draws, element for
    /// element.</summary>
    private const string MatchingFence = """
        C4Container
            title Containers
            Person(me, "ME", "The owner")
            System_Boundary(b0, "Prompt Backlog") {
                Container(desktop, "Desktop App", ".NET MAUI", "Windows client")
                ContainerDb(store, "Local Task Store", "SQLite", "Canonical")
            }
            System_Ext(github, "GitHub", "Issues")
            Rel(me, desktop, "Captures work")
            Rel(desktop, store, "Reads and writes")
            Rel(desktop, github, "Syncs issues", "HTTPS")
        """;

    /// <summary>The fence draws Attachments, which the model lacks, and leaves out
    /// GitHub, which the model draws.</summary>
    private const string DriftingFence = """
        C4Container
            title Containers
            Person(me, "ME", "The owner")
            System_Boundary(b0, "Prompt Backlog") {
                Container(desktop, "Desktop App", ".NET MAUI", "Windows client")
                ContainerDb(store, "Local Task Store", "SQLite", "Canonical")
                ContainerDb(attachments, "Attachments", "Blob Storage", "Capture attachments")
            }
            Rel(me, desktop, "Captures work")
            Rel(desktop, store, "Reads and writes")
        """;

    private static string Chapter(string fence) =>
        "# 05. Building Block View\n\n```meta\nstatus: active\n```\n\nThe static decomposition.\n\n" +
        "## Container View\n\nThe deployable split.\n\n```mermaid\n" + fence + "\n```\n\n" +
        "## Desktop App\n\nThe desktop's components.\n";

    private readonly List<string> _roots = [];

    // ---- c4-zoom on ------------------------------------------------------------

    [Fact]
    public async Task With_the_zoom_key_on_the_view_is_drawn_under_the_section_that_names_it()
    {
        await using var harness = CreateHarness(c4ZoomEnabled: true, chapter: Chapter(MatchingFence));

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-c4-embed']")));

        var insert = component.Find("[data-testid^='markdown-chapter-insert-']");
        Assert.Contains("Container View", insert.PreviousElementSibling!.TextContent, StringComparison.Ordinal);

        var embed = component.Find("[data-testid='arc42-chapter-c4-embed']");
        Assert.Equal("containers-backlog", embed.GetAttribute("data-view"));
        Assert.Contains("Containers — Prompt Backlog", embed.TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-c4-model-diagram']"));
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-c4-open-zoom']"));
        Assert.Equal("true", component.Find("[data-testid='arc42-chapter-c4-model']").GetAttribute("aria-pressed"));
    }

    /// <summary>A chapter whose fence draws what the model draws says nothing about
    /// drift: a note on agreement would be a note nobody can act on.</summary>
    [Fact]
    public async Task A_fence_that_matches_the_model_shows_no_drift_note()
    {
        await using var harness = CreateHarness(c4ZoomEnabled: true, chapter: Chapter(MatchingFence));

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-c4-embed']")));
        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-c4-drift']"));
    }

    [Fact]
    public async Task A_fence_that_disagrees_with_the_model_lists_what_each_has_alone()
    {
        await using var harness = CreateHarness(c4ZoomEnabled: true, chapter: Chapter(DriftingFence));

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-c4-drift']")));

        var onlyInFence = component.Find("[data-testid='arc42-chapter-c4-only-in-fence']").TextContent;
        var onlyInModel = component.Find("[data-testid='arc42-chapter-c4-only-in-model']").TextContent;
        Assert.Contains("Attachments", onlyInFence, StringComparison.Ordinal);
        Assert.DoesNotContain("Desktop App", onlyInFence, StringComparison.Ordinal);
        Assert.Contains("GitHub", onlyInModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Desktop App", onlyInModel, StringComparison.Ordinal);
        Assert.Contains("the chapter stays canonical", component.Find("[data-testid='arc42-chapter-c4-drift']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_toggle_draws_the_chapter_fence_in_place_of_the_model()
    {
        await using var harness = CreateHarness(c4ZoomEnabled: true, chapter: Chapter(DriftingFence));

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-c4-fence']")));
        await component.Find("[data-testid='arc42-chapter-c4-fence']").ClickAsync(new());

        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-c4-model-diagram']"));
        var fence = component.Find("[data-testid='arc42-chapter-c4-fence-diagram']");
        Assert.Contains("Attachments", fence.InnerHtml, StringComparison.Ordinal);
        Assert.Equal("true", component.Find("[data-testid='arc42-chapter-c4-fence']").GetAttribute("aria-pressed"));

        await component.Find("[data-testid='arc42-chapter-c4-model']").ClickAsync(new());

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-c4-model-diagram']"));
    }

    [Fact]
    public async Task Open_in_C4_zoom_opens_the_picture_at_that_view_in_place_of_the_chapter()
    {
        await using var harness = CreateHarness(c4ZoomEnabled: true, chapter: Chapter(MatchingFence));

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-c4-open-zoom']")));
        await component.Find("[data-testid='arc42-chapter-c4-open-zoom']").ClickAsync(new());

        component.WaitForAssertion(() =>
            Assert.Equal("containers-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent));
        Assert.Empty(component.FindAll("[data-testid='arc42-document']"));
    }

    /// <summary>Asking again for the view after the reader zoomed elsewhere opens it
    /// again: each "Open in C4 zoom" is a request of its own.</summary>
    [Fact]
    public async Task Open_in_C4_zoom_a_second_time_reopens_the_view()
    {
        await using var harness = CreateHarness(c4ZoomEnabled: true, chapter: Chapter(MatchingFence));

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-c4-open-zoom']")));
        await component.Find("[data-testid='arc42-chapter-c4-open-zoom']").ClickAsync(new());
        component.WaitForAssertion(() =>
            Assert.Equal("containers-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent));

        // Out to the context view through the bar's first step.
        await component.FindAll("[data-testid='c4-zoom-step']")[0].ClickAsync(new());
        component.WaitForAssertion(() =>
            Assert.Equal("context-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent));

        // Back to the chapter through the left navigation's way to every chapter.
        await component.Find("[data-testid='arc42-surface-all-chapters']").ClickAsync(new());
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-c4-open-zoom']")));
        await component.Find("[data-testid='arc42-chapter-c4-open-zoom']").ClickAsync(new());

        component.WaitForAssertion(() =>
            Assert.Equal("containers-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent));
    }

    /// <summary>Two headings of one title: the view is drawn under the one the
    /// reference names — the first — and not under both.</summary>
    [Fact]
    public async Task A_title_used_twice_draws_the_view_once()
    {
        var chapter = Chapter(MatchingFence) + "\n## Container View\n\nA second section of the same name.\n";
        await using var harness = CreateHarness(c4ZoomEnabled: true, chapter: chapter);

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-c4-embed']")));
        Assert.Single(component.FindAll("[data-testid^='markdown-chapter-insert-']"));
    }

    /// <summary>A reference naming no section compares only a fence of the view's own
    /// kind: the chapter's first C4 fence may draw another view entirely, and a drift
    /// note against it would be false.</summary>
    [Fact]
    public async Task A_reference_without_a_section_compares_no_fence_of_another_kind()
    {
        const string wholeChapter = """
            { "views": { "containers-backlog": [".arc42/05-building-block-view.md"] } }
            """;
        const string contextFence = """
            C4Context
                title Context
                Person(me, "ME")
                System(other, "Something else entirely")
                Rel(me, other, "Uses")
            """;
        await using var harness = CreateHarness(c4ZoomEnabled: true, chapter: Chapter(contextFence), references: wholeChapter);

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-c4-embed']")));
        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-c4-drift']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-c4-fence']"));
    }

    /// <summary>A reference naming no section of the chapter — or a section the
    /// chapter no longer has — still documents the chapter, so the view is drawn under
    /// its first heading rather than dropped.</summary>
    [Fact]
    public async Task A_reference_without_a_section_is_drawn_under_the_chapter_title()
    {
        const string wholeChapter = """
            { "views": { "containers-backlog": [".arc42/05-building-block-view.md"] } }
            """;
        await using var harness = CreateHarness(c4ZoomEnabled: true, chapter: Chapter(MatchingFence), references: wholeChapter);

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-c4-embed']")));

        var insert = component.Find("[data-testid^='markdown-chapter-insert-']");
        Assert.Contains("05. Building Block View", insert.PreviousElementSibling!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>A model drawn from the chapters' own fences is those fences: drawing
    /// one in the chapter would draw the fence twice and compare it with itself.</summary>
    [Fact]
    public async Task A_model_drawn_from_the_chapters_is_not_embedded()
    {
        await using var harness = CreateHarness(c4ZoomEnabled: true, chapter: Chapter(DriftingFence), writeWorkspace: false, references: null);

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-surface-explorer']")));
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-document']")));
        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-c4-embed']"));
    }

    // ---- c4-zoom off -------------------------------------------------------------

    [Fact]
    public async Task With_the_zoom_key_off_the_chapter_renders_as_before()
    {
        await using var harness = CreateHarness(c4ZoomEnabled: false, chapter: Chapter(DriftingFence));

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        // The chapter's own views list is the C4 key's, and it is what tells the
        // chapter has been read with the model beside it.
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-c4-view']")));
        component.WaitForAssertion(() => Assert.Contains("Container View", component.Find("[data-testid='arc42-chapter-file']").TextContent, StringComparison.Ordinal));

        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-c4-embed']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-c4-drift']"));
        Assert.Empty(component.FindAll("[data-testid^='markdown-chapter-insert-']"));
    }

    [Fact]
    public async Task With_the_zoom_key_off_a_matching_fence_is_not_embedded_either()
    {
        await using var harness = CreateHarness(c4ZoomEnabled: false, chapter: Chapter(MatchingFence));

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-c4-view']")));
        Assert.Empty(component.FindAll("[data-testid='arc42-chapter-c4-embed']"));
    }

    // ---- harness -----------------------------------------------------------------

    private Harness CreateHarness(
        bool c4ZoomEnabled,
        string chapter,
        string? references = Documented,
        bool writeWorkspace = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-c4-chapter-" + Guid.NewGuid().ToString("N"));
        _roots.Add(root);

        var arc42 = Path.Combine(root, ".arc42");
        var c4 = Path.Combine(arc42, C4DevbookStore.ReferenceDirectory);
        Directory.CreateDirectory(c4);

        File.WriteAllText(Path.Combine(arc42, "05-building-block-view.md"), chapter);
        if (writeWorkspace) File.WriteAllText(Path.Combine(arc42, C4DevbookStore.WorkspaceFile), Workspace);
        if (references is not null) File.WriteAllText(Path.Combine(c4, C4DevbookStore.ReferenceFile), references);

        var workspaceSettings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        Assert.Null(features.SetEnabled(DevbookFeatures.C4Diagrams, true));
        if (c4ZoomEnabled) Assert.Null(features.SetEnabled(DevbookFeatures.C4Zoom, true));

        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            DevbookFolders =
            [
                .. DevbookFolderSetting.Defaults().Select(folder =>
                    string.Equals(folder.Key, ".arc42", StringComparison.OrdinalIgnoreCase)
                        ? folder with { Path = arc42 }
                        : folder)
            ]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(workspaceSettings);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHub, workspaceSettings));
        context.Services.AddSingleton<Arc42DevbookStore>();
        context.Services.AddSingleton<DevbookChapterWriter>();
        context.Services.AddSingleton<C4DevbookStore>();

        return new Harness(context, repository.Alias);
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<Arc42DevbookPanel> Render(string? selectedPath) =>
            Context.Render<Arc42DevbookPanel>(parameters => parameters
                .Add(panel => panel.RepositoryAlias, RepositoryAlias)
                .Add(panel => panel.SelectedPath, selectedPath));

        /// <summary>Past the loading spinner; the chapter's text arrives on a later
        /// render still, so each test waits on what it asserts.</summary>
        public void Settle(IRenderedComponent<Arc42DevbookPanel> component) =>
            component.WaitForAssertion(() =>
                Assert.Empty(component.FindAll("[data-testid='arc42-loading']")));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // A temp folder that will not delete is not a test failure.
            }
        }
    }
}
