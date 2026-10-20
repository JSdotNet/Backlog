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
        // A row is the rule's heading, the short claim; its first sentence waits
        // behind the toggle.
        Assert.Equal("An order is placed once.", rules[0].QuerySelector(".domain-rules__claim")!.TextContent);
        Assert.Empty(page.FindAll("[data-testid='domain-rule-sentence']"));
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
        Assert.Equal("A placed order cannot be placed again.", page.FindAll("[data-testid='domain-rule-sentence']")[0].TextContent);
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
        Assert.Equal("An amount has one currency.", group.QuerySelector(".domain-rules__claim")!.TextContent);
    }

    [Fact]
    public void The_tab_counts_the_rules_and_each_mark()
    {
        using var context = new BunitContext();
        var page = Render(context);

        Assert.Equal("Always true · 5", page.Find("[data-testid='domain-unit-always-true-tab']").TextContent.Trim());
        page.Find("[data-testid='domain-unit-always-true-tab']").Click();

        Assert.StartsWith("The 5 rules an Order keeps, whoever changes it,", page.Find("[data-testid='domain-rules-intro']").TextContent, StringComparison.Ordinal);
        var counts = page.FindAll("[data-testid='domain-rules-count']");
        Assert.Equal(["proved", "unproved", "open"], counts.Select(count => count.GetAttribute("data-proof")));
        Assert.Equal(["✓ proved by a unit test · 2", "○ no test linked · 2", "? open, nobody settled it · 1"], counts.Select(count => count.TextContent.Trim()));
    }

    [Fact]
    public void An_open_rule_says_nobody_has_settled_it()
    {
        using var context = new BunitContext();
        var page = Render(context);
        page.Find("[data-testid='domain-unit-always-true-tab']").Click();

        var open = page.Find("[data-testid='domain-rule'][data-proof='open']");
        Assert.Contains("Open — not built until someone decides.", open.TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid='domain-rule'][data-proof='proved'] .domain-rules__unsettled"));
    }

    [Fact]
    public void A_written_group_is_a_card_headed_by_its_words_with_what_it_is_about_beside_it()
    {
        using var context = new BunitContext();
        DevbookReference? left = null;
        var page = context.Render<DomainAlwaysTrue>(parameters => parameters
            .Add(p => p.Groups, DomainUnits.RulesOf(
                DomainUnits.Of(Page, [Grouped])[0], Page, [Grouped], [Page]))
            .Add(p => p.UnitTitle, "Order")
            .Add(p => p.OnNavigate, (DevbookReference reference) => left = reference));

        var groups = page.FindAll("[data-testid='domain-rule-group']");
        Assert.Equal(["How an order moves", "Lines and money", "Order"], groups.Select(group => group.QuerySelector("h3")!.TextContent.Trim()));
        // Its heading is words, not a link and no type marker.
        Assert.Null(groups[0].QuerySelector("h3 [data-testid='domain-rule-group-link']"));
        Assert.Null(groups[0].QuerySelector("h3 .devbook-type-marker"));
        var about = groups[1].QuerySelectorAll("[data-testid='domain-rule-group-about']");
        Assert.Equal(["Order Line", "Money"], about.Select(link => link.TextContent.Trim()));

        page.FindAll("[data-testid='domain-rule-group-about']")[0].Click();
        Assert.Equal("order-lifecycle", left?.Slug);
    }

    private static DomainChapterFile Grouped => DomainUnits.Parse(DomainRulesTests.GroupedInvariants, DomainRulesTests.InvariantsPath);

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
