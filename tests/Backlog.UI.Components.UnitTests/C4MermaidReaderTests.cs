using Backlog.UI.Components.Diagrams.C4;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The fallback model a folder gets when nobody has written a <c>.dsl</c>: the C4
/// fences the chapters already carry, read into one workspace.
/// <para>
/// What is pinned is what would go wrong quietly. The positions that differ between
/// macro families, the alias merge that makes two chapters' fences one model, the
/// view kind and scope a fence is read as — and the promise that anything this
/// reader does not know is reported with its chapter and line rather than dropped
/// from a picture that still looks finished.
/// </para>
/// </summary>
public sealed class C4MermaidReaderTests
{
    private const string ContextChapter = ".devbook/arc42/03-context-and-scope.md";
    private const string BuildingBlockChapter = ".devbook/arc42/05-building-block-view.md";

    /// <summary>The finance repository's own context fence, copied as it stands —
    /// the real case this fallback exists for: one fence, no <c>_c4/</c>.</summary>
    private const string FinanceContext = """
        C4Context
            title System Context: Finance

            Person(me, "ME", "Single user and owner of the finance records")
            System(finance, "Finance", "Windows desktop application for personal finance, local-first")
            System_Ext(onedrive, "OneDrive", "Microsoft's file service and its Windows client")
            System_Ext(extapi, "External finance API", "Anticipated, not chosen: for example a bank or market-data feed")

            Rel(me, finance, "Keeps and reviews their finance records")
            Rel(finance, onedrive, "Reads and writes JSON files in a OneDrive folder")
            Rel(finance, extapi, "May fetch data later", "not designed")
        """;

    private const string FinanceContainers = """
        C4Container
            title Containers: Finance

            Person(me, "ME")
            System_Boundary(finance, "Finance") {
                Container(app, "Desktop App", ".NET MAUI", "The window")
                ContainerDb(files, "Records", "JSON", "The finance records")
            }
            System_Ext(onedrive, "OneDrive")

            Rel(me, app, "Uses")
            Rel(app, files, "Reads and writes")
            Rel(files, onedrive, "Synced by")
        """;

    private static C4Workspace Read(params (string Origin, string Text)[] sources) =>
        C4MermaidReader.Read("From the chapters", sources.Select(source => new C4MermaidSource(source.Origin, source.Text)));

    private static C4Workspace ReadOne(string text) => Read((ContextChapter, text));

    // ---- recognising a fence ----------------------------------------------------

    [Theory]
    [InlineData("C4Context\n  title x")]
    [InlineData("\n%% a comment\nC4Container\n")]
    [InlineData("---\ntitle: front matter\n---\nC4Component")]
    [InlineData("  C4Dynamic")]
    [InlineData("C4Deployment")]
    public void A_fence_opening_with_a_C4_header_is_C4(string text) =>
        Assert.True(C4MermaidReader.IsC4(text));

    [Theory]
    [InlineData("flowchart LR\n  a --> b")]
    [InlineData("sequenceDiagram")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("%% C4Context")]
    public void Anything_else_is_not(string? text) =>
        Assert.False(C4MermaidReader.IsC4(text));

    // ---- the finance fence ------------------------------------------------------

    [Fact]
    public void The_finance_context_fence_reads_as_one_context_view_of_finance_with_nothing_reported()
    {
        var workspace = ReadOne(FinanceContext);

        Assert.Empty(workspace.Problems);
        var view = Assert.Single(workspace.Views);
        Assert.Equal(C4ViewKind.SystemContext, view.Kind);
        Assert.Equal("finance", view.ScopeId);
        Assert.Equal("System Context: Finance", view.Title);
        Assert.Equal("system-context-finance", view.Key);
        Assert.True(view.IncludesAll);
        Assert.Equal(["me", "finance", "onedrive", "extapi"], view.Includes);

        Assert.Equal(4, workspace.Elements.Count);
        Assert.Equal(3, workspace.Relationships.Count);
        Assert.Equal("not designed", workspace.Relationships.Single(relationship => relationship.DestinationId == "extapi").Technology);
    }

    /// <summary>The derived workspace has to reach the screen through the same
    /// writer an authored one does, with every box the fence drew.</summary>
    [Fact]
    public void The_writer_draws_every_element_of_a_derived_view()
    {
        var workspace = ReadOne(FinanceContext);

        var mermaid = C4MermaidWriter.Write(workspace, workspace.Views[0]);

        Assert.StartsWith("C4Context", mermaid, StringComparison.Ordinal);
        foreach (var alias in new[] { "me", "finance", "onedrive", "extapi" })
        {
            Assert.Contains($"({alias},", mermaid, StringComparison.Ordinal);
        }

        Assert.Contains("Rel(finance, onedrive, \"Reads and writes JSON files in a OneDrive folder\")", mermaid, StringComparison.Ordinal);
    }

    // ---- elements -----------------------------------------------------------------

    /// <summary>A person or system takes a description third; a container or
    /// component takes a technology third and the description fourth. One set of
    /// positions for both files a description as a technology.</summary>
    [Fact]
    public void A_container_takes_a_technology_where_a_system_takes_its_description()
    {
        var workspace = ReadOne("""
            C4Container
                System(sys, "System", "What it does")
                Container(api, "API", "ASP.NET Core", "Serves the app")
                Component(parser, "Parser", "C#", "Reads things")
            """);

        Assert.Equal("What it does", workspace.Element("sys")!.Description);
        Assert.Null(workspace.Element("sys")!.Technology);
        Assert.Equal("ASP.NET Core", workspace.Element("api")!.Technology);
        Assert.Equal("Serves the app", workspace.Element("api")!.Description);
        Assert.Equal(C4ElementKind.Component, workspace.Element("parser")!.Kind);
        Assert.Equal("C#", workspace.Element("parser")!.Technology);
    }

    [Theory]
    [InlineData("SystemDb(x, \"X\")", C4ElementKind.SoftwareSystem, "Database", false)]
    [InlineData("SystemQueue_Ext(x, \"X\")", C4ElementKind.SoftwareSystem, "Queue", true)]
    [InlineData("ContainerDb(x, \"X\", \"SQLite\")", C4ElementKind.Container, "Database", false)]
    [InlineData("ContainerQueue(x, \"X\")", C4ElementKind.Container, "Queue", false)]
    [InlineData("ContainerDb_Ext(x, \"X\")", C4ElementKind.Container, "Database", true)]
    [InlineData("ComponentQueue_Ext(x, \"X\")", C4ElementKind.Component, "Queue", true)]
    [InlineData("Person_Ext(x, \"X\")", C4ElementKind.Person, null, true)]
    [InlineData("Container_Ext(x, \"X\")", C4ElementKind.Container, null, true)]
    public void A_macros_shape_and_reach_become_tags(string statement, C4ElementKind kind, string? shapeTag, bool external)
    {
        var workspace = ReadOne($"C4Container\n    {statement}\n");

        var element = workspace.Element("x")!;
        Assert.Empty(workspace.Problems);
        Assert.Equal(kind, element.Kind);
        Assert.Equal("X", element.Name);
        if (shapeTag is not null) Assert.True(element.HasTag(shapeTag));
        Assert.Equal(external, element.HasTag("External"));
    }

    [Fact]
    public void Named_tags_are_read_and_other_named_arguments_are_ignored()
    {
        var workspace = ReadOne("""
            C4Container
                Container(api, "API", "C#", "Serves", $tags="v1+beta", $link="https://example.org", $sprite="api")
            """);

        var api = workspace.Element("api")!;
        Assert.Empty(workspace.Problems);
        Assert.True(api.HasTag("v1"));
        Assert.True(api.HasTag("beta"));
        Assert.Equal("Serves", api.Description);
    }

    [Fact]
    public void A_comma_inside_quotes_stays_in_its_argument()
    {
        var workspace = ReadOne("""
            C4Context
                System(sys, "Store (SQLite)", "Reads, writes, and syncs")
            """);

        Assert.Equal("Store (SQLite)", workspace.Element("sys")!.Name);
        Assert.Equal("Reads, writes, and syncs", workspace.Element("sys")!.Description);
    }

    // ---- boundaries -----------------------------------------------------------------

    [Fact]
    public void A_system_boundary_is_the_system_and_its_containers_sit_inside_it()
    {
        var workspace = ReadOne(FinanceContainers);

        Assert.Empty(workspace.Problems);
        Assert.Equal(C4ElementKind.SoftwareSystem, workspace.Element("finance")!.Kind);
        Assert.Equal("finance", workspace.Element("app")!.ParentId);
        Assert.Equal("finance", workspace.Element("files")!.ParentId);
        Assert.Null(workspace.Element("onedrive")!.ParentId);
    }

    [Fact]
    public void A_container_boundary_is_the_container_and_a_brace_on_its_own_line_opens_it()
    {
        var workspace = ReadOne("""
            C4Component
                Container_Boundary(api, "API")
                {
                    Component(parser, "Parser", "C#")
                }
                Component(outside, "Outside", "C#")
            """);

        Assert.Empty(workspace.Problems);
        Assert.Equal(C4ElementKind.Container, workspace.Element("api")!.Kind);
        Assert.Equal("api", workspace.Element("parser")!.ParentId);
        Assert.Null(workspace.Element("outside")!.ParentId);
    }

    /// <summary>An enterprise or generic boundary is a frame and nothing else, the
    /// same as a Structurizr <c>group</c>: what is inside keeps the parent it would
    /// have had outside it.</summary>
    [Fact]
    public void An_enterprise_boundary_groups_without_becoming_an_element()
    {
        var workspace = ReadOne("""
            C4Context
                Enterprise_Boundary(corp, "Contoso") {
                    System(sys, "System")
                    Boundary(team, "Team A", "team") {
                        System_Boundary(inner, "Inner") {
                            Container(c, "C", "C#")
                        }
                    }
                }
            """);

        Assert.Empty(workspace.Problems);
        Assert.Null(workspace.Element("corp"));
        Assert.Null(workspace.Element("team"));
        Assert.Equal("Contoso", workspace.Element("sys")!.Group);
        Assert.Null(workspace.Element("sys")!.ParentId);
        Assert.Equal("Team A", workspace.Element("inner")!.Group);
        Assert.Null(workspace.Element("inner")!.ParentId);
        Assert.Equal("inner", workspace.Element("c")!.ParentId);
    }

    // ---- relationships ----------------------------------------------------------------

    [Fact]
    public void Every_relationship_variant_is_read_with_its_ends_the_right_way_round()
    {
        var workspace = ReadOne("""
            C4Context
                System(a, "A")
                System(b, "B")
                System(c, "C")
                Rel_U(a, b, "up")
                Rel_Down(a, b, "down", "HTTPS")
                Rel_Back(a, c, "back")
                BiRel(b, c, "both")
                RelIndex(7, c, a, "indexed")
            """);

        Assert.Empty(workspace.Problems);
        var edges = workspace.Relationships.Select(edge => (edge.SourceId, edge.DestinationId, edge.Description)).ToList();

        Assert.Contains(("a", "b", "up"), edges);
        Assert.Contains(("a", "b", "down"), edges);
        Assert.Contains(("c", "a", "back"), edges);
        Assert.Contains(("b", "c", "both"), edges);
        Assert.Contains(("c", "b", "both"), edges);
        Assert.Contains(("c", "a", "indexed"), edges);
        Assert.Equal("HTTPS", workspace.Relationships.Single(edge => edge.Description == "down").Technology);
    }

    [Fact]
    public void The_same_relationship_in_two_fences_is_kept_once()
    {
        var workspace = Read(
            (ContextChapter, "C4Context\n System(a, \"A\")\n System(b, \"B\")\n Rel(a, b, \"Uses\")"),
            (BuildingBlockChapter, "C4Container\n System(a, \"A\")\n System(b, \"B\")\n Rel(a, b, \"Uses\")"));

        Assert.Single(workspace.Relationships);
    }

    [Fact]
    public void A_dynamic_fence_numbers_its_steps_and_adds_no_static_relationship()
    {
        var workspace = ReadOne("""
            C4Dynamic
                title Signing in
                Person(user, "User")
                Container(web, "Web", "React")
                Container(api, "API", "C#")
                Rel(user, web, "Opens")
                Rel(web, api, "Calls")
                RelIndex(9, api, web, "Answers")
            """);

        Assert.Empty(workspace.Problems);
        Assert.Empty(workspace.Relationships);

        var view = Assert.Single(workspace.Views);
        Assert.Equal(C4ViewKind.Dynamic, view.Kind);
        Assert.Equal(
            [(1, "user", "web", "Opens"), (2, "web", "api", "Calls"), (9, "api", "web", "Answers")],
            view.Steps.Select(step => (step.Order, step.SourceId, step.DestinationId, step.Description)));
    }

    // ---- deployment ---------------------------------------------------------------

    /// <summary>
    /// A deployment fence's nodes and instances are its own, namespaced under the
    /// view, so a node aliased like a static element does not collide with it — and
    /// a container drawn inside a node is the container running there, pointing at
    /// the static declaration of its alias wherever in the chapters that is.
    /// </summary>
    [Fact]
    public void A_deployment_fence_declares_instances_of_the_containers_it_names()
    {
        var workspace = Read(
            (".arc42/07-deployment-view.md", """
                C4Deployment
                    title Production
                    Deployment_Node(cloud, "Azure", "Cloud", "The tenant") {
                        Node(appservice, "App Service", "PaaS") {
                            Container(api, "API", "C#")
                        }
                    }
                    Node_L(api2, "Spare", "VM")
                """),
            (BuildingBlockChapter, """
                C4Container
                    System_Boundary(sys, "System") {
                        Container(api, "API", "ASP.NET Core", "Serves")
                    }
                """));

        Assert.Empty(workspace.Problems);

        var node = workspace.Element("production.cloud")!;
        Assert.Equal(C4ElementKind.DeploymentNode, node.Kind);
        Assert.Equal("Cloud", node.Technology);
        Assert.Equal("The tenant", node.Description);
        Assert.Equal("production", node.Environment);

        var instance = workspace.Element("production.api")!;
        Assert.Equal(C4ElementKind.ContainerInstance, instance.Kind);
        Assert.Equal("production.appservice", instance.ParentId);
        Assert.Equal("api", instance.InstanceOfId);

        Assert.Equal(C4ElementKind.Container, workspace.Element("api")!.Kind);

        var view = workspace.View("production")!;
        Assert.Equal(C4ViewKind.Deployment, view.Kind);
        Assert.Equal("production", view.Environment);
        Assert.Contains("production.api", view.Includes);

        var mermaid = C4MermaidWriter.Write(workspace, view);
        Assert.Contains("production_api", mermaid, StringComparison.Ordinal);
        Assert.Contains("production_cloud", mermaid, StringComparison.Ordinal);
    }

    // ---- merging across chapters ---------------------------------------------------

    /// <summary>
    /// The convention that makes the chapters one model: the same alias is the same
    /// element. A system declared as a box in the context chapter and as a boundary
    /// in the building block chapter is one system with its containers inside it,
    /// so the context view can be drilled into the container view.
    /// </summary>
    [Fact]
    public void Fences_sharing_an_alias_merge_into_one_element()
    {
        var workspace = Read((ContextChapter, FinanceContext), (BuildingBlockChapter, FinanceContainers));

        Assert.Empty(workspace.Problems);
        Assert.Single(workspace.Elements, element => element.Id == "finance");

        var finance = workspace.Element("finance")!;
        Assert.Equal("Windows desktop application for personal finance, local-first", finance.Description);
        Assert.Equal(["app", "files"], workspace.Children("finance").Select(child => child.Id));

        // The person kept the first chapter's description; the second fence left it out.
        Assert.Equal("Single user and owner of the finance records", workspace.Element("me")!.Description);

        var containers = workspace.Views.Single(view => view.Kind == C4ViewKind.Container);
        Assert.Equal("finance", containers.ScopeId);

        var drawn = C4MermaidWriter.VisibleElements(workspace, containers).Select(element => element.Id).ToList();
        Assert.Contains("app", drawn);
        Assert.Contains("files", drawn);

        var context = workspace.Views.Single(view => view.Kind == C4ViewKind.SystemContext);
        Assert.Equal("finance", context.ScopeId);
    }

    [Fact]
    public void A_later_declaration_fills_what_the_first_left_empty_and_adds_its_tags()
    {
        var workspace = Read(
            (ContextChapter, "C4Context\n System(sys, \"System\")"),
            (BuildingBlockChapter, "C4Context\n System(sys, \"Renamed\", \"Now described\", $tags=\"core\")"));

        var sys = Assert.Single(workspace.Elements);
        Assert.Equal("System", sys.Name);
        Assert.Equal("Now described", sys.Description);
        Assert.True(sys.HasTag("core"));
    }

    [Fact]
    public void Two_chapters_disagreeing_about_what_an_alias_is_is_reported()
    {
        var workspace = Read(
            (ContextChapter, "C4Context\n Person(x, \"X\")"),
            (BuildingBlockChapter, "C4Container\n Container(x, \"X\", \"C#\")"));

        Assert.Equal(C4ElementKind.Person, workspace.Element("x")!.Kind);
        var problem = Assert.Single(workspace.Problems);
        Assert.Equal(2, problem.Line);
        Assert.StartsWith(BuildingBlockChapter, problem.Message, StringComparison.Ordinal);
    }

    // ---- view kind, scope and key -------------------------------------------------------

    [Fact]
    public void A_context_fence_with_no_single_subject_is_a_landscape()
    {
        var workspace = ReadOne("""
            C4Context
                System(a, "A")
                System(b, "B")
                System_Ext(c, "C")
            """);

        var view = Assert.Single(workspace.Views);
        Assert.Equal(C4ViewKind.SystemLandscape, view.Kind);
        Assert.Null(view.ScopeId);
    }

    [Fact]
    public void An_external_system_is_never_what_a_context_fence_is_about()
    {
        var workspace = ReadOne("C4Context\n System(a, \"A\")\n System_Ext(b, \"B\")");

        Assert.Equal("a", workspace.Views[0].ScopeId);
        Assert.Equal(C4ViewKind.SystemContext, workspace.Views[0].Kind);
    }

    [Fact]
    public void A_component_fence_is_scoped_on_its_first_container_boundary()
    {
        var workspace = ReadOne("""
            C4Component
                Container_Boundary(api, "API") {
                    Component(a, "A", "C#")
                }
                Container_Boundary(web, "Web") {
                    Component(b, "B", "C#")
                }
            """);

        Assert.Equal(C4ViewKind.Component, workspace.Views[0].Kind);
        Assert.Equal("api", workspace.Views[0].ScopeId);
    }

    /// <summary>
    /// A container fence with no boundary has nothing to be scoped on, and the
    /// writer's default for an unscoped container view is the whole top of the
    /// model. In a model merged from every chapter that would be another chapter's
    /// systems, so the view draws exactly what its own fence drew.
    /// </summary>
    [Fact]
    public void A_container_fence_with_no_system_boundary_draws_only_its_own_elements()
    {
        var workspace = Read(
            (ContextChapter, FinanceContext),
            (BuildingBlockChapter, """
                C4Container
                    title Loose containers
                    Container(app, "Desktop App", ".NET MAUI")
                    ContainerDb(files, "Records", "JSON")
                    Rel(app, files, "Reads")
                """));

        var view = workspace.View("loose-containers")!;
        Assert.Equal(C4ViewKind.Container, view.Kind);
        Assert.Null(view.ScopeId);

        Assert.Equal(
            ["app", "files"],
            C4MermaidWriter.VisibleElements(workspace, view).Select(element => element.Id).Order(StringComparer.Ordinal));
    }

    /// <summary>The same for a landscape: a context fence with two subjects draws
    /// its own people and systems, not every one the other chapters declared.</summary>
    [Fact]
    public void A_landscape_beside_another_chapters_fence_draws_only_its_own_elements()
    {
        var workspace = Read(
            (ContextChapter, FinanceContext),
            (BuildingBlockChapter, """
                C4Context
                    title Two systems
                    Person(me, "ME")
                    System(budget, "Budget")
                    System(ledger, "Ledger")
                    Rel(me, budget, "Plans")
                """));

        var view = workspace.View("two-systems")!;
        Assert.Equal(C4ViewKind.SystemLandscape, view.Kind);

        Assert.Equal(
            ["budget", "ledger", "me"],
            C4MermaidWriter.VisibleElements(workspace, view).Select(element => element.Id).Order(StringComparer.Ordinal));

        // The scoped finance view is untouched by the merge's other fence.
        var finance = workspace.View("system-context-finance")!;
        Assert.Empty(finance.Excludes);
    }

    // ---- parents that would loop ---------------------------------------------------

    /// <summary>A boundary that redeclares itself inside itself would make the
    /// system its own parent, and every walk down the model would never end.
    /// Finishing at all is half of what this pins.</summary>
    [Fact]
    public void A_system_declared_inside_its_own_boundary_is_reported_and_stays_at_the_top()
    {
        var workspace = ReadOne("""
            C4Container
                System_Boundary(finance, "Finance") {
                    System(finance, "Finance")
                    Container(app, "App", "C#")
                }
            """);

        Assert.Null(workspace.Element("finance")!.ParentId);
        var problem = Assert.Single(workspace.Problems);
        Assert.Contains("would sit inside itself", problem.Message, StringComparison.Ordinal);

        Assert.Equal(["finance", "app"], workspace.Subtree("finance").Select(element => element.Id));
        foreach (var view in workspace.Views) Assert.NotEmpty(C4MermaidWriter.Write(workspace, view));
    }

    [Fact]
    public void Two_chapters_nesting_two_systems_inside_each_other_are_reported_rather_than_looped()
    {
        var workspace = Read(
            (ContextChapter, "C4Container\n System_Boundary(a, \"A\") {\n  System(b, \"B\")\n }"),
            (BuildingBlockChapter, "C4Container\n System_Boundary(b, \"B\") {\n  System(a, \"A\")\n }"));

        Assert.Equal("a", workspace.Element("b")!.ParentId);
        Assert.Null(workspace.Element("a")!.ParentId);
        var problem = Assert.Single(workspace.Problems);
        Assert.StartsWith(BuildingBlockChapter, problem.Message, StringComparison.Ordinal);

        Assert.Equal(["a", "b"], workspace.Subtree("a").Select(element => element.Id));
        foreach (var view in workspace.Views) Assert.NotEmpty(C4MermaidWriter.Write(workspace, view));
    }

    [Fact]
    public void An_untitled_fence_is_keyed_by_its_chapter_and_diagram_type()
    {
        var workspace = ReadOne("C4Container\n Container(a, \"A\", \"C#\")");

        Assert.Equal("03-context-and-scope-container", workspace.Views[0].Key);
        Assert.Null(workspace.Views[0].Title);
    }

    [Fact]
    public void Two_fences_with_the_same_title_get_distinct_keys()
    {
        var workspace = Read(
            (ContextChapter, "C4Context\n title Overview\n System(a, \"A\")"),
            (BuildingBlockChapter, "C4Context\n title Overview\n System(b, \"B\")"),
            (".arc42/06-runtime-view.md", "C4Context\n title Overview\n System(c, \"C\")"));

        Assert.Equal(["overview", "overview-2", "overview-3"], workspace.Views.Select(view => view.Key));
    }

    [Fact]
    public void Each_view_knows_the_chapter_its_fence_came_from()
    {
        var reading = C4MermaidReader.ReadWithOrigins(null, [
            new C4MermaidSource(ContextChapter, FinanceContext),
            new C4MermaidSource(BuildingBlockChapter, FinanceContainers)]);

        Assert.Equal(ContextChapter, reading.ViewOrigins["system-context-finance"]);
        Assert.Equal(BuildingBlockChapter, reading.ViewOrigins["containers-finance"]);
    }

    // ---- what could not be read --------------------------------------------------------

    [Fact]
    public void An_unknown_macro_is_reported_with_its_line_within_the_fence_and_its_chapter()
    {
        var workspace = ReadOne("""
            C4Context
                System(a, "A")
                Frobnicate(a, "B")
            """);

        var problem = Assert.Single(workspace.Problems);
        Assert.Equal(3, problem.Line);
        Assert.Equal("Frobnicate", problem.Construct);
        Assert.StartsWith(ContextChapter, problem.Message, StringComparison.Ordinal);
        Assert.NotNull(workspace.Element("a"));
    }

    [Fact]
    public void A_line_that_is_no_statement_at_all_is_reported()
    {
        var workspace = ReadOne("C4Context\n    a --> b\n");

        var problem = Assert.Single(workspace.Problems);
        Assert.Equal(2, problem.Line);
        Assert.Equal("a", problem.Construct);
    }

    /// <summary>Mermaid would draw an edge to an undeclared alias as an empty box;
    /// the writer drops it, so it is the report that has to say so.</summary>
    [Fact]
    public void A_relationship_to_an_alias_no_fence_declares_is_reported()
    {
        var workspace = ReadOne("C4Context\n System(a, \"A\")\n Rel(a, ghost, \"Haunts\")");

        Assert.Empty(workspace.Relationships);
        var problem = Assert.Single(workspace.Problems);
        Assert.Equal(3, problem.Line);
        Assert.Contains("`ghost`", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Style_and_layout_macros_and_comments_are_skipped_without_a_problem()
    {
        var workspace = ReadOne("""
            C4Context
                %% the subject first
                title Styled
                System(a, "A")
                System(b, "B")
                Rel(a, b, "Uses")
                UpdateElementStyle(a, $bgColor="grey")
                UpdateRelStyle(a, b, $textColor="blue", $offsetY="-10")
                UpdateLayoutConfig($c4ShapeInRow="3", $c4BoundaryInRow="1")
                UpdateBoundaryStyle($elementName="x", $bgColor="white")
                AddElementTag("v1", $bgColor="#d73027")
                AddRelTag("backup", $textColor="orange")
                AddBoundaryTag("cloud", $borderColor="red")
            """);

        Assert.Empty(workspace.Problems);
        Assert.Equal("Styled", workspace.Views[0].Title);
    }

    [Fact]
    public void A_fence_that_is_not_C4_is_reported_and_draws_no_view()
    {
        var workspace = ReadOne("flowchart LR\n a --> b");

        Assert.Empty(workspace.Views);
        var problem = Assert.Single(workspace.Problems);
        Assert.Equal("flowchart", problem.Construct);
    }

    [Fact]
    public void An_unclosed_boundary_is_reported()
    {
        var workspace = ReadOne("C4Container\n System_Boundary(s, \"S\") {\n Container(a, \"A\", \"C#\")");

        Assert.Single(workspace.Problems);
        Assert.Equal("s", workspace.Element("a")!.ParentId);
    }
}
