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
        C4ZoomCode? code = null) =>
        _context.Render<C4ZoomExplorer>(parameters =>
        {
            parameters
                .Add(c => c.Workspace, Workspace)
                .Add(c => c.HintDismissed, true)
                .Add(c => c.Navigation, Nav)
                .Add(c => c.Document, document)
                .Add(c => c.OpenView, open)
                .Add(c => c.Code, code)
                .Add(c => c.ViewCaption, view => builder => builder.AddContent(0, $" · documented by {view.Key}"));

            if (navigated is not null) parameters.Add(c => c.OnNavigate, EventCallback.Factory.Create(this, navigated));
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

        // Nothing selected: the right-hand column says what selecting does.
        Assert.Single(explorer.FindAll("[data-testid='c4-zoom-aside-empty']"));
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
}
