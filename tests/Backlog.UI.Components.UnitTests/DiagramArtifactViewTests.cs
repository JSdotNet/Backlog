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
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-renderers']"));
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

        // Two permissions and no more. `allow-scripts` is what makes the viewer
        // run at all; `allow-downloads` is what makes its Export menu write a
        // file. `allow-same-origin` is deliberately absent, so the document stays
        // in an opaque origin and cannot reach back into this page.
        Assert.Equal("allow-scripts allow-downloads", frame.GetAttribute("sandbox"));

        // Which renderer this is, said out loud — and as the control that changes
        // it, since the reader comparing the two needs to be able to.
        Assert.Equal("Archify", diagram.Find("[data-testid='diagram-view-renderer-artifact']").TextContent);
        Assert.Equal("Mermaid", diagram.Find("[data-testid='diagram-view-renderer-mermaid']").TextContent);

        // And the language badge steps aside, because the switch has already said
        // it. "Archify | Mermaid | mermaid" put the same word on screen twice: the
        // switch's second option IS the fence's language, since drawing the fence
        // is what that option does.
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-language']"));

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

        // A chapter diagram keeps the card a node opens.
        Assert.Equal(false, invocation.Arguments[4]);
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
    /// The disclosure is gone, and for an artifact especially. It used to be kept
    /// here whatever the host asked for, because an artifact is a re-authoring of
    /// the fence rather than a rendering of it, so with the fold shut the
    /// canonical text was readable nowhere on the screen. The renderer switch is
    /// the better answer to that: a reader who doubts the picture presses Mermaid
    /// and watches the fence itself be drawn.
    /// </summary>
    [Fact]
    public void An_artifact_carries_no_source_disclosure_either()
    {
        using var context = Context(new DiagramArtifact(
            "<!doctype html><html><body>Archify</body></html>",
            "/repo/.domain/orders/_archify/flow.1.workflow.html",
            "/repo/.domain/orders/_archify/flow.1.workflow.json",
            "workflow",
            IsOutOfDate: false));

        var diagram = Render(context);

        Assert.NotNull(diagram.Find("[data-testid='diagram-view-artifact']"));
        Assert.Empty(diagram.FindAll("details"));
        Assert.Empty(diagram.FindAll(".diagram-view__details"));
    }

    /// <summary>
    /// The switch, and the one condition on it: there has to be a choice. A
    /// diagram with nothing authored for it has one renderer, and offering to
    /// switch to the only thing on offer would be a control that does nothing.
    /// </summary>
    [Fact]
    public void The_renderer_switch_appears_only_where_both_renderers_are_available()
    {
        using var both = Context(new DiagramArtifact(
            "<!doctype html><html><body>Archify</body></html>",
            "/repo/.domain/orders/_archify/flow.1.workflow.html",
            null,
            "workflow",
            IsOutOfDate: false));

        var offered = Render(both);

        Assert.NotNull(offered.Find("[data-testid='diagram-view-renderers']"));
        Assert.Equal("Archify", offered.Find("[data-testid='diagram-view-renderer-artifact']").TextContent);
        Assert.Equal("Mermaid", offered.Find("[data-testid='diagram-view-renderer-mermaid']").TextContent);

        // Selected is said out loud rather than only coloured: this is one of two,
        // and a screen reader has to be able to hear which.
        Assert.Equal("true", offered.Find("[data-testid='diagram-view-renderer-artifact']").GetAttribute("aria-pressed"));
        Assert.Equal("false", offered.Find("[data-testid='diagram-view-renderer-mermaid']").GetAttribute("aria-pressed"));

        using var neither = Context(null);

        Assert.Empty(Render(neither).FindAll("[data-testid='diagram-view-renderers']"));
    }

    /// <summary>
    /// Pressing Mermaid puts the reader on the mermaid, and takes the artifact's
    /// document off the page rather than parking it in a frame nobody is looking
    /// at — 675 KB and a running viewer per diagram is not something to leave
    /// behind a hidden element.
    /// </summary>
    [Fact]
    public void Choosing_mermaid_draws_the_fence_and_drops_the_artifact()
    {
        using var context = Context(new DiagramArtifact(
            "<!doctype html><html><body>Archify</body></html>",
            "/repo/.domain/orders/_archify/flow.1.workflow.html",
            null,
            "workflow",
            IsOutOfDate: false));

        var diagram = Render(context);

        Assert.Single(context.JSInterop.Invocations["backlogDiagrams.renderArtifact"]);

        diagram.Find("[data-testid='diagram-view-renderer-mermaid']").Click();

        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-artifact']"));
        Assert.NotNull(diagram.Find(".diagram-view__rendered"));
        Assert.NotEmpty(context.JSInterop.Invocations["backlogDiagrams.dispose"]);

        var drawn = Assert.Single(context.JSInterop.Invocations["backlogDiagrams.render"]);
        Assert.Equal(Flowchart, drawn.Arguments[3]);

        // And back, which has to redraw rather than decide it is already current.
        diagram.Find("[data-testid='diagram-view-renderer-artifact']").Click();

        Assert.NotNull(diagram.Find("[data-testid='diagram-view-artifact']"));
        Assert.Equal(2, context.JSInterop.Invocations["backlogDiagrams.renderArtifact"].Count);
    }


    /// <summary>
    /// The way back out. Fullscreen is requested on the stage rather than on the
    /// frame so that this button can be inside it — only the element in the top
    /// layer and its descendants are painted, and the frame itself is sandboxed
    /// into an opaque origin and can carry nothing of ours. A reader who pressed
    /// "Full screen" had Esc, and no way of knowing it.
    /// </summary>
    [Fact]
    public void The_way_out_of_full_screen_is_inside_the_element_that_goes_full_screen()
    {
        using var context = Context(new DiagramArtifact(
            "<!doctype html><html><body>Archify</body></html>",
            "/repo/.domain/orders/_archify/flow.1.workflow.html",
            null,
            "workflow",
            IsOutOfDate: false));

        var diagram = Render(context);

        // Both in the stage: the one that takes the screen and the one that gives
        // it back. Asserted as containment rather than as two separate finds,
        // because being in the same element is the entire fix.
        var stage = diagram.Find("[data-testid='diagram-view-stage']");
        Assert.NotNull(stage.QuerySelector("[data-testid='diagram-view-artifact']"));

        var exit = stage.QuerySelector("[data-testid='diagram-view-exit-fullscreen']");
        Assert.NotNull(exit);
        Assert.Contains("full screen", exit!.TextContent, StringComparison.OrdinalIgnoreCase);

        exit.Click();

        Assert.Single(context.JSInterop.Invocations["backlogDiagrams.toggleArtifactFullscreen"]);
    }

    /// <summary>A drawn mermaid diagram is an SVG in this page and has nothing to
    /// take to the screen, so it is offered no fullscreen and carries no way out of
    /// one.</summary>
    [Fact]
    public void A_mermaid_diagram_is_offered_no_full_screen_and_carries_no_way_out_of_one()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var diagram = Render(context);

        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-fullscreen']"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-stage']"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-exit-fullscreen']"));
    }

    /// <summary>
    /// A picture the host generated from live data rather than found for a fence is
    /// shown without any source being registered, and
    /// wins over whatever a registered source would have found. The source stays
    /// what the Mermaid side of the switch draws.
    /// </summary>
    [Fact]
    public void An_artifact_the_caller_hands_over_is_shown_with_no_source_asked()
    {
        using var context = Context(artifact: null);

        var diagram = context.Render<DiagramView>(parameters => parameters
            .Add(view => view.Source, Flowchart)
            .Add(view => view.Language, "mermaid")
            .Add(view => view.CssClass, "host-artifact")
            .Add(view => view.Compact, true)
            .Add(view => view.HideNodeDetails, true)
            .Add(view => view.Artifact, new DiagramArtifact(
                "<!doctype html><html><body>Run</body></html>",
                @"C:\Temp\run-0123456789abcdef.html",
                null,
                "architecture",
                IsOutOfDate: false)));

        Assert.Equal("diagram-view host-artifact", diagram.Find("figure").ClassName);
        Assert.Equal("IFRAME", diagram.Find("[data-testid='diagram-view-artifact']").TagName);
        var invocation = Assert.Single(context.JSInterop.Invocations["backlogDiagrams.renderArtifact"]);
        Assert.Equal("<!doctype html><html><body>Run</body></html>", invocation.Arguments[2]);

        // Compact asks the frame to leave its own header and toolbar out while it
        // is in the page; a chapter diagram never asks.
        Assert.Equal(true, invocation.Arguments[3]);

        // And to leave out the card a node opens, in the page and in full screen.
        Assert.Equal(true, invocation.Arguments[4]);
        Assert.NotNull(diagram.Find("[data-testid='diagram-view-renderer-mermaid']"));
    }

    /// <summary>A new artifact path is a new picture — the run moved — and the frame
    /// is handed the new document rather than left showing the old one.</summary>
    [Fact]
    public void A_new_artifact_path_reloads_the_frame()
    {
        using var context = Context(artifact: null);

        static DiagramArtifact Artifact(string key) =>
            new($"<!doctype html><p>{key}</p>", $@"C:\Temp\run-{key}.html", null, "architecture", IsOutOfDate: false);

        var diagram = context.Render<DiagramView>(parameters => parameters
            .Add(view => view.Source, Flowchart)
            .Add(view => view.Language, "mermaid")
            .Add(view => view.Artifact, Artifact("aaaa")));

        diagram.Render(parameters => parameters.Add(view => view.Artifact, Artifact("bbbb")));

        var documents = context.JSInterop.Invocations["backlogDiagrams.renderArtifact"]
            .Select(invocation => invocation.Arguments[2])
            .ToList();

        Assert.Equal(["<!doctype html><p>aaaa</p>", "<!doctype html><p>bbbb</p>"], documents);
    }

    /// <summary>
    /// A specification whose index entry matches but which has not been drawn on
    /// this machine yet (local ADR 0033). The reader gets the mermaid at once and a
    /// line saying the Archify picture is on its way, and no Render button: the
    /// drawing is already happening, and a second press would only start it twice.
    /// When the render lands, the artifact replaces the mermaid with no
    /// interaction at all.
    /// </summary>
    [Fact]
    public async Task A_specification_drawn_on_demand_shows_the_mermaid_first_and_the_artifact_once_it_lands()
    {
        var pending = new DiagramArtifact(null, null, "/repo/.domain/orders/_archify/flow.1.workflow.json", "workflow", IsOutOfDate: false, Quality: "showcase", IsRenderPending: true);
        var landed = new TaskCompletionSource<DiagramArtifact?>();
        var source = new OnDemandDiagramArtifactSource(pending, landed.Task);

        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IDiagramArtifactSource>(source);

        var diagram = Render(context);

        Assert.NotNull(diagram.Find(".diagram-view__rendered"));
        Assert.Single(context.JSInterop.Invocations["backlogDiagrams.render"]);
        Assert.Contains("Archify", diagram.Find("[data-testid='diagram-view-artifact-pending']").TextContent, StringComparison.Ordinal);
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-render']"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-artifact']"));

        await diagram.InvokeAsync(() => landed.SetResult(pending with
        {
            Html = "<!doctype html><html><body>Archify</body></html>",
            ArtifactPath = "/cache/_archify-renders/flow.1.workflow-0123456789abcdef.html",
            IsRenderPending = false
        }));

        diagram.WaitForAssertion(() => Assert.NotNull(diagram.Find("[data-testid='diagram-view-artifact']")));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-artifact-pending']"));
        var invocation = Assert.Single(context.JSInterop.Invocations["backlogDiagrams.renderArtifact"]);
        Assert.Equal("<!doctype html><html><body>Archify</body></html>", invocation.Arguments[2]);
        Assert.Equal(1, source.Loads);
    }

    /// <summary>
    /// A render that failed is said out loud with its reason — no Node, no
    /// generator, a timeout, a specification Archify rejected — rather than
    /// leaving a reader to wonder why a diagram with a specification has no
    /// picture. The mermaid stays, and the Render button stays as the retry.
    /// </summary>
    [Fact]
    public void A_render_that_failed_keeps_the_mermaid_and_says_why()
    {
        var pending = new DiagramArtifact(null, null, "/repo/.domain/orders/_archify/flow.1.workflow.json", "workflow", IsOutOfDate: false, Quality: "showcase", IsRenderPending: true);
        var failed = pending with { IsRenderPending = false, RenderFailure = "Node.js is not installed or not on the PATH." };
        var source = new OnDemandDiagramArtifactSource(pending, Task.FromResult<DiagramArtifact?>(failed));

        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IDiagramArtifactSource>(source);

        var diagram = Render(context);

        diagram.WaitForAssertion(() => Assert.NotNull(diagram.Find("[data-testid='diagram-view-render-failure']")));
        Assert.Contains("Node.js is not installed", diagram.Find("[data-testid='diagram-view-render-failure']").TextContent, StringComparison.Ordinal);
        Assert.NotNull(diagram.Find(".diagram-view__rendered"));
        Assert.NotNull(diagram.Find("[data-testid='diagram-view-render']"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-artifact']"));
        Assert.Empty(diagram.FindAll("[data-testid='diagram-view-artifact-pending']"));

        // A parent re-render asks Find again, which still answers "pending" because
        // a failure is never cached. That must not start the render over on every
        // pass: one attempt per source until the reader asks again.
        diagram.Render(parameters => parameters.Add(view => view.Title, "Again"));

        Assert.Equal(1, source.Loads);
        Assert.NotNull(diagram.Find("[data-testid='diagram-view-render-failure']"));
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
        string source = Flowchart) =>
        context.Render<DiagramView>(parameters => parameters
            .Add(diagram => diagram.Source, source)
            .Add(diagram => diagram.Language, "mermaid"));
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

/// <summary>
/// A host that draws on demand: <see cref="Find"/> says a picture is coming, and
/// <see cref="LoadAsync"/> hands it over when the test says so.
/// </summary>
file sealed class OnDemandDiagramArtifactSource(DiagramArtifact pending, Task<DiagramArtifact?> load) : IDiagramArtifactSource
{
    internal int Loads { get; private set; }

    public bool CanAuthor => false;

    public DiagramArtifact? Find(string? source, string? language) =>
        load.IsCompletedSuccessfully && load.Result is { CanRender: true } drawn ? drawn : pending;

    public Task<DiagramArtifact?> LoadAsync(string? source, string? language, CancellationToken cancellationToken = default)
    {
        Loads++;
        return load;
    }

    public Task<string?> RenderAsync(string? source, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> AuthorAsync(string? source, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
