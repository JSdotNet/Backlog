namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The collapse rule. It is a property of the set rather than of any member, so
/// every assertion here is about which acts a reader can see given a budget —
/// never about how one of them looks.
/// </summary>
public sealed class IntegrationActionBarTests
{
    private static IntegrationActionSpec Standard(string id) => new(id, id);

    private static IReadOnlyList<IntegrationActionSpec> Standards(int count) =>
        [.. Enumerable.Range(1, count).Select(index => Standard($"a{index}"))];

    private static IRenderedComponent<IntegrationActionBar> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<IntegrationActionBar>> parameters)
    {
        // A copy action renders CopyButton, which reaches for the clipboard the
        // moment it is pressed. Nothing here presses it, but the loose mode keeps
        // a bar that contains one renderable without a registered module.
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<IntegrationActionBar>(builder =>
        {
            builder.Add(bar => bar.ActionTestIdPrefix, "act");
            builder.Add(bar => bar.OverflowTestId, "overflow");
            parameters(builder);
        });
    }

    [Theory]
    [InlineData(IntegrationDensity.Toolbar, 4)]
    [InlineData(IntegrationDensity.Inline, 3)]
    [InlineData(IntegrationDensity.Compact, 2)]
    [InlineData(IntegrationDensity.Menu, 0)]
    public void Each_density_shows_its_own_budget(IntegrationDensity density, int budget)
    {
        // Deliberately tighter than the six acts this family ships, so the
        // resting state of a busy surface is a short row and a menu.
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, Standards(6))
            .Add(b => b.Density, density));

        Assert.Equal(budget, bar.FindAll("button[data-testid^='act-']").Count);
    }

    [Fact]
    public void An_overflow_of_exactly_one_collapses_to_zero()
    {
        // A menu holding one item costs a click and buys nothing.
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, Standards(4))
            .Add(b => b.Density, IntegrationDensity.Inline));

        Assert.Equal(4, bar.FindAll("button[data-testid^='act-']").Count);
        Assert.Empty(bar.FindAll("[data-testid='overflow']"));
    }

    [Fact]
    public void An_overflow_of_two_stays_a_menu()
    {
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, Standards(5))
            .Add(b => b.Density, IntegrationDensity.Inline));

        Assert.Equal(3, bar.FindAll("button[data-testid^='act-']").Count);
        Assert.Single(bar.FindAll("[data-testid='overflow']"));
    }

    [Fact]
    public void An_act_the_host_marked_overflow_is_never_pulled_back_out_of_it()
    {
        // Rule 2 removes it unconditionally, so the one-item collapse must not
        // undo a choice the host made explicitly.
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, new IntegrationActionSpec[]
            {
                Standard("a1"),
                new("hidden", "Hidden", Prominence: IntegrationProminence.Overflow)
            })
            .Add(b => b.Density, IntegrationDensity.Toolbar));

        Assert.Single(bar.FindAll("button[data-testid^='act-']"));
        Assert.Single(bar.FindAll("[data-testid='overflow']"));
    }

    [Theory]
    [InlineData(IntegrationDensity.Toolbar)]
    [InlineData(IntegrationDensity.Inline)]
    [InlineData(IntegrationDensity.Compact)]
    [InlineData(IntegrationDensity.Menu)]
    public void The_copy_act_is_present_at_every_density(IntegrationDensity density)
    {
        // It costs no budget and never collapses, because a MenuItem has no
        // interop and no status line: a copy in the menu would be a row that
        // claims to copy and silently does not.
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, new IntegrationActionSpec[]
            {
                Standard("a1"), Standard("a2"), Standard("a3"), Standard("a4"), Standard("a5"),
                new("copy", "Copy prompt", CopyText: "the prompt")
            })
            .Add(b => b.Density, density));

        Assert.Single(bar.FindAll("[data-testid='act-copy']"));
    }

    [Fact]
    public void A_copy_act_is_rendered_by_CopyButton_and_not_by_the_lifecycle()
    {
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, new IntegrationActionSpec[] { new("copy", "Copy prompt", CopyText: "the prompt") }));

        // CopyButton's own status line, which is the part that keeps being
        // dropped when a copy is reimplemented.
        Assert.Single(bar.FindAll("[data-testid='act-copy-status'][role='status']"));
    }

    [Fact]
    public void A_primary_act_is_pinned_at_every_density_which_is_what_makes_Ask_AI_everywhere()
    {
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, new IntegrationActionSpec[]
            {
                Standard("a1"), Standard("a2"), Standard("a3"), Standard("a4"), Standard("a5"),
                new("ask", "Ask AI", Prominence: IntegrationProminence.Primary)
            })
            .Add(b => b.Density, IntegrationDensity.Compact));

        Assert.Single(bar.FindAll("[data-testid='act-ask']"));
    }

    [Fact]
    public void A_running_act_survives_an_over_budget_render()
    {
        // Collapse is recomputed every render, and a bar whose contents change
        // mid-act could otherwise push a spinner into a closed menu while the
        // reader is watching it.
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, new IntegrationActionSpec[]
            {
                Standard("a1"),
                Standard("a2"),
                Standard("a3"),
                Standard("a4"),
                new("busy", "Run in Copilot CLI", State: IntegrationActionState.Running)
            })
            .Add(b => b.Density, IntegrationDensity.Compact));

        var running = bar.Find("[data-testid='act-busy']");

        Assert.Equal("true", running.GetAttribute("aria-busy"));
    }

    [Fact]
    public void An_unavailable_act_is_never_hidden_by_being_unavailable()
    {
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, new IntegrationActionSpec[]
            {
                new("a1", "One", Readiness: IntegrationReadiness.Offline()),
                Standard("a2")
            })
            .Add(b => b.Density, IntegrationDensity.Toolbar));

        Assert.True(bar.Find("[data-testid='act-a1']").HasAttribute("disabled"));
    }

    [Fact]
    public void An_act_in_the_menu_carries_its_reason_in_its_label()
    {
        // A menu row has no second line for a described-by, so losing the reason
        // on the way into the menu would leave the exact shape this family exists
        // to prevent: a greyed row that says nothing about why.
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, new IntegrationActionSpec[]
            {
                Standard("a1"),
                Standard("a2"),
                new("blocked", "Open in VS Code", Readiness: IntegrationReadiness.NotInstalled("VS Code")),
                new("also", "Also blocked", Readiness: IntegrationReadiness.Offline())
            })
            .Add(b => b.Density, IntegrationDensity.Compact)
            .Add(b => b.MenuTestId, "menu"));

        bar.Find("[data-testid='overflow']").Click();

        var row = bar.Find("[data-testid='menu-item-blocked']");

        Assert.Contains("Open in VS Code", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("not installed", row.TextContent, StringComparison.Ordinal);
        Assert.True(row.HasAttribute("disabled"));
    }

    [Fact]
    public void Selecting_a_menu_row_raises_invoke_with_the_spec_it_came_from()
    {
        using var context = new BunitContext();
        IntegrationActionSpec? invoked = null;

        var bar = Render(context, p => p
            .Add(b => b.Actions, Standards(5))
            .Add(b => b.Density, IntegrationDensity.Inline)
            .Add(b => b.MenuTestId, "menu")
            .Add(b => b.OnInvoke, spec => invoked = spec));

        bar.Find("[data-testid='overflow']").Click();
        bar.Find("[data-testid='menu-item-a4']").Click();

        Assert.Equal("a4", invoked?.Id);
    }

    [Fact]
    public void The_cluster_readiness_overrides_each_act_and_disables_the_trigger()
    {
        // Nothing can be individually ready when the thing they all go through is
        // not — and the trigger says so rather than the bar disappearing.
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, Standards(5))
            .Add(b => b.Density, IntegrationDensity.Inline)
            .Add(b => b.Readiness, IntegrationReadiness.NotAuthorized("GitHub")));

        Assert.All(
            bar.FindAll("button[data-testid^='act-a']"),
            button => Assert.True(button.HasAttribute("disabled")));

        var trigger = bar.Find("[data-testid='overflow']");

        Assert.True(trigger.HasAttribute("disabled"));
        Assert.Equal("GitHub is not connected.", trigger.GetAttribute("title"));
    }

    [Fact]
    public void A_copy_stays_available_when_the_cluster_is_not()
    {
        // The clipboard is local. Refusing a copy because a token expired would
        // be the product withholding something it can plainly still do.
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, new IntegrationActionSpec[] { new("copy", "Copy prompt", CopyText: "the prompt") })
            .Add(b => b.Readiness, IntegrationReadiness.Offline()));

        Assert.False(bar.Find("[data-testid='act-copy']").HasAttribute("disabled"));
    }

    [Fact]
    public void The_bar_never_reorders_what_it_was_given()
    {
        // Ordering is a host judgement about that surface, and a bar that sorted
        // would be fighting it.
        using var context = new BunitContext();

        var bar = Render(context, p => p
            .Add(b => b.Actions, new IntegrationActionSpec[]
            {
                Standard("first"),
                new("copy", "Copy", CopyText: "text"),
                new("ask", "Ask AI", Prominence: IntegrationProminence.Primary),
                Standard("last")
            })
            .Add(b => b.Density, IntegrationDensity.Toolbar));

        Assert.Equal(
            new[] { "act-first", "act-copy", "act-ask", "act-last" },
            bar.FindAll("button[data-testid^='act-']").Select(element => element.GetAttribute("data-testid")));
    }
}
