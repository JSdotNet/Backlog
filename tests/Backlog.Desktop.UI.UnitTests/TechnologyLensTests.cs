using Backlog.Modules.Devbook.Abstractions;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The type lenses (<c>.devbook/domain/devbook/features.md#technology-view</c>,
/// The inspector and its lenses; <c>requirements.md</c>, A lens shows only what
/// the chapter and the graph record): each lens's rows, and the rule that a row
/// whose fact the chapter does not record is not drawn — nor a lens left with no
/// row. Then the Type lenses board: a card per type in use, its most-used
/// technology, and the lens-per-type table.
/// <para>
/// The folder is built by hand, so each test states exactly the metadata it reads.
/// Graph ids are spelled <c>.tech/…</c>, as the reader spells them.
/// </para>
/// </summary>
public sealed class TechnologyLensTests : IDisposable
{
    private readonly BunitContext _context = new();

    [Fact]
    public void A_package_shows_its_version_and_alternatives()
    {
        var view = Folder(Tech("mcp", "package", version: "2.2.0", alternatives: ["Raw JSON-RPC", "A hand-rolled host"]));

        var lens = RenderLens(view, "mcp");

        Assert.Equal("package", lens.Find("[data-testid='technology-lens']").GetAttribute("data-lens"));
        Assert.Equal(["version", "alternatives"], Rows(lens));
        Assert.Equal("2.2.0", RowText(lens, "version"));
        Assert.Equal("Raw JSON-RPC, A hand-rolled host", RowText(lens, "alternatives"));
    }

    [Fact]
    public void A_package_with_no_version_shows_no_version_line()
    {
        var view = Folder(Tech("mcp", "package", alternatives: ["Raw JSON-RPC"]));

        var lens = RenderLens(view, "mcp");

        Assert.Equal(["alternatives"], Rows(lens));
        Assert.DoesNotContain("Version", lens.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_with_no_alternatives_shows_only_its_version()
    {
        var view = Folder(Tech("mcp", "package", version: "2.2.0"));

        var lens = RenderLens(view, "mcp");

        Assert.Equal(["version"], Rows(lens));
    }

    [Fact]
    public void A_package_that_records_neither_fact_draws_no_lens()
    {
        var view = Folder(Tech("mcp", "package"));

        var lens = RenderLens(view, "mcp");

        Assert.Empty(lens.FindAll("[data-testid='technology-lens']"));
        Assert.Equal(string.Empty, lens.Markup.Trim());
    }

    [Theory]
    [InlineData("framework")]
    [InlineData("platform")]
    [InlineData("runtime")]
    [InlineData("language")]
    public void A_stack_type_shows_what_it_is_built_on_and_what_is_built_on_it_by_layer_and_its_decisions_and_chapters(string type)
    {
        var view = Folder(
            Tech("net", "runtime", layer: "Shared Technologies"),
            Tech("razor", "framework", layer: "Shared Technologies"),
            Tech("maui", type, layer: "Desktop Stack", dependsOn: ["net", "razor"],
                related: [".devbook/arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md", ".devbook/arc42/04-solution-strategy.md#technology-choices"]),
            Tech("blazor-hybrid", "framework", layer: "Desktop Stack", dependsOn: ["maui"]),
            Tech("android", "platform", layer: "Mobile Stack", dependsOn: ["maui"]));

        var lens = RenderLens(view, "maui");

        Assert.Equal("stack", lens.Find("[data-testid='technology-lens']").GetAttribute("data-lens"));
        Assert.Equal(["built-on", "built-on-it", "decisions", "chapters"], Rows(lens));
        Assert.Equal(["Shared"], Layers(lens, "built-on"));
        Assert.Equal(["net", "razor"], Technologies(lens, "built-on"));
        Assert.Equal(["Desktop", "Mobile"], Layers(lens, "built-on-it"));
        Assert.Equal(["blazor-hybrid", "android"], Technologies(lens, "built-on-it"));
        Assert.Equal("ADR 0001 desktop stack maui blazor hybrid", RowText(lens, "decisions"));
        Assert.Equal("technology choices", RowText(lens, "chapters"));
    }

    [Fact]
    public void A_stack_type_that_nothing_is_built_on_shows_only_what_it_is_built_on()
    {
        var view = Folder(
            Tech("net", "runtime"),
            Tech("razor", "framework", dependsOn: ["net"]));

        var lens = RenderLens(view, "razor");

        Assert.Equal(["built-on"], Rows(lens));
    }

    /// <summary>A depends-on naming nothing the graph holds is not a technology to
    /// show, so the row it would have filled is not drawn empty.</summary>
    [Fact]
    public void A_stack_type_whose_neighbours_do_not_resolve_draws_no_row_for_them()
    {
        var view = Folder(Tech("razor", "framework", dependsOn: ["gone"]));

        var lens = RenderLens(view, "razor");

        Assert.Empty(lens.FindAll("[data-testid='technology-lens']"));
    }

    [Fact]
    public void A_stack_type_with_nothing_related_shows_only_the_graph_rows_it_has()
    {
        var view = Folder(
            Tech("net", "runtime"),
            Tech("razor", "framework", dependsOn: ["net"]));

        var lens = RenderLens(view, "net");

        Assert.Equal(["built-on-it"], Rows(lens));
    }

    [Theory]
    [InlineData("plugin")]
    [InlineData("skill")]
    [InlineData("agent")]
    public void An_ai_type_shows_its_ai_chapters_its_other_chapters_and_what_uses_it(string type)
    {
        var view = Folder(
            Tech("skills", type, related: [".devbook/ai/delivery.md#flows", ".devbook/arc42/08-crosscutting-concepts.md#agents"]),
            Tech("backlog-tools", "plugin", dependsOn: ["skills"]));

        var lens = RenderLens(view, "skills");

        Assert.Equal("ai", lens.Find("[data-testid='technology-lens']").GetAttribute("data-lens"));
        Assert.Equal(["ai-chapters", "chapters", "used-by"], Rows(lens));
        Assert.Equal("flows", RowText(lens, "ai-chapters"));
        Assert.Equal("agents", RowText(lens, "chapters"));
        Assert.Equal(["backlog-tools"], Technologies(lens, "used-by"));
    }

    [Fact]
    public void An_ai_type_nothing_uses_with_only_other_chapters_shows_only_those()
    {
        var view = Folder(Tech("skills", "skill", related: [".devbook/arc42/08-crosscutting-concepts.md#agents"]));

        var lens = RenderLens(view, "skills");

        Assert.Equal(["chapters"], Rows(lens));
    }

    [Fact]
    public void An_ai_type_that_relates_to_nothing_shows_only_what_uses_it()
    {
        var view = Folder(
            Tech("skills", "skill"),
            Tech("backlog-tools", "plugin", dependsOn: ["skills"]));

        var lens = RenderLens(view, "skills");

        Assert.Equal(["used-by"], Rows(lens));
    }

    [Fact]
    public void A_tool_shows_its_alternatives_and_what_it_relates_to()
    {
        var view = Folder(
            Tech("gh", "tool", alternatives: ["Octokit.NET", "raw REST calls"],
                related: [".devbook/tech/shared.md#actions", ".devbook/arc42/08-crosscutting-concepts.md#github"]),
            Tech("actions", "tool"));

        var lens = RenderLens(view, "gh");

        Assert.Equal(["alternatives", "related"], Rows(lens));
        Assert.Equal("Octokit.NET, raw REST calls", RowText(lens, "alternatives"));
        Assert.Equal(["actions"], Technologies(lens, "related"));
        Assert.Equal(["github"], References(lens, "related"));
    }

    [Fact]
    public void A_tool_that_relates_to_nothing_shows_only_its_alternatives()
    {
        var view = Folder(Tech("gh", "tool", alternatives: ["Octokit.NET"]));

        var lens = RenderLens(view, "gh");

        Assert.Equal(["alternatives"], Rows(lens));
    }

    /// <summary>A related entry that names no chapter in a section stays the text
    /// the author wrote, never a control that goes nowhere.</summary>
    [Fact]
    public void A_related_entry_that_names_no_chapter_is_inert_text()
    {
        var view = Folder(Tech("gh", "tool", related: ["the GitHub REST docs"]));

        var lens = RenderLens(view, "gh");

        Assert.Equal("the GitHub REST docs", RowText(lens, "related"));
        Assert.Empty(lens.FindAll("button"));
        Assert.Single(lens.FindAll("[data-testid='technology-lens-text']"));
    }

    [Fact]
    public void A_tool_with_no_alternatives_shows_only_what_it_relates_to()
    {
        var view = Folder(Tech("gh", "tool", related: [".devbook/arc42/08-crosscutting-concepts.md#github"]));

        var lens = RenderLens(view, "gh");

        Assert.Equal(["related"], Rows(lens));
    }

    [Fact]
    public void A_service_shows_what_it_relates_to_and_its_layer()
    {
        var view = Folder(Tech("cosmos", "service", layer: "Cloud Technologies", related: [".devbook/arc42/07-deployment-view.md#sync"]));

        var lens = RenderLens(view, "cosmos");

        Assert.Equal(["related", "layer"], Rows(lens));
        Assert.Equal("sync", RowText(lens, "related"));
        Assert.Equal("Cloud", RowText(lens, "layer"));
    }

    [Fact]
    public void A_service_that_relates_to_nothing_shows_only_its_layer()
    {
        var view = Folder(Tech("cosmos", "service", layer: "Cloud Technologies"));

        var lens = RenderLens(view, "cosmos");

        Assert.Equal(["layer"], Rows(lens));
    }

    [Theory]
    [InlineData("library")]
    [InlineData("format")]
    [InlineData("protocol")]
    [InlineData("an-unknown-word")]
    public void A_common_facts_type_draws_no_lens_whatever_its_chapter_records(string type)
    {
        var view = Folder(
            Tech("thing", type, version: "1.0.0", alternatives: ["Other"], related: [".devbook/arc42/04-solution-strategy.md#technology-choices"]),
            Tech("user", "tool", dependsOn: ["thing"]));

        var lens = RenderLens(view, "thing");

        Assert.Empty(lens.FindAll("[data-testid='technology-lens']"));
    }

    [Fact]
    public void A_technology_in_a_lens_is_selected_and_a_chapter_is_followed()
    {
        var view = Folder(
            Tech("gh", "tool", related: [".devbook/tech/shared.md#actions", ".devbook/arc42/08-crosscutting-concepts.md#github"]),
            Tech("actions", "tool"));
        string? selected = null;
        DevbookChapterLink? opened = null;

        var lens = _context.Render<TechnologyLens>(parameters => parameters
            .Add(component => component.View, view)
            .Add(component => component.Technology, view.Graph.Nodes.Single(node => node.Id == Id("gh")))
            .Add(component => component.SelectedIdChanged, id => selected = id)
            .Add(component => component.OnNavigateToChapter, link => opened = link));

        lens.Find("[data-testid='technology-lens-technology']").Click();
        lens.Find("[data-testid='technology-lens-reference']").Click();

        Assert.Equal(Id("actions"), selected);
        Assert.NotNull(opened);
        Assert.Equal("arc42", opened.AreaKey);
        Assert.Equal("github", opened.Anchor);
    }

    [Fact]
    public void The_board_shows_one_card_per_type_in_use_with_its_most_used_technology()
    {
        var view = Folder(
            Tech("net", "runtime"),
            Tech("razor", "framework", dependsOn: ["net"]),
            Tech("blazor", "framework", dependsOn: ["razor", "net"]),
            Tech("hybrid", "framework", dependsOn: ["blazor"]),
            Tech("bunit", "package", version: "2.4.1", dependsOn: ["blazor"]),
            Tech("json", "format"));

        var board = RenderBoard(view);

        var cards = board.FindAll("[data-testid='technology-lens-card']");
        Assert.Equal(["framework", "format", "package", "runtime"], cards.Select(card => card.GetAttribute("data-type")).ToArray());

        var names = cards.ToDictionary(card => card.GetAttribute("data-type")!, card => card.QuerySelector("[data-testid='technology-lens-card-name']")!.TextContent.Trim());
        Assert.Equal("blazor", names["framework"]);
        Assert.Equal("bunit", names["package"]);

        var package = cards.Single(card => card.GetAttribute("data-type") == "package");
        Assert.Equal("PKG", package.QuerySelector(".tech-lens-card__kicker .badge")!.TextContent.Trim());
        Assert.Contains("Package lens", package.QuerySelector(".tech-lens-card__kicker")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("Sits on 1 · used by 0", package.QuerySelector("[data-testid='technology-lens-card-facts']")!.TextContent.Trim());
        Assert.Equal("package", package.QuerySelector("[data-testid='technology-lens']")!.GetAttribute("data-lens"));

        var format = cards.Single(card => card.GetAttribute("data-type") == "format");
        Assert.Null(format.QuerySelector("[data-testid='technology-lens']"));
    }

    [Fact]
    public void The_board_table_says_what_each_lens_shows_and_reads()
    {
        var board = RenderBoard(Folder(Tech("net", "runtime")));

        var rows = board.FindAll("[data-testid='technology-lens-table-table'] tbody tr")
            .Select(row => row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToArray())
            .ToArray();

        Assert.Equal(
            ["package", "framework · platform · runtime · language", "plugin · skill · agent", "tool", "service", "library · format · protocol"],
            rows.Select(row => row[0]).ToArray());
        Assert.Equal("version, alternatives", rows[0][2]);
        Assert.Equal("depends-on, related", rows[1][2]);
        Assert.Equal("alternatives, related", rows[3][2]);
    }

    [Fact]
    public void A_card_opens_its_technology_selected_in_the_overview()
    {
        var view = Folder(Tech("net", "runtime"));
        string? selected = null;
        string? opened = null;

        var board = _context.Render<TechnologyTypeLensesView>(parameters => parameters
            .Add(component => component.View, view)
            .Add(component => component.SelectedIdChanged, id => selected = id)
            .Add(component => component.OnOpenView, viewId => opened = viewId));

        board.Find("[data-testid='technology-lens-card-name']").Click();

        Assert.Equal(Id("net"), selected);
        Assert.Equal(TechnologyViews.Overview, opened);
    }

    [Fact]
    public void A_technology_a_card_lens_names_opens_selected_in_the_overview()
    {
        var view = Folder(
            Tech("net", "runtime"),
            Tech("razor", "framework", dependsOn: ["net"]));
        var selected = new List<string?>();
        string? opened = null;

        var board = _context.Render<TechnologyTypeLensesView>(parameters => parameters
            .Add(component => component.View, view)
            .Add(component => component.SelectedIdChanged, id => selected.Add(id))
            .Add(component => component.OnOpenView, viewId => opened = viewId));

        board.Find("[data-testid='technology-lens-card'][data-type='runtime'] [data-testid='technology-lens-technology']").Click();

        Assert.Equal([Id("razor")], selected);
        Assert.Equal(TechnologyViews.Overview, opened);
    }

    /// <summary>The shared selection wins on its own type's card — a package fewer
    /// use than bUnit — and only there: every other card keeps its most-used one.</summary>
    [Fact]
    public void The_selected_technologys_type_card_shows_it_and_says_it_is_the_selection()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        var view = BoardFolder();

        var board = RenderBoard(view, selectedId: Id("polly"));

        var package = Card(board, "package");
        Assert.Equal("polly", CardName(package));
        Assert.Equal("true", package.GetAttribute("aria-current"));
        Assert.Contains("tech-lens-card--selected", package.ClassList);
        Assert.NotNull(package.QuerySelector("[data-testid='technology-lens-card-selected']"));
        Assert.Equal("Sits on 1 · used by 0", package.QuerySelector("[data-testid='technology-lens-card-facts']")!.TextContent.Trim());
        Assert.Contains("8.4.0", package.QuerySelector("[data-testid='technology-lens']")!.TextContent, StringComparison.Ordinal);

        var framework = Card(board, "framework");
        Assert.Equal("blazor", CardName(framework));
        Assert.Null(framework.GetAttribute("aria-current"));
        Assert.Single(board.FindAll("[data-testid='technology-lens-card'][aria-current]"));
    }

    [Fact]
    public void With_nothing_selected_every_card_shows_its_most_used_technology_and_none_is_marked()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        var board = RenderBoard(BoardFolder());

        Assert.Equal("bunit", CardName(Card(board, "package")));
        Assert.Equal("blazor", CardName(Card(board, "framework")));
        Assert.Empty(board.FindAll("[data-testid='technology-lens-card'][aria-current]"));
        Assert.Empty(board.FindAll("[data-testid='technology-lens-card-selected']"));
        Assert.Empty(_context.JSInterop.Invocations["backlogRevealRow"]);
    }

    [Fact]
    public void The_board_brings_the_selected_card_into_view_and_focuses_it_only_when_a_link_opened_it()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        var view = BoardFolder();

        RenderBoard(view, selectedId: Id("polly"));

        Assert.Equal(
            "[data-testid='technology-lens-card'][aria-current='true']",
            Assert.Single(_context.JSInterop.Invocations["backlogRevealRow"]).Arguments[0]);
        Assert.Empty(_context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);

        RenderBoard(view, selectedId: Id("polly"), focusSelection: true);

        Assert.Equal(2, _context.JSInterop.Invocations["backlogRevealRow"].Count);
        Assert.Single(_context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
    }

    /// <summary>A technology with no type has no card on the board, so the
    /// inspector offers no lens link to land on nothing.</summary>
    [Fact]
    public void The_inspector_offers_the_lens_link_only_for_a_type_the_board_has_a_card_for()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        var view = Folder(Tech("net", "runtime"), Tech("untyped", " "));

        IRenderedComponent<TechnologyInspector> Inspect(string slug) =>
            _context.Render<TechnologyInspector>(parameters => parameters
                .Add(component => component.View, view)
                .Add(component => component.SelectedId, Id(slug)));

        Assert.Equal("Open runtime lens", Inspect("net").Find("[data-testid='technology-inspector-open-lens']").TextContent.Trim());
        Assert.Empty(Inspect("untyped").FindAll("[data-testid='technology-inspector-open-lens']"));
    }

    public void Dispose() => _context.Dispose();

    private static TechnologyDevbookView BoardFolder() => Folder(
        Tech("net", "runtime"),
        Tech("razor", "framework", dependsOn: ["net"]),
        Tech("blazor", "framework", dependsOn: ["razor", "net"]),
        Tech("hybrid", "framework", dependsOn: ["blazor"]),
        Tech("bunit", "package", version: "2.4.1", dependsOn: ["blazor"]),
        Tech("polly", "package", version: "8.4.0", dependsOn: ["net"]),
        Tech("tests", "tool", dependsOn: ["bunit"]));

    private static AngleSharp.Dom.IElement Card(IRenderedComponent<TechnologyTypeLensesView> board, string type) =>
        board.Find($"[data-testid='technology-lens-card'][data-type='{type}']");

    private static string CardName(AngleSharp.Dom.IElement card) =>
        card.QuerySelector("[data-testid='technology-lens-card-name']")!.TextContent.Trim();

    private IRenderedComponent<TechnologyLens> RenderLens(TechnologyDevbookView view, string slug) =>
        _context.Render<TechnologyLens>(parameters => parameters
            .Add(component => component.View, view)
            .Add(component => component.Technology, view.Graph.Nodes.Single(node => node.Id == Id(slug))));

    private IRenderedComponent<TechnologyTypeLensesView> RenderBoard(TechnologyDevbookView view, string? selectedId = null, bool focusSelection = false) =>
        _context.Render<TechnologyTypeLensesView>(parameters => parameters
            .Add(component => component.View, view)
            .Add(component => component.SelectedId, selectedId)
            .Add(component => component.FocusSelection, focusSelection));

    private static string[] Rows(IRenderedComponent<TechnologyLens> lens) =>
        [.. lens.FindAll("[data-testid='technology-lens-row']").Select(row => row.GetAttribute("data-row")!)];

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<TechnologyLens> lens, string row) =>
        lens.Find($"[data-testid='technology-lens-row'][data-row='{row}']");

    private static string RowText(IRenderedComponent<TechnologyLens> lens, string row) =>
        Row(lens, row).QuerySelector("dd")!.TextContent.Trim();

    private static string[] Layers(IRenderedComponent<TechnologyLens> lens, string row) =>
        [.. Row(lens, row).QuerySelectorAll("[data-testid='technology-lens-layer']").Select(group => group.GetAttribute("data-layer")!)];

    private static string[] Technologies(IRenderedComponent<TechnologyLens> lens, string row) =>
        [.. Row(lens, row).QuerySelectorAll("[data-testid='technology-lens-technology']").Select(button => button.TextContent.Trim())];

    private static string[] References(IRenderedComponent<TechnologyLens> lens, string row) =>
        [.. Row(lens, row).QuerySelectorAll("[data-testid='technology-lens-reference']").Select(link => link.TextContent.Trim())];

    private static string Id(string slug) => $".tech/shared.md#{slug}";

    private sealed record TechSpec(
        string Slug,
        string Type,
        string Layer,
        string? Version,
        IReadOnlyList<string> DependsOn,
        IReadOnlyList<string> Related,
        IReadOnlyList<string> Alternatives);

    private static TechSpec Tech(
        string slug,
        string type,
        string layer = "Shared Technologies",
        string? version = null,
        IReadOnlyList<string>? dependsOn = null,
        IReadOnlyList<string>? related = null,
        IReadOnlyList<string>? alternatives = null) =>
        new(slug, type, layer, version, dependsOn ?? [], related ?? [], alternatives ?? []);

    /// <summary>A folder of these technologies, every chapter in one layer file so
    /// the ids stay predictable, with each node's neighbours and use counts worked
    /// out from the depends-on lists the way the reader's measures count them.</summary>
    private static TechnologyDevbookView Folder(params TechSpec[] specs)
    {
        var chapters = specs.Select(spec => new TechnologyNode(
            Id(spec.Slug),
            spec.Slug,
            "shared.md",
            spec.Layer,
            new DevbookMetadata("adopted", spec.Type, spec.Version, [.. spec.DependsOn.Select(Id)], spec.Related, spec.Alternatives),
            string.Empty,
            [.. spec.DependsOn.Select(Id)],
            spec.Related,
            spec.Alternatives)).ToList();

        var nodes = specs.Select(spec =>
        {
            var users = specs.Where(other => other.DependsOn.Contains(spec.Slug)).Select(other => Id(other.Slug)).ToList();
            return new TechnologyGraphNode(Id(spec.Slug), spec.Slug, spec.Layer, spec.Type, "adopted", string.Empty, "shared.md")
            {
                DependencyIds = [.. spec.DependsOn.Select(Id)],
                UserIds = users,
                UsedBy = users.Count,
            };
        }).ToList();

        var countsByType = specs
            .GroupBy(spec => spec.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return new TechnologyDevbookView(
            new DevbookFolderLocation("tech", true, null, "JSdotNet/Backlog", null, null),
            "Technology",
            string.Empty,
            [new TechnologyLayer("shared.md", "Shared Technologies", DevbookMetadata.Empty, chapters)],
            [],
            [],
            new TechnologyGraphData(nodes, []) { CountsByType = countsByType },
            TechnologyGraphStats.Empty);
    }
}
