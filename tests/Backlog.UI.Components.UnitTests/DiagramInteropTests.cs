namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The rendered diagram belongs to a JS library, so what is worth pinning here is
/// the call: the function name and the arguments handed across the boundary.
/// </summary>
public sealed class DiagramInteropTests
{
    [Fact]
    public void A_renderable_diagram_is_handed_to_the_diagram_renderer()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Render<DiagramView>(parameters => parameters
            .Add(d => d.Source, "graph TD; a-->b;")
            .Add(d => d.Language, "mermaid"));

        var invocation = Assert.Single(context.JSInterop.Invocations["backlogDiagrams.render"]);

        Assert.Equal("mermaid", invocation.Arguments[2]);
        Assert.Equal("graph TD; a-->b;", invocation.Arguments[3]);
    }

    [Fact]
    public void A_language_nothing_can_render_falls_back_to_the_source()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var diagram = context.Render<DiagramView>(parameters => parameters
            .Add(d => d.Source, "@startuml\nA -> B\n@enduml")
            .Add(d => d.Language, "plantuml"));

        Assert.Empty(context.JSInterop.Invocations["backlogDiagrams.render"]);
        Assert.Contains("@startuml", diagram.Find("pre.diagram-view__source").TextContent, StringComparison.Ordinal);
        Assert.Empty(diagram.FindAll(".diagram-view__rendered"));
    }

    [Fact]
    public void Only_a_real_diagram_language_gets_the_diagram_title()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var diagram = context.Render<DiagramView>(parameters => parameters
            .Add(d => d.Source, "var x = 1;")
            .Add(d => d.Language, "csharp")
            .Add(d => d.Title, "Backlog diagram"));

        Assert.Equal("Code diagram", diagram.Find(".diagram-view__title").TextContent);
    }

    [Fact]
    public void A_host_that_shows_the_source_itself_does_not_get_it_twice()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var diagram = context.Render<DiagramView>(parameters => parameters
            .Add(d => d.Source, "graph TD; a-->b;")
            .Add(d => d.Language, "mermaid")
            .Add(d => d.ShowSource, false));

        Assert.NotNull(diagram.Find(".diagram-view__rendered"));
        Assert.Empty(diagram.FindAll(".diagram-view__details"));
    }

    // A render that failed keeps its source whichever way ShowSource was set, and
    // that clause is still in the markup — but it cannot be exercised from here.
    // A JSException makes OnAfterRenderAsync clear the source it recorded and ask
    // for a redraw, so the next render tries the same call again; against a
    // runtime that fails every time, that is a loop, and in bUnit the planned
    // invocation faults synchronously so the loop is one stack. Reaching the
    // failed state means fixing the retry, which is a change to the error path
    // this work was told to leave alone.

    [Fact]
    public void The_graph_view_calls_the_function_it_was_given_with_the_data_it_was_given()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var data = new { nodes = new[] { "a" } };

        context.Render<GraphView>(parameters => parameters
            .Add(g => g.Data, data)
            .Add(g => g.JsFunction, "backlogDiagrams.renderTechnologyGraph"));

        var invocation = Assert.Single(context.JSInterop.Invocations["backlogDiagrams.renderTechnologyGraph"]);

        Assert.Same(data, invocation.Arguments[2]);
        Assert.Empty(context.JSInterop.Invocations["backlogDiagrams.renderGraph"]);
    }

    [Fact]
    public void Two_graphs_on_one_page_are_labelled_by_headings_of_their_own()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var first = context.Render<GraphView>(parameters => parameters.Add(g => g.Data, new object()));
        var second = context.Render<GraphView>(parameters => parameters.Add(g => g.Data, new object()));

        var firstId = first.Find("section").GetAttribute("aria-labelledby");
        var secondId = second.Find("section").GetAttribute("aria-labelledby");

        Assert.Equal(firstId, first.Find("h3").Id);
        Assert.NotEqual(firstId, secondId);
    }
}
