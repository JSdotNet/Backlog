namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The inspector the C4 zoom tab sets beside the picture for the selected element
/// (<c>.devbook/domain/devbook/requirements.md</c>, "A selected element opens its
/// inspector"): what the element is, the zoom-in button when a deeper view exists, and
/// what the host's knowledge folders say about it — each section on its own, an element
/// with no records among them.
/// </summary>
public sealed class C4ElementInspectorTests : IDisposable
{
    private const string Attach = "backlogC4Zoom.attach";

    private readonly BunitContext _context = new();

    public C4ElementInspectorTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.JSInterop.Setup<bool>(Attach, _ => true).SetResult(true);
    }

    public void Dispose() => _context.Dispose();

    private static C4Workspace Workspace => C4ZoomNavigationTests.Workspace;

    private static string Alias(string elementId) => C4MermaidWriter.AliasOf(elementId);

    /// <summary>What a host might say about Desktop App: two chapters, an accepted and
    /// a proposed decision, and one debt.</summary>
    private static readonly C4ElementRecords DesktopRecords = new(
        [
            new C4InspectorChapter(".devbook/arc42/03-context-and-scope.md#access-channels", "03", "Context and Scope › Access channels"),
            new C4InspectorChapter(".devbook/arc42/05-building-block-view.md", "05", "Building Block View"),
        ],
        [
            new C4InspectorRecord(".devbook/arc42/adr/0001-desktop.md", "0001", "Desktop channel uses .NET MAUI Blazor Hybrid", C4InspectorRecordKind.Decision),
            new C4InspectorRecord(".devbook/arc42/adr/0030-credentials.md", "0030", "Stored credentials go through the OS envelope", C4InspectorRecordKind.Decision, Proposed: true),
        ],
        [
            new C4InspectorRecord(".devbook/arc42/tdr/0001-renders.md", "0001", "Renders vanish on a branch snapshot", C4InspectorRecordKind.Debt, Severity: "Medium", Lifecycle: "identified"),
        ]);

    private IRenderedComponent<C4ZoomExplorer> Render(
        string viewKey,
        Func<string, C4ElementRecords>? recordsFor = null,
        Action<string>? opened = null) =>
        _context.Render<C4ZoomExplorer>(parameters =>
        {
            parameters
                .Add(c => c.Workspace, Workspace)
                .Add(c => c.HintDismissed, true)
                .Add(c => c.OpenView, new C4ZoomOpen(viewKey))
                .Add(c => c.RecordsFor, recordsFor);

            if (opened is not null) parameters.Add(c => c.OnOpenRecord, opened);
        });

    private static async Task SelectAsync(IRenderedComponent<C4ZoomExplorer> explorer, string elementId, string viewKey) =>
        await explorer.InvokeAsync(() => explorer.Instance.NodeSelected(Alias(elementId), viewKey));

    private static AngleSharp.Dom.IElement Inspector(IRenderedComponent<C4ZoomExplorer> explorer) =>
        explorer.Find("[data-testid='c4-element-inspector']");

    private static string Text(AngleSharp.Dom.IElement scope, string testId) =>
        scope.QuerySelector($"[data-testid='{testId}']")!.TextContent.Trim();

    [Fact]
    public void Nothing_selected_shows_no_inspector()
    {
        var explorer = Render("containers-backlog");

        Assert.Empty(explorer.FindAll("[data-testid='c4-element-inspector']"));
    }

    [Fact]
    public async Task A_selected_element_shows_its_kind_name_technology_and_description()
    {
        var explorer = Render("containers-backlog");

        await SelectAsync(explorer, "backlog.desktop", "containers-backlog");

        var inspector = Inspector(explorer);
        Assert.Equal("Selected element", inspector.GetAttribute("aria-label"));
        Assert.Equal("Container", Text(inspector, "c4-inspector-kind"));
        Assert.Equal("Desktop App", Text(inspector, "c4-inspector-name"));
        Assert.Equal(".NET MAUI Blazor Hybrid", Text(inspector, "c4-inspector-technology"));
        Assert.Equal("Windows client", Text(inspector, "c4-inspector-description"));
    }

    [Fact]
    public async Task An_element_with_a_deeper_view_offers_to_open_it()
    {
        var explorer = Render("containers-backlog");
        await SelectAsync(explorer, "backlog.desktop", "containers-backlog");

        var zoom = explorer.Find("[data-testid='c4-inspector-zoom-in']");
        Assert.Contains("Open its 2 components", zoom.TextContent, StringComparison.Ordinal);

        zoom.Click();

        Assert.Equal("components-desktop", explorer.Instance.ViewKey);
    }

    [Fact]
    public async Task An_element_with_nothing_deeper_offers_no_zoom_in()
    {
        var explorer = Render("containers-backlog");

        await SelectAsync(explorer, "backlog.cloud", "containers-backlog");

        Assert.Equal("Cloud Database", Text(Inspector(explorer), "c4-inspector-name"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-inspector-zoom-in']"));
    }

    [Fact]
    public async Task The_chapters_documenting_it_are_listed_and_each_opens_its_chapter()
    {
        var opened = new List<string>();
        var explorer = Render("containers-backlog", _ => DesktopRecords, opened.Add);
        await SelectAsync(explorer, "backlog.desktop", "containers-backlog");

        var section = explorer.Find("[data-testid='c4-inspector-documented']");
        var chapters = section.QuerySelectorAll("[data-testid='c4-inspector-chapter']");
        Assert.Equal(["03 Context and Scope › Access channels", "05 Building Block View"],
            chapters.Select(chapter => string.Join(" ", chapter.Children.Select(child => child.TextContent.Trim()))));

        chapters[0].Click();

        Assert.Equal([".devbook/arc42/03-context-and-scope.md#access-channels"], opened);
    }

    [Fact]
    public async Task Decisions_are_listed_with_a_proposed_chip_on_a_proposed_one_and_open_their_record()
    {
        var opened = new List<string>();
        var explorer = Render("containers-backlog", _ => DesktopRecords, opened.Add);
        await SelectAsync(explorer, "backlog.desktop", "containers-backlog");

        var section = explorer.Find("[data-testid='c4-inspector-decisions']");
        Assert.Contains("Decisions · 2", section.QuerySelector("h4")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("1 proposed", Text(section, "c4-inspector-proposed-count"));

        var decisions = section.QuerySelectorAll("[data-testid='c4-inspector-decision']");
        Assert.Equal(2, decisions.Length);
        Assert.Null(decisions[0].QuerySelector("[data-testid='c4-inspector-proposed']"));
        Assert.Equal("proposed", decisions[1].QuerySelector("[data-testid='c4-inspector-proposed']")!.TextContent.Trim());
        Assert.Contains("0030", decisions[1].TextContent, StringComparison.Ordinal);

        decisions[1].Click();

        Assert.Equal([".devbook/arc42/adr/0030-credentials.md"], opened);
    }

    [Fact]
    public async Task Debt_is_listed_with_its_severity_and_lifecycle_and_opens_its_record()
    {
        var opened = new List<string>();
        var explorer = Render("containers-backlog", _ => DesktopRecords, opened.Add);
        await SelectAsync(explorer, "backlog.desktop", "containers-backlog");

        var section = explorer.Find("[data-testid='c4-inspector-debt']");
        Assert.Contains("Technical debt · 1", section.QuerySelector("h4")!.TextContent, StringComparison.Ordinal);

        var debt = section.QuerySelector("[data-testid='c4-inspector-debt-record']")!;
        Assert.Contains("TDR 0001", debt.TextContent, StringComparison.Ordinal);
        Assert.Equal("Medium · identified", Text(debt, "c4-inspector-debt-state"));
        Assert.Contains("Renders vanish on a branch snapshot", debt.TextContent, StringComparison.Ordinal);

        debt.Click();

        Assert.Equal([".devbook/arc42/tdr/0001-renders.md"], opened);
    }

    [Fact]
    public async Task An_element_with_no_records_says_so_in_each_section()
    {
        var asked = new List<string>();
        var explorer = Render("containers-backlog", elementId =>
        {
            asked.Add(elementId);
            return C4ElementRecords.None;
        });

        await SelectAsync(explorer, "backlog.cloud", "containers-backlog");

        Assert.Contains("backlog.cloud", asked);
        var inspector = Inspector(explorer);
        Assert.Equal(3, inspector.QuerySelectorAll("[data-testid='c4-inspector-none']").Length);
        Assert.Contains("Decisions · 0", explorer.Find("[data-testid='c4-inspector-decisions'] h4").TextContent, StringComparison.Ordinal);
        Assert.Contains("Technical debt · 0", explorer.Find("[data-testid='c4-inspector-debt'] h4").TextContent, StringComparison.Ordinal);
        Assert.Empty(inspector.QuerySelectorAll("[data-testid='c4-inspector-proposed-count']"));
        Assert.Empty(inspector.QuerySelectorAll("button[data-testid^='c4-inspector-d']"));
    }

    [Fact]
    public async Task With_no_host_records_the_record_sections_are_left_out()
    {
        var explorer = Render("containers-backlog");

        await SelectAsync(explorer, "backlog.desktop", "containers-backlog");

        Assert.Empty(explorer.FindAll("[data-testid='c4-inspector-documented']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-inspector-decisions']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-inspector-debt']"));
    }

    [Fact]
    public async Task Moving_to_another_view_drops_the_inspector_with_the_selection()
    {
        var explorer = Render("containers-backlog");
        await SelectAsync(explorer, "backlog.desktop", "containers-backlog");

        explorer.Find("[data-testid='c4-inspector-zoom-in']").Click();

        Assert.Null(explorer.Instance.SelectedElementId);
        Assert.Empty(explorer.FindAll("[data-testid='c4-element-inspector']"));
    }
}
