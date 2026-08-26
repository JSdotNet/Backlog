using Microsoft.Extensions.DependencyInjection;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a reader sees for one diagram, for each answer the host can give about
/// it. The artifact source is resolved optionally, so the first thing worth
/// pinning is the case where there is none — the storybook, the mobile harness
/// and every other test in this project render a <see cref="DiagramView"/> with
/// nothing registered, and all of them must keep getting mermaid.
/// <para>
/// The rest are the four honest answers: a picture to show, a specification to
/// render, a diagram to author, and nothing to offer at all. The last is the one
/// the design cares most about — an offer is a promise, and a class diagram has
/// nothing behind it.
/// </para>
/// </summary>
public sealed class DiagramArtifactViewTests
{
    private const string Flowchart = "flowchart TD\n    A[Start] --> B[Stop]";

    private const string ClassDiagram = "classDiagram\n    class Order {\n        +OrderId Id\n    }";

    [Fact]
    public void With_no_artifact_source_registered_a_diagram_renders_exactly_as_it_always_did()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var diagram = Render(context);

        Assert.Single(context.JSInterop.Invocations["backlogDiagrams.render"]);
        Assert.NotNull(diagram.Find(".diagram-view__rendered"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-footer']"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-renderer']"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-artifact']"));
    }

    [Fact]
    public void An_artifact_with_a_document_is_shown_in_a_sandboxed_frame_instead_of_the_mermaid()
    {
        using var context = Context(new DiagramArtifact(
            "<!doctype html><html><body>Archify</body></html>",
            "/repo/.domain/orders/_archify/flow.1.workflow.html",
            "/repo/.domain/orders/_archify/flow.1.workflow.json",
            "workflow",
            IsOutOfDate: false));

        var diagram = Render(context);

        var frame = diagram.Find("[data-testid='diagram-view-artifact']");
        Assert.Equal("IFRAME", frame.TagName);
        Assert.Equal("allow-scripts", frame.GetAttribute("sandbox"));

        // Said out loud, because the two renderers draw the same diagram
        // differently and the reader is comparing the picture against the source
        // below it.
        Assert.Equal("Archify", diagram.Find("[data-testid='diagram-view-renderer']").TextContent);
        Assert.Equal("mermaid", diagram.Find("[data-testid='diagram-view-language']").TextContent);

        // The artifact is a re-authoring rather than a rendering, so the
        // disclosure is the only place the canonical text can still be read.
        Assert.Equal("Diagram source", diagram.Find(".diagram-view__details summary").TextContent);
        Assert.Contains("A[Start]", diagram.Find(".diagram-view__details pre").TextContent, StringComparison.Ordinal);

        Assert.Empty(diagram.FindAll(".diagram-view__rendered"));
        Assert.Empty(context.JSInterop.Invocations["backlogDiagrams.render"]);
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-footer']"));
    }

    [Fact]
    public void The_artifact_document_is_handed_to_the_frame_renderer()
    {
        const string document = "<!doctype html><html><body>Archify</body></html>";
        using var context = Context(new DiagramArtifact(
            document,
            "/repo/.domain/orders/_archify/flow.1.workflow.html",
            null,
            "workflow",
            IsOutOfDate: false));

        Render(context);

        var invocation = Assert.Single(context.JSInterop.Invocations["backlogDiagrams.renderArtifact"]);

        Assert.Equal(document, invocation.Arguments[2]);
    }

    [Fact]
    public void A_specification_nobody_has_rendered_yet_offers_the_render_that_is_purely_mechanical()
    {
        using var context = Context(new DiagramArtifact(
            null,
            null,
            "/repo/.domain/orders/_archify/flow.1.workflow.json",
            "workflow",
            IsOutOfDate: false));

        var diagram = Render(context);

        Assert.NotNull(diagram.Find(".diagram-view__rendered"));
        Assert.NotNull(diagram.Find("[data-testid='diagram-view-footer']"));
        Assert.NotNull(diagram.Find("[data-testid='diagram-view-render']"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-author']"));

        // Nothing has drifted: there is simply no picture yet.
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-outdated']"));
    }

    /// <summary>
    /// The drift case the hash exists for. The reader is looking at mermaid, a
    /// picture exists that is not of what they can see, and neither of those is
    /// obvious from the screen — so it is said out loud, and the render stays
    /// offered so they can put it right.
    /// </summary>
    [Fact]
    public void An_artifact_authored_from_an_earlier_version_of_the_source_says_so_out_loud()
    {
        using var context = Context(new DiagramArtifact(
            null,
            null,
            "/repo/.domain/orders/_archify/flow.1.workflow.json",
            "workflow",
            IsOutOfDate: true));

        var diagram = Render(context);

        var notice = diagram.Find("[data-testid='diagram-view-outdated']");
        Assert.Contains("earlier version", notice.TextContent, StringComparison.Ordinal);
        Assert.Equal("note", notice.GetAttribute("role"));

        Assert.NotNull(diagram.Find("[data-testid='diagram-view-render']"));
        Assert.NotNull(diagram.Find(".diagram-view__rendered"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-artifact']"));
    }

    [Fact]
    public void A_kind_Archify_can_author_offers_the_agent_when_one_is_available()
    {
        using var context = Context(
            new DiagramArtifact(null, null, null, "workflow", IsOutOfDate: false),
            canAuthor: true);

        var diagram = Render(context);

        Assert.NotNull(diagram.Find("[data-testid='diagram-view-footer']"));
        Assert.NotNull(diagram.Find("[data-testid='diagram-view-author']"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-render']"));
    }

    /// <summary>
    /// The gate the whole feature turns on. An agent is available and the host
    /// knows about artifacts, and still nothing is offered — because none of
    /// Archify's five types can express a class diagram, and an offer nobody can
    /// honour is worse than no offer.
    /// </summary>
    [Fact]
    public void A_class_diagram_is_offered_nothing_even_with_an_agent_available()
    {
        using var context = Context(
            new DiagramArtifact(null, null, null, null, IsOutOfDate: false),
            canAuthor: true);

        var diagram = Render(context, ClassDiagram);

        Assert.NotNull(diagram.Find(".diagram-view__rendered"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-footer']"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-author']"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-render']"));
    }

    [Fact]
    public void Without_an_agent_on_this_machine_authoring_is_hidden_rather_than_offered_and_refused()
    {
        using var context = Context(
            new DiagramArtifact(null, null, null, "workflow", IsOutOfDate: false),
            canAuthor: false);

        var diagram = Render(context);

        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-footer']"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-author']"));
    }

    /// <summary>
    /// The one case where a host does not get to turn the disclosure off. The
    /// knowledge panels pass <c>ShowDiagramSource="false"</c> because a drawn
    /// mermaid diagram is its source rendered, and there is nothing to read that
    /// the picture does not already say. An artifact is not that: it is a
    /// re-authoring of the fence, so with the disclosure off the canonical text
    /// would be readable nowhere on the screen.
    /// </summary>
    [Fact]
    public void An_artifact_keeps_the_source_disclosure_even_where_the_host_turned_it_off()
    {
        using var context = Context(new DiagramArtifact(
            "<!doctype html><html><body>Archify</body></html>",
            "/repo/.domain/orders/_archify/flow.1.workflow.html",
            "/repo/.domain/orders/_archify/flow.1.workflow.json",
            "workflow",
            IsOutOfDate: false));

        var diagram = Render(context, showSource: false);

        Assert.NotNull(diagram.Find("[data-testid='diagram-view-artifact']"));
        Assert.Equal("Diagram source", diagram.Find(".diagram-view__details summary").TextContent);

        // The fence itself, not a paraphrase of it: what the artifact claims to
        // say has to be checkable against what the chapter actually says.
        Assert.Contains(Flowchart, diagram.Find(".diagram-view__details pre").TextContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// The scope of that override, which is the half worth guarding. An artifact
    /// overriding <c>ShowSource</c> must not amount to switching the host's choice
    /// off everywhere — a mermaid diagram in a knowledge panel still gets no
    /// disclosure, whether nothing was authored for it or something was authored
    /// and cannot be shown.
    /// </summary>
    [Fact]
    public void With_the_disclosure_off_a_diagram_that_is_still_mermaid_does_not_get_one()
    {
        using var nothing = Context(null);
        Assert.Empty(Render(nothing, showSource: false).FindAll(".diagram-view__details"));

        // An artifact exists and is withheld because the fence moved on. The
        // reader is looking at mermaid, so the host's "I already show this text"
        // is true again and the disclosure stays off.
        using var withheld = Context(new DiagramArtifact(
            null,
            null,
            "/repo/.domain/orders/_archify/flow.1.workflow.json",
            "workflow",
            IsOutOfDate: true));

        var diagram = Render(withheld, showSource: false);

        Assert.NotNull(diagram.Find("[data-testid='diagram-view-outdated']"));
        Assert.Empty(diagram.FindAll(".diagram-view__details"));
    }

    /// <summary>A host that never turned it off is unaffected by the override:
    /// the disclosure it asked for is the disclosure it gets.</summary>
    [Fact]
    public void A_host_that_wants_the_source_disclosure_still_gets_it_under_an_artifact()
    {
        using var context = Context(new DiagramArtifact(
            "<!doctype html><html><body>Archify</body></html>",
            "/repo/.domain/orders/_archify/flow.1.workflow.html",
            null,
            "workflow",
            IsOutOfDate: false));

        var diagram = Render(context, showSource: true);

        Assert.Equal("Diagram source", diagram.Find(".diagram-view__details summary").TextContent);
        Assert.Contains(Flowchart, diagram.Find(".diagram-view__details pre").TextContent, StringComparison.Ordinal);
    }

    private static BunitContext Context(DiagramArtifact? artifact, bool canAuthor = false)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IDiagramArtifactSource>(new StubDiagramArtifactSource(artifact, canAuthor));
        return context;
    }

    private static IRenderedComponent<DiagramView> Render(
        BunitContext context,
        string source = Flowchart,
        bool showSource = true) =>
        context.Render<DiagramView>(parameters => parameters
            .Add(diagram => diagram.Source, source)
            .Add(diagram => diagram.Language, "mermaid")
            .Add(diagram => diagram.ShowSource, showSource));
}

/// <summary>
/// A host that has already made up its mind. The component asks the same two
/// questions of it that the real adapter answers from disk — what exists for this
/// diagram, and whether an agent can be started — so a fixed answer to each is
/// the whole of what a render-state test needs.
/// </summary>
file sealed class StubDiagramArtifactSource(DiagramArtifact? artifact, bool canAuthor) : IDiagramArtifactSource
{
    public bool CanAuthor => canAuthor;

    public DiagramArtifact? Find(string? source, string? language) => artifact;

    public Task<string?> RenderAsync(string? source, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> AuthorAsync(string? source, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
