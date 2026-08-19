namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The panel owns an order and one precedence rule. There is nothing else in it,
/// which is why there is nothing else in here.
/// </summary>
public sealed class IntegrationsPanelTests
{
    private static readonly IntegrationLinkRef Issue =
        IntegrationLinkRef.Issue("128", "#128", IntegrationArtifactState.Open);

    private static readonly IntegrationActionSpec Create =
        new("create-issue", "Create GitHub issue", IntegrationProvider.GitHub);

    private static IRenderedComponent<IntegrationsPanel> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<IntegrationsPanel>> parameters)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<IntegrationsPanel>(builder =>
        {
            builder.Add(panel => panel.TestId, "panel");
            builder.Add(panel => panel.HeaderTestId, "header");
            builder.Add(panel => panel.ActionsTestId, "actions");
            builder.Add(panel => panel.LinksTestId, "links");
            parameters(builder);
        });
    }

    [Fact]
    public void The_order_is_header_then_acts_then_references_then_whatever_the_surface_adds()
    {
        // Without a fixed order every surface composes the same three parts its
        // own way, and half the consistency this family exists for goes unmet.
        using var context = new BunitContext();

        var panel = Render(context, p => p
            .Add(x => x.Actions, new[] { Create })
            .Add(x => x.Links, new[] { Issue })
            .Add(x => x.ChildContent, builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "data-testid", "extra");
                builder.CloseElement();
            }));

        var order = panel.Find("[data-testid='panel']").Children
            .Select(child => child.GetAttribute("data-testid"))
            .ToList();

        Assert.Equal(new[] { "header", "actions", "links", "extra" }, order);
    }

    [Fact]
    public void An_unavailable_cluster_replaces_the_acts_and_keeps_the_references()
    {
        // A reference read last Tuesday is still true after the token expired,
        // and blanking it would tell the reader something false.
        using var context = new BunitContext();

        var panel = Render(context, p => p
            .Add(x => x.Actions, new[] { Create })
            .Add(x => x.Links, new[] { Issue })
            .Add(x => x.Readiness, IntegrationReadiness.NotAuthorized("GitHub")));

        Assert.Empty(panel.FindAll("[data-testid='actions']"));
        Assert.Contains("GitHub is not connected.", panel.Find(".integration-unavailable").TextContent, StringComparison.Ordinal);
        Assert.Single(panel.FindAll("[data-testid='links'] .integration-link-list__item"));
    }

    [Fact]
    public void The_freshness_line_is_stated_once_and_only_in_the_header()
    {
        // Two copies of one fact a few pixels apart is the shape this panel
        // exists to stop, not to introduce.
        using var context = new BunitContext();

        var panel = Render(context, p => p
            .Add(x => x.Links, new[] { Issue })
            .Add(x => x.Reading, new IntegrationReading("4 minutes ago"))
            .Add(x => x.OnRefresh, () => { }));

        Assert.Single(panel.FindAll(".integration-freshness"));
        Assert.Contains("as of 4 minutes ago", panel.Find("[data-testid='header']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_callback_reaches_the_part_that_raises_it()
    {
        using var context = new BunitContext();
        IntegrationActionSpec? invoked = null;
        IntegrationLinkRef? opened = null;
        var checks = 0;

        var panel = Render(context, p => p
            .Add(x => x.Actions, new[] { Create })
            .Add(x => x.Links, new[] { Issue })
            .Add(x => x.OnInvoke, spec => invoked = spec)
            .Add(x => x.OnOpen, link => opened = link)
            .Add(x => x.OnRefresh, () => checks++));

        panel.Find("[data-testid='actions'] button").Click();
        panel.Find("[data-testid='links'] button").Click();
        panel.Find("[data-testid='header'] button").Click();

        Assert.Equal("create-issue", invoked?.Id);
        Assert.Equal("128", opened?.Id);
        Assert.Equal(1, checks);
    }

    [Fact]
    public void The_title_names_the_action_group_so_two_panels_are_two_groups()
    {
        using var context = new BunitContext();

        var panel = Render(context, p => p
            .Add(x => x.Title, "GitHub")
            .Add(x => x.Actions, new[] { Create }));

        Assert.Equal("GitHub", panel.Find(".integration-panel__title").TextContent);
        Assert.Equal("GitHub", panel.Find("[role='group']").GetAttribute("aria-label"));
    }
}
