namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The first-party rendering: what a C4 view is actually drawn as, now that mermaid
/// no longer draws it.
/// <para>
/// Two things here are load-bearing rather than cosmetic. The card's id is
/// <c>&lt;svg id&gt;-&lt;alias&gt;</c>, which is the only thread the explorer's viewer
/// follows back to the model — get it wrong and click-to-drill, the Highlighter's
/// dimming and the search ring all stop working at once, silently. And the text is
/// XML-escaped, because this is emitted as raw markup from strings somebody authored
/// in a <c>.dsl</c>.
/// </para>
/// </summary>
public sealed class C4SvgWriterTests
{
    private const string Source = """
        workspace "Backlog" "For the renderer tests" {
            !identifiers hierarchical
            model {
                me = person "ME" "Personal owner of the system"
                backlog = softwareSystem "Prompt Backlog" "Local-first work management" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI" {
                        shell = component "Shell" "Holds the panes" "Blazor"
                        inbox = component "Inbox" "Triage" "Razor"
                    }
                    store = container "Local Task Store" "Canonical store" "SQLite" "Database"
                }
                github = softwareSystem "GitHub" "Issues and webhooks" "External"
                me -> backlog.desktop "Captures work"
                backlog.desktop -> backlog.store "Reads and writes" "file system"
                backlog.desktop -> github "Syncs issues" "HTTPS"
            }
            views {
                systemLandscape "landscape" "Everything" { include * }
                systemContext backlog "context" "System Context" { include * }
                container backlog "containers" "Container Diagram" { include * }
                component backlog.desktop "components" "Component Diagram" { include * }
            }
        }
        """;

    private static readonly C4Workspace Workspace = C4DslReader.Read(Source);

    private static C4Diagram Layout(string key)
    {
        var view = Workspace.View(key);
        Assert.NotNull(view);
        return C4LayoutEngine.Build(Workspace, view);
    }

    private static string Svg(string key) => C4SvgWriter.Write(Layout(key), "diag", "A view");

    [Fact]
    public void The_test_workspace_parses_clean()
    {
        Assert.Empty(Workspace.Problems);
    }

    // ---- the contract the viewer depends on ------------------------------------

    /// <summary>
    /// The one thing that must not change. The viewer strips the svg's id off a
    /// clicked shape to get the alias, and that alias is what resolves back to a model
    /// element — so this id is the whole of click-to-drill, dimming and the search
    /// ring, and all three fail silently together if it drifts.
    /// </summary>
    [Fact]
    public void Every_card_carries_the_svg_id_and_its_alias()
    {
        var svg = Svg("containers");

        Assert.Contains("id=\"diag-backlog_desktop\"", svg, StringComparison.Ordinal);
        Assert.Contains("id=\"diag-backlog_store\"", svg, StringComparison.Ordinal);
    }

    /// <summary>The viewer selects on <c>g.node</c>, which is mermaid's class and is
    /// kept deliberately: swapping the renderer underneath the viewer was supposed to
    /// need no change to it.</summary>
    [Fact]
    public void Every_card_is_a_node_group()
    {
        var svg = Svg("containers");
        var cards = Layout("containers").Nodes.Count;

        Assert.Equal(cards, Occurrences(svg, "class=\"node c4-diagram__node"));
    }

    /// <summary>Only the cards that lead somewhere are marked, which is what the hand
    /// cursor and the hover outline hang off. Marking all of them promised a click
    /// most of them could not answer.</summary>
    [Fact]
    public void Only_a_card_with_a_deeper_view_is_marked_drillable()
    {
        var svg = Svg("containers");

        Assert.Matches(@"id=""diag-backlog_desktop""", svg);
        Assert.Contains("c4-drillable\" id=\"diag-backlog_desktop\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("c4-drillable\" id=\"diag-backlog_store\"", svg, StringComparison.Ordinal);
    }

    // ---- what the picture says --------------------------------------------------

    [Fact]
    public void A_card_names_the_element_its_level_and_its_technology()
    {
        var svg = Svg("containers");

        Assert.Contains("Desktop App", svg, StringComparison.Ordinal);
        Assert.Contains("[Container: .NET MAUI]", svg, StringComparison.Ordinal);
        Assert.Contains("Windows client", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void An_element_with_no_technology_shows_its_level_alone()
    {
        Assert.Contains("[Person]", Svg("landscape"), StringComparison.Ordinal);
    }

    /// <summary>A frame, not a card. The system a container view opens up is drawn
    /// around its containers — and is never also a box beside them.</summary>
    [Fact]
    public void The_system_a_container_view_opens_is_drawn_as_a_frame()
    {
        var diagram = Layout("containers");

        var boundary = Assert.Single(diagram.Boundaries);
        Assert.Equal("Prompt Backlog", boundary.Label);
        Assert.Equal("Software System", boundary.Kind);
        Assert.DoesNotContain(diagram.Nodes, node => node.Node.ElementId == "backlog");
    }

    [Fact]
    public void A_frame_is_big_enough_to_hold_what_is_inside_it()
    {
        var diagram = Layout("containers");
        var boundary = Assert.Single(diagram.Boundaries);

        var inside = diagram.Nodes.Where(node => node.Node.ElementId.StartsWith("backlog.", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(inside);

        foreach (var node in inside)
        {
            Assert.True(node.X >= boundary.X, $"{node.Node.Name} starts left of its frame");
            Assert.True(node.Y >= boundary.Y, $"{node.Node.Name} starts above its frame");
            Assert.True(node.X + node.Width <= boundary.X + boundary.Width, $"{node.Node.Name} runs past its frame");
            Assert.True(node.Y + node.Height <= boundary.Y + boundary.Height, $"{node.Node.Name} runs below its frame");
        }
    }

    [Fact]
    public void An_element_outside_the_scope_is_marked_external()
    {
        var diagram = Layout("context");

        Assert.True(Assert.Single(diagram.Nodes, node => node.Node.ElementId == "github").External);
        Assert.False(Assert.Single(diagram.Nodes, node => node.Node.ElementId == "backlog").External);
    }

    /// <summary>Nothing is outside a landscape: it is a view of the whole model, so
    /// there is no inside for anything to be outside of.</summary>
    [Fact]
    public void Nothing_is_external_on_a_landscape()
    {
        Assert.DoesNotContain(Layout("landscape").Nodes, node => node.External);
    }

    /// <summary>
    /// An actor is not a box of software, and C4 has always said so by outline before
    /// anything else. A person drawn in the same rounded rectangle as a container
    /// reads as another piece of software — the confusion the notation exists to
    /// prevent.
    /// </summary>
    [Fact]
    public void A_person_is_drawn_as_a_stadium_with_a_figure()
    {
        var svg = Svg("landscape");
        var person = Assert.Single(Layout("landscape").Nodes, node => node.Node.Kind == C4ElementKind.Person);

        // A stadium is a rectangle whose corner radius is half its height.
        Assert.Contains($"rx=\"{person.Height / 2:0.##}\"", svg, StringComparison.Ordinal);
        Assert.Contains("c4-diagram__figure", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Everything_that_is_not_a_person_keeps_the_rounded_rectangle()
    {
        var svg = Svg("containers");

        Assert.Contains("rx=\"8\"", svg, StringComparison.Ordinal);
    }

    /// <summary>The dashed outline says "outside" to a reader who knows the notation;
    /// the chip says it to one who does not.</summary>
    [Fact]
    public void An_external_card_says_so_in_words_as_well_as_in_its_outline()
    {
        var svg = Svg("context");

        Assert.Contains("EXTERNAL", svg, StringComparison.Ordinal);
        Assert.Contains("c4-diagram__chip", svg, StringComparison.Ordinal);
    }

    /// <summary>The chip sits on the card's last line, so an external card gets one
    /// line of description rather than two printed under it.</summary>
    [Fact]
    public void An_external_cards_description_leaves_room_for_its_chip()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "App" "A description long enough to need more than one line of wrapping on a card"
                    other = softwareSystem "Other" "A description long enough to need more than one line of wrapping on a card" "External"
                    app -> other "Uses"
                }
                views {
                    systemContext app "context" "Context" { include * }
                }
            }
            """);

        var diagram = C4LayoutEngine.Build(workspace, workspace.View("context")!);
        var external = Assert.Single(diagram.Nodes, node => node.External);
        var svg = C4SvgWriter.Write(diagram, "diag");

        // Both descriptions are long enough to wrap, so the internal card takes two
        // lines and the external one takes a single line with its chip beneath.
        Assert.Equal(3, Occurrences(svg, "class=\"c4-diagram__descr\""));
        Assert.Contains("EXTERNAL", svg, StringComparison.Ordinal);
        Assert.True(external.External);
    }

    [Fact]
    public void Nothing_says_external_on_a_landscape()
    {
        Assert.DoesNotContain("EXTERNAL", Svg("landscape"), StringComparison.Ordinal);
    }

    // ---- layout -----------------------------------------------------------------

    /// <summary>An actor below the system it uses reads as though the system calls the
    /// person. People are pinned to the top row for that reason, not for tidiness.</summary>
    [Fact]
    public void A_person_sits_above_what_they_use()
    {
        var diagram = Layout("landscape");

        var me = Assert.Single(diagram.Nodes, node => node.Node.ElementId == "me");
        var backlog = Assert.Single(diagram.Nodes, node => node.Node.ElementId == "backlog");

        Assert.True(me.Y < backlog.Y, "the person should be laid out above the system they use");
    }

    [Fact]
    public void Nothing_overlaps_anything_else()
    {
        foreach (var key in new[] { "landscape", "context", "containers", "components" })
        {
            var nodes = Layout(key).Nodes;

            for (var a = 0; a < nodes.Count; a++)
            {
                for (var b = a + 1; b < nodes.Count; b++)
                {
                    var overlaps =
                        nodes[a].X < nodes[b].X + nodes[b].Width &&
                        nodes[b].X < nodes[a].X + nodes[a].Width &&
                        nodes[a].Y < nodes[b].Y + nodes[b].Height &&
                        nodes[b].Y < nodes[a].Y + nodes[a].Height;

                    Assert.False(overlaps, $"{key}: {nodes[a].Node.Name} overlaps {nodes[b].Node.Name}");
                }
            }
        }
    }

    [Fact]
    public void Every_card_sits_inside_the_canvas()
    {
        foreach (var key in new[] { "landscape", "context", "containers", "components" })
        {
            var diagram = Layout(key);

            foreach (var node in diagram.Nodes)
            {
                Assert.True(node.X >= 0 && node.Y >= 0, $"{key}: {node.Node.Name} is off the top or left");
                Assert.True(node.X + node.Width <= diagram.Width, $"{key}: {node.Node.Name} runs past the right edge");
                Assert.True(node.Y + node.Height <= diagram.Height, $"{key}: {node.Node.Name} runs past the bottom");
            }
        }
    }

    /// <summary>
    /// Two labels on the same spot is the failure the mermaid rendering had all over
    /// it: three edges through the middle, three labels printed on top of each other,
    /// none of them readable.
    /// </summary>
    [Fact]
    public void Two_edge_labels_do_not_land_on_the_same_spot()
    {
        var edges = Layout("context").Edges.Where(edge => !string.IsNullOrWhiteSpace(edge.Label)).ToList();

        for (var a = 0; a < edges.Count; a++)
        {
            for (var b = a + 1; b < edges.Count; b++)
            {
                var collides =
                    Math.Abs(edges[a].LabelX - edges[b].LabelX) < 120 &&
                    Math.Abs(edges[a].LabelY - edges[b].LabelY) < 22;

                Assert.False(collides, $"'{edges[a].Label}' and '{edges[b].Label}' land on top of each other");
            }
        }
    }

    /// <summary>
    /// A line through a card is the failure that made the picture hard to read: the
    /// edge from one card to another in the same row went straight through whatever
    /// sat between them, and took its label through it too.
    /// <para>
    /// Sampled off the emitted path rather than off the layout, so it checks the curve
    /// that is actually drawn — including the detour, which is the part that could be
    /// computed correctly and then written wrong.
    /// </para>
    /// </summary>
    [Fact]
    public void No_edge_passes_through_a_card_it_does_not_connect()
    {
        foreach (var key in new[] { "landscape", "context", "containers", "components" })
        {
            var diagram = Layout(key);
            var byAlias = diagram.Nodes.ToDictionary(node => node.Alias, StringComparer.Ordinal);

            foreach (var edge in diagram.Edges)
            {
                foreach (var (x, y) in SamplePath(edge.Path))
                {
                    foreach (var node in diagram.Nodes)
                    {
                        if (node.Alias == edge.FromAlias || node.Alias == edge.ToAlias) continue;

                        var inside = x > node.X + 4 && x < node.X + node.Width - 4
                            && y > node.Y + 4 && y < node.Y + node.Height - 4;

                        Assert.False(inside, $"{key}: the edge {edge.FromAlias} to {edge.ToAlias} runs through {node.Node.Name}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The label rides the curve. Halfway between the endpoints is a different place
    /// once the edge detours, and it is the middle of the card the detour avoided —
    /// so routing around a card and then printing the label on it would fix nothing.
    /// </summary>
    [Fact]
    public void No_edge_label_lands_on_a_card_it_does_not_connect()
    {
        foreach (var key in new[] { "landscape", "context", "containers", "components" })
        {
            var diagram = Layout(key);

            foreach (var edge in diagram.Edges.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Label)))
            {
                foreach (var node in diagram.Nodes)
                {
                    if (node.Alias == edge.FromAlias || node.Alias == edge.ToAlias) continue;

                    var inside = edge.LabelX > node.X && edge.LabelX < node.X + node.Width
                        && edge.LabelY > node.Y && edge.LabelY < node.Y + node.Height;

                    Assert.False(inside, $"{key}: '{edge.Label}' sits on {node.Node.Name}");
                }
            }
        }
    }

    // ---- the zoom tab's option ---------------------------------------------------

    /// <summary>The C4 tab passes no options, and its picture must be the one it was:
    /// no options and the empty options write the same string, with no chip in it.</summary>
    [Fact]
    public void Without_options_the_picture_is_unchanged_and_carries_no_zoom_chip()
    {
        foreach (var key in new[] { "landscape", "context", "containers", "components" })
        {
            var plain = C4SvgWriter.Write(Layout(key), "diag", "A view");

            Assert.Equal(plain, C4SvgWriter.Write(Layout(key), "diag", "A view", C4SvgOptions.None));
            Assert.Equal(plain, C4SvgWriter.Write(Layout(key), "diag", "A view", new C4SvgOptions(new HashSet<string>())));
            Assert.DoesNotContain("data-c4-zoom-in", plain, StringComparison.Ordinal);
        }
    }

    /// <summary>The chip goes on the named cards and only on them, inside the card's own
    /// group so a press on it still resolves to that card.</summary>
    [Fact]
    public void A_zoom_chip_is_drawn_inside_each_named_card_only()
    {
        var desktop = C4MermaidWriter.AliasOf("backlog.desktop");
        var svg = C4SvgWriter.Write(Layout("containers"), "diag", "A view", new C4SvgOptions(new HashSet<string> { desktop }));

        Assert.Equal(1, Occurrences(svg, "data-c4-zoom-in=\"true\""));

        // The card that opened last before the chip is the one it belongs to.
        var chip = svg.IndexOf("data-c4-zoom-in", StringComparison.Ordinal);
        var owner = svg.LastIndexOf("<g class=\"node ", chip, StringComparison.Ordinal);

        Assert.True(owner >= 0);
        Assert.StartsWith($"<g class=\"node ", svg[owner..]);
        Assert.Contains($"id=\"diag-{desktop}\"", svg[owner..chip], StringComparison.Ordinal);
        Assert.Contains("<title>Zoom in</title>", svg, StringComparison.Ordinal);
    }

    // ---- the zoom tab's record overlay --------------------------------------------

    /// <summary>The C4 tab never sets <see cref="C4SvgOptions.RecordChips"/>, and an
    /// overlay with nothing to count must not change the picture either: no option, a
    /// null map and an empty one all write the same string, with no chip and no debt
    /// surface in it.</summary>
    [Fact]
    public void Without_record_chips_the_picture_is_unchanged()
    {
        foreach (var key in new[] { "landscape", "context", "containers", "components" })
        {
            var plain = C4SvgWriter.Write(Layout(key), "diag", "A view");

            Assert.Equal(plain, C4SvgWriter.Write(Layout(key), "diag", "A view", new C4SvgOptions { RecordChips = null }));
            Assert.Equal(plain, C4SvgWriter.Write(Layout(key), "diag", "A view", new C4SvgOptions { RecordChips = new Dictionary<string, C4CardRecords>() }));
            Assert.DoesNotContain("data-c4-records", plain, StringComparison.Ordinal);
            Assert.DoesNotContain("c4-diagram__node--debt", plain, StringComparison.Ordinal);
        }
    }

    /// <summary>A card with nothing to count is drawn as it was, even when it is named:
    /// a zero draws no chip and no surface.</summary>
    [Fact]
    public void A_card_counted_at_zero_is_drawn_as_it_was()
    {
        var desktop = C4MermaidWriter.AliasOf("backlog.desktop");
        var plain = C4SvgWriter.Write(Layout("containers"), "diag", "A view");

        var zero = C4SvgWriter.Write(Layout("containers"), "diag", "A view",
            new C4SvgOptions { RecordChips = new Dictionary<string, C4CardRecords> { [desktop] = new(0, 0) } });

        Assert.Equal(plain, zero);
    }

    /// <summary>Decisions: an <c>n ADR</c> chip inside the named card's own group, so a
    /// press on it still selects the card, and no debt surface.</summary>
    [Fact]
    public void A_decision_count_draws_an_adr_chip_inside_its_card()
    {
        var desktop = C4MermaidWriter.AliasOf("backlog.desktop");
        var svg = C4SvgWriter.Write(Layout("containers"), "diag", "A view",
            new C4SvgOptions { RecordChips = new Dictionary<string, C4CardRecords> { [desktop] = new(2, 0) } });

        Assert.Equal(1, Occurrences(svg, "data-c4-records=\"adr\""));
        Assert.Equal(0, Occurrences(svg, "data-c4-records=\"tdr\""));
        Assert.Contains(">2 ADR</text>", svg, StringComparison.Ordinal);
        Assert.Contains("<title>2 decision records name this element</title>", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("c4-diagram__node--debt", svg, StringComparison.Ordinal);

        var chip = svg.IndexOf("data-c4-records", StringComparison.Ordinal);
        var owner = svg.LastIndexOf("<g class=\"node ", chip, StringComparison.Ordinal);

        Assert.True(owner >= 0);
        Assert.Contains($"id=\"diag-{desktop}\"", svg[owner..chip], StringComparison.Ordinal);
    }

    /// <summary>Debt: an <c>n TDR</c> chip, and the card itself on the debt surface —
    /// the class the stylesheet fills with <c>color-error</c>. Only the counted card.</summary>
    [Fact]
    public void An_open_debt_count_draws_a_tdr_chip_on_the_debt_surface()
    {
        var store = C4MermaidWriter.AliasOf("backlog.store");
        var desktop = C4MermaidWriter.AliasOf("backlog.desktop");
        var svg = C4SvgWriter.Write(Layout("containers"), "diag", "A view",
            new C4SvgOptions { RecordChips = new Dictionary<string, C4CardRecords> { [store] = new(0, 1) } });

        Assert.Equal(1, Occurrences(svg, "data-c4-records=\"tdr\""));
        Assert.Equal(0, Occurrences(svg, "data-c4-records=\"adr\""));
        Assert.Contains(">1 TDR</text>", svg, StringComparison.Ordinal);
        Assert.Contains("<title>1 open debt record names this element</title>", svg, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(svg, "c4-diagram__node--debt"));

        var debtCard = svg.IndexOf("c4-diagram__node--debt", StringComparison.Ordinal);
        var cardEnd = svg.IndexOf('>', debtCard);
        Assert.Contains($"id=\"diag-{store}\"", svg[debtCard..cardEnd], StringComparison.Ordinal);
        Assert.DoesNotContain($"c4-diagram__node--debt\" id=\"diag-{desktop}\"", svg, StringComparison.Ordinal);
    }

    /// <summary>A card with both counts carries both chips side by side, the decision
    /// chip first, and beside a Zoom in chip it already carries.</summary>
    [Fact]
    public void Both_counts_draw_both_chips_beside_the_zoom_chip()
    {
        var desktop = C4MermaidWriter.AliasOf("backlog.desktop");
        var svg = C4SvgWriter.Write(Layout("containers"), "diag", "A view",
            new C4SvgOptions(new HashSet<string> { desktop })
            {
                RecordChips = new Dictionary<string, C4CardRecords> { [desktop] = new(3, 2) }
            });

        Assert.Equal(1, Occurrences(svg, "data-c4-zoom-in=\"true\""));
        Assert.True(svg.IndexOf(">3 ADR<", StringComparison.Ordinal) < svg.IndexOf(">2 TDR<", StringComparison.Ordinal));
        Assert.Equal(1, Occurrences(svg, "c4-diagram__node--debt"));
    }

    /// <summary>Walks an emitted <c>M … C …</c> path and hands back points along the
    /// curve.</summary>
    private static IEnumerable<(double X, double Y)> SamplePath(string path) => C4PathSampler.Along(path);

    [Fact]
    public void An_edge_is_a_curve_between_the_two_cards()
    {
        var edge = Assert.Single(Layout("context").Edges, candidate => candidate.Label == "Captures work");

        Assert.StartsWith("M ", edge.Path, StringComparison.Ordinal);
        Assert.Contains(" C ", edge.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dynamic_views_steps_keep_their_numbers()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                !identifiers hierarchical
                model {
                    me = person "ME"
                    app = softwareSystem "App" {
                        web = container "Web"
                        api = container "API"
                    }
                }
                views {
                    dynamic app "capture" "Capturing" {
                        me -> app.web "Types"
                        app.web -> app.api "Posts"
                    }
                }
            }
            """);

        var diagram = C4LayoutEngine.Build(workspace, workspace.View("capture")!);

        Assert.Equal([1, 2], diagram.Edges.Select(edge => edge.Order));
        Assert.Contains("1. ", C4SvgWriter.Write(diagram, "diag"), StringComparison.Ordinal);
    }

    // ---- safety -----------------------------------------------------------------

    /// <summary>
    /// This is emitted as raw markup, and every string in it came out of a file
    /// somebody authored. An ampersand has to stay an ampersand and an angle bracket
    /// must never become a tag.
    /// </summary>
    [Fact]
    public void Text_out_of_the_workspace_is_escaped()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "Fish & <script>Chips</script>" "A & B"
                }
                views {
                    systemLandscape "all" "All" { include * }
                }
            }
            """);

        var svg = C4SvgWriter.Write(C4LayoutEngine.Build(workspace, workspace.View("all")!), "diag");

        Assert.DoesNotContain("<script>", svg, StringComparison.Ordinal);
        Assert.Contains("&amp;", svg, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_view_that_draws_nothing_says_so_rather_than_rendering_an_empty_frame()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "App"
                }
                views {
                    systemLandscape "empty" "Empty" { exclude app }
                }
            }
            """);

        var svg = C4SvgWriter.Write(C4LayoutEngine.Build(workspace, workspace.View("empty")!), "diag");

        Assert.Contains("no elements", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void The_svg_is_well_formed_enough_to_parse()
    {
        foreach (var key in new[] { "landscape", "context", "containers", "components" })
        {
            var svg = Svg(key);
            var document = System.Xml.Linq.XDocument.Parse(svg);

            Assert.Equal("svg", document.Root!.Name.LocalName);
        }
    }

    // ---- group bands ------------------------------------------------------------

    private static string Banded(string key, C4LayoutOptions? options = null) =>
        C4SvgWriter.Write(C4LayoutGroupTests.Layout(key, options), "diag", "A view");

    /// <summary>Each band is a labelled frame of its own, named for its group, as the
    /// design's landscape draws the capture sources, work tracking and operations.</summary>
    [Fact]
    public void Each_group_is_drawn_as_a_labelled_band()
    {
        var svg = Banded("landscape", C4LayoutGroupTests.Bands);

        foreach (var name in new[] { "Capture sources", "Work tracking", "Operations" })
        {
            Assert.Contains($"<g class=\"c4-diagram__group\" data-c4-group=\"{name}\">", svg, StringComparison.Ordinal);
            Assert.Contains($"class=\"c4-diagram__group-name\">{name}</text>", svg, StringComparison.Ordinal);
        }

        Assert.Equal(3, Occurrences(svg, "class=\"c4-diagram__group\""));
    }

    /// <summary>A band is a heading, not an element: it carries no <c>node</c> class and
    /// no id, so the viewer can never select, dim or drill it.</summary>
    [Fact]
    public void A_band_is_not_a_node_the_viewer_can_select()
    {
        var document = System.Xml.Linq.XDocument.Parse(Banded("landscape", C4LayoutGroupTests.Bands));

        var bands = document.Descendants()
            .Where(element => element.Attribute("data-c4-group") is not null && element.Attribute("class")?.Value == "c4-diagram__group")
            .ToList();

        Assert.Equal(3, bands.Count);
        Assert.All(bands, band => Assert.Null(band.Attribute("id")));
    }

    /// <summary>Bands are painted first, under the cards they hold, so a card is never
    /// hidden behind the band it sits in.</summary>
    [Fact]
    public void Bands_are_painted_under_the_cards()
    {
        var svg = Banded("landscape", C4LayoutGroupTests.Bands);

        var lastBand = svg.LastIndexOf("class=\"c4-diagram__group\"", StringComparison.Ordinal);
        var firstCard = svg.IndexOf("c4-diagram__node", StringComparison.Ordinal);

        Assert.True(lastBand < firstCard);
    }

    /// <summary>The line standing for several relationships into a group says which
    /// group it reaches; an ordinary line says nothing of the kind.</summary>
    [Fact]
    public void A_line_drawn_to_a_band_names_its_group()
    {
        var svg = Banded("landscape", C4LayoutGroupTests.Bands);

        Assert.Contains("<g class=\"c4-diagram__edge c4-diagram__edge--group\" data-c4-group=\"Capture sources\">", svg, StringComparison.Ordinal);
        Assert.Contains("<g class=\"c4-diagram__edge c4-diagram__edge--group\" data-c4-group=\"Operations\">", svg, StringComparison.Ordinal);
        Assert.Contains("<g class=\"c4-diagram__edge\">", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void A_banded_svg_is_well_formed_enough_to_parse()
    {
        foreach (var key in new[] { "landscape", "context-backlog", "containers-backlog" })
        {
            var document = System.Xml.Linq.XDocument.Parse(Banded(key, C4LayoutGroupTests.Bands));

            Assert.Equal("svg", document.Root!.Name.LocalName);
        }
    }

    /// <summary>
    /// The C4 tab's explorer asks the layout for no options, and its picture must be the
    /// one it was even on a model full of groups: no options and the empty options lay
    /// out and write the same string, with no band and no line to one in it.
    /// </summary>
    [Fact]
    public void Without_the_band_option_a_grouped_model_draws_no_band()
    {
        foreach (var key in new[] { "landscape", "context-backlog", "containers-backlog" })
        {
            var plain = Banded(key);

            Assert.Equal(plain, Banded(key, C4LayoutOptions.None));
            Assert.Equal(plain, Banded(key, new C4LayoutOptions(GroupBands: false)));
            Assert.DoesNotContain("c4-diagram__group", plain, StringComparison.Ordinal);
            Assert.DoesNotContain("data-c4-group", plain, StringComparison.Ordinal);
        }
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;

        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
