using System.Text.RegularExpressions;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// How much of an Archify artifact a reader actually sees.
///
/// <para>An artifact is a whole document in an iframe, and the frame it sits in
/// used to be a fixed 28rem. The document inside is one <c>width: 100%</c> SVG
/// over its own viewBox, so its height follows the frame's <em>width</em> — and
/// under <c>data-embed</c> its body is <c>overflow: hidden</c>. The two together
/// meant everything past 28rem was cut off with no scrollbar to admit it. For a
/// portrait diagram — <c>.arc42/_archify/05-building-block-view.2</c> is
/// 1200x2458 — that was most of the picture.</para>
///
/// <para>Asserted on the script and the stylesheet rather than on a render, for
/// the reason <c>MarkdownEditorAutoGrowLayoutTests</c> gives for the same shape of
/// test: the markup was never wrong. bUnit brings no layout engine and no frame,
/// so a render can only confirm the iframe is there — which it already was. What
/// decides whether a reader sees the whole diagram lives entirely in the sizing
/// handshake and the cascade, so that is what is pinned.</para>
/// </summary>
public sealed class ArchifyArtifactFrameTests
{
    /// <summary>
    /// The frame's stylesheet may state an opening bid and nothing more. A
    /// <c>min-height</c> would be a floor the measured answer could not go under,
    /// which is the same bug in the other direction: a 720x404 diagram left
    /// sitting in 28rem of empty panel.
    /// </summary>
    [Fact]
    public void The_stylesheet_gives_the_artifact_frame_a_starting_height_it_can_be_talked_out_of()
    {
        var rule = Rule(".diagram-view__artifact");

        Assert.Contains("height: 28rem;", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("min-height", rule, StringComparison.Ordinal);
    }

    /// <summary>
    /// The measurement, and the trap it has to avoid. <c>scrollHeight</c> never
    /// reports less than the viewport, and inside the frame the viewport *is* the
    /// box being sized — so a frame that opened at 28rem would report 28rem for
    /// ever and could never shrink to fit a small diagram. The root element's
    /// border box has no such floor.
    /// </summary>
    [Fact]
    public void The_artifact_measures_its_own_height_off_the_root_box_rather_than_off_scroll_height()
    {
        var render = RenderArtifact();

        Assert.Contains("getBoundingClientRect().height", render, StringComparison.Ordinal);
        Assert.DoesNotContain("scrollHeight", render, StringComparison.Ordinal);

        // One measurement after load is not enough: the artifact settles in stages,
        // and the frame has to stay right through a pane resize as well.
        Assert.Contains("new ResizeObserver(schedule)", render, StringComparison.Ordinal);

        // A frame the browser is not painting has its animation frames throttled
        // to nothing, and on a chapter with six diagrams most frames are scrolled
        // out of view. Layout is still computed for one, so the measurement is
        // scheduled on a timer that still runs rather than on a frame that does not.
        Assert.Contains("setTimeout(post,0)", render, StringComparison.Ordinal);
        Assert.DoesNotContain("requestAnimationFrame", render, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('resize',schedule)", render, StringComparison.Ordinal);
    }

    /// <summary>
    /// Which frame a height belongs to. <c>sandbox="allow-scripts"</c> without
    /// <c>allow-same-origin</c> puts every artifact frame in an opaque origin, so
    /// <c>event.origin</c> arrives as the string <c>"null"</c> for all of them and
    /// can tell none of them apart — on a chapter with several diagrams, trusting
    /// it would let the first frame to report resize somebody else's. The window
    /// reference is the identity, and nothing inside the frame can forge it.
    /// </summary>
    [Fact]
    public void A_reported_height_only_resizes_the_frame_that_reported_it()
    {
        var watch = ArtifactHeightWatcher();

        Assert.Contains("if (event.source !== element.contentWindow) return;", watch, StringComparison.Ordinal);
        Assert.Contains("message.channel !== 'backlog-artifact-height'", watch, StringComparison.Ordinal);
        Assert.Contains("message.id !== id", watch, StringComparison.Ordinal);
        Assert.DoesNotContain("event.origin", watch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Archify's own controls, put back — and the two groups are separate places.
    /// Zoom is on <c>.diagram-nav</c>, the dock in the diagram's corner; Style and
    /// Export are on <c>.toolbar</c>, which floats in the frame's top-right and
    /// costs no layout height. Each comes back minus the buttons that cannot work
    /// inside an embedded frame.
    ///
    /// <para>Where the rules go is the whole of why they work. Each has to beat a
    /// <c>display: none !important</c> in the artifact's own stylesheet at
    /// identical specificity — so the only thing that can separate them is which
    /// one the parser reads last. Spliced into the head every one would lose.</para>
    /// </summary>
    [Fact]
    public void The_artifacts_own_controls_are_unhidden_after_its_own_stylesheet_minus_the_dead_ones()
    {
        var render = RenderArtifact();

        Assert.Contains(
            """html[data-embed="true"] .diagram-nav{display:inline-flex!important}""",
            render,
            StringComparison.Ordinal);

        // Only the three zoom controls. The dock's other five buttons open overlay
        // panels that data-embed still hides, and a button that visibly does
        // nothing is the problem hiding the toolbar exists to avoid. The two groups
        // separate without a list to maintain: the panel openers carry an id, the
        // zoom controls carry data-view and no id.
        Assert.Contains(
            """html[data-embed="true"] .diagram-nav button[id]{display:none!important}""",
            render,
            StringComparison.Ordinal);

        var chrome = render.IndexOf("const chrome =", StringComparison.Ordinal);
        var applied = render.IndexOf("element.srcdoc = injected + chrome;", StringComparison.Ordinal);

        Assert.True(chrome >= 0 && applied > chrome, "The host's rules must be appended after the artifact's own document.");

        // And the toolbar, for Style, Motion and Export — minus the two that
        // cannot work here. `#btn-theme` toggles a theme this host pins dark from
        // the outside; `#btn-present` drives a mode whose every rule is written
        // `:not([data-embed="true"])`, so the artifact itself rules it out.
        Assert.Contains("""html[data-embed="true"] .toolbar{display:flex!important}""", render, StringComparison.Ordinal);
        Assert.Contains("#btn-theme", render, StringComparison.Ordinal);
        Assert.Contains("#btn-present", render, StringComparison.Ordinal);

        // Style survives only while it is a choice. Asked as "unless it holds two
        // options that are not hidden" rather than as a count, because the sibling
        // combinator inside :has() is exactly that question. Inert on today's
        // artifacts, which all offer four presets.
        Assert.Contains(
            """:not(:has(.preset-option:not([hidden]) ~ .preset-option:not([hidden])))""",
            render,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The renderer switch is two badges, not a segmented pill. It was a pill
    /// first — one background with the inner corners squared off — and that made
    /// the loudest thing in the header the one part of it that is only a label.
    /// A badge is already this library's word for "a small fact about this block";
    /// being able to press it does not make it a different kind of thing.
    /// </summary>
    [Fact]
    public void The_renderer_switch_wears_the_badge_every_other_fact_in_the_header_wears()
    {
        Assert.Contains(""""BaseClass="language-badge diagram-view__renderer"""", View.Value, StringComparison.Ordinal);

        // Nothing but a flex row and the gap the badges beside it already use.
        var group = Rule(".diagram-view__renderers");
        Assert.Contains("gap: var(--spacing-xs);", group, StringComparison.Ordinal);
        Assert.DoesNotContain("background", group, StringComparison.Ordinal);
        Assert.DoesNotContain("border-radius", group, StringComparison.Ordinal);

        // Selected, said the way the library's other selected chips say it.
        var active = Rule(".diagram-view__renderer--active");
        Assert.Contains("var(--color-primary)", active, StringComparison.Ordinal);
        Assert.Contains("color: var(--color-text-primary);", active, StringComparison.Ordinal);
    }

    /// <summary>
    /// Export writes a file by clicking an anchor with a <c>download</c> attribute
    /// at a blob URL, and a sandboxed frame without <c>allow-downloads</c> has that
    /// click silently ignored — the menu opened, the button pressed, and nothing
    /// ever arrived. <c>allow-same-origin</c> stays off, so granting this costs
    /// none of the isolation: the document is still in an opaque origin.
    /// </summary>
    [Fact]
    public void The_frame_may_write_a_file_and_still_nothing_else()
    {
        var markup = View.Value;

        Assert.Contains(""""sandbox="allow-scripts allow-downloads"""", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-same-origin", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// Embed mode's blanket <c>animation: none !important</c> on the traced edges,
    /// the pulsing dot and the ambient sweep, lifted.
    /// <para>
    /// This changes nothing visible on today's artifacts, and that is the part
    /// worth pinning. Archify animates only a diagram whose <c>&lt;svg&gt;</c>
    /// carries <c>data-animation="trace"</c>, and none of the 38 artifacts in this
    /// repository does — the string is in every one of them, but only inside the
    /// stylesheet's own selectors, so the Motion Governor reports
    /// <c>capable: false</c>. These diagrams are static where they are generated,
    /// not where they are embedded.
    /// </para>
    /// <para>
    /// Lifted anyway, because it is the second lock on the same door: leave it and
    /// an artifact regenerated with motion still would not move here, for a reason
    /// three layers from the thing that changed. <c>revert-layer</c> rather than a
    /// named animation, so what plays is whatever the artifact authored, and
    /// guarded on the two states the Motion Governor writes — its Still switch and
    /// its pause while the tab is hidden — so a reader who asked for stillness
    /// still wins.
    /// </para>
    /// </summary>
    [Fact]
    public void Embed_modes_blanket_stop_on_motion_is_lifted_without_overriding_the_reader()
    {
        var render = RenderArtifact();

        Assert.Contains("{animation:revert-layer!important}", render, StringComparison.Ordinal);
        Assert.Contains("""svg[data-animation="trace"] [data-animate]""", render, StringComparison.Ordinal);

        Assert.Contains(""":not([data-motion="still"])""", render, StringComparison.Ordinal);
        Assert.Contains(""":not([data-document-hidden="true"])""", render, StringComparison.Ordinal);
    }

    /// <summary>
    /// The navy slab out. The artifact paints one three ways — <c>--bg</c> on the
    /// body, <c>--panel</c> on the diagram container, and a grid rect filling the
    /// SVG — and inside a chapter that reads as a card the diagram sits on rather
    /// than as part of the page.
    /// <para>
    /// It takes both sides. The frame element paints its own background too, and
    /// either one left opaque and the diagram is still on a slab.
    /// </para>
    /// </summary>
    [Fact]
    public void Nothing_between_the_drawing_and_the_page_paints_a_background()
    {
        var render = RenderArtifact();

        Assert.Contains("background:transparent!important", render, StringComparison.Ordinal);
        Assert.Contains("background-image:none!important", render, StringComparison.Ordinal);

        // The grid has no class to aim at — it is a bare
        // `<rect width="100%" height="100%" fill="url(#grid)"/>` — so it is
        // addressed as exactly that.
        Assert.Contains("""rect[fill="url(#grid)"]{display:none}""", render, StringComparison.Ordinal);

        Assert.Contains("background: transparent;", Rule(".diagram-view__artifact"), StringComparison.Ordinal);

        // And the line that decides whether any of the rest can be seen. A frame
        // whose root declares a `color-scheme` gets an opaque base canvas painted
        // behind its document in that scheme, so `dark` here would trade Archify's
        // navy slab for the browser's own and nothing behind the frame would ever
        // show through.
        Assert.Contains(":root{color-scheme:normal}", render, StringComparison.Ordinal);
        Assert.DoesNotContain("color-scheme:dark", render, StringComparison.Ordinal);
    }

    /// <summary>
    /// Teardown gives back everything the frame took: the window listener, which
    /// would otherwise outlive a closed pane and hold the element with it, and the
    /// inline height, so a frame reused for a different artifact starts from the
    /// stylesheet's bid rather than from the last diagram's measurement.
    /// </summary>
    [Fact]
    public void Disposing_an_artifact_stops_listening_and_gives_the_frame_its_height_back()
    {
        var render = RenderArtifact();

        Assert.Contains("const unwatch = backlogWatchArtifactHeight(element, id);", render, StringComparison.Ordinal);
        Assert.Contains("unwatch();", render, StringComparison.Ordinal);
        Assert.Contains("element.style.removeProperty('height');", render, StringComparison.Ordinal);

        Assert.Contains("return () => window.removeEventListener('message', onMessage);", ArtifactHeightWatcher(), StringComparison.Ordinal);
    }

    /// <summary>The body of <c>backlogDiagrams.renderArtifact</c>, so an assertion
    /// about it cannot be satisfied by an unrelated line elsewhere in a four
    /// thousand line script.</summary>
    private static string RenderArtifact() =>
        Region("        renderArtifact(element, id, html) {", "        renderGraph(element, id, data) {");

    /// <summary>The receiving half, which lives outside <c>backlogDiagrams</c>
    /// because it is the parent's side of the exchange.</summary>
    private static string ArtifactHeightWatcher() =>
        Region("    const backlogWatchArtifactHeight = (element, id) => {", "    window.backlogDiagrams = {");

    private static string Region(string from, string to)
    {
        var script = ComponentsJs.Value;

        var start = script.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{from}' is no longer in components.js.");

        var end = script.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, $"'{to}' no longer follows '{from}' in components.js.");

        return WithoutComments(script[start..end]);
    }

    /// <summary>
    /// The code, without the prose around it. Several of the assertions here are
    /// negative — no <c>scrollHeight</c>, no <c>event.origin</c> — and the comments
    /// that explain why those are wrong name them, at length. Reading the comments
    /// as code would make each of those tests fail on its own explanation.
    /// </summary>
    private static string WithoutComments(string script) =>
        Regex.Replace(Regex.Replace(script, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline), @"(?m)^\s*//.*$", string.Empty);

    private static string Rule(string selector)
    {
        var stylesheet = ComponentsCss.Value;

        var start = stylesheet.IndexOf($"\n{selector} {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{selector}' is no longer declared in components.css.");

        var end = stylesheet.IndexOf('}', start);
        Assert.True(end > start, $"'{selector}' has no closing brace in components.css.");

        return stylesheet[start..end];
    }

    /// <summary>The component's markup, for the one part of this contract that is
    /// an attribute on an element rather than a rule in a stylesheet. Razor
    /// comments stripped for the reason the script's are: the comment explaining
    /// why <c>allow-same-origin</c> is absent has to name it, and reading that as
    /// markup would fail the test on its own explanation.</summary>
    private static readonly Lazy<string> View = new(() =>
        Regex.Replace(
            File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "Diagrams", "DiagramView.razor"))
                .Replace("\r\n", "\n", StringComparison.Ordinal),
            @"@\*.*?\*@",
            string.Empty,
            RegexOptions.Singleline));

    private static readonly Lazy<string> ComponentsJs = new(() => Read("components.js"));

    private static readonly Lazy<string> ComponentsCss = new(() => Read("components.css"));

    private static string Read(string asset) =>
        File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", asset))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
}
