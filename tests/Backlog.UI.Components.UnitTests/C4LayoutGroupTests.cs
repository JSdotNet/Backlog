namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The group bands the C4 zoom tab asks the layout for: the outside systems a
/// <c>group</c> block gathered, kept together and drawn as one labelled band, as the
/// design's landscape screen draws them.
/// <para>
/// The model is the slice of <c>.devbook/arc42/workspace.dsl</c> the bands are about —
/// its three groups, <c>Capture sources</c>, <c>Work tracking</c> and
/// <c>Operations</c>, with the members and relationships they hold there — so the
/// tests pin the picture the landscape actually draws without reading the devbook,
/// which changes for reasons of its own.
/// </para>
/// </summary>
public sealed class C4LayoutGroupTests
{
    internal const string Source = """
        workspace "Prompt Backlog" "For the group band tests" {
            !identifiers hierarchical
            model {
                me = person "ME" "Personal owner of the system"
                backlog = softwareSystem "Prompt Backlog" "Local-first work management" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI" {
                        workers = component "Fetch Workers" "Polling" "Background services"
                        dashboard = component "Dashboard" "Monitoring" "Razor Class Library"
                    }
                    cloud = container "Cloud Service" "Thin sync layer" "ASP.NET Core"
                }

                group "Capture sources" {
                    youtube = softwareSystem "YouTube" "Subscription feed for content capture." "External"
                    email = softwareSystem "Email / IMAP" "Email capture inbox." "External"
                    websites = softwareSystem "Websites / RSS" "Web content monitoring." "External"
                }

                group "Work tracking" {
                    github = softwareSystem "GitHub" "Issue tracking and webhook events." "External"
                }

                group "Operations" {
                    appInsights = softwareSystem "Application Insights" "Telemetry." "External"
                    pushProvider = softwareSystem "Push Provider" "FCM, for Android alerts." "External"
                }

                me -> backlog.desktop "Captures, triages, manages" "local"
                backlog.desktop.workers -> youtube "Polls the subscription feed" "HTTPS"
                backlog.desktop.workers -> websites "Monitors by RSS and DOM diff" "HTTPS"
                backlog.desktop.workers -> email "Ingests" "IMAP"
                backlog.desktop.workers -> github "Syncs issues" "HTTPS / gh CLI"
                backlog.desktop.dashboard -> appInsights "Reads telemetry signals" "HTTPS"
                backlog.desktop -> backlog.cloud "Pushes state snapshots" "HTTPS, optional"
                backlog.cloud -> pushProvider "Sends notifications" "HTTPS"
                github -> backlog.cloud "Delivers webhooks" "HTTPS"
            }
            views {
                systemLandscape "landscape" "Everything Prompt Backlog touches" { include * }
                systemContext backlog "context-backlog" "System Context" { include * }
                container backlog "containers-backlog" "Containers" { include * }
            }
        }
        """;

    internal static readonly C4Workspace Workspace = C4DslReader.Read(Source);

    internal static readonly C4LayoutOptions Bands = new(GroupBands: true);

    internal static C4Diagram Layout(string key, C4LayoutOptions? options = null)
    {
        var view = Workspace.View(key);
        Assert.NotNull(view);
        return C4LayoutEngine.Build(Workspace, view, options);
    }

    [Fact]
    public void The_test_workspace_parses_clean_and_keeps_its_groups()
    {
        Assert.Empty(Workspace.Problems);
        Assert.Equal("Capture sources", Workspace.Element("email")?.Group);
        Assert.Equal("Work tracking", Workspace.Element("github")?.Group);
        Assert.Equal("Operations", Workspace.Element("pushProvider")?.Group);
    }

    [Fact]
    public void Without_the_option_no_band_is_laid_out()
    {
        foreach (var key in new[] { "landscape", "context-backlog", "containers-backlog" })
        {
            var diagram = Layout(key);

            Assert.Empty(diagram.Groups);
            Assert.All(diagram.Edges, edge => Assert.Null(edge.Group));
        }
    }

    [Fact]
    public void With_the_option_each_group_is_one_band_holding_its_members()
    {
        var diagram = Layout("landscape", Bands);

        Assert.Equal(["Capture sources", "Work tracking", "Operations"], diagram.Groups.Select(group => group.Name));
        Assert.Equal(["youtube", "email", "websites"], Band(diagram, "Capture sources").MemberAliases);
        Assert.Equal(["github"], Band(diagram, "Work tracking").MemberAliases);
        Assert.Equal(["appInsights", "pushProvider"], Band(diagram, "Operations").MemberAliases);
    }

    /// <summary>Kept together means inside: every member's card lies within its band,
    /// and no card of anything else does.</summary>
    [Fact]
    public void Every_member_sits_inside_its_band_and_nothing_else_does()
    {
        var diagram = Layout("landscape", Bands);

        foreach (var band in diagram.Groups)
        {
            foreach (var node in diagram.Nodes)
            {
                var inside = node.X >= band.X && node.Y >= band.Y
                    && node.X + node.Width <= band.X + band.Width
                    && node.Y + node.Height <= band.Y + band.Height;

                Assert.Equal(band.MemberAliases.Contains(node.Alias), inside);
            }
        }
    }

    [Fact]
    public void Bands_do_not_overlap_each_other_and_fit_the_canvas()
    {
        var diagram = Layout("landscape", Bands);
        var bands = diagram.Groups.ToList();

        for (var i = 0; i < bands.Count; i++)
        {
            Assert.True(bands[i].X + bands[i].Width <= diagram.Width && bands[i].Y + bands[i].Height <= diagram.Height);

            for (var j = i + 1; j < bands.Count; j++)
            {
                var apart = bands[i].X + bands[i].Width <= bands[j].X || bands[j].X + bands[j].Width <= bands[i].X
                    || bands[i].Y + bands[i].Height <= bands[j].Y || bands[j].Y + bands[j].Height <= bands[i].Y;

                Assert.True(apart, $"{bands[i].Name} overlaps {bands[j].Name}");
            }
        }
    }

    /// <summary>The landscape collapses the system's three relationships into the
    /// capture sources to one line to that band, and its two into operations to one
    /// more. A band's line keeps no single member's wording.</summary>
    [Fact]
    public void Relationships_to_several_members_of_a_group_are_drawn_once_to_the_band()
    {
        var diagram = Layout("landscape", Bands);

        var capture = Assert.Single(diagram.Edges, edge => edge.Group == "Capture sources");
        Assert.Equal("backlog", capture.FromAlias);
        Assert.Equal(C4LayoutEngine.GroupAlias("Capture sources"), capture.ToAlias);
        Assert.Null(capture.Label);
        Assert.Equal("HTTPS", Assert.Single(diagram.Edges, edge => edge.Group == "Operations").Technology);

        Assert.DoesNotContain(diagram.Edges, edge => edge.ToAlias is "youtube" or "email" or "websites" or "appInsights" or "pushProvider");
    }

    /// <summary>A group with one member, or one relationship to one member, has nothing
    /// to fold: GitHub keeps both its lines, each to its own card.</summary>
    [Fact]
    public void A_single_relationship_to_a_member_stays_its_own_line()
    {
        var diagram = Layout("landscape", Bands);

        Assert.Contains(diagram.Edges, edge => edge.FromAlias == "backlog" && edge.ToAlias == "github" && edge.Group is null);
        Assert.Contains(diagram.Edges, edge => edge.FromAlias == "github" && edge.ToAlias == "backlog" && edge.Group is null);
    }

    [Fact]
    public void A_line_to_a_band_ends_on_the_bands_edge()
    {
        var diagram = Layout("landscape", Bands);
        var band = Band(diagram, "Capture sources");
        var edge = Assert.Single(diagram.Edges, edge => edge.Group == band.Name);

        var end = edge.Path.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^2..]
            .Select(value => double.Parse(value.TrimEnd(','), System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        var onEdge = Near(end[0], band.X) || Near(end[0], band.X + band.Width)
            || Near(end[1], band.Y) || Near(end[1], band.Y + band.Height);

        Assert.True(onEdge, $"{edge.Path} does not end on the band at {band.X},{band.Y} {band.Width}x{band.Height}");
    }

    /// <summary>A container view keeps the system it opens as a frame and the outside
    /// systems as bands beside it; the frame is never folded into a band.</summary>
    [Fact]
    public void A_boundary_is_still_a_frame_where_bands_are_drawn()
    {
        var diagram = Layout("containers-backlog", Bands);

        Assert.Contains(diagram.Boundaries, boundary => boundary.Id == "backlog");
        Assert.DoesNotContain(diagram.Groups.SelectMany(group => group.MemberAliases), alias => alias.StartsWith("backlog_", StringComparison.Ordinal));
    }

    private const string Edges = """
        workspace "Edge cases" "For the group band tests" {
            !identifiers hierarchical
            model {
                group "People" {
                    system = softwareSystem "Admin Console" "Declared before the person"
                    me = person "ME" "The owner"
                }
                backlog = softwareSystem "Prompt Backlog" "The system"
                group "Capture sources" {
                    youtube = softwareSystem "YouTube" "Feed" "External"
                    email = softwareSystem "Email / IMAP" "Inbox" "External"
                }
                me -> backlog "Uses"
                backlog -> youtube "Polls"
                backlog -> email "Ingests"
            }
            views {
                systemLandscape "landscape" "Everything" { include * }
                dynamic * "capture" "Capture" {
                    backlog -> youtube "Polls"
                    backlog -> email "Ingests"
                }
            }
        }
        """;

    private static C4Diagram EdgeCase(string key)
    {
        var workspace = C4DslReader.Read(Edges);
        Assert.Empty(workspace.Problems);
        return C4LayoutEngine.Build(workspace, workspace.View(key)!, Bands);
    }

    /// <summary>A dynamic view plays its arrows one by one, so two numbered steps into
    /// one band stay two arrows, each to its own card.</summary>
    [Fact]
    public void Numbered_steps_into_a_band_are_never_folded()
    {
        var diagram = EdgeCase("capture");

        Assert.Equal([1, 2], diagram.Edges.Select(edge => edge.Order).OfType<int>().Order());
        Assert.All(diagram.Edges, edge => Assert.Null(edge.Group));
        Assert.Contains(diagram.Edges, edge => edge.ToAlias == "youtube");
        Assert.Contains(diagram.Edges, edge => edge.ToAlias == "email");
    }

    /// <summary>A band holding a person sits on the top row, as the person alone would,
    /// even when the person is not the first thing the band holds.</summary>
    [Fact]
    public void A_band_holding_a_person_is_pinned_to_the_top()
    {
        var diagram = EdgeCase("landscape");

        var people = Band(diagram, "People");
        Assert.Equal(["system", "me"], people.MemberAliases);
        Assert.True(diagram.Groups.All(group => group.Y >= people.Y));
        Assert.True(diagram.Nodes.Single(node => node.Alias == "backlog").Y > people.Y + people.Height);
    }

    private static C4PlacedGroup Band(C4Diagram diagram, string name) =>
        Assert.Single(diagram.Groups, group => group.Name == name);

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.5;
}
