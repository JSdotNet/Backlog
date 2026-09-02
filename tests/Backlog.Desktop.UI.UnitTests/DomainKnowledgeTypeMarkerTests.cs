using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Knowledge;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A `.domain` chapter's <c>type</c>, read as a mark in the heading instead of a
/// label-and-value row three lines under it.
///
/// <para>Three claims are pinned here, and the third is the one that makes the
/// other two safe. The heading gains a mark; the strip loses the row; and nothing
/// is lost in the trade — the mark is announced with its type, so a listener still
/// hears what the strip stopped saying, and the heading's own text does not move,
/// because the accessible name is an attribute and the tooltip that would have
/// been a text node is declined here.</para>
///
/// <para>The fourth is the fallback, and it is why the suppression is conditional
/// rather than a blanket rule. `.domain`'s vocabulary grows; a value the marker
/// has no glyph for keeps its plain row, because dropping the row *and* drawing
/// nothing would take the fact off the screen altogether.</para>
///
/// <para><b>Two surfaces, and both are asserted here.</b> The panel draws a
/// document two ways: a summary card assembled from its own parse, for a document
/// nobody opened, and — for the one document that <em>is</em> open — the file
/// itself, handed whole to the file view. Opening a file from the knowledge tree
/// always produces the second, so that is the surface a reader reads, and the
/// first shipping with marks while the second had none is exactly how this went
/// out wrong the first time. The tests below say <c>Card</c> or <c>Opened</c> in
/// their harness, and neither branch is allowed to stand in for the other.</para>
/// </summary>
public sealed class DomainKnowledgeTypeMarkerTests : IDisposable
{
    private const string DocumentPath = ".domain/inbox/model.md";

    private readonly List<string> _roots = [];

    /// <summary>The context map a `.domain` folder has to have before the store
    /// will read it at all. Deliberately plain: the document under test is the one
    /// the harness selects, and a map with marks of its own would make a failure
    /// there look like a failure here.</summary>
    private const string ContextMapFile =
        "# Context Map\n\n```meta\nstatus: draft\n```\n\nWhat divides the contexts.\n";

    /// <summary>A `.domain` file as the convention writes one, on disk, so the panel
    /// opens it the way tree navigation does: a file-level <c>type</c> under the
    /// title and a chapter-level one under each heading — with the third chapter
    /// stating a type this library has no glyph for.</summary>
    private const string ModelFile =
        """
        # Inbox

        ```meta
        type: model
        status: draft
        ```

        The structure of the Inbox.

        ## Inbox Item

        ```meta
        type: aggregate
        status: draft
        related: [.domain/capture/model.md#capture]
        ```

        The thing that arrives.

        ## Capture Source

        ```meta
        type: term
        status: draft
        ```

        Where it came from.

        ## Retention Policy

        ```meta
        type: policy-fragment
        status: draft
        ```

        How long it is kept.

        """;

    /// <summary>The root document a bounded context is discovered by. Nothing in
    /// these tests reads it; the folder scan needs it to call `inbox` a
    /// context.</summary>
    private const string DomainFile =
        "# Inbox\n\n```meta\ntype: domain\nstatus: draft\n```\n\nWhat the Inbox owns.\n";

    [Fact]
    public void A_chapter_heading_leads_with_its_type_and_still_reads_as_itself()
    {
        using var harness = CreateCardHarness();

        var component = harness.Render(ChapterView("aggregate"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll(".domain-section__header h4")));

        var heading = component.Find(".domain-section__header h4");
        var mark = heading.QuerySelector("svg")!;

        Assert.Contains("knowledge-type-marker--aggregate", mark.ClassList);

        // Named, because the row below that used to say this is gone. The mark is
        // now the only statement of the chapter's type on the screen, and a listener
        // has to be able to reach it.
        Assert.Equal("img", mark.GetAttribute("role"));
        Assert.Equal("type: aggregate", mark.GetAttribute("aria-label"));
        Assert.Null(mark.GetAttribute("aria-hidden"));

        // And named for free. No title element, so the heading's text is byte for
        // byte the title it was before any mark existed — not "starts with", not
        // trimmed.
        Assert.Empty(mark.QuerySelectorAll("title"));
        Assert.Equal("Inbox Item", heading.TextContent);
    }

    [Fact]
    public void A_recognised_type_stops_being_a_row_in_the_strip()
    {
        using var harness = CreateCardHarness();

        var component = harness.Render(ChapterView("aggregate"));

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".domain-section .domain-metadata")));

        var strip = component.Find(".domain-section .domain-metadata");

        // The field name and its value are both gone from the strip, because the
        // heading above is now saying it.
        Assert.DoesNotContain("type", strip.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aggregate", strip.TextContent, StringComparison.OrdinalIgnoreCase);

        // What the strip is for stays: the pointers to somewhere else.
        Assert.Contains(".domain/capture/model.md#capture", strip.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_the_marker_does_not_know_keeps_its_row_exactly_as_it_was()
    {
        using var harness = CreateCardHarness();

        var component = harness.Render(ChapterView("policy-fragment"));

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll(".domain-section .domain-metadata")));

        var heading = component.Find(".domain-section__header h4");
        var strip = component.Find(".domain-section .domain-metadata");

        // No glyph, so the word must still be somewhere — and it is where it has
        // always been.
        Assert.Empty(heading.QuerySelectorAll("svg"));
        Assert.Equal("Inbox Item", heading.TextContent);
        Assert.Contains("type", strip.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("policy-fragment", strip.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_chapter_whose_only_field_is_a_marked_type_draws_no_empty_strip()
    {
        // The suppression has to reach the "is there anything to show?" question as
        // well as the loop that shows it, or a chapter stating nothing but its type
        // is left with an empty metadata row taking up a line.
        using var harness = CreateCardHarness();

        var component = harness.Render(ChapterView("aggregate", related: null));

        component.WaitForAssertion(() => Assert.Single(component.FindAll(".domain-section__header h4")));
        Assert.Empty(component.FindAll(".domain-section .domain-metadata"));
        Assert.Single(component.FindAll(".domain-section__header h4 svg"));
    }

    [Fact]
    public void The_document_header_marks_what_kind_of_file_it_is()
    {
        using var harness = CreateCardHarness();

        var component = harness.Render(ChapterView("aggregate"));

        component.WaitForAssertion(() => Assert.Single(component.FindAll(".domain-document__kind")));

        var kind = component.Find(".domain-document__kind");
        var mark = kind.QuerySelector("svg")!;

        // The file-type mark, derived from the document's kind rather than from a
        // second reading of its filename.
        Assert.Contains("knowledge-type-marker--model", mark.ClassList);

        // Unlabelled: the words are right there, and a mark announced beside its own
        // text label is the word twice.
        Assert.Equal("true", mark.GetAttribute("aria-hidden"));
        Assert.Equal("Structural model", kind.TextContent);

        // And the document's own `type: model` row went with it, for the same reason
        // a chapter's did.
        Assert.DoesNotContain(
            "model",
            component.Find(".domain-document > .domain-metadata").TextContent,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Domain_file_rows_in_the_knowledge_menu_carry_their_file_type()
    {
        using var context = new BunitContext();

        var root = DomainMenu();
        var component = context.Render<KnowledgeMenuTreeView>(parameters => parameters
            .Add(view => view.HeadingLabel, "Domain")
            .Add(view => view.RootFolder, root)
            .Add(view => view.Nodes, root.Children)
            .Add(view => view.IsNodeExpanded, node => node.Kind == KnowledgeMenuNodeKind.Folder));

        var marks = component.FindAll(".knowledge-menu__mark svg");

        // Decorative, unlike the chapter heading's. The row's visible label is the
        // same fact humanised, and `.design/accessibility.md` hides an icon that
        // sits beside a label rather than reading the word twice. The heading is
        // named because the row that stated the type there was taken away; nothing
        // was taken away here.
        Assert.All(marks, mark =>
        {
            Assert.Equal("true", mark.GetAttribute("aria-hidden"));
            Assert.Null(mark.GetAttribute("role"));
            Assert.Null(mark.GetAttribute("aria-label"));
            Assert.Empty(mark.QuerySelectorAll("title"));
        });

        // Seven files, seven marks, one each and in the order the rows are drawn —
        // and the folder row between them has none, because a folder has no file
        // type to state.
        Assert.Equal(
            ["context-map", "domain", "model", "features", "flow", "dependencies", "naming"],
            marks.Select(TypeOf));

        // And the row reads out exactly as it did before there were marks at all:
        // the mark is its own element beside the label, it is hidden, and with no
        // tooltip it brings no text with it. That is what let two navigation tests
        // that assert on a row's whole text stay as they were written.
        var contextMapRow = component.FindAll(".knowledge-menu__row button")[0];
        Assert.Equal("Context Map", contextMapRow.TextContent);
    }

    [Fact]
    public void Only_the_domain_area_gets_marks()
    {
        // `.tech` and `.design` write no `type` of this shape, and `.arc42` writes
        // filenames that mean something else entirely. A `model.md` under another
        // area must not pick up the domain model's mark on the strength of its name.
        using var context = new BunitContext();

        var root = new KnowledgeMenuNode("tech", "Technology", "tech", KnowledgeMenuNodeKind.Folder, "tech",
        [
            new KnowledgeMenuNode("model.md", "Model", "model.md", KnowledgeMenuNodeKind.File, "tech", [], true),
            new KnowledgeMenuNode("flow.md", "Flow", "flow.md", KnowledgeMenuNodeKind.File, "tech", [], true)
        ], true);

        var component = context.Render<KnowledgeMenuTreeView>(parameters => parameters
            .Add(view => view.HeadingLabel, "Technology")
            .Add(view => view.RootFolder, root)
            .Add(view => view.Nodes, root.Children));

        Assert.Empty(component.FindAll(".knowledge-menu__mark"));
    }

    [Fact]
    public async Task The_opened_document_marks_every_chapter_it_shows()
    {
        // The test the first pass at this needed and did not have. Everything above
        // asserts the card, which is what the panel draws for a document nobody
        // opened; opening one from the tree lands here instead, and the whole suite
        // was green while this branch drew no marks at all.
        await using var harness = CreateOpenHarness();

        var panel = harness.Open(DocumentPath);

        var marks = panel.FindAll(
            "[data-testid='domain-chapter-file'] .md-heading [data-testid='markdown-chapter-type-mark']");

        // Two of the three chapters. `policy-fragment` is not a value this library
        // draws, and the third mark's absence is the fallback holding rather than a
        // chapter being missed — see the test below, which finds its word.
        Assert.Equal(["aggregate", "term"], marks.Select(TypeOf));
    }

    [Fact]
    public async Task An_opened_chapters_heading_is_announced_with_its_type_and_still_reads_as_itself()
    {
        await using var harness = CreateOpenHarness();

        var panel = harness.Open(DocumentPath);

        // The `#` title is heading zero; the first `##` chapter is the one after it.
        var heading = panel.FindAll("[data-testid='domain-chapter-file'] .md-heading")[1];
        var mark = heading.QuerySelector("svg")!;

        Assert.Equal("img", mark.GetAttribute("role"));
        Assert.Equal("type: aggregate", mark.GetAttribute("aria-label"));
        Assert.Null(mark.GetAttribute("aria-hidden"));

        // No title element, so nothing was added to what the heading says.
        Assert.Empty(mark.QuerySelectorAll("title"));
        Assert.Equal("Inbox Item", heading.TextContent);
    }

    [Fact]
    public async Task The_opened_document_marks_what_kind_of_file_it_is_on_its_own_title()
    {
        // The file-level `type`, on the name in the file view's header — which is the
        // part of the pane that stays put while the chapters scroll past.
        await using var harness = CreateOpenHarness();

        var panel = harness.Open(DocumentPath);

        var name = panel.Find("[data-testid='domain-chapter-file'] h3.file-view__name");
        var mark = name.QuerySelector("[data-testid='domain-chapter-file-file-type-mark']")!;

        Assert.Equal("model", TypeOf(mark));
        Assert.Equal("type: model", mark.GetAttribute("aria-label"));
        Assert.Empty(mark.QuerySelectorAll("title"));

        // The header truncates this element, so anything the mark added to its text
        // would be eating the file's own name.
        Assert.Equal("Inbox", name.TextContent);
    }

    [Fact]
    public async Task An_opened_chapter_whose_type_nobody_drew_is_left_exactly_as_it_was()
    {
        await using var harness = CreateOpenHarness();

        var panel = harness.Open(DocumentPath);

        var heading = panel.FindAll("[data-testid='domain-chapter-file'] .md-heading")[3];

        Assert.Equal("Retention Policy", heading.TextContent);
        Assert.Empty(heading.QuerySelectorAll("svg"));

        // Its status is still drawn beside it, which is what says the chapter is
        // being read as a record at all rather than skipped.
        Assert.Equal(
            "draft",
            panel.FindAll("[data-testid='domain-chapter-file'] .knowledge-record__headline select")[2]
                .GetAttribute("value"));

        // What is *not* asserted here, deliberately: that the plain `type` row
        // survived. This panel passes RenderKnowledgeMetadataFields="false", so no
        // chapter on this surface draws any field but its status — recognised or
        // not, there is no row to keep. That rule belongs where the rows exist, and
        // KnowledgeTypeMarkerReadSurfaceTests is where it is pinned.
    }

    /// <summary>The `type` slug a rendered mark is drawing, read back off its
    /// modifier class.</summary>
    private static string TypeOf(AngleSharp.Dom.IElement mark) =>
        mark.ClassList
            .First(name => name.StartsWith("knowledge-type-marker--", StringComparison.Ordinal))
            ["knowledge-type-marker--".Length..];

    /// <summary>A `.domain` folder as the knowledge menu builds one: the context
    /// map at the root and one bounded context's seven files under it.</summary>
    private static KnowledgeMenuNode DomainMenu()
    {
        KnowledgeMenuNode File(string path, string label) =>
            new(path, label, path, KnowledgeMenuNodeKind.File, "domain", [], true);

        var inbox = new KnowledgeMenuNode("inbox", "Inbox", "inbox", KnowledgeMenuNodeKind.Folder, "domain",
        [
            File("inbox/domain.md", "Domain"),
            File("inbox/model.md", "Model"),
            File("inbox/features.md", "Features"),
            File("inbox/flow.md", "Flow"),
            File("inbox/dependencies.md", "Dependencies"),
            File("inbox/naming.md", "Naming")
        ], true);

        return new KnowledgeMenuNode("domain", "Domain", "domain", KnowledgeMenuNodeKind.Folder, "domain",
            [File("context-map.md", "Context Map"), inbox], true);
    }

    /// <summary>
    /// A document with one chapter in it, handed in rather than read: the strip and
    /// the heading are what is under test, and a view built here states them
    /// without a folder on disk having to spell the fence.
    /// <para>The document goes in the context-map slot because that is what the
    /// panel draws when nothing is selected, which is the read view — the one that
    /// assembles a heading, a metadata strip and a section list out of the parse.
    /// A selected chapter is shown through the file view instead and draws none of
    /// them.</para>
    /// </summary>
    private static DomainKnowledgeView ChapterView(string chapterType, string? related = ".domain/capture/model.md#capture")
    {
        var absent = Path.Combine(Path.GetTempPath(), "backlog-domain-type-marker", Guid.NewGuid().ToString("N"));

        var chapterMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["type"] = chapterType };
        if (related is not null) chapterMetadata["related"] = $"[{related}]";

        var chapter = new DomainKnowledgeSection(
            "Inbox Item",
            2,
            "draft",
            chapterMetadata,
            string.Empty,
            [],
            [],
            $"{DocumentPath}#inbox-item");

        var document = new DomainKnowledgeDocument(
            DocumentPath,
            "Inbox",
            DomainKnowledgeDocumentKind.Model,
            "draft",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = "model",
                ["related"] = "[.domain/inbox/domain.md]"
            },
            "A summary nobody can edit.",
            [],
            [chapter],
            []);

        return new DomainKnowledgeView("JSdotNet/Backlog", absent, Path.Combine(absent, ".domain"), null, document, []);
    }

    /// <summary>
    /// The panel with a real `.domain` folder behind it and a file selected — which
    /// is what the knowledge tree produces on every open, and the branch the first
    /// pass at these marks never reached.
    /// <para>A folder on disk rather than a view handed in, because that is what it
    /// takes: the panel only treats a document as the open one once it has read the
    /// chapter's text off the file, and it is the file's own text that the file view
    /// renders. A fabricated view leaves the panel drawing the card.</para>
    /// </summary>
    private OpenHarness CreateOpenHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-type-marker-open", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "inbox"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), ContextMapFile);
        File.WriteAllText(Path.Combine(root, ".domain", "inbox", "domain.md"), DomainFile);
        File.WriteAllText(Path.Combine(root, ".domain", "inbox", "model.md"), ModelFile);

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            KnowledgeFolders = KnowledgeFolderSetting.Defaults()
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHub, settings));
        context.Services.AddSingleton(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<KnowledgeChapterWriter>();
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());

        return new OpenHarness(context, repository.Alias);
    }

    private Harness CreateCardHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-type-marker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));

        var context = new BunitContext();

        // The markdown editor watches its textarea through interop for the
        // highlight layer, which is not what any of this is about.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHub, settings));
        context.Services.AddSingleton(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<KnowledgeChapterWriter>();
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());

        return new Harness(context);
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Harness(BunitContext Context) : IDisposable
    {
        /// <summary>The panel with a view handed in and nothing selected, which is
        /// the folder overview — the card branch, where a document nobody opened is
        /// summarised out of this panel's own parse.</summary>
        public IRenderedComponent<DomainKnowledgePanel> Render(DomainKnowledgeView view) =>
            Context.Render<DomainKnowledgePanel>(parameters => parameters.Add(panel => panel.View, view));

        public void Dispose() => Context.Dispose();
    }

    /// <summary>The panel reading a real folder, with one file selected — the branch
    /// every route through the knowledge tree lands on.</summary>
    private sealed record OpenHarness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<DomainKnowledgePanel> Open(string selectedPath)
        {
            var panel = Context.Render<DomainKnowledgePanel>(parameters => parameters
                .Add(view => view.RepositoryAlias, RepositoryAlias)
                .Add(view => view.SelectedPath, selectedPath));

            // The chapter is read off disk after the first render, so nothing about
            // the open document is on screen until the file view is. Waiting on the
            // file view rather than on a mark, so a run with no marks at all fails
            // as a missing assertion rather than as a timeout.
            panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-testid='domain-chapter-file']")));
            return panel;
        }

        /// <summary>Awaited, for the reason <c>DomainKnowledgePanelTests</c> records:
        /// the panel hands work to the renderer's dispatcher on the way out, and the
        /// folder delete that follows must not race it.</summary>
        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
