using Bunit;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The diagram's two hosts: a div that fills the explorer's frame, and a span for a
/// diagram drawn inside running text — a chapter's <c>![…](c4:&lt;key&gt;)</c> embed sits in
/// a paragraph, where a div is invalid and an HTML parser closes the paragraph on it.
/// </summary>
public sealed class C4DiagramViewTests
{
    private const string Source = """
        workspace "Backlog" "For the host tests" {
            model {
                me = person "ME" "The owner"
                backlog = softwareSystem "Prompt Backlog" "The system"
                me -> backlog "Uses"
            }
            views {
                systemLandscape "Landscape" {
                    include *
                }
            }
        }
        """;

    private static C4Diagram Diagram()
    {
        var workspace = C4DslReader.Read(Source);
        return C4LayoutEngine.Build(workspace, workspace.Views[0]);
    }

    [Fact]
    public void By_default_the_host_is_a_div()
    {
        using var context = new BunitContext();

        var view = context.Render<C4DiagramView>(parameters => parameters
            .Add(p => p.Diagram, Diagram())
            .Add(p => p.Id, "host-test"));

        var host = view.Find("[data-testid='c4-diagram']");
        Assert.Equal("DIV", host.TagName);
        Assert.NotNull(host.QuerySelector("svg#host-test"));
    }

    [Fact]
    public void Inline_the_host_is_a_span_that_stays_inside_its_paragraph()
    {
        using var context = new BunitContext();

        var view = context.Render<C4DiagramView>(parameters => parameters
            .Add(p => p.Diagram, Diagram())
            .Add(p => p.Id, "host-test")
            .Add(p => p.Inline, true));

        var host = view.Find("[data-testid='c4-diagram']");
        Assert.Equal("SPAN", host.TagName);
        Assert.Contains("c4-diagram-host--inline", host.ClassList);
        Assert.NotNull(host.QuerySelector("svg#host-test"));
    }
}
