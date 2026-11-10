namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The C4 zoom explorer drawn as the architecture surface (plan c4-zoom-navigation):
/// handed a Navigation, it lays out the bar across the top and the host's navigation,
/// the picture and the inspector under it; handed a Document, the document stands in
/// for the picture while the bar stays and a press on it hands the reader back.
/// </summary>
public sealed class C4ZoomSurfaceTests : IDisposable
{
    private const string Attach = "backlogC4Zoom.attach";

    private readonly BunitContext _context = new();

    public C4ZoomSurfaceTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.JSInterop.Setup<bool>(Attach, _ => true).SetResult(true);
    }

    public void Dispose() => _context.Dispose();

    private static C4Workspace Workspace => C4ZoomNavigationTests.Workspace;

    private static readonly RenderFragment Nav = builder => builder.AddMarkupContent(0, "<p data-testid='host-nav'>Views</p>");
    private static readonly RenderFragment Doc = builder => builder.AddMarkupContent(0, "<p data-testid='host-doc'>Chapter 05</p>");

    private IRenderedComponent<C4ZoomExplorer> Render(
        RenderFragment? document = null,
        Action? navigated = null,
        List<C4ZoomLocation>? locations = null,
        C4ZoomOpen? open = null,
        C4ZoomCode? code = null,
        Func<string, C4ElementRecords>? recordsFor = null,
        Action<string>? opened = null) =>
        _context.Render<C4ZoomExplorer>(parameters =>
        {
            parameters
                .Add(c => c.Workspace, Workspace)
                .Add(c => c.HintDismissed, true)
                .Add(c => c.Navigation, Nav)
                .Add(c => c.Document, document)
                .Add(c => c.OpenView, open)
                .Add(c => c.Code, code)
                .Add(c => c.RecordsFor, recordsFor)
                .Add(c => c.ViewCaption, view => builder => builder.AddContent(0, $" · documented by {view.Key}"));

            if (navigated is not null) parameters.Add(c => c.OnNavigate, EventCallback.Factory.Create(this, navigated));
            if (opened is not null) parameters.Add(c => c.OnOpenRecord, opened);
            if (locations is not null) parameters.Add(c => c.OnLocationChanged, EventCallback.Factory.Create<C4ZoomLocation>(this, locations.Add));
        });

    [Fact]
    public void With_a_navigation_it_draws_the_bar_over_the_navigation_the_picture_and_the_inspector()
    {
        var explorer = Render();

        Assert.Single(explorer.FindAll(".c4-zoom-explorer--surface"));
        var surface = explorer.Find("[data-testid='c4-zoom-surface']");
        Assert.Equal(
            ["c4-zoom-explorer__nav", "c4-zoom-explorer__main", "c4-zoom-explorer__aside"],
            surface.Children.Select(child => child.ClassList[0]));
        Assert.Single(surface.QuerySelectorAll("[data-testid='host-nav']"));
        Assert.Single(explorer.FindAll("[data-testid='c4-zoom-bar']"));

        // Nothing selected: the right-hand column says what the open view is.
        Assert.Single(explorer.Find("[data-testid='c4-zoom-aside']").QuerySelectorAll("[data-testid='c4-zoom-view-summary']"));
    }

    [Fact]
    public void The_view_heading_carries_the_title_the_key_the_hosts_caption_and_the_scale_controls()
    {
        var explorer = Render();

        Assert.Equal("H2", explorer.Find("[data-testid='c4-zoom-title']").TagName);
        Assert.Contains("documented by context-backlog", explorer.Find("[data-testid='c4-zoom-key-line']").TextContent, StringComparison.Ordinal);
        Assert.Single(explorer.FindAll("[data-testid='c4-zoom-scale-out']"));
        Assert.Single(explorer.FindAll("[data-testid='c4-zoom-scale-fit']"));
        Assert.Single(explorer.FindAll("[data-testid='c4-zoom-scale-in']"));
    }

    [Fact]
    public void Plus_and_minus_size_the_picture_and_Fit_puts_it_back()
    {
        var explorer = Render();

        Assert.Equal(100, explorer.Instance.ScalePercent);
        Assert.Null(explorer.Find(".c4-zoom-explorer__canvas").GetAttribute("style"));

        explorer.Find("[data-testid='c4-zoom-scale-in']").Click();
        explorer.Find("[data-testid='c4-zoom-scale-in']").Click();
        Assert.Equal(150, explorer.Instance.ScalePercent);
        Assert.Contains("width: 150%", explorer.Find(".c4-zoom-explorer__canvas").GetAttribute("style"), StringComparison.Ordinal);

        explorer.Find("[data-testid='c4-zoom-scale-fit']").Click();
        Assert.Equal(100, explorer.Instance.ScalePercent);

        for (var press = 0; press < 5; press++) explorer.Find("[data-testid='c4-zoom-scale-out']").Click();
        Assert.Equal(50, explorer.Instance.ScalePercent);
        Assert.True(explorer.Find("[data-testid='c4-zoom-scale-out']").HasAttribute("disabled"));
    }

    [Fact]
    public void A_document_stands_in_for_the_picture_and_the_inspector_while_the_bar_stays_with_no_step_current()
    {
        var explorer = Render(document: Doc);

        Assert.Single(explorer.FindAll("[data-testid='host-doc']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-frame']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-aside']"));
        Assert.Single(explorer.FindAll("[data-testid='c4-zoom-bar']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-step'][aria-current]"));
    }

    [Fact]
    public void A_press_on_the_bar_with_a_document_open_tells_the_host_and_opens_that_level()
    {
        var navigated = 0;
        var explorer = Render(document: Doc, navigated: () => navigated++);

        // The step the reader came from, no longer marked current, takes them back.
        explorer.FindAll("[data-testid='c4-zoom-step']").Single(step => step.GetAttribute("data-level") == "1").Click();
        Assert.Equal(1, navigated);
        Assert.Equal("context-backlog", explorer.Instance.ViewKey);

        explorer.FindAll("[data-testid='c4-zoom-step']").Single(step => step.GetAttribute("data-level") == "2").Click();
        Assert.Equal(2, navigated);
        Assert.Equal("containers-backlog", explorer.Instance.ViewKey);

        explorer.FindAll("[data-testid='c4-zoom-also-link']").Single(link => link.TextContent == "Landscape").Click();
        Assert.Equal(3, navigated);
        Assert.Equal("landscape", explorer.Instance.ViewKey);
    }

    [Fact]
    public async Task The_host_is_told_where_the_explorer_is_once_per_move()
    {
        var locations = new List<C4ZoomLocation>();
        var explorer = Render(locations: locations);

        explorer.WaitForAssertion(() => Assert.Equal("context-backlog", Assert.Single(locations).ViewKey));
        Assert.Equal("System context", locations[0].Title);

        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(C4MermaidWriter.AliasOf("backlog"), "context-backlog"));

        explorer.WaitForAssertion(() => Assert.Equal(["context-backlog", "containers-backlog"], locations.Select(location => location.ViewKey)));
    }

    [Fact]
    public void A_request_to_open_a_code_level_opens_it_over_its_components_view()
    {
        const string Devbook = "backlog.desktop.devbook";
        var locations = new List<C4ZoomLocation>();
        var code = new C4ZoomCode(
            elementId => string.Equals(elementId, Devbook, StringComparison.OrdinalIgnoreCase),
            (_, _) => Task.FromResult(C4CodeViewTests.Level));

        var explorer = Render(locations: locations, open: new C4ZoomOpen("components-desktop", Devbook, OpenCode: true), code: code);

        Assert.Equal(Devbook, explorer.Instance.CodeComponentId);
        Assert.Equal("components-desktop", explorer.Instance.ViewKey);
        explorer.WaitForAssertion(() => Assert.Equal("Devbook", locations[^1].CodeComponent));
    }

    /// <summary>The Code level keeps what every other level has on the surface: the
    /// host's navigation, headed by its find field, beside it, and − / Fit / + over its
    /// picture — its own, sizing the class boxes.</summary>
    [Fact]
    public void The_code_level_keeps_the_navigation_beside_it_and_its_own_scale_over_it()
    {
        const string Devbook = "backlog.desktop.devbook";
        var code = new C4ZoomCode(
            elementId => string.Equals(elementId, Devbook, StringComparison.OrdinalIgnoreCase),
            (_, _) => Task.FromResult(C4CodeViewTests.Level));

        var explorer = Render(open: new C4ZoomOpen("components-desktop", Devbook, OpenCode: true), code: code);
        explorer.WaitForAssertion(() => Assert.Equal(3, explorer.FindAll("[data-testid='c4-code-type']").Count));

        var surface = explorer.Find("[data-testid='c4-zoom-surface']");
        Assert.Single(surface.QuerySelectorAll(".c4-zoom-explorer__nav [data-testid='host-nav']"));
        Assert.Single(surface.QuerySelectorAll(".c4-zoom-explorer__main [data-testid='c4-zoom-code']"));
        Assert.Single(surface.QuerySelectorAll(".c4-zoom-explorer__main [data-testid='c4-code-panel']"));
        Assert.Single(explorer.FindAll("[data-testid='c4-code-scale']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-scale']"));

        // The type panel is the level's own right-hand column: no aside, so no view
        // summary or component inspector beside the selected type, and the level
        // takes both columns.
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-aside']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-view-summary']"));
        Assert.Contains("c4-zoom-explorer__surface--code", surface.ClassList);
    }

    /// <summary>The explorer asks for edge ends, so its viewer can light the selected
    /// card's relationships; the classic C4 tab never does (C4SvgWriterTests).</summary>
    [Fact]
    public async Task The_picture_names_the_ends_of_its_lines_so_the_selected_cards_can_be_lit()
    {
        var explorer = Render();
        await explorer.InvokeAsync(() => explorer.Instance.ZoomIn(C4MermaidWriter.AliasOf("backlog"), "context-backlog"));

        var diagram = explorer.Find("[data-testid='c4-zoom-diagram']").InnerHtml;
        Assert.Contains($"data-c4-from=\"{C4MermaidWriter.AliasOf("backlog.desktop")}\"", diagram, StringComparison.Ordinal);
        Assert.Contains("-arrow-related\"", diagram, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_navigation_the_explorer_draws_as_the_zoom_tab_always_did()
    {
        var explorer = _context.Render<C4ZoomExplorer>(parameters => parameters
            .Add(c => c.Workspace, Workspace)
            .Add(c => c.HintDismissed, true));

        Assert.Empty(explorer.FindAll(".c4-zoom-explorer--surface"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-scale']"));
        Assert.Equal("P", explorer.Find("[data-testid='c4-zoom-title']").TagName);
        Assert.Equal("context-backlog", explorer.Find("[data-testid='c4-zoom-key']").TextContent);
    }

    /// <summary>The design's Dynamic screen: the steps panel is the right-hand column,
    /// beside the picture, and a selected element's inspector follows it there rather
    /// than wrapping under the picture.</summary>
    [Fact]
    public async Task On_a_dynamic_view_the_steps_and_the_inspector_are_the_right_hand_column()
    {
        var explorer = Render(open: new C4ZoomOpen("capture-to-issue"));
        await explorer.InvokeAsync(() => explorer.Instance.NodeSelected(C4MermaidWriter.AliasOf("backlog.desktop"), "capture-to-issue"));

        var aside = explorer.Find("[data-testid='c4-zoom-aside']");
        Assert.Equal(
            ["c4-dynamic-steps", "c4-element-inspector"],
            aside.Children.Select(child => child.GetAttribute("data-testid")));
        Assert.Empty(explorer.Find(".c4-zoom-explorer__main").QuerySelectorAll("[data-testid='c4-dynamic-steps']"));
    }

    // ---- the surface's polish: overlay row, view summary, path decisions ----------

    private static C4ElementRecords Records(string elementId) => elementId switch
    {
        "backlog.desktop" => new C4ElementRecords([],
        [
            new C4InspectorRecord(".devbook/arc42/adr/0003-store.md", "0003", "SQLite is the canonical store", C4InspectorRecordKind.Decision) { Accepted = true },
            new C4InspectorRecord(".devbook/arc42/adr/0031-events.md", "0031", "Live events", C4InspectorRecordKind.Decision, Proposed: true),
        ], []),
        "github" => new C4ElementRecords([],
        [
            new C4InspectorRecord(".devbook/arc42/adr/0003-store.md", "0003", "SQLite is the canonical store", C4InspectorRecordKind.Decision) { Accepted = true },
            new C4InspectorRecord(".devbook/arc42/adr/0020-github.md", "0020", "GitHub issues", C4InspectorRecordKind.Decision) { Accepted = true },
        ], []),
        "backlog.cloud" => new C4ElementRecords([],
        [
            new C4InspectorRecord(".devbook/arc42/adr/0005-replica.md", "0005", "Replica", C4InspectorRecordKind.Decision) { Accepted = true },
        ], []),
        _ => C4ElementRecords.None
    };

    /// <summary>The design's header carries the overlay switch, not the view heading.</summary>
    [Fact]
    public void On_the_surface_the_overlay_switch_is_the_top_rows_not_the_view_headings()
    {
        var explorer = Render(recordsFor: Records);

        var top = explorer.Find("[data-testid='c4-zoom-top']");
        Assert.Single(top.QuerySelectorAll("[data-testid='c4-zoom-overlay']"));
        Assert.Empty(explorer.Find(".c4-zoom-explorer__head").QuerySelectorAll("[data-testid='c4-zoom-overlay']"));

        // The top row comes before the bar.
        var surface = explorer.Find("[data-testid='c4-zoom-explorer']");
        var order = surface.Children.Select(child => child.GetAttribute("data-testid")).ToList();
        Assert.True(order.IndexOf("c4-zoom-top") < order.IndexOf("c4-zoom-bar"));

        explorer.Find("[data-testid='c4-zoom-overlay-decisions']").Click();
        Assert.Equal(C4RecordOverlay.Decisions, explorer.Instance.Overlay);
    }

    [Fact]
    public void With_a_document_open_the_overlay_switch_stands_down()
    {
        var explorer = Render(document: Doc, recordsFor: Records);

        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-top']"));
    }

    /// <summary>The design's Landscape screen: with nothing selected the right-hand
    /// panel says what the view is, the way in, and the external systems.</summary>
    [Fact]
    public void Nothing_selected_on_the_landscape_shows_its_summary()
    {
        var explorer = Render(open: new C4ZoomOpen("landscape"));

        var summary = explorer.Find("[data-testid='c4-zoom-view-summary']");
        Assert.Equal("Landscape", summary.QuerySelector("[data-testid='c4-view-summary-kind']")!.TextContent.Trim());
        Assert.Equal("2 systems, 1 owned", summary.QuerySelector("[data-testid='c4-view-summary-title']")!.TextContent.Trim());
        Assert.Equal("Everything", summary.QuerySelector("[data-testid='c4-view-summary-description']")!.TextContent.Trim());

        var list = summary.QuerySelector("[data-testid='c4-view-summary-list']")!;
        Assert.Contains("External systems · 1", list.QuerySelector("h4")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("GitHub", list.TextContent, StringComparison.Ordinal);
        Assert.Contains("Issues and webhooks", list.TextContent, StringComparison.Ordinal);

        var way = summary.QuerySelector("[data-testid='c4-view-summary-way']")!;
        Assert.Contains("Prompt Backlog — system context", way.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summarys_way_in_opens_the_view_below()
    {
        var explorer = Render(open: new C4ZoomOpen("landscape"));

        explorer.Find("[data-testid='c4-view-summary-way']").Click();

        Assert.Equal("context-backlog", explorer.Instance.ViewKey);
    }

    [Fact]
    public void An_element_picked_in_the_summary_is_selected_and_inspected()
    {
        var explorer = Render(open: new C4ZoomOpen("context-backlog"));

        explorer.FindAll("[data-testid='c4-view-summary-element']").Single(element => element.GetAttribute("data-element") == "github").Click();

        Assert.Equal("github", explorer.Instance.SelectedElementId);
        Assert.Equal("GitHub", explorer.Find("[data-testid='c4-inspector-name']").TextContent.Trim());
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-view-summary']"));
    }

    /// <summary>The summary goes with the press, so the keyboard is handed to the
    /// inspector's heading that stands in its place rather than dropped to the page.</summary>
    [Fact]
    public void An_element_picked_in_the_summary_takes_the_focus_to_the_inspectors_heading()
    {
        var explorer = Render(open: new C4ZoomOpen("context-backlog"));

        explorer.FindAll("[data-testid='c4-view-summary-element']").Single(element => element.GetAttribute("data-element") == "github").Click();

        explorer.WaitForAssertion(() => Assert.Single(_context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"]));
        Assert.Equal("-1", explorer.Find("[data-testid='c4-inspector-name']").GetAttribute("tabindex"));
    }

    /// <summary>Two cards that zoom into views of the same key — a workspace that
    /// reuses one — offer one way in to it, never two buttons under one render key.</summary>
    [Fact]
    public void The_summary_offers_one_way_in_per_view()
    {
        // The reader renames a repeated key ("context-2"); a workspace handed over
        // otherwise — derived from the chapters' fences, say — may still repeat one.
        var read = C4DslReader.Read("""
            workspace "Twice" {
                model {
                    a = softwareSystem "Alpha" "First"
                    b = softwareSystem "Beta" "Second"
                }
                views {
                    systemLandscape "landscape" "Everything" { include * }
                    systemContext a "context" "Alpha context" { include * }
                    systemContext b "context" "Beta context" { include * }
                }
            }
            """);
        var workspace = read with { Views = [.. read.Views.Select(view => view.Key == "context-2" ? view with { Key = "context" } : view)] };

        var summary = _context.Render<C4ViewSummary>(parameters => parameters
            .Add(c => c.Workspace, workspace)
            .Add(c => c.View, workspace.View("landscape")!));

        var ways = summary.FindAll("[data-testid='c4-view-summary-way']").Select(way => way.GetAttribute("data-view")).ToList();
        Assert.Equal(["context"], ways);
    }

    [Fact]
    public void Off_the_landscape_the_summary_counts_and_lists_every_card()
    {
        var explorer = Render(open: new C4ZoomOpen("containers-backlog"));

        var summary = explorer.Find("[data-testid='c4-zoom-view-summary']");
        Assert.Equal("Container", summary.QuerySelector("[data-testid='c4-view-summary-kind']")!.TextContent.Trim());
        Assert.Equal("1 person, 1 software system, 2 containers", summary.QuerySelector("[data-testid='c4-view-summary-title']")!.TextContent.Trim());
        Assert.Contains("On this view · 4", summary.QuerySelector("[data-testid='c4-view-summary-list'] h4")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("Desktop App — components", summary.QuerySelector("[data-testid='c4-view-summary-way']")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>The design's Dynamic screen: the decisions that name an element on the
    /// flow's path, each once, a proposed one marked, each opening its record.</summary>
    [Fact]
    public void A_dynamic_view_lists_the_decisions_on_its_path()
    {
        var opened = new List<string>();
        var explorer = Render(open: new C4ZoomOpen("capture-to-issue"), recordsFor: Records, opened: opened.Add);

        var decisions = explorer.FindAll("[data-testid='c4-dynamic-decision']");
        Assert.Equal(["0003", "0020", "0031"], decisions.Select(decision => decision.QuerySelector(".c4-dynamic-steps__decision-number")!.TextContent.Trim()));
        Assert.Single(decisions[2].QuerySelectorAll("[data-testid='c4-dynamic-decision-proposed']"));

        decisions[1].Click();

        Assert.Equal([".devbook/arc42/adr/0020-github.md"], opened);
    }

    /// <summary>A guideline numbers from 0001 as the local records do: the path lists
    /// the local decisions first, and says which chip is a guideline, aloud and drawn.</summary>
    [Fact]
    public void A_guideline_on_the_path_comes_after_the_local_decisions_and_says_so()
    {
        static C4ElementRecords WithGuideline(string elementId) => elementId == "backlog.desktop"
            ? new C4ElementRecords([],
            [
                new C4InspectorRecord(".devbook/arc42/adr/guidelines/0001-results.md", "0001", "Result objects", C4InspectorRecordKind.Decision) { Guideline = true },
                new C4InspectorRecord(".devbook/arc42/adr/0031-events.md", "0031", "Live events", C4InspectorRecordKind.Decision),
            ], [])
            : C4ElementRecords.None;

        var explorer = Render(open: new C4ZoomOpen("capture-to-issue"), recordsFor: WithGuideline);

        var decisions = explorer.FindAll("[data-testid='c4-dynamic-decision']");
        Assert.Equal(["local", "guideline"], decisions.Select(decision => decision.GetAttribute("data-scope")));
        Assert.StartsWith("Guideline ADR 0001", decisions[1].GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.StartsWith("ADR 0031", decisions[0].GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Single(decisions[1].QuerySelectorAll("[data-testid='c4-dynamic-decision-guideline']"));
        Assert.Empty(decisions[0].QuerySelectorAll("[data-testid='c4-dynamic-decision-guideline']"));
    }

    [Fact]
    public void Without_host_records_the_path_decisions_are_left_out()
    {
        var explorer = Render(open: new C4ZoomOpen("capture-to-issue"));

        Assert.Single(explorer.FindAll("[data-testid='c4-dynamic-steps']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-dynamic-decisions']"));
    }
}
