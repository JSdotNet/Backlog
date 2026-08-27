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
    /// The artifact is not put into embed mode, and that is most of this feature.
    ///
    /// <para><c>data-embed</c> is not a stylesheet. The artifact enforces it in
    /// twenty-four JavaScript guards, each a plain
    /// <c>if (html.getAttribute('data-embed') === 'true') return false;</c> at the
    /// top of something a reader would want — the visual style menu will not open
    /// under it, and neither will the node finder, the semantic lens, the route
    /// probe, a guided view's journey or presentation mode. The first attempt at
    /// this unhid the Style button with CSS and produced a control that refused to
    /// do anything, which is how the guards were found.</para>
    ///
    /// <para>So the attribute is never set, and what this host asks for afterwards
    /// is only what a frame genuinely cannot leave to the document.</para>
    /// </summary>
    [Fact]
    public void The_artifact_is_never_put_into_embed_mode()
    {
        var render = RenderArtifact();

        Assert.DoesNotContain("data-embed', 'true'", render, StringComparison.Ordinal);
        Assert.DoesNotContain("data-embed','true'", render, StringComparison.Ordinal);

        // And nothing is written against it either, since none of it would match.
        Assert.DoesNotContain("""html[data-embed""", render, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one rule that would break the sizing outright. The frame's viewport
    /// height is the height this host just gave it from the content, so a body
    /// insisting on filling the viewport can never report less than the frame
    /// already is — it would latch at its opening 28rem and stay there.
    /// </summary>
    [Fact]
    public void The_body_may_not_insist_on_filling_a_viewport_this_host_decides()
    {
        Assert.Contains("body{min-height:0}", RenderArtifact(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two controls are still taken away, and each for a reason the reader would
    /// otherwise meet as a button that refuses.
    /// <para>
    /// The theme toggle has nothing behind it: this host pins <c>data-theme</c> to
    /// dark and re-pins it through a MutationObserver, so pressing it snaps back.
    /// Style goes only when it holds fewer than two selectable presets — asked as
    /// "unless it has two options that are not hidden" rather than as a count,
    /// because the sibling combinator inside <c>:has()</c> is exactly that
    /// question. Inert on today's artifacts, which all offer four.
    /// </para>
    /// </summary>
    [Fact]
    public void Only_the_controls_that_could_not_work_here_are_taken_away()
    {
        var render = RenderArtifact();

        Assert.Contains("#btn-theme{display:none!important}", render, StringComparison.Ordinal);
        Assert.Contains(
            """:not(:has(.preset-option:not([hidden]) ~ .preset-option:not([hidden])))""",
            render,
            StringComparison.Ordinal);

        // Present goes too, and for a third reason: presentation is not a mode to
        // toggle here, it is always on, so a control claiming to turn it on lies
        // about the state it is in.
        Assert.Contains("#btn-present{display:none!important}", render, StringComparison.Ordinal);

        // Everything else the viewer ships stays. These were hidden while the
        // artifact was in embed mode and are the reason it no longer is.
        foreach (var kept in new[] { "#btn-export", ".diagram-nav", ".node-finder", ".guided-views" })
        {
            Assert.DoesNotContain($"{kept}{{display:none", render, StringComparison.Ordinal);
        }

        var chrome = render.IndexOf("const chrome =", StringComparison.Ordinal);
        var applied = render.IndexOf("element.srcdoc = injected + chrome;", StringComparison.Ordinal);

        Assert.True(chrome >= 0 && applied > chrome, "The host's rules must be appended after the artifact's own document.");
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

        // Both listeners, since the watcher now holds two: the height channel and
        // the document's fullscreen change.
        var watch = ArtifactHeightWatcher();
        Assert.Contains("window.removeEventListener('message', onMessage);", watch, StringComparison.Ordinal);
        Assert.Contains("document.removeEventListener('fullscreenchange', onFullscreenChange);", watch, StringComparison.Ordinal);
    }

    /// <summary>
    /// Presentation mode is on and stays on — it is the reading mode, with the
    /// diagram taking the frame and the info cards out of the way. It used to be a
    /// button that did nothing worth seeing: present mode sizes the diagram to the
    /// viewport, and in a frame already sized to its content that only moves the
    /// same box around.
    /// <para>
    /// Which is also why its viewport sizing has to be neutralised in the chapter,
    /// and this is the rule the whole feature stands on. Present mode pins the
    /// document to <c>100dvh</c>; inside the frame that IS the height this host
    /// just gave it, so the measurement would report back exactly what it was told
    /// and every frame would latch at its opening 28rem for ever. The override
    /// lifts in fullscreen, where <c>100dvh</c> means the screen — a number the
    /// host did not choose and cannot feed back into.
    /// </para>
    /// </summary>
    [Fact]
    public void Presentation_is_always_on_and_may_not_size_itself_from_a_height_this_host_chose()
    {
        var render = RenderArtifact();

        Assert.Contains("data-present','true'", render, StringComparison.Ordinal);

        Assert.Contains(
            """html[data-present="true"]:not([data-host-fullscreen]) body""",
            render,
            StringComparison.Ordinal);
        Assert.Contains("{height:auto!important;min-height:0!important", render, StringComparison.Ordinal);
        Assert.Contains(
            """html[data-present="true"]:not([data-host-fullscreen]) .container{height:auto!important}""",
            render,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Room, which is the one thing a diagram in a column of prose cannot have.
    /// The native Fullscreen API rather than a pop-out of this app's own: the
    /// artifact is already a self-contained viewer, so what it needs is space, not
    /// a second frame around it.
    /// <para>
    /// Two things have to happen together for it to read right. The measured height
    /// stops being written, because on a fullscreen element the browser owns the
    /// size and a pixel value there is a letterbox. And the document is told, since
    /// whether it is the whole screen is a fact about the frame it cannot see and
    /// the one that decides what its own <c>100dvh</c> means.
    /// </para>
    /// </summary>
    [Fact]
    public void Fullscreen_hands_over_the_sizing_and_says_so_to_the_document()
    {
        var watch = ArtifactHeightWatcher();

        Assert.Contains("if (document.fullscreenElement === element) return;", watch, StringComparison.Ordinal);
        Assert.Contains("element.style.removeProperty('height');", watch, StringComparison.Ordinal);
        Assert.Contains("channel: 'backlog-artifact-fullscreen', on: mine", watch, StringComparison.Ordinal);

        // And the frame listens for exactly that.
        var render = RenderArtifact();
        Assert.Contains("backlog-artifact-fullscreen", render, StringComparison.Ordinal);
        Assert.Contains("data-host-fullscreen", render, StringComparison.Ordinal);
    }

    /// <summary>The body of <c>backlogDiagrams.renderArtifact</c>, so an assertion
    /// about it cannot be satisfied by an unrelated line elsewhere in a four
    /// thousand line script.</summary>
    private static string RenderArtifact() =>
        Region("        renderArtifact(element, id, html) {", "        renderGraph(element, id, data) {");

    /// <summary>The receiving half, which lives outside <c>backlogDiagrams</c>
    /// because it is the parent's side of the exchange.</summary>
    private static string ArtifactHeightWatcher() =>
        // Anchored on the assignment rather than on the object literal that used
        // to follow it: `window.backlogDiagrams` is merged into now, not replaced,
        // so the next character after this is no longer a brace.
        Region("    const backlogWatchArtifactHeight = (element, id) => {", "    window.backlogDiagrams = ");

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
