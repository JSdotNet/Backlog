namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a view actually shows, and whether mermaid can parse the result.
/// <para>
/// The writer's real job is selection rather than formatting: <c>include *</c> means
/// "the default set for this kind of view", not "every element", and an edge between
/// two containers has to survive being drawn on a diagram that only shows systems.
/// Both of those fail quietly — as a view that draws too much, or one that draws no
/// edges at all — so both are pinned here.
/// </para>
/// </summary>
public sealed class C4MermaidWriterTests
{
    private const string Source = """
        workspace "Backlog" "Local-first work management" {
            !identifiers hierarchical
            model {
                user = person "ME" "Personal owner of the system"
                backlog = softwareSystem "Prompt Backlog" "Local-first work management" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI Blazor Hybrid"
                    cloud = container "Cloud Service" "Thin sync layer" "ASP.NET Core"
                    store = container "Local Storage" "Markdown source of truth" "Markdown, JSON" "Database"
                }
                github = softwareSystem "GitHub" "Issues and webhooks" "External"
                user -> backlog.desktop "Captures and organises work"
                backlog.desktop -> backlog.store "Reads and writes" "File I/O"
                backlog.desktop -> github "Reads issues" "HTTPS"
                backlog.cloud -> github "Forwards webhooks" "HTTPS"
            }
            views {
                systemContext backlog "context" "How Backlog sits in its world" { include * }
                container backlog "containers" "The deployable split" { include * }
                systemLandscape "landscape" "Everything" { include * }
            }
        }
        """;

    private static string Render(string key, string? source = null)
    {
        var workspace = C4DslReader.Read(source ?? Source);
        var view = workspace.View(key);
        Assert.NotNull(view);
        return C4MermaidWriter.Write(workspace, view);
    }

    [Theory]
    [InlineData("context", "C4Context")]
    [InlineData("containers", "C4Container")]
    [InlineData("landscape", "C4Context")]
    public void Each_view_kind_opens_with_the_mermaid_header_that_draws_it(string key, string header)
    {
        Assert.StartsWith(header + "\n", Render(key), StringComparison.Ordinal);
    }

    [Fact]
    public void The_title_names_the_view_and_carries_no_line_breaks()
    {
        Assert.Contains("title How Backlog sits in its world", Render("context"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A container view draws the scoped system as the boundary around its
    /// containers, not as a box of its own beside them.
    /// </summary>
    [Fact]
    public void A_container_view_draws_its_scope_as_a_boundary_around_the_containers()
    {
        var mermaid = Render("containers");

        Assert.Contains("System_Boundary(backlog, \"Prompt Backlog\") {", mermaid, StringComparison.Ordinal);
        Assert.Contains("Container(backlog_desktop, \"Desktop App\", \".NET MAUI Blazor Hybrid\", \"Windows client\")", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("System(backlog,", mermaid, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>include *</c> on a container view of one system must not pull in the
    /// containers of another. This is the failure that reads as "the diagram is
    /// showing everything".
    /// </summary>
    [Fact]
    public void A_container_view_does_not_open_up_the_other_systems_it_talks_to()
    {
        var mermaid = Render("containers");

        Assert.Contains("System_Ext(github, \"GitHub\", \"Issues and webhooks\")", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("System_Boundary(github", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void An_element_outside_the_scope_is_drawn_as_external()
    {
        var mermaid = Render("context");

        Assert.Contains("Person_Ext(user,", mermaid, StringComparison.Ordinal);
        Assert.Contains("System_Ext(github,", mermaid, StringComparison.Ordinal);
        Assert.Contains("System(backlog,", mermaid, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing is external on a landscape: it is a view of the whole model, so there
    /// is no inside for anything to be outside of.
    /// </summary>
    [Fact]
    public void Nothing_is_external_on_a_system_landscape()
    {
        var mermaid = Render("landscape");

        Assert.DoesNotContain("_Ext(", mermaid, StringComparison.Ordinal);
        Assert.Contains("Person(user,", mermaid, StringComparison.Ordinal);
        Assert.Contains("System(github,", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void A_landscape_shows_systems_and_people_and_no_containers()
    {
        var mermaid = Render("landscape");

        Assert.DoesNotContain("Container(", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("Boundary(", mermaid, StringComparison.Ordinal);
    }

    /// <summary>
    /// The relationships in a model are mostly between containers, and a context view
    /// draws systems. Without rolling an endpoint up to the nearest thing the view
    /// does draw, a context view would come out with almost no edges — which reads as
    /// a broken diagram rather than as a modelling choice.
    /// </summary>
    [Fact]
    public void An_edge_between_containers_is_rolled_up_to_the_systems_a_context_view_draws()
    {
        var mermaid = Render("context");

        Assert.Contains("Rel(user, backlog, \"Captures and organises work\")", mermaid, StringComparison.Ordinal);
        Assert.Contains("Rel(backlog, github, \"Reads issues\", \"HTTPS\")", mermaid, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two containers of the same system, rolled up on a context view, become the
    /// same box. An edge from a thing to itself is dropped rather than drawn as a
    /// loop nobody modelled.
    /// </summary>
    [Fact]
    public void An_edge_that_rolls_up_onto_itself_is_dropped()
    {
        var mermaid = Render("context");

        Assert.DoesNotContain("Rel(backlog, backlog", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("Reads and writes", mermaid, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two container-level edges to the same outside system roll up onto the same pair
    /// of boxes but say different things, and both survive. Collapsing them to one
    /// arrow would drop a relationship the model states — a context view that showed
    /// only "reads issues" would be hiding the webhook forwarding entirely.
    /// </summary>
    [Fact]
    public void Two_different_relationships_that_roll_up_to_the_same_pair_keep_both_labels()
    {
        var mermaid = Render("context");

        Assert.Contains("Rel(backlog, github, \"Reads issues\", \"HTTPS\")", mermaid, StringComparison.Ordinal);
        Assert.Contains("Rel(backlog, github, \"Forwards webhooks\", \"HTTPS\")", mermaid, StringComparison.Ordinal);
    }

    /// <summary>
    /// When the roll-up makes two relationships genuinely identical, one arrow is
    /// drawn. The same arrow twice is not extra information, it is a doubled line.
    /// </summary>
    [Fact]
    public void An_edge_that_rolls_up_to_something_already_drawn_is_written_once()
    {
        var mermaid = Render("context", """
            workspace {
                !identifiers hierarchical
                model {
                    app = softwareSystem "App" {
                        web = container "Web"
                        api = container "API"
                    }
                    other = softwareSystem "Other"
                    app.web -> other "Calls" "HTTPS"
                    app.api -> other "Calls" "HTTPS"
                }
                views {
                    systemContext app "context" { include * }
                }
            }
            """);

        Assert.Equal(1, mermaid.Split("Rel(app, other").Length - 1);
    }

    [Fact]
    public void A_container_tagged_Database_is_drawn_as_one()
    {
        Assert.Contains("ContainerDb(backlog_store,", Render("containers"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A dot in a mermaid C4 alias is a parse error, and hierarchical identifiers are
    /// dotted. If this ever regresses the diagram does not render at all.
    /// </summary>
    [Fact]
    public void Hierarchical_identifiers_become_aliases_mermaid_can_parse()
    {
        var mermaid = Render("containers");

        Assert.Contains("backlog_desktop", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("backlog.desktop", mermaid, StringComparison.Ordinal);
    }

    /// <summary>
    /// A quote inside a mermaid C4 argument closes the argument, and mermaid has no
    /// escape for it, so the label has to give up the character rather than the
    /// diagram giving up rendering.
    /// </summary>
    [Fact]
    public void A_quote_inside_a_name_does_not_break_the_argument()
    {
        var mermaid = Render("context", """
            workspace {
                model {
                    app = softwareSystem "The \"main\" system" "It is \"fine\""
                }
                views {
                    systemContext app "context" { include * }
                }
            }
            """);

        Assert.Contains("System(app, \"The 'main' system\", \"It is 'fine'\")", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void A_description_spanning_lines_is_flattened_so_the_macro_stays_on_one_line()
    {
        var mermaid = Render("context", """
            workspace {
                model {
                    app = softwareSystem "App" "First line\nSecond line"
                }
                views {
                    systemContext app "context" { include * }
                }
            }
            """);

        Assert.Contains("System(app, \"App\", \"First line Second line\")", mermaid, StringComparison.Ordinal);
    }

    /// <summary>
    /// Mermaid will not accept a <c>Rel</c> whose endpoint is a boundary. It reports
    /// "references an unknown shape" and refuses the <em>whole</em> diagram — so one
    /// such line is a blank frame, not a missing arrow.
    /// <para>
    /// This is the ordinary shape of a component view, not an edge case: something
    /// outside relates to the container, the container is the frame, and the endpoint
    /// rolls up onto it. Unit tests could not see it — the emitted text looked
    /// perfectly reasonable — and only a browser refusing to draw it did.
    /// </para>
    /// </summary>
    [Fact]
    public void A_relationship_that_rolls_up_onto_a_boundary_is_dropped_rather_than_drawn()
    {
        var mermaid = Render("components", """
            workspace {
                !identifiers hierarchical
                model {
                    user = person "ME"
                    app = softwareSystem "App" {
                        desktop = container "Desktop App" "Windows client" ".NET MAUI" {
                            shell = component "Shell"
                            inbox = component "Inbox"
                        }
                    }
                    user -> app.desktop "Uses"
                    app.desktop.shell -> app.desktop.inbox "Offers the pane"
                }
                views {
                    component app.desktop "components" "Components" { include * }
                }
            }
            """);

        // The container is the frame here, so it is a boundary and never an endpoint.
        Assert.Contains("Container_Boundary(app_desktop,", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("Rel(user, app_desktop", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("Rel(app_desktop,", mermaid, StringComparison.Ordinal);

        // What is inside it still draws.
        Assert.Contains("Rel(app_desktop_shell, app_desktop_inbox, \"Offers the pane\")", mermaid, StringComparison.Ordinal);
    }

    /// <summary>The same rule on a container view: a system with containers on screen
    /// is a boundary, so nothing points at it.</summary>
    [Fact]
    public void No_relationship_points_at_the_system_a_container_view_opens_up()
    {
        var mermaid = Render("containers");

        Assert.Contains("System_Boundary(backlog,", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("Rel(backlog,", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain(", backlog,", mermaid, StringComparison.Ordinal);
    }

    // ---- dynamic -------------------------------------------------------------

    [Fact]
    public void A_dynamic_view_writes_its_steps_in_order_and_nothing_else()
    {
        var mermaid = Render("capture", """
            workspace {
                !identifiers hierarchical
                model {
                    user = person "ME"
                    app = softwareSystem "App" {
                        web = container "Web"
                        api = container "API"
                        store = container "Store" "" "" "Database"
                    }
                    other = softwareSystem "Other"
                    app.api -> other "Never on this view"
                }
                views {
                    dynamic app "capture" "Capturing an entry" {
                        user -> app.web "Types an entry"
                        app.web -> app.api "Posts it"
                        app.api -> app.store "Saves it"
                    }
                }
            }
            """);

        Assert.StartsWith("C4Dynamic\n", mermaid, StringComparison.Ordinal);

        var relationships = mermaid.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("Rel(", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(
        [
            "Rel(user, app_web, \"Types an entry\")",
            "Rel(app_web, app_api, \"Posts it\")",
            "Rel(app_api, app_store, \"Saves it\")"
        ], relationships);
    }

    // ---- deployment ----------------------------------------------------------

    private const string Deployed = """
        workspace {
            !identifiers hierarchical
            model {
                app = softwareSystem "App" {
                    api = container "API" "Serves requests" "ASP.NET Core"
                    store = container "Store" "Holds it" "SQLite" "Database"
                }
                deploymentEnvironment "Production" {
                    pc = deploymentNode "Windows PC" "Personal machine" "Windows 11" {
                        runtime = deploymentNode "Runtime" "" ".NET 10" {
                            containerInstance app.api
                            containerInstance app.store
                        }
                        disk = infrastructureNode "Local disk" "" "NTFS"
                    }
                }
                deploymentEnvironment "Staging" {
                    vm = deploymentNode "Test VM"
                }
            }
            views {
                deployment app "Production" "deploy-production" "Where it runs" { include * }
            }
        }
        """;

    [Fact]
    public void A_deployment_view_nests_its_nodes()
    {
        var mermaid = Render("deploy-production", Deployed);

        Assert.StartsWith("C4Deployment\n", mermaid, StringComparison.Ordinal);
        Assert.Contains("Deployment_Node(pc, \"Windows PC\", \"Windows 11\", \"Personal machine\") {", mermaid, StringComparison.Ordinal);
        Assert.Contains("Deployment_Node(pc_runtime, \"Runtime\", \".NET 10\", \"\") {", mermaid, StringComparison.Ordinal);
    }

    /// <summary>
    /// A deployment view is of one environment. Drawing another environment's nodes
    /// would put the staging machine on the production diagram.
    /// </summary>
    [Fact]
    public void A_deployment_view_shows_only_its_own_environment()
    {
        var mermaid = Render("deploy-production", Deployed);

        Assert.DoesNotContain("Test VM", mermaid, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>containerInstance app.api</c> names the container that runs here, not a box
    /// called "app.api". Drawing the identifier would label the reference instead of
    /// the thing.
    /// </summary>
    [Fact]
    public void An_instance_is_drawn_as_the_container_it_instantiates()
    {
        var mermaid = Render("deploy-production", Deployed);

        Assert.Contains("\"API\", \"ASP.NET Core\", \"Serves requests\"", mermaid, StringComparison.Ordinal);
        Assert.Contains("ContainerDb(", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("\"app.api\"", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void An_infrastructure_node_is_drawn_with_its_technology()
    {
        Assert.Contains("Container(pc_disk, \"Local disk\", \"NTFS\", \"\")", Render("deploy-production", Deployed), StringComparison.Ordinal);
    }

    // ---- edges of the contract ------------------------------------------------

    /// <summary>
    /// A view that selected nothing is a real state — an <c>include</c> naming
    /// identifiers that do not exist — and an empty frame reads as a rendering
    /// failure, so the diagram says so itself.
    /// </summary>
    [Fact]
    public void A_view_that_selects_nothing_says_so_in_the_diagram()
    {
        var mermaid = Render("empty", """
            workspace {
                model {
                    app = softwareSystem "App"
                }
                views {
                    systemLandscape "empty" {
                        exclude app
                    }
                }
            }
            """);

        Assert.Contains("Nothing to draw", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void An_excluded_element_is_not_drawn()
    {
        var mermaid = Render("landscape", """
            workspace {
                model {
                    keep = softwareSystem "Keep"
                    drop = softwareSystem "Drop"
                }
                views {
                    systemLandscape "landscape" {
                        include *
                        exclude drop
                    }
                }
            }
            """);

        Assert.Contains("\"Keep\"", mermaid, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Drop\"", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void An_element_named_by_include_is_drawn_even_when_the_default_set_leaves_it_out()
    {
        var mermaid = Render("context", """
            workspace {
                !identifiers hierarchical
                model {
                    app = softwareSystem "App" {
                        api = container "API"
                    }
                    lonely = softwareSystem "Lonely"
                }
                views {
                    systemContext app "context" {
                        include *
                        include lonely
                    }
                }
            }
            """);

        Assert.Contains("\"Lonely\"", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void Element_styles_reach_the_diagram_as_mermaid_style_updates()
    {
        var mermaid = Render("landscape", """
            workspace {
                model {
                    app = softwareSystem "App" "" "Highlight"
                }
                views {
                    systemLandscape "landscape" { include * }
                    styles {
                        element "Highlight" {
                            background #438dd5
                            color #ffffff
                        }
                    }
                }
            }
            """);

        // Quoted, and that is the whole point of pinning it. Mermaid's C4 lexer
        // refuses a bare `#438dd5` and refuses the whole diagram with it, so an
        // unquoted colour is not a cosmetic difference — it is a blank frame. This
        // assertion was written wrong first and only a browser found it.
        Assert.Contains("UpdateElementStyle(app, $bgColor=\"#438dd5\", $fontColor=\"#ffffff\")", mermaid, StringComparison.Ordinal);
    }

    /// <summary>
    /// A workspace with no styles must not emit a trailing block of style calls, and
    /// a view of a model with no colours should be plain mermaid.
    /// </summary>
    [Fact]
    public void A_workspace_without_styles_emits_no_style_updates()
    {
        Assert.DoesNotContain("UpdateElementStyle", Render("containers"), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_view_of_the_sample_workspace_writes_something_mermaid_shaped()
    {
        var workspace = C4DslReader.Read(Source);

        foreach (var view in workspace.Views)
        {
            var mermaid = C4MermaidWriter.Write(workspace, view);

            Assert.StartsWith("C4", mermaid, StringComparison.Ordinal);
            Assert.Contains("title ", mermaid, StringComparison.Ordinal);

            // Balanced braces, or mermaid refuses the whole diagram.
            Assert.Equal(mermaid.Count(character => character == '{'), mermaid.Count(character => character == '}'));
        }
    }
}
