using AngleSharp.Dom;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The ten capture-kind marks.
///
/// <para>The same three properties <c>KnowledgeTypeMarkerTests</c> pins, for
/// the same reasons. It draws something for every value the vocabulary
/// publishes, so a kind added to the list without a glyph fails rather than
/// shipping blank. It draws nothing at all for a value it does not know, which
/// is what makes it safe beside a word a caller is still printing. And naming it
/// is separate from the tooltip, so a row can hide the mark from assistive
/// technology and let the printed word do the talking.</para>
/// </summary>
public sealed class CaptureKindMarkerTests
{
    private const string ShapeSelector = "path, circle, rect";

    [Fact]
    public void Every_kind_the_vocabulary_publishes_has_a_glyph()
    {
        using var context = new BunitContext();

        Assert.Equal(10, CaptureKinds.All.Count);
        Assert.Equal(10, CaptureKinds.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var value in CaptureKinds.All)
        {
            var marker = context.Render<CaptureKindMarker>(parameters => parameters
                .Add(mark => mark.Value, value));

            var svg = marker.Find("svg");

            Assert.Equal("0 0 16 16", svg.GetAttribute("viewBox"));
            Assert.Contains($"capture-kind-marker--{value}", svg.ClassList);
            Assert.NotEmpty(svg.QuerySelectorAll(ShapeSelector));
        }
    }

    [Fact]
    public void The_five_kinds_the_request_named_are_all_in_the_vocabulary()
    {
        // YouTube, article, Claude artifact, text, image: the ones asked for by
        // name. The other five are the answer to "what else?".
        foreach (var value in new[] { "youtube", "article", "claude-artifact", "text", "image" })
        {
            Assert.True(CaptureKinds.IsRecognised(value), value);
        }
    }

    [Fact]
    public void The_family_is_stroked_and_only_the_play_triangle_is_filled()
    {
        using var context = new BunitContext();

        foreach (var value in CaptureKinds.All)
        {
            var svg = context.Render<CaptureKindMarker>(parameters => parameters
                .Add(mark => mark.Value, value)).Find("svg");

            Assert.Equal("1.6", svg.GetAttribute("stroke-width"));
            Assert.Equal("currentColor", svg.GetAttribute("stroke"));
            Assert.Equal("none", svg.GetAttribute("fill"));

            var filled = svg.QuerySelectorAll("[fill='currentColor']");

            if (value == "youtube")
            {
                Assert.Single(filled);
            }
            else
            {
                Assert.Empty(filled);
            }
        }
    }

    [Fact]
    public void A_glyph_contains_no_whitespace_text_nodes()
    {
        using var context = new BunitContext();

        foreach (var value in CaptureKinds.All)
        {
            var svg = context.Render<CaptureKindMarker>(parameters => parameters
                .Add(mark => mark.Value, value)).Find("svg");

            Assert.Equal(string.Empty, svg.TextContent);
        }
    }

    [Fact]
    public void An_unrecognised_kind_renders_nothing_at_all()
    {
        using var context = new BunitContext();

        Assert.False(CaptureKinds.IsRecognised("podcast"));
        Assert.False(CaptureKinds.IsRecognised(null));
        Assert.False(CaptureKinds.IsRecognised("  "));

        var marker = context.Render<CaptureKindMarker>(parameters => parameters
            .Add(mark => mark.Value, "podcast"));

        Assert.Equal(string.Empty, marker.Markup.Trim());
    }

    [Fact]
    public void Unlabelled_by_default_because_the_row_prints_the_word()
    {
        using var context = new BunitContext();

        var svg = context.Render<CaptureKindMarker>(parameters => parameters
            .Add(mark => mark.Value, "article")).Find("svg");

        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Null(svg.GetAttribute("role"));
        Assert.Null(svg.GetAttribute("aria-label"));
        Assert.Empty(svg.QuerySelectorAll("title"));
    }

    [Fact]
    public void Labelled_names_the_kind_and_the_tooltip_is_a_separate_ask()
    {
        using var context = new BunitContext();

        var named = context.Render<CaptureKindMarker>(parameters => parameters
            .Add(mark => mark.Value, "claude-artifact")
            .Add(mark => mark.Labelled, true)).Find("svg");

        Assert.Equal("img", named.GetAttribute("role"));
        Assert.Equal("kind: Claude artifact", named.GetAttribute("aria-label"));
        Assert.Null(named.GetAttribute("aria-hidden"));
        Assert.Equal("kind: Claude artifact", Assert.Single(named.QuerySelectorAll("title")).TextContent);

        var quiet = context.Render<CaptureKindMarker>(parameters => parameters
            .Add(mark => mark.Value, "claude-artifact")
            .Add(mark => mark.Labelled, true)
            .Add(mark => mark.ShowTooltip, false)).Find("svg");

        Assert.Equal("kind: Claude artifact", quiet.GetAttribute("aria-label"));
        Assert.Empty(quiet.QuerySelectorAll("title"));
    }

    [Fact]
    public void Value_is_normalised_before_lookup()
    {
        using var context = new BunitContext();

        var svg = context.Render<CaptureKindMarker>(parameters => parameters
            .Add(mark => mark.Value, "  YouTube ")).Find("svg");

        Assert.Contains("capture-kind-marker--youtube", svg.ClassList);
        Assert.Equal("YouTube", CaptureKinds.Label(" YOUTUBE "));
    }
}
