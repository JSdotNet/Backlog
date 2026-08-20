namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The picture itself is drawn by JS, which bUnit does not run. What is worth
/// pinning here is the contract around it: the shell the renderer is handed,
/// the call it is made through, and the data that crosses the boundary
/// untouched — the component must never interpret the model.
/// </summary>
public sealed class GraphExplorerTests
{
    private static object Model() => new
    {
        nodes = new[] { new { id = "a", label = "A" } },
        edges = Array.Empty<object>()
    };

    [Fact]
    public void The_model_is_handed_to_the_graph_explorer_renderer_by_default()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var data = Model();

        context.Render<GraphExplorer>(parameters => parameters.Add(g => g.Data, data));

        var invocation = Assert.Single(context.JSInterop.Invocations["backlogGraphExplorer.render"]);

        Assert.Same(data, invocation.Arguments[2]);
    }

    [Fact]
    public void A_caller_can_point_the_explorer_at_a_renderer_of_its_own()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var data = Model();

        context.Render<GraphExplorer>(parameters => parameters
            .Add(g => g.Data, data)
            .Add(g => g.JsFunction, "backlogDiagrams.renderTechnologyGraph"));

        var invocation = Assert.Single(context.JSInterop.Invocations["backlogDiagrams.renderTechnologyGraph"]);

        Assert.Same(data, invocation.Arguments[2]);
        Assert.Empty(context.JSInterop.Invocations["backlogGraphExplorer.render"]);
    }

    [Fact]
    public void The_container_the_renderer_fills_carries_the_aria_label_it_was_given()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var explorer = context.Render<GraphExplorer>(parameters => parameters
            .Add(g => g.Data, Model())
            .Add(g => g.AriaLabel, "Crew explorer")
            .Add(g => g.StatusText, "Rendering crew..."));

        var canvas = explorer.Find(".tech-graph__canvas");

        Assert.Equal("Crew explorer", canvas.GetAttribute("aria-label"));
        Assert.Equal("Rendering crew...", explorer.Find(".tech-graph__status").TextContent);
    }

    [Fact]
    public void A_description_and_a_badge_are_only_rendered_when_they_say_something()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var bare = context.Render<GraphExplorer>(parameters => parameters.Add(g => g.Data, Model()));
        var described = context.Render<GraphExplorer>(parameters => parameters
            .Add(g => g.Data, Model())
            .Add(g => g.Title, "Delivery crew")
            .Add(g => g.Description, "People and the work they are on.")
            .Add(g => g.BadgeText, "demo"));

        Assert.Empty(bare.FindAll(".tech-graph__header p"));
        Assert.Empty(bare.FindAll(".badge"));
        Assert.Equal("Delivery crew", described.Find("h3").TextContent);
        Assert.Equal("People and the work they are on.", described.Find(".tech-graph__header p").TextContent);
        Assert.Equal("demo", described.Find(".badge").TextContent);
    }

    [Fact]
    public void An_empty_title_drops_the_header_and_names_the_section_instead()
    {
        // For a host that already names the graph around it. The section keeps a
        // name either way — the heading it no longer has would otherwise have
        // taken the section's accessible name with it.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var explorer = context.Render<GraphExplorer>(parameters => parameters
            .Add(g => g.Data, Model())
            .Add(g => g.Title, string.Empty)
            .Add(g => g.AriaLabel, "Crew explorer"));

        var section = explorer.Find("section");

        Assert.Empty(explorer.FindAll(".tech-graph__header"));
        Assert.False(section.HasAttribute("aria-labelledby"));
        Assert.Equal("Crew explorer", section.GetAttribute("aria-label"));
    }

    [Fact]
    public void Two_explorers_on_one_page_are_labelled_by_headings_of_their_own()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var first = context.Render<GraphExplorer>(parameters => parameters.Add(g => g.Data, Model()));
        var second = context.Render<GraphExplorer>(parameters => parameters.Add(g => g.Data, Model()));

        var firstId = first.Find("section").GetAttribute("aria-labelledby");
        var secondId = second.Find("section").GetAttribute("aria-labelledby");

        Assert.Equal(firstId, first.Find("h3").Id);
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public void The_same_model_is_not_rendered_twice()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var data = Model();

        var explorer = context.Render<GraphExplorer>(parameters => parameters.Add(g => g.Data, data));
        explorer.Render(parameters => parameters.Add(g => g.Data, data));

        Assert.Single(context.JSInterop.Invocations["backlogGraphExplorer.render"]);
    }

    [Fact]
    public void A_new_model_is_rendered_again()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var explorer = context.Render<GraphExplorer>(parameters => parameters.Add(g => g.Data, Model()));
        explorer.Render(parameters => parameters.Add(g => g.Data, Model()));

        Assert.Equal(2, context.JSInterop.Invocations["backlogGraphExplorer.render"].Count);
    }
}
