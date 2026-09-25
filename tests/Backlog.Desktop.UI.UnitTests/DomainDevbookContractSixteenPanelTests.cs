using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Devbook;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the domain panel draws of contract 16's new fields: the nine decision and
/// review state fields, <c>ext.*</c> keys, a context's <c>deployment</c>, and an
/// additional page's own-filename type.
///
/// <para>The state fields are the rule with teeth here. They are "not chapter
/// content": a view renders them as state beside the status and never as field
/// rows in the body. So the strip loses them and the header gains one line saying
/// who approved, who accepted, and whose move the review is.</para>
///
/// <para>Every view is handed in rather than read, because what is under test is
/// the drawing, and a view built here says what the store would have said without
/// a folder on disk having to spell it.</para>
/// </summary>
public sealed class DomainDevbookContractSixteenPanelTests
{
    private const string ContextMapPath = ".domain/context-map.md";

    [Fact]
    public void The_nine_state_fields_are_never_rows_in_the_strip()
    {
        using var context = CreateContext();

        var component = context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.View, MapView(ApprovedChapter())));

        var strip = component.Find(".domain-section .domain-metadata");
        foreach (var field in DevbookSchema.StateFields)
        {
            Assert.DoesNotContain(field, strip.TextContent, StringComparison.OrdinalIgnoreCase);
        }

        // An ordinary field beside them is untouched: `tests` is content.
        Assert.Contains("unit:dotnet:Billing.Tests", strip.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Approval_and_acceptance_are_drawn_beside_the_status_as_who_and_when()
    {
        using var context = CreateContext();

        var component = context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.View, MapView(ApprovedChapter())));

        var section = component.Find(".domain-section");
        var state = section.QuerySelector(".domain-section__header [data-testid='devbook-decision-state']")!;

        Assert.Equal("approved by jobsc · 2026-09-02; accepted by ann · 2026-09-20", state.TextContent);

        // Beside the status, in the same bar — not somewhere in the body.
        Assert.Same(state.ParentElement, section.QuerySelector(".domain-section__header .domain-devbook__actions"));

        // And never the hashes, which are for the check, not the reader.
        Assert.DoesNotContain("abc123", section.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_review_triad_says_whose_move_it_is()
    {
        using var context = CreateContext();

        var chapter = Section(new()
        {
            ["type"] = "aggregate",
            ["status"] = "proposed",
            ["review"] = "requested",
            ["reviewer"] = "ann",
            ["review-at"] = "2026-09-24"
        });

        var component = context.Render<DomainDevbookPanel>(parameters => parameters.Add(panel => panel.View, MapView(chapter)));

        Assert.Equal(
            "review: requested · reviewer ann · 2026-09-24",
            component.Find(".domain-section [data-testid='devbook-decision-state']").TextContent);
        Assert.Empty(component.FindAll(".domain-section .domain-metadata"));
    }

    [Fact]
    public void A_chapter_with_no_state_draws_no_state_line()
    {
        using var context = CreateContext();

        var component = context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.View, MapView(Section(new() { ["type"] = "aggregate", ["status"] = "draft" }))));

        Assert.Empty(component.FindAll("[data-testid='devbook-decision-state']"));
    }

    [Fact]
    public void The_status_selector_never_offers_a_decision_rung()
    {
        // The rungs are written by the approval gate, never picked. This panel does
        // not own the option list, but it must not add them to it either.
        using var context = CreateContext();

        var component = context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.View, MapView(Section(new() { ["type"] = "aggregate", ["status"] = "draft" }))));

        var options = component.FindAll(".domain-section [data-testid='devbook-state-select'] option")
            .Select(option => option.GetAttribute("value") ?? string.Empty)
            .ToList();

        Assert.NotEmpty(options);
        Assert.All(DevbookSchema.DecisionRungs, rung => Assert.DoesNotContain(rung, options, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_extension_key_stays_a_row_and_is_marked_as_one()
    {
        using var context = CreateContext();

        var component = context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.View, MapView(Section(new()
            {
                ["type"] = "aggregate",
                ["ext.archify.layer"] = "core",
                ["deployment"] = "module"
            }))));

        var extension = Assert.Single(component.FindAll(".domain-section .domain-metadata__item--extension"));
        Assert.Contains("ext.archify.layer", extension.TextContent, StringComparison.Ordinal);
        Assert.Contains("core", extension.TextContent, StringComparison.Ordinal);

        // An ordinary field is an ordinary chip.
        Assert.Contains(
            component.FindAll(".domain-section .domain-metadata__item"),
            item => item.TextContent.Contains("deployment", StringComparison.Ordinal) && !item.ClassList.Contains("domain-metadata__item--extension"));
    }

    [Fact]
    public void The_context_overview_shows_how_the_context_ships()
    {
        using var context = CreateContext();

        var component = context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.View, ContextView(mapDeployment: "module", contextDeployment: "module"))
            .Add(panel => panel.SelectedPath, "billing"));

        var overview = component.Find("[data-testid='domain-context']");
        Assert.Equal("deploymentmodule", overview.QuerySelector("[data-testid='domain-context-deployment']")!.TextContent);
        Assert.Empty(component.FindAll("[data-testid='domain-context-deployment-problem']"));
    }

    [Fact]
    public void A_disagreement_is_reported_naming_both_values()
    {
        using var context = CreateContext();

        var component = context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.View, ContextView(mapDeployment: "service", contextDeployment: "module"))
            .Add(panel => panel.SelectedPath, "billing"));

        var problem = component.Find("[data-testid='domain-context-deployment-problem']");
        Assert.Contains("\"service\"", problem.TextContent, StringComparison.Ordinal);
        Assert.Contains("\"module\"", problem.TextContent, StringComparison.Ordinal);
        Assert.Contains("context-map.md", problem.TextContent, StringComparison.Ordinal);
        Assert.Contains("context.md", problem.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_on_one_side_only_is_reported_too()
    {
        using var context = CreateContext();

        var component = context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.View, ContextView(mapDeployment: null, contextDeployment: "service"))
            .Add(panel => panel.SelectedPath, "billing"));

        Assert.Equal("deploymentservice", component.Find("[data-testid='domain-context-deployment']").TextContent);
        Assert.Contains("states none", component.Find("[data-testid='domain-context-deployment-problem']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_context_that_has_not_decided_says_nothing_about_deployment()
    {
        using var context = CreateContext();

        var component = context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.View, ContextView(mapDeployment: null, contextDeployment: null))
            .Add(panel => panel.SelectedPath, "billing"));

        Assert.Single(component.FindAll("[data-testid='domain-context']"));
        Assert.Empty(component.FindAll("[data-testid='domain-context-deployment']"));
        Assert.Empty(component.FindAll("[data-testid='domain-context-deployment-problem']"));
    }

    [Fact]
    public void An_additional_page_is_marked_with_the_page_sheet_and_its_own_filename_type()
    {
        using var context = CreateContext();

        var component = context.Render<DomainDevbookPanel>(parameters => parameters
            .Add(panel => panel.View, ContextView(mapDeployment: null, contextDeployment: null))
            .Add(panel => panel.SelectedPath, "billing"));

        var page = component.FindAll("[data-testid='domain-document']")
            .Single(document => document.TextContent.Contains("go-live-takeover.md", StringComparison.Ordinal));
        var mark = page.QuerySelector("[data-testid='domain-document-kind-mark']")!;

        Assert.Contains("devbook-type-marker--page", mark.ClassList);
        Assert.Contains("Additional page", page.QuerySelector(".domain-document__kind")!.TextContent, StringComparison.Ordinal);

        // The context file beside it draws its own contract-16 mark.
        var root = component.FindAll("[data-testid='domain-document']")
            .Single(document => document.TextContent.Contains("billing/context.md", StringComparison.Ordinal));
        Assert.Contains("devbook-type-marker--context", root.QuerySelector("[data-testid='domain-document-kind-mark']")!.ClassList);
    }

    private static DomainDevbookSection ApprovedChapter() => Section(new()
    {
        ["type"] = "aggregate",
        ["status"] = "accepted",
        ["approved-by"] = "jobsc",
        ["approved-at"] = "2026-09-02",
        ["approved-hash"] = "abc123",
        ["accepted-by"] = "ann",
        ["accepted-at"] = "2026-09-20",
        ["accepted-hash"] = "abc123",
        ["tests"] = "unit:dotnet:Billing.Tests"
    });

    private static DomainDevbookSection Section(Dictionary<string, string> metadata)
    {
        var fields = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
        return new DomainDevbookSection(
            "Invoice",
            2,
            fields.TryGetValue("status", out var status) ? status : "none",
            fields,
            "What an invoice is.",
            [],
            [],
            $"{ContextMapPath}#invoice");
    }

    /// <summary>The panel's read view with nothing selected: the context map,
    /// holding the one chapter under test.</summary>
    private static DomainDevbookView MapView(DomainDevbookSection chapter) =>
        new("JSdotNet/Backlog", "D:\\repo", "D:\\repo\\.domain", null,
            Document(ContextMapPath, "Order Platform", DomainDevbookDocumentKind.ContextMap, new() { ["type"] = "context-map" }, [chapter]),
            []);

    /// <summary>A billing context with its root <c>context.md</c> and one
    /// additional page, and a map whose <c>bounded-context</c> chapter stands for
    /// it.</summary>
    private static DomainDevbookView ContextView(string? mapDeployment, string? contextDeployment)
    {
        var chapterFields = new Dictionary<string, string> { ["type"] = "bounded-context", ["related"] = "[.domain/billing/context.md]" };
        if (mapDeployment is not null) chapterFields["deployment"] = mapDeployment;
        var mapChapter = Section(chapterFields);

        var contextFields = new Dictionary<string, string> { ["type"] = "context", ["index"] = "root" };
        if (contextDeployment is not null) contextFields["deployment"] = contextDeployment;

        var documents = new[]
        {
            Document(".domain/billing/context.md", "Billing", DomainDevbookDocumentKind.Context, contextFields, []),
            Document(".domain/billing/go-live-takeover.md", "Billing", DomainDevbookDocumentKind.Page, new() { ["type"] = "go-live-takeover" }, [])
        };

        return new DomainDevbookView("JSdotNet/Backlog", "D:\\repo", "D:\\repo\\.domain", null,
            Document(ContextMapPath, "Order Platform", DomainDevbookDocumentKind.ContextMap, new() { ["type"] = "context-map" }, [mapChapter]),
            [new DomainDevbookContext("billing", "Billing", "none", documents, mapChapter)]);
    }

    private static DomainDevbookDocument Document(
        string path,
        string title,
        DomainDevbookDocumentKind kind,
        Dictionary<string, string> metadata,
        IReadOnlyList<DomainDevbookSection> sections) =>
        new(path, title, kind, "none", new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase), string.Empty, [], sections, []);

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var root = Path.Combine(Path.GetTempPath(), "backlog-domain-contract-16-panel", Guid.NewGuid().ToString("n"));
        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        context.Services.AddSingleton(new DomainDevbookStore(new DevbookFolderSource(settings)));
        context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());
        context.Services.AddSingleton<IAppFeatureSettings>(new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features.json")));
        return context;
    }
}
