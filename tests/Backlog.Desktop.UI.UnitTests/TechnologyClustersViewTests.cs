using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Devbook;
using Backlog.UI.Components.Buttons;
using Backlog.UI.Components.Devbook;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Clusters view of the Technology page
/// (<c>.devbook/domain/devbook/features.md#technology-view</c>): every layer as a
/// cluster, a layer opened into pills and satellites, a technology's links lit up,
/// and following them — across layers, along the trail and Back.
/// <para>
/// The fixture is five technologies in three layers. Shared holds .NET and Razor
/// on it; Desktop holds Blazor on both and Playwright on Blazor; Testing holds bUnit
/// on Razor and Blazor. So Desktop links to Shared twice, Testing to Shared once and
/// to Desktop once.
/// </para>
/// </summary>
public sealed class TechnologyClustersViewTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task All_layers_draws_one_cluster_per_layer_with_a_dot_per_technology()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        Assert.Equal(["shared.md", "desktop.md", "testing.md"], component.FindAll("[data-testid='technology-clusters-cluster']").Select(cluster => cluster.GetAttribute("data-layer")).ToArray());
        Assert.Equal(5, component.FindAll("[data-testid='technology-clusters-dot']").Count);

        // Not adopted is hollow; adopted is filled.
        Assert.Contains("tech-clusters__dot--hollow", Dot(component, "desktop.md#blazor").ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-clusters__dot--hollow", Dot(component, "desktop.md#playwright").ClassName, StringComparison.Ordinal);
        Assert.DoesNotContain("tech-clusters__dot--hollow", Dot(component, "shared.md#net").ClassName, StringComparison.Ordinal);

        // Colour follows the family: two frameworks share one, a tool takes another.
        Assert.Contains("tech-family--framework", Dot(component, "shared.md#razor").ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-family--framework", Dot(component, "desktop.md#blazor").ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-family--library-tool-plugin", Dot(component, "desktop.md#playwright").ClassName, StringComparison.Ordinal);

        // One weighted line per linked pair, each with its count.
        Assert.Equal(3, component.FindAll("[data-testid='technology-clusters-line'][data-tone='link']").Count);
        Assert.Equal(["2", "1", "1"], component.FindAll("[data-testid='technology-clusters-link-count']").Select(badge => badge.TextContent.Trim()).ToArray());

        Assert.True(component.Find("[data-testid='technology-clusters-back']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Opening_a_layer_shows_its_pills_and_the_other_layers_as_satellites()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();

        // Grouped by family: the framework before the tool.
        Assert.Equal(["Blazor", "Playwright"], PillNames(component));
        Assert.Equal("Desktop", component.Find("[data-testid='technology-clusters-crumb-layer']").TextContent.Trim());
        Assert.Empty(component.FindAll("[data-testid='technology-clusters-crumb-technology']"));

        var satellites = component.FindAll("[data-testid='technology-clusters-satellite']");
        Assert.Equal(["shared.md", "testing.md"], satellites.Select(satellite => satellite.GetAttribute("data-layer")).ToArray());
        Assert.Equal(["2", "1"], component.FindAll("[data-testid='technology-clusters-satellite-links']").Select(badge => badge.TextContent.Trim()).ToArray());

        // Blazor on Playwright's side of the layer is the one quiet line inside it.
        Assert.Single(component.FindAll("[data-testid='technology-clusters-line'][data-tone='quiet']"));

        // A satellite opens its layer.
        component.Find("[data-testid='technology-clusters-satellite'][data-layer='testing.md']").Click();
        Assert.Equal(["bUnit"], PillNames(component));
    }

    /// <summary>Inside an open layer the technologies sit in one labelled group per
    /// family, in the families' order: Shared holds a framework and a runtime.</summary>
    [Fact]
    public async Task An_open_layer_groups_its_pills_by_family_under_a_heading()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        component.Find("[data-testid='technology-clusters-cluster'][data-layer='shared.md']").Click();

        var groups = component.FindAll("[data-testid='technology-clusters-family']");
        Assert.Equal(["framework", "runtime"], groups.Select(group => group.GetAttribute("data-family")).ToArray());

        foreach (var group in groups)
        {
            Assert.Equal("group", group.GetAttribute("role"));
            var heading = component.Find($"#{group.GetAttribute("aria-labelledby")}");
            Assert.Contains("tech-clusters__family-heading", heading.ClassName, StringComparison.Ordinal);

            // The companion's title is an h3 under the panel's h2; the map comes
            // first, so its headings are h3 too and no level is skipped.
            Assert.Equal("H3", heading.TagName);
        }

        Assert.Equal(["Framework", "Runtime"], groups.Select(group => component.Find($"#{group.GetAttribute("aria-labelledby")}").TextContent.Trim()).ToArray());
        Assert.Equal(["Razor"], groups[0].QuerySelectorAll("[data-testid='technology-clusters-pill'] .tech-clusters__pill-label").Select(label => label.TextContent.Trim()).ToArray());
        Assert.Equal([".NET"], groups[1].QuerySelectorAll("[data-testid='technology-clusters-pill'] .tech-clusters__pill-label").Select(label => label.TextContent.Trim()).ToArray());
    }

    /// <summary>On All layers every dot is a button named for its technology;
    /// pressing one selects it and opens its layer on it.</summary>
    [Fact]
    public async Task A_dot_on_All_layers_is_a_button_that_selects_its_technology_and_opens_its_layer()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        var dots = component.FindAll("[data-testid='technology-clusters-dot']");
        Assert.All(dots, dot =>
        {
            Assert.Equal("BUTTON", dot.TagName);
            Assert.False(string.IsNullOrWhiteSpace(dot.GetAttribute("aria-label")));
            Assert.Null(dot.GetAttribute("aria-hidden"));
        });
        Assert.StartsWith("Playwright", Dot(component, "desktop.md#playwright").GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Contains("not adopted", Dot(component, "desktop.md#playwright").GetAttribute("aria-label"), StringComparison.Ordinal);

        var focusCalls = FocusCalls(harness);

        // Enter or Space on a focused button is its click.
        Dot(component, "testing.md#bunit").Click();

        Assert.Equal("Testing", component.Find("[data-testid='technology-clusters-crumb-layer']").TextContent.Trim());
        Assert.Equal("bUnit", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());
        Assert.Equal("true", Pill(component, "testing.md#bunit").GetAttribute("aria-pressed"));

        // The pressed dot left with the map it sat on, so focus goes to where the
        // reader now is, the technology in the breadcrumb, never to the page.
        component.WaitForAssertion(() => Assert.Equal(focusCalls + 1, FocusCalls(harness)));
        Assert.Equal("-1", component.Find("[data-testid='technology-clusters-crumb-technology']").GetAttribute("tabindex"));
    }

    /// <summary>Each cluster is one tab stop: its open-layer button first, then its
    /// dots. The arrows, Home and End are the browser's — components.js moves the stop
    /// and the focus across a <c>data-roving-buttons</c> group's buttons, which
    /// RovingButtonsScriptTests reads — so the view writes the starting stop only,
    /// handles no key itself, and writes the same stop on every render, leaving the
    /// stop the browser moved where it is.</summary>
    [Fact]
    public async Task Each_cluster_is_one_tab_stop_its_button_first_and_the_browser_roves_it()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        var groups = component.FindAll("[data-testid='technology-clusters-cluster-group']");
        Assert.Equal(3, groups.Count);
        Assert.All(groups, group =>
        {
            Assert.Equal("group", group.GetAttribute("role"));
            Assert.True(group.HasAttribute("data-roving-buttons"));

            // The open-layer button is the stop; every dot waits at -1. Each is a
            // direct child, as the script's `[data-roving-buttons] > button` needs.
            var buttons = group.Children.Where(child => child.TagName == "BUTTON").ToList();
            Assert.Equal(buttons.Count, group.QuerySelectorAll("button").Length);
            Assert.Equal("technology-clusters-cluster", buttons[0].GetAttribute("data-testid"));
            Assert.Equal(["0", .. Enumerable.Repeat("-1", buttons.Count - 1)], buttons.Select(button => button.GetAttribute("tabindex")).ToArray());

            // No server round trip per key: the stop it would carry is stale by the next press.
            Assert.All(buttons, button => Assert.Null(button.GetAttribute("blazor:onkeydown")));
        });

        // Desktop: the cluster, then Blazor and Playwright, family order.
        Assert.Equal(
            [".tech/desktop.md#blazor", ".tech/desktop.md#playwright"],
            Group(component, "desktop.md").QuerySelectorAll("[data-testid='technology-clusters-dot']").Select(dot => dot.GetAttribute("data-id")).ToArray());

        // A render writes the same stops again, so it never takes back a stop the browser moved.
        string?[] Stops() => [.. Group(component, "desktop.md").QuerySelectorAll("button").Select(button => button.GetAttribute("tabindex"))];
        var before = Stops();
        component.Find("[data-testid='technology-clusters-crumb-all']").Click();
        Assert.Equal(before, Stops());
    }

    /// <summary>A re-read that takes a technology away must not hand its dot's button
    /// to the next technology: that button may hold the cluster's tab stop, which the
    /// browser moved there. Keyed by id, every remaining dot keeps its own button —
    /// Playwright's survives Blazor, before it in the cluster, being taken away.</summary>
    [Fact]
    public async Task A_dot_keeps_its_own_button_when_a_reread_takes_away_a_technology_before_it()
    {
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var component = context.Render<TechnologyClustersView>(parameters => parameters.Add(view => view.View, KeyedFixture(withBlazor: true)));
        var playwright = DotButton(component, ".tech/desktop.md#playwright");
        var razor = DotButton(component, ".tech/shared.md#razor");

        component.Render(parameters => parameters.Add(view => view.View, KeyedFixture(withBlazor: false)));

        Assert.Empty(component.FindAll("[data-testid='technology-clusters-dot'][data-id='.tech/desktop.md#blazor']"));
        Assert.Same(playwright, DotButton(component, ".tech/desktop.md#playwright"));
        Assert.Same(razor, DotButton(component, ".tech/shared.md#razor"));
    }

    private static ToggleButton DotButton(IRenderedComponent<TechnologyClustersView> component, string id) =>
        component.FindComponents<ToggleButton>()
            .Single(button => button.Instance.AdditionalAttributes?.TryGetValue("data-id", out var value) == true && Equals(value, id))
            .Instance;

    /// <summary>Shared holds Razor; Desktop holds Blazor, a framework, then Playwright,
    /// a tool, which uses it — or Playwright alone once Blazor is taken away.</summary>
    private static TechnologyDevbookView KeyedFixture(bool withBlazor)
    {
        List<TechnologyGraphNode> nodes =
        [
            new(".tech/shared.md#razor", "Razor", "Shared Technologies", "framework", "adopted", string.Empty, "shared.md", 0, 0),
            new(".tech/desktop.md#playwright", "Playwright", "Desktop Stack", "tool", "candidate", string.Empty, "desktop.md", 1, 1),
        ];
        List<TechnologyGraphEdge> edges = [];
        if (withBlazor)
        {
            nodes.Insert(1, new(".tech/desktop.md#blazor", "Blazor", "Desktop Stack", "framework", "trial", string.Empty, "desktop.md", 1, 0));
            edges.Add(new("p->b", ".tech/desktop.md#playwright", ".tech/desktop.md#blazor", "depends on"));
        }

        return new TechnologyDevbookView(
            new DevbookFolderLocation("tech", true, null, "JSdotNet/Backlog", null, null),
            "Technology",
            string.Empty,
            [
                new TechnologyLayer("shared.md", "Shared Technologies", DevbookMetadata.Empty, []),
                new TechnologyLayer("desktop.md", "Desktop Stack", DevbookMetadata.Empty, []),
            ],
            [],
            [],
            TechnologyGraphMeasures.Apply(nodes, edges),
            TechnologyGraphStats.Empty);
    }

    /// <summary>Every view marks a technology with its family, so its type code or
    /// badge takes the family's hue: Razor is a framework, Blazor a framework, bUnit
    /// a package.</summary>
    [Fact]
    public async Task Every_view_marks_a_technology_with_its_family()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        // Clusters' own inspector badge.
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();
        Pill(component, "desktop.md#blazor").Click();
        Assert.Contains("tech-family--framework", component.Find("[data-testid='technology-clusters-side'] .tech-inspector__kicker .badge").ClassName, StringComparison.Ordinal);

        // Overview: the row's type code and the inspector's badge.
        component.Find("[data-testid='technology-overview-tab']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-row']")));
        var bunit = component.FindAll("[data-testid='technology-row']").Single(row => row.QuerySelector(".tech-overview__name")!.TextContent.Trim() == "bUnit");
        Assert.Contains("tech-family--language-package", bunit.QuerySelector(".tech-overview__code")!.ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-family--framework", component.Find("[data-testid='technology-overview-panel'] .tech-inspector__kicker .badge").ClassName, StringComparison.Ordinal);

        // Dependencies: the focus card and every reached card carry their family on the code.
        component.Find("[data-testid='technology-dependencies-tab']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-dependencies-card']")));
        Assert.Contains("tech-family--framework", component.Find("[data-testid='technology-dependencies-selected'] .tech-deps__code").ClassName, StringComparison.Ordinal);
        var razor = component.Find("[data-testid='technology-dependencies-card'][data-id='.tech/shared.md#razor'] .tech-deps__code");
        Assert.Contains("tech-family--framework", razor.ClassName, StringComparison.Ordinal);
        var playwright = component.Find("[data-testid='technology-dependencies-card'][data-id='.tech/desktop.md#playwright'] .tech-deps__code");
        Assert.Contains("tech-family--library-tool-plugin", playwright.ClassName, StringComparison.Ordinal);

        // Type lenses: each card's badge carries its type's family.
        component.Find("[data-testid='technology-type-lenses-tab']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-lens-card']")));
        Assert.Contains("tech-family--runtime", LensBadge(component, "runtime").ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-family--language-package", LensBadge(component, "package").ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-family--library-tool-plugin", LensBadge(component, "tool").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_layer_opens_from_the_side_panel_list_too()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        component.Find("[data-testid='technology-clusters-layer-row'][data-layer='shared.md']").Click();

        Assert.Equal(["Razor", ".NET"], PillNames(component));
        Assert.Equal(["Razor", ".NET"], component.FindAll("[data-testid='technology-clusters-technology-row'] .tech-clusters__row-name").Select(name => name.TextContent.Trim()).ToArray());
    }

    [Fact]
    public async Task Selecting_a_technology_lights_its_links_and_dims_the_rest()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();

        Pill(component, "desktop.md#blazor").Click();

        Assert.Equal("Blazor", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());
        Assert.Equal("true", Pill(component, "desktop.md#blazor").GetAttribute("aria-pressed"));
        Assert.Contains("tech-clusters__pill--used-by", Pill(component, "desktop.md#playwright").ClassName, StringComparison.Ordinal);

        // Gold to Shared, which it sits on; blue to Playwright beside it and to Testing, which use it.
        Assert.Contains("tech-clusters__satellite--sits-on", Satellite(component, "shared.md").ClassName, StringComparison.Ordinal);
        Assert.Contains("tech-clusters__satellite--used-by", Satellite(component, "testing.md").ClassName, StringComparison.Ordinal);
        Assert.Single(component.FindAll("[data-testid='technology-clusters-line'][data-tone='sits-on']"));
        Assert.Equal(2, component.FindAll("[data-testid='technology-clusters-line'][data-tone='used-by']").Count);

        // The panel shows its measures, and every link carries its layer.
        Assert.Equal("Blazor", component.Find("[data-testid='technology-clusters-side'] .tech-inspector__title").TextContent.Trim());
        Assert.Contains("Rests on", component.Find("[data-testid='technology-clusters-side'] [data-testid='technology-inspector-facts']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Breaks if it changes", component.Find("[data-testid='technology-clusters-side'] [data-testid='technology-inspector-facts']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Shared", Related(component, "sits-on", "Razor").TextContent, StringComparison.Ordinal);
        Assert.Contains("here", Related(component, "used-by", "Playwright").TextContent, StringComparison.Ordinal);
        Assert.Contains("Testing", Related(component, "used-by", "bUnit").TextContent, StringComparison.Ordinal);

        // Playwright uses only Blazor, so both satellites dim when it is selected.
        Pill(component, "desktop.md#playwright").Click();
        Assert.All(component.FindAll("[data-testid='technology-clusters-satellite']"), satellite =>
            Assert.Contains("tech-clusters__satellite--dimmed", satellite.ClassName, StringComparison.Ordinal));
    }

    /// <summary>The side panel is the same inspector the Overview draws, so the
    /// section the selection's type chooses renders in it too: Blazor is a
    /// framework, so the stack lens; Playwright is a tool, which has none.</summary>
    [Fact]
    public async Task The_side_panel_shows_the_lens_the_selections_type_chooses()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();

        Pill(component, "desktop.md#blazor").Click();

        var lens = component.Find("[data-testid='technology-clusters-side'] [data-testid='technology-inspector'] [data-testid='technology-lens']");
        Assert.Equal("stack", lens.GetAttribute("data-lens"));

        Pill(component, "desktop.md#playwright").Click();

        Assert.Empty(component.FindAll("[data-testid='technology-clusters-side'] [data-testid='technology-lens']"));
    }

    [Fact]
    public async Task Following_a_link_to_another_layer_opens_that_layer_and_shares_the_selection()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();
        Pill(component, "desktop.md#blazor").Click();

        Related(component, "sits-on", "Razor").Click();

        Assert.Equal(["Razor", ".NET"], PillNames(component));
        Assert.Equal("Shared", component.Find("[data-testid='technology-clusters-crumb-layer']").TextContent.Trim());
        Assert.Equal("Razor", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());
        Assert.Equal("true", Pill(component, "shared.md#razor").GetAttribute("aria-pressed"));

        // The selection is the panel's: Overview's inspector shows it.
        component.Find("[data-testid='technology-overview-tab']").Click();
        component.WaitForAssertion(() => Assert.Equal("Razor", component.Find("[data-testid='technology-overview-panel'] .tech-inspector__title").TextContent.Trim()));
    }

    [Fact]
    public async Task The_trail_keeps_the_last_six_and_steps_back_to_any_of_them()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();

        Pill(component, "desktop.md#playwright").Click();
        Related(component, "sits-on", "Blazor").Click();
        Related(component, "sits-on", "Razor").Click();

        // Blazor first: the view opened on the shared selection.
        Assert.Equal(["Blazor", "Playwright", "Blazor", "Razor"], TrailNames(component));

        component.FindAll("[data-testid='technology-clusters-trail-step']")[1].Click();

        Assert.Equal("Playwright", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());
        Assert.Equal(["Blazor", "Playwright"], PillNames(component));
        Assert.Equal(["Blazor", "Playwright"], TrailNames(component));
        Related(component, "sits-on", "Blazor").Click();

        // Back and forth between two: the trail keeps the last six visits.
        for (var visit = 0; visit < 4; visit++)
        {
            Related(component, "sits-on", "Razor").Click();
            Related(component, "used-by", "Blazor").Click();
        }

        Assert.Equal(["Razor", "Blazor", "Razor", "Blazor", "Razor", "Blazor"], TrailNames(component));
    }

    [Fact]
    public async Task Back_retraces_the_trail_then_leaves_the_technology_then_the_layer()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();
        Pill(component, "desktop.md#playwright").Click();
        Related(component, "sits-on", "Blazor").Click();
        Related(component, "sits-on", "Razor").Click();

        var back = () => component.Find("[data-testid='technology-clusters-back']").Click();

        back();
        Assert.Equal("Blazor", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());
        Assert.Equal(["Blazor", "Playwright"], PillNames(component));

        back();
        Assert.Equal("Playwright", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());

        // The trail began on Blazor, the selection the view opened on.
        back();
        Assert.Equal("Blazor", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());

        back();
        Assert.Empty(component.FindAll("[data-testid='technology-clusters-crumb-technology']"));
        Assert.Equal("Desktop", component.Find("[data-testid='technology-clusters-crumb-layer']").TextContent.Trim());

        back();
        Assert.Empty(component.FindAll("[data-testid='technology-clusters-crumb-layer']"));
        Assert.Equal(3, component.FindAll("[data-testid='technology-clusters-cluster']").Count);
        Assert.True(component.Find("[data-testid='technology-clusters-back']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task The_layer_crumb_leaves_the_technology_and_All_layers_goes_home()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();
        component.Find("[data-testid='technology-clusters-technology-row'][data-id='.tech/desktop.md#blazor']").Click();

        Assert.Equal("Blazor", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim());

        component.Find("[data-testid='technology-clusters-crumb-layer']").Click();
        Assert.Empty(component.FindAll("[data-testid='technology-clusters-crumb-technology']"));
        Assert.All(component.FindAll("[data-testid='technology-clusters-pill']"), pill => Assert.Equal("false", pill.GetAttribute("aria-pressed")));

        component.Find("[data-testid='technology-clusters-crumb-all']").Click();
        Assert.Equal(3, component.FindAll("[data-testid='technology-clusters-cluster']").Count);
    }

    [Fact]
    public async Task The_view_opens_on_the_selection_another_view_made()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        component.Find("[data-testid='technology-overview-tab']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-row']")));
        component.FindAll("[data-testid='technology-row']")
            .Single(row => row.QuerySelector(".tech-overview__name")!.TextContent.Trim() == "bUnit")
            .Click();

        component.Find("[data-testid='technology-clusters-tab']").Click();

        component.WaitForAssertion(() => Assert.Equal("bUnit", component.Find("[data-testid='technology-clusters-crumb-technology']").TextContent.Trim()));
        Assert.Equal("Testing", component.Find("[data-testid='technology-clusters-crumb-layer']").TextContent.Trim());
        Assert.Equal("true", Pill(component, "testing.md#bunit").GetAttribute("aria-pressed"));
        Assert.Equal("true", component.Find("[data-testid='technology-clusters-technology-row'][data-id='.tech/testing.md#bunit']").GetAttribute("aria-pressed"));
    }

    /// <summary>Blazor is on the trail twice; only the visit the reader is on is current.</summary>
    [Fact]
    public async Task A_technology_on_the_trail_twice_is_current_only_at_its_latest_step()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();

        Pill(component, "desktop.md#playwright").Click();
        Related(component, "sits-on", "Blazor").Click();

        Assert.Equal(["Blazor", "Playwright", "Blazor"], TrailNames(component));
        Assert.Equal(
            [null, null, "step"],
            component.FindAll("[data-testid='technology-clusters-trail-step']").Select(step => step.GetAttribute("aria-current")).ToArray());
        Assert.Single(component.FindAll(".tech-clusters__trail-step--current"));
    }

    /// <summary>The pressed link belongs to the technology just left and is gone
    /// once the inspector redraws, so focus goes to the new title, not the page.</summary>
    [Fact]
    public async Task Following_a_link_in_the_inspector_moves_focus_to_the_new_title()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();
        Pill(component, "desktop.md#blazor").Click();

        var before = FocusCalls(harness);

        Related(component, "sits-on", "Razor").Click();

        component.WaitForAssertion(() =>
        {
            var title = component.Find("[data-testid='technology-clusters-side'] .tech-inspector__title");
            Assert.Equal("Razor", title.TextContent.Trim());
            Assert.Equal("-1", title.GetAttribute("tabindex"));
            // One focus call, made after the follow; its target is the title, the inspector's only captured element.
            Assert.Equal(before + 1, FocusCalls(harness));
        });
    }

    [Fact]
    public async Task The_inspector_renders_a_code_span_in_the_description_as_code()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();

        Pill(component, "desktop.md#blazor").Click();

        var lede = component.Find("[data-testid='technology-clusters-side'] .tech-inspector__lede");
        Assert.DoesNotContain("`", lede.TextContent, StringComparison.Ordinal);
        Assert.Equal("Microsoft.AspNetCore.Components", lede.QuerySelector("code")!.TextContent.Trim());

        // The lede is the first paragraph; the list after it stays out.
        Assert.DoesNotContain("Used for", lede.TextContent, StringComparison.Ordinal);
    }

    /// <summary>The control pressed leaves the screen — a cluster gives way to the
    /// layer, the trail collapses, Back disables itself — so focus goes to where the
    /// reader now is, never to the page.</summary>
    [Fact]
    public async Task Focus_goes_to_where_the_reader_is_when_the_pressed_control_leaves()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();

        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();
        component.WaitForAssertion(() => Assert.Equal(1, FocusCalls(harness)));
        Assert.Equal("-1", component.Find("[data-testid='technology-clusters-layer-title']").GetAttribute("tabindex"));

        Pill(component, "desktop.md#playwright").Click();
        Related(component, "sits-on", "Blazor").Click();
        var focusCalls = FocusCalls(harness);

        component.FindAll("[data-testid='technology-clusters-trail-step']")[0].Click();
        component.WaitForAssertion(() => Assert.Equal(focusCalls + 1, FocusCalls(harness)));
        Assert.Equal("-1", component.Find("[data-testid='technology-clusters-crumb-technology']").GetAttribute("tabindex"));

        component.Find("[data-testid='technology-clusters-crumb-layer']").Click();
        component.Find("[data-testid='technology-clusters-back']").Click();
        component.WaitForAssertion(() => Assert.Equal(focusCalls + 2, FocusCalls(harness)));
        Assert.Equal("-1", component.Find("[data-testid='technology-clusters-stack-title']").GetAttribute("tabindex"));
    }

    /// <summary>Overview keeps its inspector while Clusters draws one, so each
    /// carries its own ids and each labels itself.</summary>
    [Fact]
    public async Task Two_inspectors_on_the_page_never_share_an_id()
    {
        await using var harness = CreateHarness();
        var component = await harness.RenderClustersAsync();
        component.Find("[data-testid='technology-clusters-cluster'][data-layer='desktop.md']").Click();
        Pill(component, "desktop.md#blazor").Click();

        var inspectors = component.FindAll("[data-testid='technology-inspector']");
        Assert.Equal(2, inspectors.Count);
        var ids = component.FindAll("[data-testid='technology-devbook-panel'] [id]").Select(element => element.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(inspectors, inspector => Assert.Equal(
            inspector.QuerySelector(".tech-inspector__title")!.Id,
            inspector.GetAttribute("aria-labelledby")));
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static AngleSharp.Dom.IElement Dot(IRenderedComponent<TechnologyDevbookPanel> component, string id) =>
        component.Find($"[data-testid='technology-clusters-dot'][data-id='.tech/{id}']");

    private static AngleSharp.Dom.IElement Cluster(IRenderedComponent<TechnologyDevbookPanel> component, string layer) =>
        component.Find($"[data-testid='technology-clusters-cluster'][data-layer='{layer}']");

    private static AngleSharp.Dom.IElement LensBadge(IRenderedComponent<TechnologyDevbookPanel> component, string type) =>
        component.Find($"[data-testid='technology-lens-card'][data-type='{type}'] .tech-lens-card__kicker .badge");

    private static AngleSharp.Dom.IElement Group(IRenderedComponent<TechnologyDevbookPanel> component, string layer) =>
        component.Find($"[data-testid='technology-clusters-cluster-group'][data-layer='{layer}']");

    private static AngleSharp.Dom.IElement Pill(IRenderedComponent<TechnologyDevbookPanel> component, string id) =>
        component.Find($"[data-testid='technology-clusters-pill'][data-id='.tech/{id}']");

    private static AngleSharp.Dom.IElement Satellite(IRenderedComponent<TechnologyDevbookPanel> component, string layer) =>
        component.Find($"[data-testid='technology-clusters-satellite'][data-layer='{layer}']");

    private static AngleSharp.Dom.IElement Related(IRenderedComponent<TechnologyDevbookPanel> component, string list, string name) =>
        component.FindAll($"[data-testid='technology-clusters-side'] [data-testid='technology-inspector-{list}'] [data-testid='technology-inspector-related']")
            .Single(button => button.TextContent.Trim().StartsWith(name, StringComparison.Ordinal));

    private static string[] PillNames(IRenderedComponent<TechnologyDevbookPanel> component) =>
        [.. component.FindAll("[data-testid='technology-clusters-pill'] .tech-clusters__pill-label").Select(label => label.TextContent.Trim())];

    private static int FocusCalls(Harness harness) =>
        harness.Context.JSInterop.Invocations.Count(invocation => invocation.Identifier.EndsWith(".focus", StringComparison.Ordinal));

    private static string[] TrailNames(IRenderedComponent<TechnologyDevbookPanel> component) =>
        [.. component.FindAll("[data-testid='technology-clusters-trail-step']").Select(step => step.TextContent.Trim())];

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-technology-clusters-tests", Guid.NewGuid().ToString("n"));
        var repository = Path.Combine(root, "repo");
        var tech = Path.Combine(repository, ".tech");
        Directory.CreateDirectory(tech);
        _roots.Add(root);

        File.WriteAllText(Path.Combine(tech, "technology-graph.md"), TechnologyGraph);
        File.WriteAllText(Path.Combine(tech, "shared.md"), SharedLayer);
        File.WriteAllText(Path.Combine(tech, "desktop.md"), DesktopLayer);
        File.WriteAllText(Path.Combine(tech, "testing.md"), TestingLayer);

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        Assert.Null(gitHubSettings.SetRepositories(repositories));
        gitHubSettings.SetCloneDirectory("backlog", repository);

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var folders = new RecordingDevbookFolderSource(
            new DevbookFolderSource(gitHubSettings),
            Path.Combine(tech, "shared.md"));

        context.Services.AddSingleton<IDevbookFolderSource>(folders);
        context.Services.AddSingleton<TechnologyDevbookService>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();

        return new Harness(context);
    }

    private const string TechnologyGraph = """
        # Technology graph
        ```meta
        status: draft
        ```

        Repository technology overview.
        """;

    private const string SharedLayer = """
        # Shared Technologies
        ```meta
        status: adopted
        type: layer
        ```

        Shared platform choices.

        ## .NET
        ```meta
        status: adopted
        type: runtime
        ```

        Cross-platform runtime.

        ## Razor
        ```meta
        status: adopted
        type: framework
        depends-on: [".tech/shared.md#net"]
        ```

        Component syntax.
        """;

    private const string DesktopLayer = """
        # Desktop Stack
        ```meta
        status: adopted
        type: layer
        ```

        Desktop UI choices.

        ## Blazor
        ```meta
        status: trial
        type: framework
        depends-on: [".tech/shared.md#razor", ".tech/shared.md#net"]
        ```

        Component model, hosted by `Microsoft.AspNetCore.Components`.

        - **Used for** — every screen.

        ## Playwright
        ```meta
        status: candidate
        type: tool
        depends-on: [".tech/desktop.md#blazor"]
        ```

        Browser tests.
        """;

    private const string TestingLayer = """
        # Testing
        ```meta
        status: adopted
        type: layer
        ```

        Test choices.

        ## bUnit
        ```meta
        status: adopted
        type: package
        depends-on: [".tech/shared.md#razor", ".tech/desktop.md#blazor"]
        ```

        Component tests.
        """;

    private sealed record Harness(BunitContext Context) : IAsyncDisposable
    {
        /// <summary>Renders the panel, waits for the folder to load, and opens Clusters.</summary>
        public async Task<IRenderedComponent<TechnologyDevbookPanel>> RenderClustersAsync()
        {
            var component = Context.Render<TechnologyDevbookPanel>(parameters => parameters.Add(panel => panel.RepositoryAlias, "backlog"));
            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-row']")));
            await component.InvokeAsync(() => { });

            component.Find("[data-testid='technology-clusters-tab']").Click();
            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-clusters']")));

            // Every test starts from All layers; the one about arriving checks where the view opens.
            component.Find("[data-testid='technology-clusters-crumb-all']").Click();
            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='technology-clusters-cluster']")));
            return component;
        }

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
