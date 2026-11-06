namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The zoom tab's record overlay (<c>.devbook/domain/devbook/requirements.md</c>, "An
/// overlay counts the records on each card"): the None / Decisions / Debt switch, the
/// chips it puts on the cards from the host's <see cref="C4ZoomExplorer.RecordsFor"/>,
/// the count of records that sit only below the view, and the cross-cutting tray of open
/// debt that names no element.
/// </summary>
public sealed class C4RecordOverlayTests : IDisposable
{
    private const string Attach = "backlogC4Zoom.attach";

    private readonly BunitContext _context = new();

    public C4RecordOverlayTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.JSInterop.Setup<bool>(Attach, _ => true).SetResult(true);
    }

    public void Dispose() => _context.Dispose();

    private static C4Workspace Workspace => C4ZoomNavigationTests.Workspace;

    private static string Alias(string elementId) => C4MermaidWriter.AliasOf(elementId);

    private static C4InspectorRecord Adr(string number, string title) =>
        new($".devbook/arc42/adr/{number}-x.md", number, title, C4InspectorRecordKind.Decision);

    private static C4InspectorRecord Tdr(string number, string title, string lifecycle, string severity = "Medium") =>
        new($".devbook/arc42/tdr/{number}-x.md", number, title, C4InspectorRecordKind.Debt, Severity: severity, Lifecycle: lifecycle);

    /// <summary>Desktop App: two decisions. Shell: one decision and a resolved debt.
    /// Devbook: one open debt. Nothing else is named.</summary>
    private static C4ElementRecords RecordsFor(string elementId) => elementId switch
    {
        "backlog.desktop" => new C4ElementRecords([], [Adr("0001", "Desktop channel"), Adr("0030", "Credentials")], []),
        "backlog.desktop.shell" => new C4ElementRecords([], [Adr("0022", "One main view")], [Tdr("0008", "Old shell debt", "resolved")]),
        "backlog.desktop.devbook" => new C4ElementRecords([], [], [Tdr("0001", "Artifacts invisible on a branch snapshot", "identified")]),
        _ => C4ElementRecords.None
    };

    /// <summary>Two open debt records that name no element, and one resolved.</summary>
    private static readonly IReadOnlyList<C4InspectorRecord> CrossCutting =
    [
        Tdr("0002", "Conventions have no checked-in home", "identified"),
        Tdr("0003", "The check is not a zero-warning signal", "planned", "Low"),
        Tdr("0004", "A settled one", "resolved"),
    ];

    private IRenderedComponent<C4ZoomExplorer> Render(string viewKey, bool withRecords = true, Action<string>? opened = null) =>
        _context.Render<C4ZoomExplorer>(parameters =>
        {
            parameters
                .Add(c => c.Workspace, Workspace)
                .Add(c => c.HintDismissed, true)
                .Add(c => c.OpenView, new C4ZoomOpen(viewKey));

            if (withRecords)
            {
                parameters
                    .Add(c => c.RecordsFor, RecordsFor)
                    .Add(c => c.CrossCuttingDebt, CrossCutting);
            }

            if (opened is not null) parameters.Add(c => c.OnOpenRecord, opened);
        });

    private static void Choose(IRenderedComponent<C4ZoomExplorer> explorer, string overlay) =>
        explorer.Find($"[data-testid='c4-zoom-overlay-{overlay}']").Click();

    private static string Picture(IRenderedComponent<C4ZoomExplorer> explorer) =>
        explorer.Find("[data-testid='c4-zoom-diagram']").InnerHtml;

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    // ---- the switch ----------------------------------------------------------------

    [Fact]
    public void A_host_with_no_records_offers_no_overlay_switch()
    {
        var explorer = Render("containers-backlog", withRecords: false);

        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-overlay']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-below']"));
    }

    [Fact]
    public void The_switch_is_one_choice_of_three_starting_at_none_with_no_chips()
    {
        var explorer = Render("containers-backlog");

        var group = explorer.Find("[data-testid='c4-zoom-overlay']");
        Assert.Equal("radiogroup", group.GetAttribute("role"));

        var choices = group.QuerySelectorAll("button");
        Assert.Equal(["None", "Decisions", "Debt"], choices.Select(choice => choice.TextContent.Trim()));
        Assert.Equal("true", explorer.Find("[data-testid='c4-zoom-overlay-none']").GetAttribute("aria-checked"));

        Assert.Equal(C4RecordOverlay.None, explorer.Instance.Overlay);
        Assert.DoesNotContain("data-c4-records", Picture(explorer), StringComparison.Ordinal);
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-below']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-cross-cutting']"));
    }

    // ---- Decisions -----------------------------------------------------------------

    [Fact]
    public void Decisions_puts_an_adr_count_on_each_named_card_and_no_tdr_count()
    {
        var explorer = Render("containers-backlog");

        Choose(explorer, "decisions");

        var picture = Picture(explorer);
        Assert.Equal(C4RecordOverlay.Decisions, explorer.Instance.Overlay);
        Assert.Equal("true", explorer.Find("[data-testid='c4-zoom-overlay-decisions']").GetAttribute("aria-checked"));
        Assert.Equal(1, Occurrences(picture, "data-c4-records=\"adr\""));
        Assert.Contains(">2 ADR</text>", picture, StringComparison.Ordinal);
        Assert.DoesNotContain("data-c4-records=\"tdr\"", picture, StringComparison.Ordinal);
        Assert.DoesNotContain("c4-diagram__node--debt", picture, StringComparison.Ordinal);

        // Decisions has no tray: the tray is debt's.
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-cross-cutting']"));
    }

    /// <summary>Shell's decision names only a component of Desktop App: it is not on the
    /// containers view, but a reader finds it by zooming in.</summary>
    [Fact]
    public void Decisions_counts_the_records_that_name_only_elements_below_the_view()
    {
        var explorer = Render("containers-backlog");

        Choose(explorer, "decisions");

        Assert.Equal("Not on this view: 1 ADR links only to elements below it.",
            explorer.Find("[data-testid='c4-zoom-below']").TextContent.Trim());
    }

    // ---- Debt ----------------------------------------------------------------------

    [Fact]
    public void Debt_puts_a_tdr_count_and_the_debt_surface_on_cards_with_open_debt_only()
    {
        var explorer = Render("components-desktop");

        Choose(explorer, "debt");

        var picture = Picture(explorer);
        Assert.Equal(1, Occurrences(picture, "data-c4-records=\"tdr\""));
        Assert.Contains(">1 TDR</text>", picture, StringComparison.Ordinal);
        Assert.DoesNotContain("data-c4-records=\"adr\"", picture, StringComparison.Ordinal);

        // Devbook's debt is open; Shell's is resolved, so Shell is drawn as it was.
        Assert.Equal(1, Occurrences(picture, "c4-diagram__node--debt"));
        var debtCard = picture.IndexOf("c4-diagram__node--debt", StringComparison.Ordinal);
        Assert.Contains($"-{Alias("backlog.desktop.devbook")}\"", picture[debtCard..picture.IndexOf('>', debtCard)], StringComparison.Ordinal);
    }

    [Fact]
    public void Debt_counts_open_debt_that_names_only_elements_below_the_view()
    {
        var explorer = Render("containers-backlog");

        Choose(explorer, "debt");

        Assert.Equal("Not on this view: 1 open TDR links only to elements below it.",
            explorer.Find("[data-testid='c4-zoom-below']").TextContent.Trim());
    }

    [Fact]
    public void On_the_deepest_view_nothing_sits_below_it()
    {
        var explorer = Render("components-desktop");

        Choose(explorer, "decisions");

        Assert.Equal("Not on this view: no ADR links only to elements below it.",
            explorer.Find("[data-testid='c4-zoom-below']").TextContent.Trim());
    }

    // ---- the cross-cutting tray ------------------------------------------------------

    [Fact]
    public void Debt_lists_the_open_debt_that_names_no_element_in_the_cross_cutting_tray()
    {
        var explorer = Render("components-desktop");

        Choose(explorer, "debt");

        var tray = explorer.Find("[data-testid='c4-zoom-cross-cutting']");
        Assert.Contains("Cross-cutting debt", tray.TextContent, StringComparison.Ordinal);

        var rows = tray.QuerySelectorAll("[data-testid='c4-cross-cutting-record']");
        Assert.Equal([".devbook/arc42/tdr/0002-x.md", ".devbook/arc42/tdr/0003-x.md"], rows.Select(row => row.GetAttribute("data-path")));
        Assert.Contains("TDR 0002", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Conventions have no checked-in home", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Low", rows[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Picking_a_tray_record_asks_the_host_to_open_it()
    {
        var opened = new List<string>();
        var explorer = Render("containers-backlog", opened: opened.Add);

        Choose(explorer, "debt");
        explorer.FindAll("[data-testid='c4-cross-cutting-record']")[1].Click();

        Assert.Equal([".devbook/arc42/tdr/0003-x.md"], opened);
    }

    [Fact]
    public void The_tray_stays_on_every_view_while_debt_is_on()
    {
        var explorer = Render("containers-backlog");

        Choose(explorer, "debt");
        explorer.InvokeAsync(() => explorer.Instance.ZoomIn(Alias("backlog.desktop"), "containers-backlog"));

        Assert.Equal("components-desktop", explorer.Instance.ViewKey);
        Assert.Equal(C4RecordOverlay.Debt, explorer.Instance.Overlay);
        Assert.NotEmpty(explorer.FindAll("[data-testid='c4-zoom-cross-cutting']"));
    }

    [Fact]
    public void Choosing_none_again_takes_the_chips_and_the_tray_away()
    {
        var explorer = Render("components-desktop");

        Choose(explorer, "debt");
        Choose(explorer, "none");

        Assert.DoesNotContain("data-c4-records", Picture(explorer), StringComparison.Ordinal);
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-cross-cutting']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-below']"));
    }

    // ---- what else the overlay owes ---------------------------------------------------

    /// <summary>The viewer labels each card with the name this hands it, and that label
    /// is what a screen reader says in place of the chips' titles, so the count goes in
    /// it.</summary>
    [Fact]
    public void A_counted_card_says_its_count_in_its_accessible_name()
    {
        var explorer = Render("components-desktop");

        Choose(explorer, "debt");

        var attach = _context.JSInterop.Invocations.Last(invocation => invocation.Identifier == Attach);
        var names = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(attach.Arguments[^1]);

        Assert.Equal("Devbook, 1 open TDR", names[Alias("backlog.desktop.devbook")]);
        Assert.Equal("Shell", names[Alias("backlog.desktop.shell")]);
    }

    /// <summary>A host that read its folders again hands a new delegate, and the chips
    /// follow it rather than the answers kept from the old one.</summary>
    [Fact]
    public void New_records_from_the_host_redraw_the_chips()
    {
        var explorer = Render("containers-backlog");
        Choose(explorer, "decisions");

        Func<string, C4ElementRecords> more = elementId => elementId == "backlog.cloud"
            ? new C4ElementRecords([], [Adr("0040", "Cloud store")], [])
            : RecordsFor(elementId);

        explorer.Render(parameters => parameters.Add(c => c.RecordsFor, more));

        Assert.Equal(2, Occurrences(Picture(explorer), "data-c4-records=\"adr\""));
    }

    /// <summary>A deployment view draws instances, not the elements records name, so it
    /// claims nothing about what sits below it.</summary>
    [Fact]
    public void A_deployment_view_draws_no_below_line()
    {
        var explorer = Render("deployment-local");

        Choose(explorer, "decisions");

        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-below']"));
    }

    [Fact]
    public void The_tray_draws_nothing_with_nothing_to_list()
    {
        var tray = _context.Render<C4CrossCuttingDebt>(parameters => parameters.Add(c => c.Records, []));

        Assert.Empty(tray.FindAll("[data-testid='c4-zoom-cross-cutting']"));
        Assert.Equal(string.Empty, tray.Markup.Trim());
    }
}
