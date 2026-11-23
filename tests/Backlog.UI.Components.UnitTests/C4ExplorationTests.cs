namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The exploration layer: where a node drills to, what the breadcrumb says, which
/// facet values exist, and what a search matches.
/// <para>
/// The one that matters most is the alias. Mermaid puts it in the rendered shape's
/// DOM id and it is the only thread from a clicked box back to a model element — so
/// if the index and the writer ever disagree about how an identifier is sanitised, a
/// click lands on nothing for exactly the elements that needed sanitising, and
/// nothing else notices.
/// </para>
/// </summary>
public sealed class C4ExplorationTests
{
    private const string Source = """
        workspace "Backlog" "For the exploration tests" {
            !identifiers hierarchical
            model {
                me = person "ME" "The owner" "Internal"
                backlog = softwareSystem "Prompt Backlog" "The system" {
                    properties {
                        "owner" "Platform"
                        "status" "Active"
                    }
                    desktop = container "Desktop App" "Windows client" ".NET MAUI" {
                        properties {
                            "team" "Platform"
                            "status" "Active"
                        }
                        shell = component "Shell" "Holds the panes" "Blazor"
                        knowledge = component "Devbook" "Draws the folders" "Razor"
                    }
                    cloud = container "Cloud Service" "Sync" "ASP.NET Core" {
                        properties {
                            "owner" "Cloud"
                            "status" "Preview"
                        }
                    }
                    store = container "Local Task Store" "Tasks" "SQLite" "Database"
                }
                github = softwareSystem "GitHub" "Issues" "External"
                me -> backlog.desktop "Captures work"
                backlog.desktop -> backlog.store "Reads and writes"
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

    private static C4View View(string key)
    {
        var view = Workspace.View(key);
        Assert.NotNull(view);
        return view;
    }

    [Fact]
    public void The_test_workspace_parses_clean()
    {
        Assert.Empty(Workspace.Problems);
    }

    // ---- element properties ----------------------------------------------------

    /// <summary>
    /// Structurizr has no field for a team or a lifecycle status, so c4hero keeps
    /// them in a <c>properties</c> block, and the inspector shows both. A block that
    /// was skipped, as this reader did before, left both permanently empty.
    /// </summary>
    [Fact]
    public void An_elements_properties_block_is_read()
    {
        var desktop = Workspace.Element("backlog.desktop")!;

        Assert.Equal("Platform", desktop.Owner);
        Assert.Equal("Active", desktop.Status);
    }

    /// <summary>There is no standard key for either, so several spellings are
    /// accepted. This workspace writes <c>owner</c> on one element and <c>team</c> on
    /// another, and both have to answer.</summary>
    [Fact]
    public void Owner_is_found_however_the_workspace_spells_it()
    {
        Assert.Equal("Platform", Workspace.Element("backlog")!.Owner);
        Assert.Equal("Platform", Workspace.Element("backlog.desktop")!.Owner);
        Assert.Equal("Cloud", Workspace.Element("backlog.cloud")!.Owner);
    }

    [Fact]
    public void An_element_with_no_properties_block_has_no_owner_or_status()
    {
        var store = Workspace.Element("backlog.store")!;

        Assert.Null(store.Owner);
        Assert.Null(store.Status);
    }

    // ---- the node index --------------------------------------------------------

    /// <summary>
    /// The index has to be exactly what the picture draws. Built from its own idea of
    /// the view, it would offer a drill-in on a box nobody can see, or miss one that
    /// is right there.
    /// </summary>
    [Fact]
    public void The_nodes_of_a_view_are_the_elements_the_writer_draws()
    {
        var view = View("containers");

        var drawn = C4MermaidWriter.VisibleElements(Workspace, view).Select(element => element.Id).OrderBy(id => id).ToList();
        var indexed = C4Exploration.Nodes(Workspace, view).Select(node => node.ElementId).OrderBy(id => id).ToList();

        Assert.Equal(drawn, indexed);
    }

    /// <summary>
    /// The thread from the picture back to the model. A dot is not legal in a mermaid
    /// alias, so a hierarchical identifier is sanitised — and the index must sanitise
    /// it with the writer's own function, not a copy.
    /// </summary>
    [Fact]
    public void A_nodes_alias_is_the_one_mermaid_puts_in_the_rendered_shape()
    {
        var node = Assert.Single(
            C4Exploration.Nodes(Workspace, View("containers")),
            candidate => candidate.ElementId == "backlog.desktop");

        Assert.Equal("backlog_desktop", node.Alias);
        Assert.Equal(C4MermaidWriter.AliasOf("backlog.desktop"), node.Alias);
    }

    // ---- drilling --------------------------------------------------------------

    [Fact]
    public void A_software_system_drills_into_its_container_view()
    {
        Assert.Equal("containers", C4Exploration.DrillViewKey(Workspace, "backlog"));
    }

    [Fact]
    public void A_container_drills_into_its_component_view()
    {
        Assert.Equal("components", C4Exploration.DrillViewKey(Workspace, "backlog.desktop"));
    }

    /// <summary>A component is a leaf: C4 stops there, and so does drilling.</summary>
    [Fact]
    public void A_component_drills_nowhere()
    {
        Assert.Null(C4Exploration.DrillViewKey(Workspace, "backlog.desktop.shell"));
    }

    /// <summary>
    /// Only a view that exists is offered. This reader cannot invent one the workspace
    /// never declared, and an affordance that opens nothing is worse than none.
    /// </summary>
    [Fact]
    public void A_container_with_no_component_view_drills_nowhere()
    {
        Assert.Null(C4Exploration.DrillViewKey(Workspace, "backlog.cloud"));
        Assert.Null(C4Exploration.DrillViewKey(Workspace, "backlog.store"));
    }

    /// <summary>Drilling into the picture you are already looking at is not a
    /// move.</summary>
    [Fact]
    public void The_view_being_looked_at_is_never_offered_as_a_drill_target()
    {
        Assert.Null(C4Exploration.DrillViewKey(Workspace, "backlog", fromViewKey: "containers"));
        Assert.Equal("containers", C4Exploration.DrillViewKey(Workspace, "backlog", fromViewKey: "context"));
    }

    [Fact]
    public void Nodes_carry_their_own_drill_target()
    {
        var nodes = C4Exploration.Nodes(Workspace, View("containers"));

        Assert.Equal("components", Assert.Single(nodes, node => node.ElementId == "backlog.desktop").DrillViewKey);
        Assert.Null(Assert.Single(nodes, node => node.ElementId == "backlog.store").DrillViewKey);
    }

    // ---- views panel -----------------------------------------------------------

    /// <summary>Level order, not declaration order: the navigation is a descent, and
    /// a landscape listed under a component view reads as a list of files.</summary>
    [Fact]
    public void Views_are_ordered_by_level()
    {
        Assert.Equal(
            ["landscape", "context", "containers", "components"],
            C4Exploration.OrderedViews(Workspace).Select(view => view.Key));
    }

    // ---- search ----------------------------------------------------------------

    [Fact]
    public void Search_finds_an_element_by_name()
    {
        var hit = Assert.Single(C4Exploration.Search(Workspace, "Cloud Service"));

        Assert.Equal("Cloud Service", hit.Label);
        Assert.Equal(C4SearchHitKind.Element, hit.Kind);
        Assert.Equal(C4MermaidWriter.AliasOf("backlog.cloud"), hit.Alias);
        Assert.NotNull(hit.ViewKey);
    }

    /// <summary>c4hero searches four things, so this does too: names, descriptions,
    /// technologies, and view titles.</summary>
    [Theory]
    [InlineData("SQLite")]
    [InlineData("Windows client")]
    public void Search_looks_at_technology_and_description_too(string query)
    {
        Assert.NotEmpty(C4Exploration.Search(Workspace, query));
    }

    [Fact]
    public void Search_finds_a_view_by_its_title()
    {
        var hits = C4Exploration.Search(Workspace, "Component Diagram");

        Assert.Contains(hits, hit => hit.Kind == C4SearchHitKind.View && hit.ViewKey == "components");
    }

    /// <summary>A name match beats a technology match, which beats a description
    /// match. Otherwise the box called "Cloud Service" ranks below everything whose
    /// description happens to mention the cloud.</summary>
    [Fact]
    public void A_name_match_ranks_above_a_description_match()
    {
        var hits = C4Exploration.Search(Workspace, "Desktop App");

        Assert.Equal("Desktop App", hits[0].Label);
    }

    /// <summary>
    /// A hit is a way in, not a destination: the shallowest view that draws the
    /// element leaves the reader somewhere they can drill from, rather than somewhere
    /// they have to climb out of.
    /// </summary>
    [Fact]
    public void A_hit_opens_the_shallowest_view_that_draws_it()
    {
        var hit = Assert.Single(C4Exploration.Search(Workspace, "Shell"));

        Assert.Equal("components", hit.ViewKey);

        var desktop = Assert.Single(C4Exploration.Search(Workspace, "Desktop App"), candidate => candidate.Kind == C4SearchHitKind.Element);
        Assert.Equal("containers", desktop.ViewKey);
    }

    [Fact]
    public void A_one_letter_query_searches_nothing()
    {
        Assert.Empty(C4Exploration.Search(Workspace, "a"));
        Assert.Empty(C4Exploration.Search(Workspace, " "));
        Assert.Empty(C4Exploration.Search(Workspace, null));
    }

    [Fact]
    public void Search_hands_back_no_more_than_it_was_asked_for()
    {
        Assert.True(C4Exploration.Search(Workspace, "e", limit: 3).Count <= 3);
        Assert.True(C4Exploration.Search(Workspace, "a", limit: 3).Count <= 3);
        Assert.True(C4Exploration.Search(Workspace, "Backlog", limit: 1).Count <= 1);
    }

    /// <summary>
    /// A deployment instance is drawn as the thing it instantiates, so it is not a
    /// second hit for the same box — otherwise searching for a container that is
    /// deployed twice returns it three times.
    /// </summary>
    [Fact]
    public void A_deployment_instance_is_not_a_second_hit_for_the_container_it_draws()
    {
        var deployed = C4DslReader.Read("""
            workspace {
                !identifiers hierarchical
                model {
                    app = softwareSystem "App" {
                        api = container "Unique Api Name" "Serves" "ASP.NET Core"
                    }
                    deploymentEnvironment "Production" {
                        pc = deploymentNode "Host" {
                            containerInstance app.api
                        }
                        vm = deploymentNode "Other host" {
                            containerInstance app.api
                        }
                    }
                }
                views {
                    container app "containers" "Containers" { include * }
                    deployment app "Production" "deploy" "Deployment" { include * }
                }
            }
            """);

        Assert.Empty(deployed.Problems);
        Assert.Single(C4Exploration.Search(deployed, "Unique Api Name"), hit => hit.Kind == C4SearchHitKind.Element);
    }

    /// <summary>An instance is indexed as what it draws, so clicking it and filtering
    /// it both behave as the container.</summary>
    [Fact]
    public void A_deployment_instance_is_indexed_as_the_container_it_draws()
    {
        var deployed = C4DslReader.Read("""
            workspace {
                !identifiers hierarchical
                model {
                    app = softwareSystem "App" {
                        api = container "API" "Serves" "ASP.NET Core" "Database"
                    }
                    deploymentEnvironment "Production" {
                        pc = deploymentNode "Host" {
                            containerInstance app.api
                        }
                    }
                }
                views {
                    deployment app "Production" "deploy" "Deployment" { include * }
                }
            }
            """);

        var node = Assert.Single(C4Exploration.Nodes(deployed, deployed.View("deploy")!), candidate => candidate.Name == "API");

        Assert.Equal("app.api", node.ElementId);
        Assert.Equal("ASP.NET Core", node.Technology);
        Assert.Contains("Database", node.Tags);
    }
}
