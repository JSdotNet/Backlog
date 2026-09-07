using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Diagrams.C4;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The C4 model beside the architecture chapters, as the panel offers it.
/// <para>
/// Three things are worth pinning here and nothing else is. That the feature stays
/// invisible when its key is off, or when there is no workspace, because a tab that
/// opens onto nothing is worse than no tab. That the chapters are untouched on the
/// first tab and the model is a second reading on its own — the shape the Technology
/// panel already keeps, and the reason the flat view list beside the chapters went
/// away. And that the reference works in both directions off the single authored
/// statement in <c>_c4/references.json</c>, because the reverse direction is derived
/// and a derivation that silently produces an empty list looks exactly like a
/// chapter that never referenced anything.
/// </para>
/// <para>
/// The exploration affordances themselves — drilling, the breadcrumb, search, the
/// Highlighter — are the explorer's own and are pinned in
/// <c>C4ExplorationTests</c> against the logic they are built on. Re-asserting them
/// through this panel would be testing the same thing twice, through more machinery.
/// </para>
/// </summary>
public sealed class C4KnowledgePanelTests : IDisposable
{
    private const string Workspace = """
        workspace "Test Backlog" "A workspace for the panel tests" {
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
                container backlog "containers-backlog" "Container Diagram" { include * }
            }
        }
        """;

    private const string ChapterPath = ".arc42/05-building-block-view.md";

    private const string Chapter =
        "# 05. Building Block View\n\n```meta\nstatus: active\n```\n\nThe static decomposition.\n";

    /// <summary>
    /// What <c>_c4/references.json</c> says.
    /// <para>
    /// The authored half of the reference, and the only statement of it. It is not in
    /// the chapter — the knowledge-meta generator refuses a <c>.dsl</c> target in a
    /// <c>related:</c> list — and it is not in the DSL, because c4hero deletes the
    /// <c>properties</c> blocks it would live in. The chapter side of the link is this
    /// inverted.
    /// </para>
    /// </summary>
    private const string Documented = """
        {
          "views": {
            "containers-backlog": [".arc42/05-building-block-view.md#container-view"]
          }
        }
        """;

    private const string Undocumented = """
        { "views": {} }
        """;

    private readonly List<string> _roots = [];

    // ---- the feature key -----------------------------------------------------

    [Fact]
    public async Task With_the_key_off_there_is_no_C4_tab()
    {
        await using var harness = CreateHarness(c4Enabled: false);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.Empty(component.FindAll("[data-testid='arc42-c4-tab']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-c4-explorer']"));
    }

    /// <summary>
    /// A host that never registered the store — the storybook page, and every panel
    /// test written before this feature existed. It has to render as it did rather
    /// than fail on a service it does not know about.
    /// </summary>
    [Fact]
    public async Task Without_the_store_registered_the_panel_still_renders_its_chapters()
    {
        await using var harness = CreateHarness(c4Enabled: true, registerStore: false);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-option']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-c4-tab']"));
    }

    /// <summary>
    /// A tab that opens onto nothing is worse than no tab, so it is absent rather
    /// than empty when there is no workspace to show.
    /// </summary>
    [Fact]
    public async Task With_no_workspace_on_disk_there_is_no_C4_tab()
    {
        await using var harness = CreateHarness(c4Enabled: true, workspace: string.Empty, writeWorkspace: false);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.Empty(component.FindAll("[data-testid='arc42-c4-tab']"));
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapters-tab']"));
    }

    // ---- the two tabs --------------------------------------------------------

    /// <summary>
    /// The shape the Technology panel already keeps, and the reason the flat list
    /// beside the chapters went away: a C4 view is not a chapter and is in no
    /// chapter, so it never sat comfortably in the chapter list.
    /// </summary>
    [Fact]
    public async Task The_panel_offers_a_chapters_tab_and_a_C4_tab()
    {
        await using var harness = CreateHarness(c4Enabled: true);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapters-tab']"));
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-c4-tab']"));
    }

    /// <summary>Chapters as they were. The first tab is the panel this feature found,
    /// not a rearrangement of it.</summary>
    [Fact]
    public async Task The_chapters_tab_opens_first_and_shows_the_chapters()
    {
        await using var harness = CreateHarness(c4Enabled: true);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-option']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-c4-explorer']"));
    }

    [Fact]
    public async Task The_C4_tab_shows_the_explorer_with_every_view_listed()
    {
        await using var harness = CreateHarness(c4Enabled: true);

        var component = harness.Render(null);
        harness.Settle(component);

        // Awaited rather than fired and forgotten, here and at every other click in
        // this class. bUnit's synchronous `Click()` hands the event to the renderer's
        // dispatcher and returns without waiting for the handler or the render it
        // causes, and this panel reaches its chapter through three awaited reads — so
        // under the parallel load of the whole suite the dispatch queues behind those
        // continuations and the assertions below read the pre-click render. The tab is
        // the sharper case: `TabPanel` renders its `ChildContent` only while active, so
        // this click is what first instantiates `C4Explorer`, and nothing asserted here
        // exists until its render batch has run.
        await component.Find("[data-testid='arc42-c4-tab']").ClickAsync(new());

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-c4-explorer']"));
        Assert.Equal(2, component.FindAll("[data-testid='c4-view-option']").Count);
        Assert.NotEmpty(component.FindAll("[data-testid='c4-breadcrumb']"));
    }

    // ---- the references ------------------------------------------------------

    /// <summary>
    /// The reverse half of the one authored statement in `_c4/references.json`,
    /// drawn under the diagram through the explorer's footer slot — because the
    /// component library has no idea what a knowledge chapter is.
    /// </summary>
    [Fact]
    public async Task A_view_names_the_chapters_that_reference_it()
    {
        await using var harness = CreateHarness(c4Enabled: true, references: Documented);

        var component = harness.Render(null);
        harness.Settle(component);

        await component.Find("[data-testid='arc42-c4-tab']").ClickAsync(new());
        // `C4Explorer.Open` is the one handler here that genuinely awaits — it awaits
        // `ViewKeyChanged.InvokeAsync`, and the footer asserted on below is drawn for
        // the `_view` set inside it.
        await component.FindAll("[data-testid='c4-view-option']")[1].ClickAsync(new());

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-c4-view-references']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-c4-view-unreferenced']"));
    }

    [Fact]
    public async Task A_view_nothing_references_says_so_rather_than_showing_an_empty_list()
    {
        await using var harness = CreateHarness(c4Enabled: true, references: Undocumented);

        var component = harness.Render(null);
        harness.Settle(component);

        await component.Find("[data-testid='arc42-c4-tab']").ClickAsync(new());

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-c4-view-unreferenced']"));
    }

    /// <summary>
    /// A reference naming a chapter section still counts for the whole chapter. The
    /// authored entry says `#container-view` because that is the section it is about,
    /// and the chapter side of the link is drawn against the open file.
    /// </summary>
    [Fact]
    public async Task A_chapter_lists_the_views_that_document_it()
    {
        await using var harness = CreateHarness(c4Enabled: true, references: Documented);

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-c4-view']"));
    }

    /// <summary>
    /// Following it switches tab. Every way in asks for a picture, and leaving the
    /// reader on the chapters tab with the selection changed underneath them answers
    /// a question nobody asked.
    /// </summary>
    [Fact]
    public async Task Following_a_chapters_view_link_opens_the_C4_tab_on_that_view()
    {
        await using var harness = CreateHarness(c4Enabled: true, references: Documented);

        var asked = new List<KnowledgeChapterLink>();
        var component = harness.Render(ChapterPath, asked);
        harness.Settle(component);

        // `SelectView` is synchronous, so it is the dispatch and the render it causes
        // that are at stake — and they are what keeps `Assert.Empty(asked)` below behind
        // the two positive assertions rather than passing on a handler that never ran.
        await component.Find("[data-testid='arc42-chapter-c4-view'] [title='.arc42/_c4/backlog.dsl#containers-backlog']").ClickAsync(new());

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-c4-explorer']"));
        Assert.Contains("Container Diagram", component.Find("[data-testid='arc42-c4-explorer']").TextContent, StringComparison.Ordinal);

        // Handled here rather than handed up: the host selects chapters by path and a
        // view is not a file in the knowledge menu.
        Assert.Empty(asked);
    }

    /// <summary>How a reference from another section arrives. A `.domain` chapter
    /// naming an arc42 view has nowhere else to send the reader.</summary>
    [Fact]
    public async Task A_view_named_by_the_host_opens_the_C4_tab_without_anybody_clicking()
    {
        await using var harness = CreateHarness(c4Enabled: true);

        var component = harness.Context.Render<Arc42KnowledgePanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, harness.RepositoryAlias)
            .Add(panel => panel.SelectedC4View, ".arc42/_c4/backlog.dsl#context-backlog"));

        harness.Settle(component);

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-c4-explorer']"));
        Assert.Contains("System Context", component.Find("[data-testid='arc42-c4-explorer']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_view_the_host_names_that_does_not_exist_leaves_the_chapters_tab_open()
    {
        await using var harness = CreateHarness(c4Enabled: true);

        var component = harness.Context.Render<Arc42KnowledgePanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, harness.RepositoryAlias)
            .Add(panel => panel.SelectedPath, ChapterPath)
            .Add(panel => panel.SelectedC4View, ".arc42/_c4/backlog.dsl#no-such-view"));

        harness.Settle(component);

        Assert.Empty(component.FindAll("[data-testid='arc42-c4-explorer']"));
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-document']"));
    }

    /// <summary>
    /// A click that arrives naming a view the explorer has already left is dropped.
    /// <para>
    /// Drilling is a single click here and a double-click in c4hero, so a reader who
    /// brings that habit sends the second click at the diagram that has only just
    /// replaced the first — and applying it would descend two levels for one gesture.
    /// The viewer suppresses the quick repeat; this is the half it cannot do, because
    /// only the component knows where it got to.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_activation_naming_a_view_that_is_no_longer_open_is_ignored()
    {
        await using var harness = CreateHarness(c4Enabled: true);

        var component = harness.Render(null);
        harness.Settle(component);

        // Un-awaited this was never a soft assert failure: `FindComponent<C4Explorer>`
        // below throws outright when the explorer has not rendered yet.
        await component.Find("[data-testid='arc42-c4-tab']").ClickAsync(new());

        var explorer = component.FindComponent<C4Explorer>();
        var opened = explorer.Find("[data-testid='c4-breadcrumb-step'][aria-current='page']").TextContent;

        await component.InvokeAsync(() => explorer.Instance.NodeActivated("backlog", "a-view-that-is-not-open"));

        Assert.Equal(opened, explorer.Find("[data-testid='c4-breadcrumb-step'][aria-current='page']").TextContent);
    }

    /// <summary>
    /// Following a chapter reference from the C4 tab brings the Chapters tab with it.
    /// <para>
    /// Without this the chapter opened <em>behind</em> the diagram: the host changed
    /// its selection, the panel stayed on C4, and the reader was left looking at the
    /// picture they had just navigated away from. The tab is part of the answer to
    /// "take me there", not a setting they are expected to change themselves.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Following_a_chapter_reference_from_the_C4_tab_returns_to_the_chapters()
    {
        await using var harness = CreateHarness(c4Enabled: true, references: Documented);

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        await component.Find("[data-testid='arc42-c4-tab']").ClickAsync(new());
        // Awaited for the same reason as in `A_view_names_the_chapters_that_reference_it`:
        // `C4Explorer.Open` awaits, and both this test's remaining reads need the view open.
        await component.FindAll("[data-testid='c4-view-option']")[1].ClickAsync(new());
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-c4-explorer']"));

        // The click #354 observed failing. `NavigateKnowledgeReferenceAsync` sets `_tab`
        // to the chapters and returns `OnNavigateToChapter.InvokeAsync`, and the two
        // assertions below are exactly that tab flip — un-awaited they read the render
        // that came before it.
        await component.Find("[data-testid='arc42-c4-view-references'] [title='.arc42/05-building-block-view.md#container-view']").ClickAsync(new());

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-document']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-c4-explorer']"));
    }

    // ---- what could not be read ----------------------------------------------

    /// <summary>
    /// The report is the whole safeguard. A workspace this reader only half
    /// understood still draws a confident picture, so it has to say what it could not
    /// read.
    /// </summary>
    [Fact]
    public async Task A_workspace_with_something_unreadable_says_so_under_the_diagram()
    {
        const string workspace = """
            workspace "Partly readable" {
                !docs docs
                model {
                    backlog = softwareSystem "Prompt Backlog" "The system"
                }
                views {
                    systemContext backlog "context-backlog" "System Context" { include * }
                }
            }
            """;

        await using var harness = CreateHarness(c4Enabled: true, workspace: workspace);

        var component = harness.Render(null);
        harness.Settle(component);

        await component.Find("[data-testid='arc42-c4-tab']").ClickAsync(new());

        Assert.NotEmpty(component.FindAll("[data-testid='c4-problems']"));
        Assert.Contains("!docs", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clean_workspace_reports_nothing()
    {
        await using var harness = CreateHarness(c4Enabled: true);

        var component = harness.Render(null);
        harness.Settle(component);

        await component.Find("[data-testid='arc42-c4-tab']").ClickAsync(new());

        // Anchored on the explorer being on screen, because the assertion below is a
        // negative and an explorer that never rendered satisfies it for the wrong
        // reason — which is exactly what an un-awaited click leaves behind.
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-c4-explorer']"));

        // The explorer's own div renders either way, so it only proves the click
        // dispatched. The breadcrumb sits in the `_view is not null` branch that also
        // holds the problems alert, so it is what proves a view actually rendered —
        // without it a workspace that parsed to no views would satisfy the negative
        // below for the second wrong reason.
        Assert.NotEmpty(component.FindAll("[data-testid='c4-breadcrumb']"));
        Assert.Empty(component.FindAll("[data-testid='c4-problems']"));
    }

    private Harness CreateHarness(
        bool c4Enabled,
        bool registerStore = true,
        string? workspace = null,
        string? references = null,
        bool writeWorkspace = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-c4-" + Guid.NewGuid().ToString("N"));
        _roots.Add(root);

        var arc42 = Path.Combine(root, ".arc42");
        Directory.CreateDirectory(Path.Combine(arc42, C4KnowledgeStore.WorkspaceDirectory));
        File.WriteAllText(Path.Combine(arc42, "05-building-block-view.md"), Chapter);

        if (writeWorkspace)
        {
            File.WriteAllText(
                Path.Combine(arc42, C4KnowledgeStore.WorkspaceDirectory, "backlog.dsl"),
                workspace ?? Workspace);
        }

        if (references is not null)
        {
            File.WriteAllText(
                Path.Combine(arc42, C4KnowledgeStore.WorkspaceDirectory, C4KnowledgeStore.ReferenceFile),
                references);
        }

        var workspaceSettings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        if (c4Enabled) Assert.Null(features.SetEnabled(KnowledgeFeatures.C4Diagrams, true));

        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            KnowledgeFolders =
            [
                .. KnowledgeFolderSetting.Defaults().Select(folder =>
                    string.Equals(folder.Key, ".arc42", StringComparison.OrdinalIgnoreCase)
                        ? folder with { Path = arc42 }
                        : folder)
            ]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var folders = new KnowledgeFolderSource(gitHub, workspaceSettings);
        context.Services.AddSingleton(workspaceSettings);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IKnowledgeFolderSource>(folders);
        context.Services.AddSingleton<Arc42KnowledgeStore>();
        context.Services.AddSingleton<KnowledgeChapterWriter>();

        if (registerStore) context.Services.AddSingleton<C4KnowledgeStore>();

        return new Harness(context, repository.Alias);
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<Arc42KnowledgePanel> Render(string? selectedPath, List<KnowledgeChapterLink>? asked = null) =>
            Context.Render<Arc42KnowledgePanel>(parameters =>
            {
                parameters
                    .Add(panel => panel.RepositoryAlias, RepositoryAlias)
                    .Add(panel => panel.SelectedPath, selectedPath);

                if (asked is not null)
                {
                    parameters.Add(panel => panel.OnNavigateToChapter, EventCallback.Factory.Create<KnowledgeChapterLink>(this, asked.Add));
                }
            });

        /// <summary>
        /// The catalog and the workspace are both read asynchronously, so the first
        /// render is the loading line and everything under test arrives on a later
        /// one. Waiting on the loading line going away rather than on anything that
        /// appears, because half these tests are about something that must
        /// <em>not</em> appear — and a wait for that would pass on the loading render
        /// and prove nothing.
        /// </summary>
        public void Settle(IRenderedComponent<Arc42KnowledgePanel> component) =>
            component.WaitForAssertion(() =>
                Assert.DoesNotContain("Loading architecture knowledge", component.Markup, StringComparison.Ordinal));

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
