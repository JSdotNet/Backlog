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
                    desktop = container "Desktop App" "Windows client" ".NET MAUI"
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

    [Fact]
    public void A_chapter_the_folder_does_not_hold_is_listed_by_its_file_name()
    {
        var records = C4ElementInspection.For(CatalogWith(), Entry, [], "backlog.desktop");

        Assert.Equal("context-and-scope", records.DocumentedIn[0].Title);
        Assert.Equal("03", records.DocumentedIn[0].Number);
    }
}
