using Backlog.UI.Components.Diagrams.C4;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the C4 zoom tab's inspector lists about one element, as
/// <see cref="C4ElementInspection"/> reads it: the chapters the views map lists against a
/// view that shows the element, and the decision and debt records whose <c>c4</c> or
/// <c>c4-touches</c> names it, told apart by folder.
/// </summary>
public sealed class C4ElementInspectionTests
{
    private const string Source = """
        workspace "Inspected" "For the inspection tests" {
            !identifiers hierarchical
            model {
                backlog = softwareSystem "Prompt Backlog" "The system" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI" {
                        devbook = component "Devbook" "Draws the devbook" "Razor"
                    }
                    store = container "Local Task Store" "Canonical" "SQLite"
                }
                github = softwareSystem "GitHub" "Issues"
                backlog.desktop -> github "Reads issues"
            }
            views {
                systemLandscape "landscape" "Landscape" { include * }
                systemContext backlog "context" "Context" { include * }
                container backlog "containers" "Containers" { include * }
            }
        }
        """;

    private static readonly C4Workspace Model = C4DslReader.Read(Source);

    private static readonly C4WorkspaceEntry Entry = new(
        "workspace.dsl",
        "Inspected",
        Model,
        [],
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["landscape"] = [".devbook/arc42/01-introduction-and-goals.md"],
            ["context"] = [".devbook/arc42/03-context-and-scope.md#business-context"],
            ["containers"] = [".devbook/arc42/05-building-block-view.md#container-view", ".devbook/arc42/03-context-and-scope.md#business-context"],
        });

    private static readonly IReadOnlyList<DevbookDocument> Chapters =
    [
        DevbookMarkdownParser.Parse(".devbook/arc42/03-context-and-scope.md", "# 03. Context and Scope\n\n## Business context\n\nText.\n"),
        DevbookMarkdownParser.Parse(".devbook/arc42/05-building-block-view.md", "# 05. Building Block View\n\n## Container view\n\nText.\n"),
    ];

    private static C4LinkedRecord Decision(string path, string title, string? status) =>
        new(path, title, C4RecordKind.Decision, null, null, []) { Status = status };

    private static C4LinkedRecord Debt(string path, string title, DevbookDebtSeverity severity, DevbookDebtLifecycle lifecycle) =>
        new(path, title, C4RecordKind.Debt, severity, lifecycle, []);

    private static C4Catalog CatalogWith(params C4ElementLink[] links) =>
        new C4Catalog(true, "arc42", [], [], [Entry]) { Links = links };

    [Fact]
    public void The_chapters_documenting_a_view_that_shows_the_element_are_listed_once_in_file_order()
    {
        var records = C4ElementInspection.For(CatalogWith(), Entry, Chapters, "backlog.desktop");

        Assert.Equal(
            ["03 Context and Scope › Business context", "05 Building Block View › Container view"],
            records.DocumentedIn.Select(chapter => $"{chapter.Number} {chapter.Title}"));
        Assert.Equal(".devbook/arc42/03-context-and-scope.md#business-context", records.DocumentedIn[0].Path);
    }

    [Fact]
    public void A_view_that_does_not_show_the_element_lends_it_no_chapter()
    {
        // The landscape draws the systems, not the containers inside one.
        var desktop = C4ElementInspection.For(CatalogWith(), Entry, Chapters, "backlog.desktop");
        var system = C4ElementInspection.For(CatalogWith(), Entry, Chapters, "backlog");

        Assert.DoesNotContain(desktop.DocumentedIn, chapter => chapter.Path.Contains("01-introduction", StringComparison.Ordinal));
        Assert.Contains(system.DocumentedIn, chapter => chapter.Path.Contains("01-introduction", StringComparison.Ordinal));
    }

    [Fact]
    public void Decisions_and_debt_are_told_apart_by_folder()
    {
        var adr = Decision(".devbook/arc42/adr/0030-credentials.md", "ADR 0030: Stored credentials go through the OS envelope", "proposed");
        var accepted = Decision(".devbook/arc42/adr/0001-desktop.md", "ADR 0001: Desktop stack", "accepted");
        var tdr = Debt(".devbook/arc42/tdr/0001-renders.md", "TDR 0001: Renders vanish on a snapshot", DevbookDebtSeverity.Medium, DevbookDebtLifecycle.InProgress);

        var records = C4ElementInspection.For(
            CatalogWith(
                new C4ElementLink("backlog.desktop", C4LinkKind.About, adr),
                new C4ElementLink("backlog.desktop", C4LinkKind.About, accepted),
                new C4ElementLink("backlog.desktop", C4LinkKind.Touches, tdr)),
            Entry,
            Chapters,
            "backlog.desktop");

        Assert.Equal(["0001 Desktop stack", "0030 Stored credentials go through the OS envelope"],
            records.Decisions.Select(record => $"{record.Number} {record.Title}"));
        Assert.All(records.Decisions, record => Assert.Equal(C4InspectorRecordKind.Decision, record.Kind));
        Assert.False(records.Decisions[0].Proposed);
        Assert.True(records.Decisions[1].Proposed);

        var debt = Assert.Single(records.Debt);
        Assert.Equal(C4InspectorRecordKind.Debt, debt.Kind);
        Assert.Equal("0001", debt.Number);
        Assert.Equal("Renders vanish on a snapshot", debt.Title);
        Assert.Equal("Medium", debt.Severity);
        Assert.Equal("in progress", debt.Lifecycle);
        Assert.True(debt.Touches);
        Assert.Equal(".devbook/arc42/tdr/0001-renders.md", debt.Path);
    }

    [Fact]
    public void A_record_about_the_element_in_one_block_and_touching_it_in_another_is_listed_once_as_about()
    {
        var adr = Decision(".devbook/arc42/adr/0001-desktop.md", "ADR 0001: Desktop stack", null);

        var records = C4ElementInspection.For(
            CatalogWith(
                new C4ElementLink("backlog.desktop", C4LinkKind.Touches, adr, "Consequences"),
                new C4ElementLink("backlog.desktop", C4LinkKind.About, adr)),
            Entry,
            Chapters,
            "backlog.desktop");

        var decision = Assert.Single(records.Decisions);
        Assert.False(decision.Touches);
    }

    [Fact]
    public void An_element_nothing_names_has_no_records()
    {
        var records = C4ElementInspection.For(CatalogWith(), Entry, Chapters, "github");

        Assert.Empty(records.Decisions);
        Assert.Empty(records.Debt);
    }

    /// <summary>The Debt overlay's tray: every debt record no <c>c4</c> or
    /// <c>c4-touches</c> field links to an element, in file order, with its severity and
    /// rung — a resolved one included, for the explorer to leave out — and no decision
    /// or linked debt among them.</summary>
    [Fact]
    public void Cross_cutting_debt_is_the_debt_records_that_name_no_element()
    {
        var linked = Debt(".devbook/arc42/tdr/0001-renders.md", "TDR 0001: Renders vanish on a snapshot", DevbookDebtSeverity.Medium, DevbookDebtLifecycle.Identified);

        IReadOnlyList<DevbookDocument> chapters =
        [
            .. Chapters,
            DevbookMarkdownParser.Parse(".devbook/arc42/tdr/0001-renders.md", "# TDR 0001: Renders vanish on a snapshot\n\n```meta\nseverity: medium\n```\n\nText.\n"),
            DevbookMarkdownParser.Parse(".devbook/arc42/tdr/0003-zero-warning.md", "# TDR 0003: The check is not a zero-warning signal\n\n```meta\nseverity: low\nstatus: resolved\n```\n\nText.\n"),
            DevbookMarkdownParser.Parse(".devbook/arc42/tdr/0002-conventions.md", "# TDR 0002: Conventions have no checked-in home\n\n```meta\nseverity: medium\n```\n\nText.\n"),
            DevbookMarkdownParser.Parse(".devbook/arc42/tdr/README.md", "# Technical debt\n\nThe index.\n"),
            DevbookMarkdownParser.Parse(".devbook/arc42/adr/0009-unlinked.md", "# ADR 0009: Names nothing\n\nText.\n"),
        ];

        var crossCutting = C4ElementInspection.CrossCuttingDebt(
            CatalogWith(new C4ElementLink("backlog.desktop", C4LinkKind.About, linked)),
            chapters);

        Assert.Equal(["0002 Conventions have no checked-in home", "0003 The check is not a zero-warning signal"],
            crossCutting.Select(record => $"{record.Number} {record.Title}"));
        Assert.All(crossCutting, record => Assert.Equal(C4InspectorRecordKind.Debt, record.Kind));
        Assert.Equal("Medium", crossCutting[0].Severity);
        Assert.Equal("identified", crossCutting[0].Lifecycle);
        Assert.True(crossCutting[0].OpenDebt);
        Assert.Equal("resolved", crossCutting[1].Lifecycle);
        Assert.False(crossCutting[1].OpenDebt);
    }

    /// <summary>The views map may list a chapter of another devbook folder against a
    /// view — the domain's context map beside the container view — but the section is
    /// "Documented in arc42", so only the arc42 folder's chapters are listed.</summary>
    [Fact]
    public void Documented_in_lists_only_arc42_chapters()
    {
        var entry = Entry with
        {
            References = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["containers"] =
                [
                    ".devbook/arc42/05-building-block-view.md#container-view",
                    ".devbook/domain/context-map.md",
                    ".devbook/arc42/adr/0030-credentials.md",
                ],
            }
        };

        var records = C4ElementInspection.For(CatalogWith(), entry, Chapters, "backlog.desktop");

        var chapter = Assert.Single(records.DocumentedIn);
        Assert.Equal(".devbook/arc42/05-building-block-view.md#container-view", chapter.Path);
    }

    [Fact]
    public void Each_decision_says_whether_it_is_accepted()
    {
        var accepted = Decision(".devbook/arc42/adr/0001-desktop.md", "ADR 0001: Desktop stack", "Accepted");
        var superseded = Decision(".devbook/arc42/adr/0002-old.md", "ADR 0002: Old", "superseded");
        var proposed = Decision(".devbook/arc42/adr/0030-credentials.md", "ADR 0030: Credentials", "proposed");

        var records = C4ElementInspection.For(
            CatalogWith(
                new C4ElementLink("backlog.desktop", C4LinkKind.About, accepted),
                new C4ElementLink("backlog.desktop", C4LinkKind.About, proposed),
                new C4ElementLink("backlog.desktop", C4LinkKind.About, superseded)),
            Entry,
            Chapters,
            "backlog.desktop");

        Assert.Equal([true, false, false], records.Decisions.Select(record => record.Accepted));
        Assert.Equal([false, false, true], records.Decisions.Select(record => record.Proposed));
    }

    /// <summary>The design's Containers screen: Desktop App lists the debt named on its
    /// Devbook component, saying which component carries it. Decisions are not rolled
    /// up — they anchor where they are.</summary>
    [Fact]
    public void A_container_lists_the_debt_on_its_components_naming_the_component()
    {
        var onComponent = Debt(".devbook/arc42/tdr/0001-renders.md", "TDR 0001: Renders vanish on a snapshot", DevbookDebtSeverity.Medium, DevbookDebtLifecycle.Identified);
        var onContainer = Debt(".devbook/arc42/tdr/0004-sessions.md", "TDR 0004: Sessions", DevbookDebtSeverity.Low, DevbookDebtLifecycle.Planned);
        var decision = Decision(".devbook/arc42/adr/0015-storage.md", "ADR 0015: Storage", "accepted");

        var records = C4ElementInspection.For(
            CatalogWith(
                new C4ElementLink("backlog.desktop.devbook", C4LinkKind.About, onComponent),
                new C4ElementLink("backlog.desktop.devbook", C4LinkKind.About, decision),
                new C4ElementLink("backlog.desktop", C4LinkKind.About, onContainer)),
            Entry,
            Chapters,
            "backlog.desktop");

        Assert.Empty(records.Decisions);
        Assert.Equal(["0001", "0004"], records.Debt.Select(record => record.Number));
        Assert.Equal("Devbook", records.Debt[0].OnComponent);
        Assert.Null(records.Debt[1].OnComponent);

        var component = C4ElementInspection.For(
            CatalogWith(new C4ElementLink("backlog.desktop.devbook", C4LinkKind.About, onComponent)),
            Entry,
            Chapters,
            "backlog.desktop.devbook");
        Assert.Null(Assert.Single(component.Debt).OnComponent);
    }

    /// <summary>The design's Components screen: a debt record carries what its file
    /// says — the debt in a sentence, the preferred remediation, and the decision it
    /// originates in.</summary>
    [Fact]
    public void Debt_carries_its_summary_remediation_and_origin()
    {
        const string path = ".devbook/arc42/tdr/0001-renders.md";
        var debt = Debt(path, "TDR 0001: Renders vanish on a snapshot", DevbookDebtSeverity.Medium, DevbookDebtLifecycle.Identified);

        IReadOnlyList<DevbookDocument> chapters =
        [
            .. Chapters,
            DevbookMarkdownParser.Parse(path, """
                # TDR 0001: Renders vanish on a snapshot

                ```meta
                severity: medium
                related: [".devbook/arc42/adr/0008-branch-snapshot.md"]
                ```

                ## Status

                **Identified**.

                ## The debt

                A branch-sourced repository never shows a **rendered** diagram.

                Second paragraph.

                ## Origin

                The third amendment to local ADR 0008 excluded the folder.

                ## Remediation options

                ### Option A — fetch `_archify` lazily per diagram (preferred before local ADR 0034)

                Text.

                ### Option B — fetch the index

                Text.

                ### Option C — drop the exclusion (preferred since local ADR 0034)

                Text.
                """),
            DevbookMarkdownParser.Parse(".devbook/arc42/adr/0008-branch-snapshot.md", "# ADR 0008: Branch snapshot\n\nText.\n"),
        ];

        var records = C4ElementInspection.For(
            CatalogWith(new C4ElementLink("backlog.desktop", C4LinkKind.About, debt)),
            Entry,
            chapters,
            "backlog.desktop");

        var detail = Assert.Single(records.Debt).Detail;
        Assert.NotNull(detail);
        Assert.Equal("A branch-sourced repository never shows a rendered diagram.", detail.Summary);
        Assert.Equal("Option C — drop the exclusion", detail.Remediation);
        var origin = Assert.Single(detail.Origin);
        Assert.Equal(".devbook/arc42/adr/0008-branch-snapshot.md", origin.Path);
        Assert.Equal("ADR 0008", origin.Label);
    }

    [Fact]
    public void A_single_option_marked_preferred_is_the_remediation()
    {
        const string path = ".devbook/arc42/tdr/0002-conventions.md";
        var debt = Debt(path, "TDR 0002: Conventions", DevbookDebtSeverity.Low, DevbookDebtLifecycle.Identified);

        var records = C4ElementInspection.For(
            CatalogWith(new C4ElementLink("backlog.desktop", C4LinkKind.About, debt)),
            Entry,
            [
                .. Chapters,
                DevbookMarkdownParser.Parse(path, "# TDR 0002: Conventions\n\n## Remediation options\n\n### Option A — one rule per group (preferred)\n\nText.\n\n### Option B — import them\n\nText.\n"),
            ],
            "backlog.desktop");

        Assert.Equal("Option A — one rule per group", Assert.Single(records.Debt).Detail?.Remediation);
    }

    [Fact]
    public void Debt_whose_file_has_none_of_those_sections_carries_an_empty_detail()
    {
        const string path = ".devbook/arc42/tdr/0002-conventions.md";
        var debt = Debt(path, "TDR 0002: Conventions", DevbookDebtSeverity.Low, DevbookDebtLifecycle.Identified);

        var records = C4ElementInspection.For(
            CatalogWith(new C4ElementLink("backlog.desktop", C4LinkKind.About, debt)),
            Entry,
            [.. Chapters, DevbookMarkdownParser.Parse(path, "# TDR 0002: Conventions\n\nText.\n")],
            "backlog.desktop");

        var detail = Assert.Single(records.Debt).Detail;
        Assert.NotNull(detail);
        Assert.Null(detail.Summary);
        Assert.Null(detail.Remediation);
        Assert.Empty(detail.Origin);
    }

    [Fact]
    public void A_chapter_the_folder_does_not_hold_is_listed_by_its_file_name()
    {
        var records = C4ElementInspection.For(CatalogWith(), Entry, [], "backlog.desktop");

        Assert.Equal("context-and-scope", records.DocumentedIn[0].Title);
        Assert.Equal("03", records.DocumentedIn[0].Number);
    }
}
