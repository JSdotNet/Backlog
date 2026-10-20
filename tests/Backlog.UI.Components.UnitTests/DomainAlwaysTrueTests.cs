using Backlog.SharedKernel.Devbook;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// An aggregate's Always true tab: each rule's claim behind its mark, grouped
/// under a heading that opens the concept, with where each is enforced folded
/// until the reader asks for it.
/// </summary>
public sealed class DomainAlwaysTrueTests
{
    private static DomainChapterFile Page => DomainUnits.Parse(DomainRulesTests.Domain, DomainRulesTests.DomainPath);

    private static DomainChapterFile Rules => DomainUnits.Parse(DomainRulesTests.Invariants, DomainRulesTests.InvariantsPath);

    private static IRenderedComponent<DomainUnitPage> Render(BunitContext context, Action<ComponentParameterCollectionBuilder<DomainUnitPage>>? more = null, bool withRules = true)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render<DomainUnitPage>(parameters =>
        {
            parameters.Add(p => p.File, Page);
            if (withRules) parameters.Add(p => p.InvariantFiles, [Rules]);
            more?.Invoke(parameters);
        });
    }

    [Fact]
    public void The_aggregate_s_always_true_tab_lists_its_rules_by_concept_with_their_marks()
    {
        using var context = new BunitContext();
        var page = Render(context);

        page.Find("[data-testid='domain-unit-always-true-tab']").Click();

        var groups = page.FindAll("[data-testid='domain-rule-group']");
        Assert.Equal(["Order", "Order Line"], groups.Select(group => group.GetAttribute("data-concept")));
        var rules = page.FindAll("[data-testid='domain-rule']");
        Assert.Equal(["proved", "open", "unproved", "unproved", "proved"], rules.Select(rule => rule.GetAttribute("data-proof")));
        Assert.Equal(["✓", "?", "○", "○", "✓"], rules.Select(rule => rule.QuerySelector(".domain-rules__mark")!.TextContent));
        Assert.Contains("Proved by a test", rules[0].QuerySelector(".sr-only")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("A placed order cannot be placed again.", rules[0].QuerySelector(".domain-rules__claim")!.TextContent);
    }

    [Fact]
    public void Where_each_rule_is_enforced_shows_only_when_asked()
    {
        using var context = new BunitContext();
        var page = Render(context);
        page.Find("[data-testid='domain-unit-always-true-tab']").Click();

        Assert.Empty(page.FindAll("[data-testid='domain-rule-enforced']"));
        Assert.DoesNotContain("Place()", page.Find("[data-testid='domain-always-true']").TextContent, StringComparison.Ordinal);

        var toggle = page.Find("[data-testid='domain-rules-show-enforced']");
        Assert.Equal("Show where each is enforced", toggle.TextContent.Trim());
        Assert.Equal("false", toggle.GetAttribute("aria-pressed"));
        toggle.Click();

        var shown = page.FindAll("[data-testid='domain-rule-enforced']");
        Assert.Equal(5, shown.Count);
        Assert.Equal("Enforced at: Place()", shown[0].TextContent);
        Assert.Equal("Place()", shown[0].QuerySelector("code")!.TextContent);
        Assert.Equal("true", page.Find("[data-testid='domain-rules-show-enforced']").GetAttribute("aria-pressed"));

        page.Find("[data-testid='domain-rules-show-enforced']").Click();
        Assert.Empty(page.FindAll("[data-testid='domain-rule-enforced']"));
    }

    [Fact]
    public void A_group_heading_opens_its_concept_on_the_unit_s_about()
    {
        using var context = new BunitContext();
        DevbookReference? left = null;
        var page = Render(context, parameters => parameters.Add(p => p.OnNavigate, (DevbookReference reference) => left = reference));
        page.Find("[data-testid='domain-unit-always-true-tab']").Click();

        var link = page.FindAll("[data-testid='domain-rule-group-link']")[1];
        Assert.Contains("#order-line", link.GetAttribute("title") ?? link.OuterHtml, StringComparison.Ordinal);
        link.Click();

        // The concept is in this file, so the page turns to the card on About
        // instead of leaving.
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='domain-aggregate-about']")));
        Assert.Contains("Order Line", page.Find("[data-testid='domain-unit-member']").TextContent, StringComparison.Ordinal);
        Assert.Null(left);
    }

    [Fact]
    public void A_shared_grouping_s_rules_show_on_the_base_page()
    {
        using var context = new BunitContext();
        var page = Render(context);

        page.Find("[data-testid='domain-unit-tab-shared-value-objects']").Click();

        var group = Assert.Single(page.FindAll("[data-testid='domain-rule-group']"));
        Assert.Equal("Money", group.GetAttribute("data-concept"));
        Assert.Equal("An amount names exactly one currency.", group.QuerySelector(".domain-rules__claim")!.TextContent);
    }

    [Fact]
    public void An_aggregate_with_no_rules_says_so()
    {
        using var context = new BunitContext();
        var page = Render(context, withRules: false);
        page.Find("[data-testid='domain-unit-always-true-tab']").Click();

        Assert.Contains("Order", page.Find("[data-testid='domain-always-true-empty']").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid='domain-rules-show-enforced']"));
    }
}
