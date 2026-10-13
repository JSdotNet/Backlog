namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// When the explorer hands its diagram to the viewer.
/// <para>
/// It used to attach after every render. Blazor leaves unchanged markup in the DOM,
/// so the viewer was re-attached to the same SVG element, whose viewBox it had
/// already zoomed, and read that back as the picture's home. Every attach also
/// reported the zoom as 100%, which re-rendered, which attached again. Fit then went
/// back only to the last zoomed box, the zoom label snapped to 100% after every
/// wheel tick, and the zoom limits were measured from a home that moved with each
/// step.
/// </para>
/// <para>
/// So the viewer is attached when the picture changes: another view, or a new
/// workspace that may have drawn the same view differently. A render that leaves the
/// picture alone, such as a zoom report, presentation mode or the Highlighter
/// opening, leaves the viewer alone too.
/// </para>
/// </summary>
public sealed class C4ExplorerAttachTests : IDisposable
{
    private const string Attach = "backlogC4Explorer.attach";

    private const string Source = """
        workspace "Backlog" "For the attach tests" {
            !identifiers hierarchical
            model {
                me = person "ME" "The owner"
                backlog = softwareSystem "Prompt Backlog" "The system" {
                    properties {
                        "status" "Active"
                    }
                    desktop = container "Desktop App" "Windows client" ".NET MAUI"
                    store = container "Local Task Store" "Tasks" "SQLite" "Database"
                }
                me -> backlog.desktop "Captures work"
                backlog.desktop -> backlog.store "Reads and writes"
            }
            views {
                systemLandscape "landscape" "Everything" { include * }
                container backlog "containers" "Container Diagram" { include * }
            }
        }
        """;

    /// <summary>The same model under another workspace name and with no views, so the
    /// explorer has nothing to draw.</summary>
    private const string NoViews = """
        workspace "Nothing drawn" "For the attach tests" {
            !identifiers hierarchical
            model {
                me = person "ME" "The owner"
            }
        }
        """;

    private readonly BunitContext _context = new();

    public C4ExplorerAttachTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;

        // The viewer answers whether it found a picture to take over, and a false
        // answer is retried on the next render. Loose mode would answer false, the
        // default, so a viewer that takes over has to be planned.
        _context.JSInterop.Setup<bool>(Attach, _ => true).SetResult(true);
    }

    public void Dispose() => _context.Dispose();

    private static C4Workspace Workspace() => C4DslReader.Read(Source);

    private IRenderedComponent<C4Explorer> Render(C4Workspace workspace) => Render(_context, workspace);

    private static IRenderedComponent<C4Explorer> Render(BunitContext context, C4Workspace workspace) =>
        context.Render<C4Explorer>(parameters => parameters
            .Add(c => c.Workspace, workspace)
            .Add(c => c.ViewKey, "landscape"));

    private int Attaches => _context.JSInterop.Invocations[Attach].Count;

    private static string DrawnLabel(IRenderedComponent<C4Explorer> explorer) =>
        explorer.Find("[data-testid=c4-diagram] svg").GetAttribute("aria-label") ?? string.Empty;

    private static string DrawnText(IRenderedComponent<C4Explorer> explorer) =>
        explorer.Find("[data-testid=c4-diagram] svg").TextContent;

    private static string[] DrawnNodeIds(IRenderedComponent<C4Explorer> explorer) =>
        [.. explorer.FindAll("[data-testid=c4-diagram] svg g.node").Select(node => node.Id ?? string.Empty).Order(StringComparer.Ordinal)];

    [Fact]
    public void The_first_render_attaches_the_viewer()
    {
        Render(Workspace());

        Assert.Equal(1, Attaches);
    }

    [Fact]
    public void Rendering_the_same_view_again_does_not_reattach()
    {
        var workspace = Workspace();
        var explorer = Render(workspace);

        explorer.Render(parameters => parameters
            .Add(c => c.Workspace, workspace)
            .Add(c => c.ViewKey, "landscape"));

        Assert.Equal(1, Attaches);
    }

    [Fact]
    public async Task A_zoom_report_does_not_reattach()
    {
        // The loop itself: the viewer reports a zoom, the explorer re-renders to show
        // it, and that render must not hand the viewer the zoomed SVG as a new picture.
        var explorer = Render(Workspace());

        await explorer.InvokeAsync(() => explorer.Instance.ZoomChanged(150));

        Assert.Equal("150%", explorer.Find("[data-testid=c4-zoom-level]").TextContent);
        Assert.Equal(1, Attaches);
    }

    [Fact]
    public void Presenting_does_not_reattach()
    {
        var explorer = Render(Workspace());

        explorer.Find("[data-testid=c4-present]").Click();

        explorer.WaitForAssertion(() => Assert.NotNull(explorer.Find(".c4-explorer--presenting")));
        Assert.Equal(1, Attaches);
    }

    [Fact]
    public void Opening_the_highlighter_does_not_reattach()
    {
        var explorer = Render(Workspace());

        explorer.Find("[data-testid=c4-highlighter-toggle]").Click();

        explorer.WaitForAssertion(() => Assert.NotNull(explorer.Find("[data-testid=c4-highlighter]")));
        Assert.Equal(1, Attaches);
    }

    [Fact]
    public void Opening_another_view_attaches_to_its_picture()
    {
        var explorer = Render(Workspace());

        explorer.FindAll("[data-testid=c4-view-option]")
            .Single(option => option.GetAttribute("aria-current") is null)
            .Click();

        explorer.WaitForAssertion(() => Assert.Equal(2, Attaches));
        Assert.Equal("containers", _context.JSInterop.Invocations[Attach][^1].Arguments[3]);
    }

    [Fact]
    public void A_new_workspace_for_the_same_view_attaches_again()
    {
        // A reload hands over a fresh workspace with the same view key. The picture
        // may have changed with it, so the key alone is not the identity.
        var explorer = Render(Workspace());

        explorer.Render(parameters => parameters
            .Add(c => c.Workspace, Workspace())
            .Add(c => c.ViewKey, "landscape"));

        Assert.Equal(2, Attaches);
    }

    [Fact]
    public void Coming_back_to_a_picture_after_one_with_nothing_drawn_attaches_again()
    {
        // The same workspace reference hands back the picture it laid out before, so
        // the diagram alone cannot tell the attach that the element is new: the
        // render that drew nothing has to forget what was attached.
        var workspace = Workspace();
        var explorer = Render(workspace);

        explorer.Render(parameters => parameters
            .Add(c => c.Workspace, C4DslReader.Read(NoViews))
            .Add(c => c.ViewKey, "landscape"));
        Assert.Empty(explorer.FindAll("[data-testid=c4-diagram] svg"));

        explorer.Render(parameters => parameters
            .Add(c => c.Workspace, workspace)
            .Add(c => c.ViewKey, "landscape"));

        Assert.Single(explorer.FindAll("[data-testid=c4-diagram] svg"));
        Assert.Equal(2, Attaches);
    }

    [Fact]
    public void An_attach_that_found_no_picture_tries_again_on_the_next_render()
    {
        // The viewer answers false when the frame or its svg is not in the DOM yet.
        // That picture is not attached, so a render that would otherwise leave the
        // viewer alone attaches it instead of leaving the view with no pan or zoom.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>(Attach, _ => true).SetResult(false);

        var workspace = Workspace();
        var explorer = Render(context, workspace);

        explorer.Render(parameters => parameters
            .Add(c => c.Workspace, workspace)
            .Add(c => c.ViewKey, "landscape"));

        Assert.Equal(2, context.JSInterop.Invocations[Attach].Count);
    }

    [Fact]
    public void Opening_another_view_draws_its_picture()
    {
        // The picture is cached between renders; the cache must not outlive the view.
        var explorer = Render(Workspace());
        var label = DrawnLabel(explorer);
        var nodes = DrawnNodeIds(explorer);

        explorer.FindAll("[data-testid=c4-view-option]")
            .Single(option => option.GetAttribute("aria-current") is null)
            .Click();

        explorer.WaitForAssertion(() => Assert.NotEqual(label, DrawnLabel(explorer)));
        Assert.NotEqual(nodes, DrawnNodeIds(explorer));
        Assert.NotEmpty(DrawnNodeIds(explorer));
    }

    [Fact]
    public void A_new_workspace_for_the_same_view_draws_its_picture()
    {
        // Same name, same counts and same view key, so only the workspace's identity
        // says the picture is stale; the drawn text has to follow the new model.
        var explorer = Render(Workspace());
        Assert.Contains("ME", DrawnText(explorer), StringComparison.Ordinal);

        explorer.Render(parameters => parameters
            .Add(c => c.Workspace, C4DslReader.Read(Source.Replace("person \"ME\"", "person \"YOU\"", StringComparison.Ordinal)))
            .Add(c => c.ViewKey, "landscape"));

        Assert.Contains("YOU", DrawnText(explorer), StringComparison.Ordinal);
        Assert.DoesNotContain("ME", DrawnText(explorer), StringComparison.Ordinal);
    }

    /// <summary>
    /// The viewer's half, which bUnit cannot run. Re-attaching to an SVG element the
    /// viewer has already zoomed must keep the home it read the first time, rather
    /// than reading the zoomed viewBox back as the picture's extent.
    /// </summary>
    [Fact]
    public void The_viewer_keeps_each_pictures_home_across_attaches()
    {
        var attach = ComponentsScript.Region("        attach(id, frameSelector, reference, viewKey, drillable) {", "        fit(id) {");

        Assert.DoesNotContain("const home = readViewBox(svg);", attach, StringComparison.Ordinal);
        Assert.Contains("viewports.get(svg)", attach, StringComparison.Ordinal);
    }
}
