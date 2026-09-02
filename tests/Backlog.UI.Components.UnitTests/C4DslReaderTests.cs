namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The reader is a second implementation of a dialect whose first implementation is
/// c4hero's TypeScript parser, in another repository, where nothing here can see
/// it. <c>.arc42/adr/0004</c> names that as a drift hazard, so what is pinned below
/// is not "the parser works" but the specific readings that would go wrong quietly:
/// the positional arguments that differ between element kinds, the identifier
/// resolution order, and the promise that an unsupported construct is reported
/// rather than dropped.
/// <para>
/// The shapes come from c4hero's own conformance fixture
/// (<c>src/lib/dsl/__fixtures__/hierarchical-landscape.dsl</c>) rather than from the
/// Structurizr language reference, which describes a superset nobody writes here.
/// </para>
/// </summary>
public sealed class C4DslReaderTests
{
    private const string Minimal = """
        workspace "Backlog" "Local-first work management" {
            model {
                user = person "ME" "Personal owner of the system"
                backlog = softwareSystem "Prompt Backlog" "Local-first work management" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI Blazor Hybrid"
                    store = container "Local Storage" "Markdown source of truth" "Markdown, JSON" "Database"
                }
                github = softwareSystem "GitHub" "Issues and webhooks" "External"
                user -> backlog "Captures and organises work"
                desktop -> github "Reads issues" "HTTPS"
            }
            views {
                systemContext backlog "context-backlog" "How Backlog sits in its world" {
                    include *
                    autolayout lr
                }
                container backlog "container-backlog" "The deployable split" {
                    include *
                    autolayout tb
                }
            }
        }
        """;

    [Fact]
    public void The_workspace_name_and_description_are_read_from_the_header()
    {
        var workspace = C4DslReader.Read(Minimal);

        Assert.Equal("Backlog", workspace.Name);
        Assert.Equal("Local-first work management", workspace.Description);
    }

    [Fact]
    public void Elements_declared_inside_a_system_carry_it_as_their_parent()
    {
        var workspace = C4DslReader.Read(Minimal);

        Assert.Equal("backlog", workspace.Element("desktop")!.ParentId);
        Assert.Null(workspace.Element("backlog")!.ParentId);
    }

    /// <summary>
    /// The reading that would be wrong invisibly. A person takes
    /// name/description/tags and a container takes name/description/technology/tags,
    /// so reading one set of positions for both files a system's tags as its
    /// technology and leaves it untagged — a container that stops being a database
    /// and a picture that stops saying so.
    /// </summary>
    [Fact]
    public void A_container_takes_a_technology_argument_where_a_system_takes_tags()
    {
        var workspace = C4DslReader.Read(Minimal);

        var store = workspace.Element("store")!;
        Assert.Equal("Markdown, JSON", store.Technology);
        Assert.True(store.HasTag("Database"));

        var github = workspace.Element("github")!;
        Assert.Null(github.Technology);
        Assert.True(github.HasTag("External"));
    }

    [Fact]
    public void Relationships_carry_their_description_and_technology()
    {
        var workspace = C4DslReader.Read(Minimal);

        var relationship = Assert.Single(workspace.Relationships, candidate => candidate.SourceId == "desktop");
        Assert.Equal("github", relationship.DestinationId);
        Assert.Equal("Reads issues", relationship.Description);
        Assert.Equal("HTTPS", relationship.Technology);
    }

    [Fact]
    public void A_clean_workspace_reports_no_problems()
    {
        Assert.Empty(C4DslReader.Read(Minimal).Problems);
    }

    // ---- identifiers ---------------------------------------------------------

    /// <summary>
    /// c4hero's fixture declares the same local name in several systems on purpose;
    /// under hierarchical identifiers those are distinct elements, and collapsing
    /// them would silently merge two containers into one.
    /// </summary>
    [Fact]
    public void Hierarchical_identifiers_qualify_a_child_with_its_parent()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                !identifiers hierarchical
                model {
                    one = softwareSystem "One" {
                        app = container "App One"
                    }
                    two = softwareSystem "Two" {
                        app = container "App Two"
                    }
                    one.app -> two.app "Calls"
                }
            }
            """);

        Assert.Empty(workspace.Problems);
        Assert.Equal("App One", workspace.Element("one.app")!.Name);
        Assert.Equal("App Two", workspace.Element("two.app")!.Name);

        var relationship = Assert.Single(workspace.Relationships);
        Assert.Equal("one.app", relationship.SourceId);
        Assert.Equal("two.app", relationship.DestinationId);
    }

    /// <summary>
    /// Resolution order is Structurizr's: the enclosing scope first, then each scope
    /// above it, then the name as written. Getting it backwards binds a local name to
    /// a same-named element in another system — an edge that is drawn, and wrong.
    /// </summary>
    [Fact]
    public void A_relative_identifier_resolves_inside_its_own_scope_first()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                !identifiers hierarchical
                model {
                    one = softwareSystem "One" {
                        api = container "API One"
                        web = container "Web One"
                        web -> api "Calls"
                    }
                    two = softwareSystem "Two" {
                        api = container "API Two"
                    }
                }
            }
            """);

        Assert.Empty(workspace.Problems);
        var relationship = Assert.Single(workspace.Relationships);
        Assert.Equal("one.web", relationship.SourceId);
        Assert.Equal("one.api", relationship.DestinationId);
    }

    [Fact]
    public void Flat_identifiers_leave_a_child_named_as_declared()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    one = softwareSystem "One" {
                        app = container "App"
                    }
                }
            }
            """);

        Assert.NotNull(workspace.Element("app"));
        Assert.Null(workspace.Element("one.app"));
    }

    [Fact]
    public void An_identifier_declared_twice_is_reported()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "First"
                    app = softwareSystem "Second"
                }
            }
            """);

        var problem = Assert.Single(workspace.Problems);
        Assert.Contains("more than once", problem.Message, StringComparison.Ordinal);
        Assert.Equal("First", workspace.Element("app")!.Name);
    }

    // ---- relationship forms --------------------------------------------------

    [Fact]
    public void An_arrow_with_no_source_takes_the_element_it_is_declared_in()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    other = softwareSystem "Other"
                    app = softwareSystem "App" {
                        -> other "Calls out"
                    }
                }
            }
            """);

        Assert.Empty(workspace.Problems);
        var relationship = Assert.Single(workspace.Relationships);
        Assert.Equal("app", relationship.SourceId);
        Assert.Equal("other", relationship.DestinationId);
    }

    [Fact]
    public void The_word_this_means_the_element_it_is_declared_in()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    other = softwareSystem "Other"
                    app = softwareSystem "App" {
                        this -> other "Calls out"
                    }
                }
            }
            """);

        var relationship = Assert.Single(workspace.Relationships);
        Assert.Equal("app", relationship.SourceId);
    }

    /// <summary>
    /// The relationship is not drawn, so the reader has to say so. Dropping it would
    /// leave a picture that is missing an edge for a reason nothing states.
    /// </summary>
    [Fact]
    public void A_relationship_naming_something_that_does_not_exist_is_reported_and_not_drawn()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "App"
                    app -> ghost "Calls"
                }
            }
            """);

        Assert.Empty(workspace.Relationships);
        var problem = Assert.Single(workspace.Problems);
        Assert.Equal("ghost", problem.Construct);
        Assert.Contains("not drawn", problem.Message, StringComparison.Ordinal);
    }

    // ---- views ---------------------------------------------------------------

    [Fact]
    public void A_view_key_and_description_are_read_after_the_scope()
    {
        var workspace = C4DslReader.Read(Minimal);

        var view = workspace.View("container-backlog")!;
        Assert.Equal(C4ViewKind.Container, view.Kind);
        Assert.Equal("backlog", view.ScopeId);
        Assert.Equal("The deployable split", view.Description);
        Assert.True(view.IncludesAll);
        Assert.Equal("tb", view.AutoLayout);
    }

    /// <summary>
    /// A landscape is of the whole model and takes no scope, so its first argument is
    /// its key. Reading it like the scoped kinds would take the key for a scope and
    /// leave the view pointed at an element that does not exist.
    /// </summary>
    [Fact]
    public void A_system_landscape_takes_no_scope()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "App"
                }
                views {
                    systemLandscape landscape "Everything at once" {
                        include *
                    }
                }
            }
            """);

        var view = Assert.Single(workspace.Views);
        Assert.Equal(C4ViewKind.SystemLandscape, view.Kind);
        Assert.Equal("landscape", view.Key);
        Assert.Null(view.ScopeId);
        Assert.Equal("Everything at once", view.Description);
    }

    [Fact]
    public void A_deployment_view_reads_its_environment_before_its_key()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "App" {
                        api = container "API"
                    }
                    deploymentEnvironment "Production" {
                        host = deploymentNode "Windows PC" "Personal machine" "Windows 11" {
                            containerInstance api
                        }
                    }
                }
                views {
                    deployment app "Production" "deploy-production" "Where it runs" {
                        include *
                    }
                }
            }
            """);

        Assert.Empty(workspace.Problems);
        var view = Assert.Single(workspace.Views);
        Assert.Equal(C4ViewKind.Deployment, view.Kind);
        Assert.Equal("app", view.ScopeId);
        Assert.Equal("Production", view.Environment);
        Assert.Equal("deploy-production", view.Key);

        var instance = Assert.Single(workspace.Elements, element => element.Kind == C4ElementKind.ContainerInstance);
        Assert.Equal("api", instance.InstanceOfId);
        Assert.Equal("host", instance.ParentId);
        Assert.Equal("Production", instance.Environment);
    }

    /// <summary>
    /// A group inside a deployment environment must not overwrite the environment:
    /// the deployment view selects on the environment, and losing it empties the
    /// view.
    /// </summary>
    [Fact]
    public void A_group_inside_a_deployment_environment_leaves_the_environment_intact()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    deploymentEnvironment "Production" {
                        group "Edge" {
                            host = deploymentNode "Host"
                        }
                    }
                }
            }
            """);

        var host = workspace.Element("host")!;
        Assert.Equal("Production", host.Environment);
        Assert.Equal("Edge", host.Group);
    }

    [Fact]
    public void Dynamic_view_steps_are_numbered_in_the_order_they_are_written()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    user = person "ME"
                    app = softwareSystem "App" {
                        web = container "Web"
                        api = container "API"
                    }
                }
                views {
                    dynamic app "capture" "Capturing an entry" {
                        user -> web "Types an entry"
                        web -> api "Posts it"
                        api -> web "Confirms"
                    }
                }
            }
            """);

        var view = Assert.Single(workspace.Views);
        Assert.Equal([1, 2, 3], view.Steps.Select(step => step.Order));
        Assert.Equal("Types an entry", view.Steps[0].Description);
        Assert.Equal("api", view.Steps[2].SourceId);
    }

    /// <summary>
    /// A view the DSL left unkeyed is still addressable. A chapter reference names a
    /// view by its key, so a view without one would be a view no chapter can point
    /// at — and the reference is the whole point of the arrangement.
    /// </summary>
    [Fact]
    public void An_unkeyed_view_is_given_a_key_it_can_be_referenced_by()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "App"
                }
                views {
                    systemContext app {
                        include *
                    }
                }
            }
            """);

        var view = Assert.Single(workspace.Views);
        Assert.Equal("systemcontext-app", view.Key);
        Assert.Same(view, workspace.View("systemcontext-app"));
    }

    [Fact]
    public void A_quoted_view_key_is_also_addressable_by_its_slug()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "App"
                }
                views {
                    systemContext app "Context Of App" {
                        include *
                    }
                }
            }
            """);

        Assert.NotNull(workspace.View("context-of-app"));
    }

    [Fact]
    public void Two_views_cannot_end_up_sharing_a_key()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "App"
                }
                views {
                    systemContext app "same" { include * }
                    container app "same" { include * }
                }
            }
            """);

        Assert.Equal(2, workspace.Views.Count);
        Assert.Equal(2, workspace.Views.Select(view => view.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ---- what is reported rather than guessed --------------------------------

    /// <summary>
    /// The promise the whole design rests on. A workspace assembled by
    /// <c>!include</c> is one this reader has only partly seen, and a reader that
    /// stayed quiet about it would draw a confident picture of half a model.
    /// </summary>
    [Theory]
    [InlineData("!include other.dsl")]
    [InlineData("!docs docs")]
    [InlineData("!adrs adrs")]
    [InlineData("!plugin com.example.Plugin")]
    public void A_directive_the_reader_cannot_honour_is_named_in_the_problems(string directive)
    {
        var workspace = C4DslReader.Read($$"""
            workspace {
                {{directive}}
                model {
                    app = softwareSystem "App"
                }
            }
            """);

        var problem = Assert.Single(workspace.Problems);
        Assert.Equal(directive.Split(' ')[0], problem.Construct);
        Assert.Contains("is not read", problem.Message, StringComparison.Ordinal);
        Assert.Equal(2, problem.Line);
    }

    [Fact]
    public void An_include_expression_is_reported_because_the_view_may_be_missing_elements()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "App"
                }
                views {
                    systemLandscape "all" {
                        include element.tag==Database
                    }
                }
            }
            """);

        var problem = Assert.Single(workspace.Problems);
        Assert.Contains("expression", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Constructs whose syntax is understood and which change no picture are skipped
    /// knowingly. Reporting them would bury the ones that matter under noise nobody
    /// can act on.
    /// </summary>
    [Fact]
    public void Configuration_and_properties_blocks_are_not_reported()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "App"
                }
                configuration {
                    scope softwaresystem
                }
                properties {
                    "structurizr.dslEditor" "false"
                }
            }
            """);

        Assert.Empty(workspace.Problems);
    }

    [Fact]
    public void A_file_that_does_not_open_with_workspace_is_refused_with_a_reason()
    {
        var workspace = C4DslReader.Read("model { app = softwareSystem \"App\" }");

        Assert.Empty(workspace.Elements);
        var problem = Assert.Single(workspace.Problems);
        Assert.Contains("has to open with", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_file_is_a_problem_and_not_an_exception()
    {
        Assert.Single(C4DslReader.Read("   ").Problems);
        Assert.Single(C4DslReader.Read(null).Problems);
    }

    // ---- lexing --------------------------------------------------------------

    [Fact]
    public void All_three_comment_forms_are_removed_without_losing_the_line_count()
    {
        var workspace = C4DslReader.Read("""
            /*
             * A block comment spanning lines.
             */
            workspace {
                # a hash comment
                model {
                    // a slash comment
                    app = softwareSystem "App"  # trailing
                }
                !docs docs
            }
            """);

        Assert.Equal("App", workspace.Element("app")!.Name);
        var problem = Assert.Single(workspace.Problems);
        Assert.Equal(10, problem.Line);
    }

    [Fact]
    public void A_hash_inside_a_quoted_string_is_not_a_comment()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "App #1" "Colour is #ff0000"
                }
            }
            """);

        Assert.Equal("App #1", workspace.Element("app")!.Name);
        Assert.Equal("Colour is #ff0000", workspace.Element("app")!.Description);
    }

    [Fact]
    public void An_escaped_quote_stays_inside_the_string()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "The \"main\" system"
                }
            }
            """);

        Assert.Equal("The \"main\" system", workspace.Element("app")!.Name);
    }

    [Fact]
    public void Element_styles_are_read_and_not_reported_as_unknown()
    {
        var workspace = C4DslReader.Read("""
            workspace {
                model {
                    app = softwareSystem "App" "" "Database"
                }
                views {
                    styles {
                        element "Database" {
                            shape Cylinder
                            background #438dd5
                            color #ffffff
                        }
                        relationship "Relationship" {
                            thickness 2
                        }
                    }
                }
            }
            """);

        Assert.Empty(workspace.Problems);
        var style = Assert.Single(workspace.ElementStyles!);
        Assert.Equal("Database", style.Tag);
        Assert.Equal("#438dd5", style.Background);
        Assert.Equal("#ffffff", style.Color);
        Assert.Equal("Cylinder", style.Shape);
    }

    /// <summary>
    /// Shaped after c4hero's conformance fixture: hierarchical identifiers, the same
    /// local name in several scopes, qualified endpoints at two and three levels,
    /// groups, component view scopes spelled <c>system.container</c>, dynamic views
    /// and styles around all of it. If this parses clean, the dialect is being read
    /// the way c4hero writes it.
    /// </summary>
    [Fact]
    public void The_shape_of_c4heros_own_conformance_fixture_parses_without_problems()
    {
        var workspace = C4DslReader.Read("""
            workspace "Label 1" "Label 2" {

                !identifiers hierarchical

                model {
                    group "People" {
                        actor1 = person "Label 3"
                        actor2 = person "Label 4" "Label 9"
                    }

                    group "Systems" {
                        sys1 = softwareSystem "Label 12" "Label 13" "Label 18,Label 19" {
                            app1 = container "Label 21" "Label 22" "Label 23"
                            app2 = container "Label 24" "" "Label 25"
                            app3 = container "Label 26" "" "Label 47" "Database"
                            app4 = container "Label 86" "" "Label 54" "Label 87" {
                                part1 = component "Label 88"
                                part2 = component "Label 89" "Label 90" "Label 91"
                            }
                        }

                        sys2 = softwareSystem "Label 52" "" "Label 53" {
                            app1 = container "Label 55" "Label 56" "Label 57"
                        }
                    }

                    actor1 -> sys1.app1 "Label 453"
                    actor2 -> sys2.app1 "Label 454"
                    sys1.app1 -> sys2 "Label 455"
                    sys1.app4.part1 -> sys1.app3 "Label 456"
                    actor1 -> sys1.app4.part2 "Label 457"

                    deploymentEnvironment "Label 700" {
                        node1 = deploymentNode "Label 701" "Label 702" "Label 703" {
                            node2 = deploymentNode "Label 704" {
                                containerInstance sys1.app1
                            }
                            infra1 = infrastructureNode "Label 705" "" "Label 706"
                        }
                    }
                }

                views {
                    systemLandscape ref2 "Label 601" {
                        include *
                        autolayout lr
                    }

                    systemContext sys1 "Label 602" {
                        include *
                        autolayout lr
                    }

                    container sys1 "Label 607" "Label 12" {
                        include *
                        autolayout lr
                    }

                    component sys1.app4 "Label 650" "Label 651" {
                        include *
                        autolayout lr
                    }

                    dynamic sys1 "Label 688" "Label 689" {
                        sys1.app1 -> sys2 "Label 690"
                        sys1.app1 -> sys1.app3 "Label 691"
                        autolayout lr
                    }

                    deployment sys1 "Label 700" "Label 710" {
                        include *
                        autolayout lr
                    }

                    styles {
                        element "Element" {
                            shape RoundedBox
                            color #ffffff
                        }
                        element "Database" {
                            shape Cylinder
                        }
                        relationship "Relationship" {
                            thickness 2
                        }
                    }
                }
            }
            """);

        Assert.Empty(workspace.Problems);

        // The same local name in two systems stayed two elements.
        Assert.Equal("Label 21", workspace.Element("sys1.app1")!.Name);
        Assert.Equal("Label 55", workspace.Element("sys2.app1")!.Name);

        // A three-level endpoint resolved.
        Assert.Contains(workspace.Relationships, relationship =>
            relationship.SourceId == "sys1.app4.part1" && relationship.DestinationId == "sys1.app3");

        // A component view scoped as system.container.
        var component = Assert.Single(workspace.Views, view => view.Kind == C4ViewKind.Component);
        Assert.Equal("sys1.app4", component.ScopeId);

        Assert.Equal(6, workspace.Views.Count);
    }
}
