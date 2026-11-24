using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Diagrams.C4;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The C4 model beside the architecture chapters, as the panel offers it.
/// <para>
/// Two things are worth pinning here and nothing else is. That the pane is the
/// architecture surface, and the only reading of the model (local ADR 0046). And that the reference works in both directions off the single
/// authored statement in <c>_c4/references.json</c> and the chapters' embeds, because
/// the reverse direction is derived and a derivation that silently produces an empty
/// list looks exactly like a chapter that never referenced anything.
/// </para>
/// <para>
/// The zoom navigation's own moves are pinned in the component library's
/// <c>C4Zoom*Tests</c>. Re-asserting them through this panel would be testing the
/// same thing twice, through more machinery.
/// </para>
/// </summary>
public sealed class C4DevbookPanelTests : IDisposable
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
    /// the chapter — the devbook-meta checker refuses a <c>.dsl</c> target in a
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

    /// <summary>A chapter carrying a C4 fence of its own — what a repository that
    /// never wrote a <c>.dsl</c> has, and what the model falls back to.</summary>
    private const string FencedChapter =
        "# 05. Building Block View\n\n```meta\nstatus: active\n```\n\n" +
        "```mermaid\nC4Context\n    title Finance context\n    Person(me, \"ME\")\n    System(finance, \"Finance\")\n    Rel(me, finance, \"Uses\")\n```\n";

    private readonly List<TemporaryDirectory> _temps = [];

    // ---- with no model -------------------------------------------------------

    /// <summary>
    /// A host that never registered the store — the storybook page, and every panel
    /// test written before this feature existed. It draws the chapters-only surface
    /// rather than fail on a service it does not know about.
    /// </summary>
    [Fact]
    public async Task Without_the_store_registered_the_panel_still_renders_its_chapters()
    {
        await using var harness = CreateHarness(registerStore: false);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.Single(component.FindAll("[data-testid='arc42-surface-chapters-only']"));
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-document']"));
    }

    /// <summary>
    /// With no model to zoom, the surface is the chapters alone: no zoom bar and no way
    /// back to a model that is not there.
    /// </summary>
    [Fact]
    public async Task With_no_workspace_on_disk_the_surface_is_the_chapters_alone()
    {
        await using var harness = CreateHarness(workspace: string.Empty, writeWorkspace: false);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.Single(component.FindAll("[data-testid='arc42-surface-chapters-only']"));
        Assert.Empty(component.FindAll("[data-testid='c4-zoom-bar']"));
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-document']"));

        // The same tree without the view groups: the chapters listed, no way back.
        Assert.Empty(component.FindAll("[data-testid='arc42-surface-view']"));
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-surface-chapter']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-surface-back']"));
    }

    // ---- the architecture surface ----------------------------------------------

    /// <summary>The C4 key on: the pane is the architecture surface — no Chapters or C4
    /// tab — opening on the model, with the zoom bar and the left navigation (local ADR
    /// 0046). No second switch is asked for.</summary>
    [Fact]
    public async Task The_pane_is_one_surface_rather_than_tabs()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.Empty(component.FindAll("[role='tab']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-tabs']"));
        Assert.Single(component.FindAll("[data-testid='arc42-surface']"));
        Assert.Single(component.FindAll("[data-testid='arc42-surface-explorer']"));
        Assert.Single(component.FindAll("[data-testid='c4-zoom-bar']"));
        Assert.Single(component.FindAll("[data-testid='arc42-surface-nav']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-document']"));
    }

    /// <summary>
    /// First opening: the System context view with the ways-to-move hint. Dismissed,
    /// it is remembered through the host's view preferences, so the next opening shows
    /// the view alone.
    /// </summary>
    [Fact]
    public async Task The_surface_opens_on_the_context_view_and_its_hint_is_dismissed_per_user()
    {
        var preferences = new RememberedViewPreferences();
        await using var harness = CreateHarness(preferences: preferences);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.Equal("context-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
        Assert.NotEmpty(component.FindAll("[data-testid='c4-zoom-hint']"));

        await component.Find("[data-testid='c4-zoom-hint-dismiss']").ClickAsync(new());

        Assert.Empty(component.FindAll("[data-testid='c4-zoom-hint']"));
        Assert.True(preferences.C4ZoomHintDismissed);

        await using var again = CreateHarness(preferences: preferences);
        var reopened = again.Render(null);
        again.Settle(reopened);

        Assert.Equal("context-backlog", reopened.Find("[data-testid='c4-zoom-key']").TextContent);
        Assert.Empty(reopened.FindAll("[data-testid='c4-zoom-hint']"));
    }

    /// <summary>
    /// The surface's inspector is handed what the folder says about the selected
    /// element: the chapter the views map lists against the view it is on, and the
    /// decision whose <c>c4</c> names it — proposed, so chipped. Picking the decision
    /// asks the host for it and opens it in the surface, in place of the picture.
    /// </summary>
    [Fact]
    public async Task The_surface_inspector_lists_the_records_and_opens_one_in_the_surface()
    {
        const string Decision =
            "# ADR 0030: Stored credentials go through the OS envelope\n\n```meta\nstatus: proposed\nc4: [backlog.desktop]\n```\n\nText.\n";

        await using var harness = CreateHarness(
            references: Documented,
            extraFiles: new Dictionary<string, string> { ["adr/0030-stored-credentials.md"] = Decision });

        var asked = new List<DevbookChapterLink>();
        var component = harness.Render(null, asked);
        harness.Settle(component);

        var explorer = component.FindComponent<C4ZoomExplorer>();
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(C4MermaidWriter.AliasOf("backlog"), "context-backlog"));
        await explorer.InvokeAsync(() => explorer.Instance.NodeSelected(C4MermaidWriter.AliasOf("backlog.desktop"), "containers-backlog"));

        var chapter = Assert.Single(component.FindAll("[data-testid='c4-inspector-chapter']"));
        Assert.Contains("Building Block View", chapter.TextContent, StringComparison.Ordinal);

        var decision = Assert.Single(component.FindAll("[data-testid='c4-inspector-decision']"));
        Assert.Contains("0030", decision.TextContent, StringComparison.Ordinal);
        Assert.Contains("Stored credentials go through the OS envelope", decision.TextContent, StringComparison.Ordinal);
        Assert.NotNull(decision.QuerySelector("[data-testid='c4-inspector-proposed']"));
        Assert.Empty(component.FindAll("[data-testid='c4-inspector-debt-record']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-document']"));

        await decision.ClickAsync(new());

        var link = Assert.Single(asked);
        Assert.EndsWith("adr/0030-stored-credentials.md", link.Path, StringComparison.Ordinal);
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-document']"));
    }

    private sealed class RememberedViewPreferences : IDevbookViewPreferences
    {
        public bool C4ZoomHintDismissed { get; private set; }

        public void DismissC4ZoomHint() => C4ZoomHintDismissed = true;
    }

    // ---- the references ------------------------------------------------------

    /// <summary>
    /// The reverse half of the one authored statement in `_c4/references.json`, drawn
    /// in the surface's left navigation beside the open view — because the component
    /// library has no idea what a devbook chapter is.
    /// </summary>
    [Fact]
    public async Task A_view_names_the_chapters_that_reference_it()
    {
        await using var harness = CreateHarness(references: Documented);

        var component = harness.Render(null);
        harness.Settle(component);

        await Harness.OpenViewAsync(component, "containers-backlog");

        Assert.Equal("containers-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-surface-chapter'][data-documents]"));
        Assert.Empty(component.FindAll("[data-testid='arc42-surface-documents-none']"));
    }

    [Fact]
    public async Task A_view_nothing_references_says_so_rather_than_showing_an_empty_list()
    {
        await using var harness = CreateHarness(references: Undocumented);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-surface-documents-none']"));
    }

    /// <summary>
    /// A reference naming a chapter section still counts for the whole chapter. The
    /// authored entry says `#container-view` because that is the section it is about,
    /// and the chapter side of the link is drawn against the open file.
    /// </summary>
    [Fact]
    public async Task A_chapter_lists_the_views_that_document_it()
    {
        await using var harness = CreateHarness(references: Documented);

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-chapter-links-view']")));
    }

    /// <summary>
    /// Following it opens the view in the surface's picture, in place of the chapter.
    /// Handled here rather than handed up: the host selects chapters by path and a view
    /// is not a file in the Devbook menu.
    /// </summary>
    [Fact]
    public async Task Following_a_chapters_view_link_opens_that_view()
    {
        await using var harness = CreateHarness(references: Documented);

        var asked = new List<DevbookChapterLink>();
        var component = harness.Render(ChapterPath, asked);
        harness.Settle(component);

        await component.WaitForElement("[data-testid='arc42-chapter-links-view']").ClickAsync(new());

        Assert.Equal("containers-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
        Assert.Empty(component.FindAll("[data-testid='arc42-document']"));
        Assert.Empty(asked);
    }

    /// <summary>How a reference from another section arrives. A `.domain` chapter
    /// naming an arc42 view has nowhere else to send the reader.</summary>
    [Fact]
    public async Task A_view_named_by_the_host_opens_without_anybody_clicking()
    {
        await using var harness = CreateHarness();

        var component = harness.RenderOnView(".arc42/workspace.dsl#containers-backlog");
        harness.Settle(component);

        Assert.Equal("containers-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
        Assert.Contains("Container Diagram", component.Find("[data-testid='c4-zoom-title']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_view_the_host_names_that_does_not_exist_leaves_the_chapter_open()
    {
        await using var harness = CreateHarness();

        var component = harness.Context.Render<Arc42DevbookPanel>(parameters => parameters
            .Add(panel => panel.RepositoryAlias, harness.RepositoryAlias)
            .Add(panel => panel.SelectedPath, ChapterPath)
            .Add(panel => panel.SelectedC4View, ".arc42/workspace.dsl#no-such-view"));

        harness.Settle(component);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-document']")));
    }

    /// <summary>
    /// Following a chapter the open view documents opens that chapter in the surface,
    /// in place of the picture, and asks the host for it too.
    /// </summary>
    [Fact]
    public async Task Following_a_chapter_the_view_documents_opens_it_in_place_of_the_picture()
    {
        await using var harness = CreateHarness(references: Documented);

        var component = harness.Render(null);
        harness.Settle(component);

        await Harness.OpenViewAsync(component, "containers-backlog");
        await component.Find("[data-testid='arc42-surface-chapter'][data-documents]").ClickAsync(new());

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='arc42-document']")));
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
                !include other.dsl
                model {
                    backlog = softwareSystem "Prompt Backlog" "The system"
                }
                views {
                    systemContext backlog "context-backlog" "System Context" { include * }
                }
            }
            """;

        await using var harness = CreateHarness(workspace: workspace);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.NotEmpty(component.FindAll("[data-testid='c4-zoom-problems']"));
    }

    /// <summary>
    /// A workspace split across <c>c4/</c> reads as one model. The reader knows no
    /// <c>!include</c>, so the store splices each one naming a file in <c>c4/</c> in
    /// before the workspace is read — the only folder devbook lets a workspace
    /// include from.
    /// </summary>
    [Fact]
    public async Task A_workspace_including_a_file_from_c4_reads_as_one_model()
    {
        const string workspace = """
            workspace "Split" {
                !identifiers hierarchical
                model {
                    !include c4/model.dsl
                }
                views {
                    systemContext backlog "context-backlog" "System Context" { include * }
                }
            }
            """;

        const string model = """
            me = person "ME" "The owner"
            backlog = softwareSystem "Prompt Backlog" "The system"
            me -> backlog "Captures work"
            """;

        await using var harness = CreateHarness(
            workspace: workspace,
            extraFiles: new Dictionary<string, string> { ["c4/model.dsl"] = model });

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.Equal("context-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
        Assert.Empty(component.FindAll("[data-testid='c4-zoom-problems']"));
        Assert.Contains("Prompt Backlog", component.Find("[data-testid='arc42-surface-explorer']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clean_workspace_reports_nothing()
    {
        await using var harness = CreateHarness();

        var component = harness.Render(null);
        harness.Settle(component);

        // Anchored on a view being on screen, because the assertion below is a negative
        // and a surface that drew no view satisfies it for the wrong reason.
        Assert.Equal("context-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
        Assert.Empty(component.FindAll("[data-testid='c4-zoom-problems']"));
    }

    // ---- the model drawn from the chapters ------------------------------------

    /// <summary>
    /// A folder with C4 fences and no workspace still gets a model. The fences are a
    /// model already, and offering nothing until somebody authors a <c>.dsl</c> hid the
    /// one picture such a repository has — while saying, over the picture, where it
    /// came from and what a fuller one would take.
    /// </summary>
    [Fact]
    public async Task A_chapters_C4_fence_with_no_workspace_gives_a_model_drawn_from_it()
    {
        await using var harness = CreateHarness(writeWorkspace: false, chapter: FencedChapter);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-surface-explorer']"));
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-c4-derived']"));
        Assert.Contains("Finance context", component.Find("[data-testid='arc42-surface-explorer']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>A derived view documents the chapter its fence is in, so the navigation
    /// names it rather than asking for a <c>references.json</c> nobody needs to
    /// write.</summary>
    [Fact]
    public async Task A_derived_view_names_the_chapter_its_fence_came_from()
    {
        await using var harness = CreateHarness(writeWorkspace: false, chapter: FencedChapter);

        var component = harness.Render(null);
        harness.Settle(component);

        await component.FindAll("[data-testid='arc42-surface-view']")[0].ClickAsync(new());

        Assert.Empty(component.FindAll("[data-testid='arc42-surface-documents-none']"));
        Assert.Contains("Building Block View", component.Find("[data-testid='arc42-surface-chapter'][data-documents]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chapter_lists_the_view_drawn_from_its_fence_and_following_it_opens_that_view()
    {
        await using var harness = CreateHarness(writeWorkspace: false, chapter: FencedChapter);

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        var link = component.WaitForElement("[data-testid='arc42-chapter-links-view']");
        await link.ClickAsync(new());

        Assert.Empty(component.FindAll("[data-testid='arc42-document']"));
        Assert.Contains("Finance context", component.Find("[data-testid='arc42-surface-explorer']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>An authored workspace is the model. The chapters' fences are not
    /// derived beside it — two models of one architecture would be a choice nobody
    /// asked the reader to make.</summary>
    [Fact]
    public async Task With_an_authored_workspace_the_chapters_fences_are_not_drawn_as_a_second_model()
    {
        await using var harness = CreateHarness(chapter: FencedChapter);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.Equal("context-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
        Assert.Empty(component.FindAll("[data-testid='arc42-c4-derived']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-c4-workspaces']"));
    }

    /// <summary>A folder that never had a <c>workspace.dsl</c> nor a <c>_c4/</c> at
    /// all — the finance case — is the same as one with an empty <c>_c4/</c>.</summary>
    [Fact]
    public async Task With_no_workspace_folder_at_all_the_chapters_fences_still_give_a_model()
    {
        await using var harness = CreateHarness(writeWorkspace: false, chapter: FencedChapter, createReferenceDirectory: false);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.NotEmpty(component.FindAll("[data-testid='arc42-c4-derived']"));
        Assert.NotEmpty(component.FindAll("[data-testid='arc42-surface-explorer']"));
    }

    /// <summary>Underscored folders are not chapters — the chapter list skips them on
    /// the same rule — so a fence in one is no part of the model.</summary>
    [Fact]
    public async Task A_fence_in_an_underscored_folder_is_not_read()
    {
        await using var harness = CreateHarness(
            writeWorkspace: false,
            extraFiles: new Dictionary<string, string> { ["_drafts/03-context.md"] = FencedChapter });

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.Single(component.FindAll("[data-testid='arc42-surface-chapters-only']"));
        Assert.Empty(component.FindAll("[data-testid='arc42-surface-explorer']"));
    }

    /// <summary>A review note quoting a C4 fence opens with four backticks so the
    /// quote can sit inside it. The quote is a sample in a note, not a diagram of the
    /// architecture.</summary>
    [Fact]
    public async Task A_C4_fence_quoted_inside_a_four_backtick_fence_is_not_read()
    {
        const string quoting =
            "# 05. Building Block View\n\n```meta\nstatus: active\n```\n\n" +
            "````annotation\nTry this:\n```mermaid\nC4Context\n    System(x, \"X\")\n```\n````\n";

        await using var harness = CreateHarness(writeWorkspace: false, chapter: quoting);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.Empty(component.FindAll("[data-testid='arc42-surface-explorer']"));
    }

    [Fact]
    public async Task A_mermaid_fence_that_is_not_C4_gives_no_model()
    {
        const string flowchart =
            "# 05. Building Block View\n\n```meta\nstatus: active\n```\n\n```mermaid\nflowchart LR\n    a --> b\n```\n";

        await using var harness = CreateHarness(writeWorkspace: false, chapter: flowchart);

        var component = harness.Render(null);
        harness.Settle(component);

        Assert.Empty(component.FindAll("[data-testid='arc42-surface-explorer']"));
    }

    /// <summary>
    /// A chapter edited in the app redraws the model drawn from it. The store caches
    /// its catalog, and nothing on disk announces a save the app itself made — so
    /// without the save telling the store, the model kept the fence as it was before
    /// the edit until something else moved the folder.
    /// </summary>
    [Fact]
    public async Task Saving_a_chapter_in_the_app_redraws_the_model_drawn_from_it()
    {
        await using var harness = CreateHarness(writeWorkspace: false, chapter: FencedChapter);
        var chapterFile = Path.Combine(harness.Arc42, "05-building-block-view.md");

        var component = harness.Render(ChapterPath);
        harness.Settle(component);
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='arc42-chapter-file-edit']")));

        await component.Find("[data-testid='arc42-chapter-file-edit']").ClickAsync(new());
        component.WaitForElement("textarea").Input(FencedChapter.Replace("Finance context", "Renamed context", StringComparison.Ordinal));

        component.WaitForAssertion(
            () => Assert.Contains("Renamed context", File.ReadAllText(chapterFile), StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));

        // The re-read that follows the save is what asks the store again.
        component.WaitForAssertion(
            () => Assert.Contains("Renamed context", component.Find("[data-testid='arc42-chapter-links-view']").TextContent, StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
    }

    // ---- a chapter embedding a view: ![…](c4:<key>) ---------------------------

    /// <summary>A chapter that embeds the container view under its own section — a
    /// heading with a <c>meta</c> block, a chapter node — with the key spelled in
    /// another case, which devbook matches without regard to.</summary>
    private const string EmbeddingChapter =
        "# 05. Building Block View\n\n```meta\nstatus: active\n```\n\n" +
        "## Container view\n\n```meta\n```\n\n![The containers](c4:Containers-Backlog)\n\nThe prose under it.\n";

    /// <summary>Every place devbook's <c>c4Embeds</c> reads an embed as an example
    /// rather than an embed: a code span, a fence, and an HTML comment, one of them
    /// spread over lines.</summary>
    private const string QuotingChapter =
        "# 05. Building Block View\n\n```meta\nstatus: active\n```\n\n" +
        "## How to embed\n\nWrite `![x](c4:containers-backlog)` in the chapter.\n\n" +
        "```markdown\n![x](c4:containers-backlog)\n```\n\n" +
        "<!-- ![x](c4:containers-backlog) -->\n\n" +
        "<!--\n![x](c4:containers-backlog)\n-->\n";

    [Fact]
    public async Task A_chapter_embedding_a_view_draws_that_view_where_the_image_was()
    {
        await using var harness = CreateHarness(chapter: EmbeddingChapter);

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        var embed = component.WaitForElement("[data-testid='devbook-c4-embed']");
        Assert.Equal("containers-backlog", embed.GetAttribute("data-c4-view"));
        Assert.Equal("The containers", embed.GetAttribute("aria-label"));
        Assert.NotNull(embed.QuerySelector("[data-testid='devbook-c4-embed-diagram'] svg"));
        Assert.Contains("Desktop App", embed.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_in_the_C4_model_under_an_embedded_view_opens_that_view()
    {
        await using var harness = CreateHarness(chapter: EmbeddingChapter);

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        var open = component.WaitForElement("[data-testid='devbook-c4-embed-open']");
        Assert.Equal(C4ViewImages.OpenLabel, open.TextContent.Trim());

        await open.ClickAsync(new());

        Assert.Equal("containers-backlog", component.Find("[data-testid='c4-zoom-key']").TextContent);
        Assert.Empty(component.FindAll("[data-testid='arc42-document']"));
    }

    /// <summary>An embed naming no view is devbook's checker's error to report. The
    /// chapter keeps its own rule for it — the alt text — rather than drawing a figure
    /// of nothing.</summary>
    [Fact]
    public async Task An_embed_naming_no_view_keeps_its_alt_text()
    {
        await using var harness = CreateHarness(
            chapter: EmbeddingChapter.Replace("c4:Containers-Backlog", "c4:no-such-view", StringComparison.Ordinal));

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() => Assert.Contains("The containers", component.Find("[data-testid='arc42-chapter-file']").TextContent, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='devbook-c4-embed']"));
    }

    /// <summary>The embed is the reference: with no <c>references.json</c> at all, the
    /// view names the chapter that embeds it.</summary>
    [Fact]
    public async Task A_view_a_chapter_embeds_names_that_chapter()
    {
        await using var harness = CreateHarness(chapter: EmbeddingChapter, createReferenceDirectory: false);

        var component = harness.Render(null);
        harness.Settle(component);

        await Harness.OpenViewAsync(component, "containers-backlog");

        Assert.Empty(component.FindAll("[data-testid='arc42-surface-documents-none']"));
        Assert.Contains("Building Block View", component.Find("[data-testid='arc42-surface-chapter'][data-documents]").TextContent, StringComparison.Ordinal);
    }

    /// <summary>The section is the builder's edge source: the innermost heading that is
    /// a chapter node. An embed under the title alone, or under a structural heading
    /// with no <c>meta</c> block, documents the file.</summary>
    [Theory]
    [InlineData("# 05. Building Block View\n\n```meta\n```\n\n![x](c4:containers-backlog)\n")]
    [InlineData("# 05. Building Block View\n\n```meta\n```\n\n## Structural\n\n![x](c4:containers-backlog)\n")]
    public async Task An_embed_outside_any_chapter_node_documents_the_file(string chapter)
    {
        await using var harness = CreateHarness(chapter: chapter, createReferenceDirectory: false);

        var catalog = await harness.LoadCatalogAsync();

        Assert.Equal([ChapterPath], Assert.Single(catalog.Views, view => view.Key == "containers-backlog").Documents);
    }

    /// <summary>A <c>references.json</c> entry naming another folder's chapter — the
    /// repository's <c>.devbook/domain/context-map.md</c> — is kept beside the embed
    /// until it is retired.</summary>
    [Fact]
    public async Task A_references_entry_for_another_folder_stays_beside_the_embed()
    {
        const string references = """
            { "views": { "containers-backlog": [".devbook/domain/context-map.md"] } }
            """;

        await using var harness = CreateHarness(chapter: EmbeddingChapter, references: references);

        var catalog = await harness.LoadCatalogAsync();

        Assert.Equal(
            [$"{ChapterPath}#container-view", ".devbook/domain/context-map.md"],
            Assert.Single(catalog.Views, view => view.Key == "containers-backlog").Documents);
    }

    [Fact]
    public async Task A_chapter_lists_the_views_it_embeds()
    {
        await using var harness = CreateHarness(chapter: EmbeddingChapter, createReferenceDirectory: false);

        var component = harness.Render(ChapterPath);
        harness.Settle(component);

        component.WaitForAssertion(() =>
        {
            var link = Assert.Single(component.FindAll("[data-testid='arc42-chapter-links-view']"));
            Assert.Contains("Container Diagram", link.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task An_embed_quoted_in_a_code_span_a_fence_or_a_comment_documents_nothing()
    {
        await using var harness = CreateHarness(chapter: QuotingChapter, references: Undocumented);

        var catalog = await harness.LoadCatalogAsync();

        Assert.All(catalog.Views, view => Assert.Empty(view.Documents));
    }

    /// <summary>Until <c>references.json</c> is retired, a view keeps what it states
    /// beside the embeds — except a second entry for a file an embed already names,
    /// which would list one chapter twice.</summary>
    [Fact]
    public async Task The_map_is_the_embeds_plus_what_references_json_still_states()
    {
        const string references = """
            {
              "views": {
                "containers-backlog": [
                  ".arc42/05-building-block-view.md#an-old-anchor",
                  ".arc42/06-runtime-view.md"
                ],
                "context-backlog": [".arc42/03-context-and-scope.md"]
              }
            }
            """;

        await using var harness = CreateHarness(chapter: EmbeddingChapter, references: references);

        var catalog = await harness.LoadCatalogAsync();

        var containers = Assert.Single(catalog.Views, view => view.Key == "containers-backlog");
        Assert.Equal([$"{ChapterPath}#container-view", ".arc42/06-runtime-view.md"], containers.Documents);
        Assert.Equal(containers.Documents, catalog.Workspaces[0].Documents("containers-backlog"));

        var context = Assert.Single(catalog.Views, view => view.Key == "context-backlog");
        Assert.Equal([".arc42/03-context-and-scope.md"], context.Documents);
    }

    /// <summary>An embed is a chapter edit, so a save in the app has to drop even a
    /// catalog read from an authored workspace — no <c>.dsl</c> changed, but the map
    /// did.</summary>
    [Fact]
    public async Task Saving_a_chapter_drops_the_map_even_for_an_authored_workspace()
    {
        await using var harness = CreateHarness(references: Undocumented);
        var store = harness.Context.Services.GetRequiredService<C4DevbookStore>();

        var before = await harness.LoadCatalogAsync();
        Assert.All(before.Views, view => Assert.Empty(view.Documents));

        File.WriteAllText(Path.Combine(harness.Arc42, "05-building-block-view.md"), EmbeddingChapter);
        store.ChapterSaved();

        var after = await harness.LoadCatalogAsync();
        Assert.NotEmpty(Assert.Single(after.Views, view => view.Key == "containers-backlog").Documents);
    }

    // ---- the image source on its own -----------------------------------------

    [Theory]
    [InlineData("c4:container-view", "container-view")]
    [InlineData("  <C4:Container-View>  ", "Container-View")]
    [InlineData("c4:", null)]
    [InlineData("https://example.com/c4:x.png", null)]
    [InlineData("assets/c4.png", null)]
    [InlineData("scenario:features#shot", null)]
    public void An_image_target_names_a_view_only_under_the_c4_scheme(string target, string? key) =>
        Assert.Equal(key, C4ViewImages.Key(target));

    [Fact]
    public void Every_image_that_is_not_a_view_goes_to_the_source_above()
    {
        var next = new RecordingImages();
        var images = new C4ViewImages { Catalog = Catalog(), Next = next };

        Assert.Null(images.Image("scenario:features#shot", "A shot", ".arc42/05.md"));
        Assert.Null(images.Image("c4:no-such-view", "Missing", ".arc42/05.md"));
        Assert.NotNull(images.Image("c4:CONTAINERS-BACKLOG", "Containers", ".arc42/05.md"));

        Assert.Equal(["scenario:features#shot", "c4:no-such-view"], next.Asked);
    }

    [Fact]
    public void Without_a_catalog_no_embed_resolves()
    {
        var images = new C4ViewImages();

        Assert.Null(images.Resolve("c4:containers-backlog"));
        Assert.Null(images.Image("c4:containers-backlog", "Containers", null));
    }

    /// <summary>The catalog the store would hand over for <see cref="Workspace"/>.</summary>
    private static C4Catalog Catalog()
    {
        var workspace = C4DslReader.Read(Workspace);
        var views = workspace.Views
            .Select(view => new C4ViewEntry(
                C4DevbookStore.Reference(".arc42/workspace.dsl", view.Key),
                C4DevbookStore.WorkspaceFile,
                workspace.Name!,
                view.Key,
                view.Kind,
                view.Title ?? view.Key,
                C4MermaidWriter.Write(workspace, view),
                []))
            .ToList();

        var entry = new C4WorkspaceEntry(C4DevbookStore.WorkspaceFile, workspace.Name!, workspace, [], new Dictionary<string, string[]>());
        return new C4Catalog(true, null, views, [], [entry]);
    }

    private sealed class RecordingImages : Backlog.UI.Components.Devbook.IDevbookImageSource
    {
        public List<string> Asked { get; } = [];

        public RenderFragment? Image(string target, string caption, string? documentPath)
        {
            Asked.Add(target);
            return null;
        }
    }

    private Harness CreateHarness(
        bool registerStore = true,
        IDevbookViewPreferences? preferences = null,
        string? workspace = null,
        string? references = null,
        bool writeWorkspace = true,
        string? chapter = null,
        bool createReferenceDirectory = true,
        IReadOnlyDictionary<string, string>? extraFiles = null)
    {
        var temp = new TemporaryDirectory("backlog-c4");
        _temps.Add(temp);
        var root = temp.Path;

        var arc42 = Path.Combine(root, ".arc42");
        Directory.CreateDirectory(arc42);
        if (createReferenceDirectory || references is not null)
        {
            Directory.CreateDirectory(Path.Combine(arc42, C4DevbookStore.ReferenceDirectory));
        }

        File.WriteAllText(Path.Combine(arc42, "05-building-block-view.md"), chapter ?? Chapter);

        foreach (var (relative, content) in extraFiles ?? new Dictionary<string, string>())
        {
            var path = Path.Combine(arc42, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        if (writeWorkspace)
        {
            File.WriteAllText(
                Path.Combine(arc42, C4DevbookStore.WorkspaceFile),
                workspace ?? Workspace);
        }

        if (references is not null)
        {
            File.WriteAllText(
                Path.Combine(arc42, C4DevbookStore.ReferenceDirectory, C4DevbookStore.ReferenceFile),
                references);
        }

        var workspaceSettings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

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

        var folders = new DevbookFolderSource(gitHub, workspaceSettings);
        context.Services.AddSingleton(workspaceSettings);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IDevbookFolderSource>(folders);
        context.Services.AddSingleton<Arc42DevbookStore>();
        context.Services.AddSingleton<DevbookChapterWriter>();

        if (registerStore) context.Services.AddSingleton<C4DevbookStore>();
        if (preferences is not null) context.Services.AddSingleton(preferences);

        return new Harness(context, repository.Alias, arc42);
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias, string Arc42) : IAsyncDisposable
    {
        public IRenderedComponent<Arc42DevbookPanel> Render(string? selectedPath, List<DevbookChapterLink>? asked = null) =>
            Context.Render<Arc42DevbookPanel>(parameters =>
            {
                parameters
                    .Add(panel => panel.RepositoryAlias, RepositoryAlias)
                    .Add(panel => panel.SelectedPath, selectedPath);

                if (asked is not null)
                {
                    parameters.Add(panel => panel.OnNavigateToChapter, EventCallback.Factory.Create<DevbookChapterLink>(this, asked.Add));
                }
            });

        /// <summary>The panel with the host naming a view, as a reference from another
        /// section arrives.</summary>
        public IRenderedComponent<Arc42DevbookPanel> RenderOnView(string view) =>
            Context.Render<Arc42DevbookPanel>(parameters => parameters
                .Add(panel => panel.RepositoryAlias, RepositoryAlias)
                .Add(panel => panel.SelectedC4View, view));

        /// <summary>Opens a view from the surface's views list, the way a reader does:
        /// what the left navigation says about the open view follows where the
        /// explorer reports it went.</summary>
        public static Task OpenViewAsync(IRenderedComponent<Arc42DevbookPanel> component, string viewKey) =>
            component.FindAll("[data-testid='arc42-surface-view']")
                .Single(view => view.GetAttribute("data-view") == viewKey)
                .ClickAsync(new());

        /// <summary>
        /// The catalog and the workspace are both read asynchronously, so the first
        /// render is the loading spinner and everything under test arrives on a later
        /// one. Waiting on the spinner going away rather than on anything that
        /// appears, because half these tests are about something that must
        /// <em>not</em> appear — and a wait for that would pass on the loading render
        /// and prove nothing.
        /// </summary>
        public void Settle(IRenderedComponent<Arc42DevbookPanel> component) =>
            component.WaitForAssertion(() =>
                Assert.Empty(component.FindAll("[data-testid='arc42-loading']")));

        /// <summary>The store's catalog for this repository, read directly — what the
        /// panel is handed, without a render in between.</summary>
        public Task<C4Catalog> LoadCatalogAsync() =>
            Context.Services.GetRequiredService<C4DevbookStore>().LoadAsync(RepositoryAlias);

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }

    public void Dispose()
    {
        foreach (var temp in _temps) temp.Dispose();
    }
}
