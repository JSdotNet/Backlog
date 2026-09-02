using AngleSharp.Dom;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The eighteen domain-type marks.
///
/// <para>Three properties carry the whole component and each is pinned here. It
/// draws something for every value the vocabulary publishes, so a value added to
/// the list without a glyph beside it fails rather than shipping as a blank. It
/// draws <em>nothing at all</em> for a value it does not know, which is what makes
/// it safe to write unconditionally beside a word a caller is still showing. And
/// naming it and giving it a tooltip are two separate asks, which is the property
/// the chapter heading rests on: <c>role="img"</c> plus an <c>aria-label</c> is an
/// attribute and costs no text node, while the <c>title</c> is an element with
/// text in it. So a heading can be announced with its type and still have exactly
/// the textContent it had before the mark existed.</para>
/// </summary>
public sealed class KnowledgeTypeMarkerTests
{
    /// <summary>The shapes a glyph is allowed to be made of. Three primitives and
    /// the paths that join them — nothing here should ever need an image, a use
    /// reference or a text element.</summary>
    private const string ShapeSelector = "path, circle, rect";

    [Fact]
    public void Every_value_the_vocabulary_publishes_has_a_glyph()
    {
        using var context = new BunitContext();

        // Eighteen, and the count is asserted so a value quietly dropped from one
        // of the two sets cannot make this pass by having less to check.
        Assert.Equal(11, KnowledgeTypeMarkers.ChapterTypes.Count);
        Assert.Equal(7, KnowledgeTypeMarkers.FileTypes.Count);
        Assert.Equal(18, KnowledgeTypeMarkers.All.Count);
        Assert.Equal(18, KnowledgeTypeMarkers.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var value in KnowledgeTypeMarkers.All)
        {
            var marker = context.Render<KnowledgeTypeMarker>(parameters => parameters
                .Add(mark => mark.Value, value));

            var svg = marker.Find("svg");

            Assert.Equal("0 0 16 16", svg.GetAttribute("viewBox"));
            Assert.Contains($"knowledge-type-marker--{value}", svg.ClassList);
            Assert.NotEmpty(svg.QuerySelectorAll(ShapeSelector));
        }
    }

    [Fact]
    public void The_family_is_stroked_at_1_6_and_only_the_identity_dots_are_filled()
    {
        // The library's rule, stated in ProviderMark: provider marks are filled and
        // state icons are stroked at 1.6, so a reader never mistakes one class of
        // thing for the other. The exception is deliberate and bounded — a filled
        // dot means identity, and a dot that is not filled is a hollow circle,
        // which in this family already means something else.
        using var context = new BunitContext();

        foreach (var value in KnowledgeTypeMarkers.All)
        {
            var svg = context
                .Render<KnowledgeTypeMarker>(parameters => parameters.Add(mark => mark.Value, value))
                .Find("svg");

            Assert.Equal("none", svg.GetAttribute("fill"));
            Assert.Equal("currentColor", svg.GetAttribute("stroke"));
            Assert.Equal("1.6", svg.GetAttribute("stroke-width"));

            Assert.All(
                svg.QuerySelectorAll(ShapeSelector).Where(shape => shape.HasAttribute("fill")),
                shape =>
                {
                    Assert.Equal("currentColor", shape.GetAttribute("fill"));
                    Assert.Equal("none", shape.GetAttribute("stroke"));
                });
        }
    }

    [Fact]
    public void Nothing_is_hard_coded_but_the_ink_it_inherits()
    {
        // No family hues. The set groups into four families and .design forbids
        // giving them tones of their own, so the only colour word in the whole
        // rendering is currentColor.
        using var context = new BunitContext();

        foreach (var value in KnowledgeTypeMarkers.All)
        {
            var markup = context
                .Render<KnowledgeTypeMarker>(parameters => parameters.Add(mark => mark.Value, value))
                .Markup;

            Assert.DoesNotContain("#", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("rgb", markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("var(--", markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_value_the_set_does_not_know_renders_nothing_at_all()
    {
        // Not an empty box, not a fallback glyph, not a question mark. The .domain
        // vocabulary grows, and a value set that grows must never make a page look
        // broken — the caller goes on showing the plain word.
        using var context = new BunitContext();

        foreach (var value in new[] { "policy-fragment", "kind", "invariant", "aggregate-root" })
        {
            var marker = context.Render<KnowledgeTypeMarker>(parameters => parameters
                .Add(mark => mark.Value, value));

            Assert.Equal(string.Empty, marker.Markup.Trim());
        }
    }

    [Fact]
    public void No_value_is_the_same_answer_as_an_unknown_one()
    {
        using var context = new BunitContext();

        foreach (var value in new string?[] { null, "", "   " })
        {
            var marker = context.Render<KnowledgeTypeMarker>(parameters => parameters
                .Add(mark => mark.Value, value));

            Assert.Equal(string.Empty, marker.Markup.Trim());
        }
    }

    [Fact]
    public void A_value_is_recognised_however_it_was_spaced_or_cased()
    {
        // A `meta` fence is hand-written, and `type:  Aggregate ` is the same
        // statement as `type: aggregate`. The class modifier is normalised with it,
        // so the stylesheet never has to match two spellings of one value.
        using var context = new BunitContext();

        var marker = context.Render<KnowledgeTypeMarker>(parameters => parameters
            .Add(mark => mark.Value, "  Value-Object "));

        Assert.Contains("knowledge-type-marker--value-object", marker.Find("svg").ClassList);
        Assert.True(KnowledgeTypeMarkers.IsRecognised("  Value-Object "));
        Assert.False(KnowledgeTypeMarkers.IsRecognised("value object"));
        Assert.False(KnowledgeTypeMarkers.IsRecognised(null));
    }

    [Fact]
    public void Unlabelled_the_mark_is_hidden_and_adds_no_text_node()
    {
        // The property the chapter heading rests on. A labelled mark carries a
        // <title>, a <title> is a text node, and a heading whose textContent grew
        // by a word is a heading every anchor keyed to it has stopped matching.
        using var context = new BunitContext();

        foreach (var value in KnowledgeTypeMarkers.All)
        {
            var marker = context.Render<KnowledgeTypeMarker>(parameters => parameters
                .Add(mark => mark.Value, value));

            var svg = marker.Find("svg");

            Assert.Equal("true", svg.GetAttribute("aria-hidden"));
            Assert.Null(svg.GetAttribute("role"));
            Assert.Null(svg.GetAttribute("aria-label"));
            Assert.Empty(svg.QuerySelectorAll("title"));

            // Asked of the element rather than of the markup, because this is
            // exactly the question a heading's own textContent asks.
            Assert.Equal(string.Empty, svg.TextContent);
        }
    }

    [Fact]
    public void A_heading_that_leads_with_a_mark_is_announced_with_it_and_still_reads_as_itself()
    {
        // Both halves at once, which is the whole point and the thing neither an
        // accessible-name test nor a textContent test proves on its own. The shape
        // is the one the knowledge panel draws: mark first, named, tooltip off,
        // title hard against it.
        using var context = new BunitContext();

        var marked = context.Render<KnowledgeTypeMarkerHeadingHarness>(parameters => parameters
            .Add(harness => harness.Value, "aggregate")
            .Add(harness => harness.Title, "Inbox Item"));

        var heading = marked.Find("h4");
        var mark = heading.QuerySelector("svg")!;

        // Announced. The strip below has stopped saying "type aggregate", so this
        // is now the only place a listener hears it.
        Assert.Equal("img", mark.GetAttribute("role"));
        Assert.Equal("type: aggregate", mark.GetAttribute("aria-label"));
        Assert.Null(mark.GetAttribute("aria-hidden"));

        // And unmoved. No title element, no whitespace between the shapes, no
        // space before the word — the heading's text is the title and nothing else.
        Assert.Empty(mark.QuerySelectorAll("title"));
        Assert.Equal("Inbox Item", heading.TextContent);

        // Said against the counterfactual as well, so the assertion above cannot
        // pass by the harness having quietly stopped drawing a mark.
        var plain = context.Render<KnowledgeTypeMarkerHeadingHarness>(parameters => parameters
            .Add(harness => harness.Value, "policy-fragment")
            .Add(harness => harness.Title, "Inbox Item"));

        Assert.Empty(plain.FindAll("h4 svg"));
        Assert.Equal(plain.Find("h4").TextContent, heading.TextContent);
    }

    [Fact]
    public void Labelled_the_mark_is_named_and_carries_the_field_it_came_from()
    {
        // "type: aggregate" rather than "aggregate": a listener handed only the
        // value has to work out which of a chapter's fields became a picture.
        using var context = new BunitContext();

        var marker = context.Render<KnowledgeTypeMarker>(parameters => parameters
            .Add(mark => mark.Value, "aggregate")
            .Add(mark => mark.Labelled, true));

        var svg = marker.Find("svg");

        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Equal("type: aggregate", svg.GetAttribute("aria-label"));
        Assert.Null(svg.GetAttribute("aria-hidden"));

        // The tooltip comes with the name by default, so a caller that has taken
        // the word off the screen does not have to ask for it twice. The
        // accessible name is still the aria-label beside it.
        Assert.Equal("type: aggregate", svg.QuerySelector("title")!.TextContent);
    }

    [Fact]
    public void The_tooltip_can_be_declined_without_giving_up_the_name()
    {
        // The split the heading depends on. An aria-label is an attribute; a title
        // is an element with text in it. Turning the second off must not take the
        // first with it.
        using var context = new BunitContext();

        foreach (var value in KnowledgeTypeMarkers.All)
        {
            var svg = context
                .Render<KnowledgeTypeMarker>(parameters => parameters
                    .Add(mark => mark.Value, value)
                    .Add(mark => mark.Labelled, true)
                    .Add(mark => mark.ShowTooltip, false))
                .Find("svg");

            Assert.Equal("img", svg.GetAttribute("role"));
            Assert.Equal($"type: {value}", svg.GetAttribute("aria-label"));
            Assert.Null(svg.GetAttribute("aria-hidden"));

            Assert.Empty(svg.QuerySelectorAll("title"));
            Assert.Equal(string.Empty, svg.TextContent);
        }
    }

    [Fact]
    public void An_unnamed_mark_has_no_tooltip_however_the_flag_is_set()
    {
        // A tooltip on a mark hidden from assistive technology would be a fact only
        // a mouse could reach, so the default true reads as "with the name" rather
        // than as "always".
        using var context = new BunitContext();

        var svg = context
            .Render<KnowledgeTypeMarker>(parameters => parameters
                .Add(mark => mark.Value, "model")
                .Add(mark => mark.ShowTooltip, true))
            .Find("svg");

        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Empty(svg.QuerySelectorAll("title"));
        Assert.Equal(string.Empty, svg.TextContent);
    }

    [Fact]
    public void The_mark_is_never_focusable_whichever_way_it_is_named()
    {
        // A decorative or a labelled image, never a stop in the tab order — the
        // legacy SVG focusable attribute is what puts one there in some engines.
        using var context = new BunitContext();

        foreach (var labelled in new[] { true, false })
        {
            var svg = context
                .Render<KnowledgeTypeMarker>(parameters => parameters
                    .Add(mark => mark.Value, "term")
                    .Add(mark => mark.Labelled, labelled))
                .Find("svg");

            Assert.Equal("false", svg.GetAttribute("focusable"));
        }
    }

    [Fact]
    public void Size_is_an_em_by_default_so_the_mark_scales_with_its_heading()
    {
        using var context = new BunitContext();

        var scaled = context
            .Render<KnowledgeTypeMarker>(parameters => parameters.Add(mark => mark.Value, "entity"))
            .Find("svg");

        Assert.Equal("0.82em", scaled.GetAttribute("width"));
        Assert.Equal("0.82em", scaled.GetAttribute("height"));

        // Fixed-density chrome passes a px value the way the state icons do.
        var fixedSize = context
            .Render<KnowledgeTypeMarker>(parameters => parameters
                .Add(mark => mark.Value, "entity")
                .Add(mark => mark.Size, "13px"))
            .Find("svg");

        Assert.Equal("13px", fixedSize.GetAttribute("width"));
        Assert.Equal("13px", fixedSize.GetAttribute("height"));
    }

    [Fact]
    public void A_caller_class_is_appended_and_never_displaces_the_ones_the_stylesheet_matches()
    {
        using var context = new BunitContext();

        var svg = context
            .Render<KnowledgeTypeMarker>(parameters => parameters
                .Add(mark => mark.Value, "flow")
                .Add(mark => mark.CssClass, "knowledge-type-marker--leads")
                .Add(mark => mark.TestId, "chapter-mark")
                .AddUnmatched("data-role", "type"))
            .Find("svg");

        Assert.Equal(
            "knowledge-type-marker knowledge-type-marker--flow knowledge-type-marker--leads",
            svg.GetAttribute("class"));
        Assert.Equal("chapter-mark", svg.GetAttribute("data-testid"));
        Assert.Equal("type", svg.GetAttribute("data-role"));
    }
}
