namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The C4 zoom tab's deployment views: the environment tab list, the dashed door to
/// each other environment, and the inspector of a selected deployment node.
/// <para>
/// The cases are the requirement's own (<c>.devbook/domain/devbook/requirements.md</c>,
/// "The supplementary views move in their own way", scenario "Another environment"),
/// on a workspace deployed the way this repository's own model is: a Windows PC running
/// an MSIX package, and Azure beside it.
/// </para>
/// </summary>
public sealed class C4ZoomDeploymentTests : IDisposable
{
    private const string Attach = "backlogC4Zoom.attach";

    internal const string Source = """
        workspace "Backlog" "For the deployment tests" {
            !identifiers hierarchical
            model {
                backlog = softwareSystem "Prompt Backlog" "Local-first work management" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI Blazor Hybrid"
                    store = container "Local Task Store" "Markdown files" "File system"
                    cloud = container "Cloud Service" "Sync" "ASP.NET Core"
                    cloudDb = container "Cloud Database" "Sync state" "Cosmos DB" "Database"
                }
                backlog.desktop -> backlog.store "Reads and writes"
                backlog.cloud -> backlog.cloudDb "Stores"

                deploymentEnvironment "Local machine" {
                    pc = deploymentNode "Windows PC" "Everything core runs here." "Windows 11" {
                        msix = deploymentNode "MSIX package" "Signed, sideloaded from GitHub Releases." "MSIX" {
                            containerInstance backlog.desktop
                        }
                        root = deploymentNode "User-owned root" "" "File system" {
                            containerInstance backlog.store
                            clones = infrastructureNode "Repository working copies" "Read in place." "Git"
                        }
                    }
                }

                deploymentEnvironment "Azure" {
                    azure = deploymentNode "Azure" "Additive and optional." "Azure subscription" {
                        app = deploymentNode "App Service" "" "Linux, .NET" {
                            containerInstance backlog.cloud
                        }
                        data = deploymentNode "Managed data" "" "PaaS" {
                            containerInstance backlog.cloudDb
                        }
                    }
                }
            }
            views {
                systemContext backlog "context-backlog" "System context" { include * }
                container backlog "containers-backlog" "Containers" { include * }
                deployment backlog "Local machine" "deployment-local" "Local deployment" { include * }
                deployment backlog "Azure" "deployment-azure" "Cloud deployment" { include * }
            }
        }
        """;

    internal static readonly C4Workspace Workspace = C4DslReader.Read(Source);

    private readonly BunitContext _context = new();

    public C4ZoomDeploymentTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.JSInterop.Setup<bool>(Attach, _ => true).SetResult(true);
    }

    public void Dispose() => _context.Dispose();

    private static C4View View(string key) => Workspace.View(key) ?? throw new InvalidOperationException(key);

    private static string Alias(string elementId) => C4MermaidWriter.AliasOf(elementId);

    [Fact]
    public void The_test_workspace_parses_clean() => Assert.Empty(Workspace.Problems);

    // ---- environments --------------------------------------------------------------

    [Fact]
    public void A_deployment_view_lists_every_environment_of_its_scope_with_the_open_one_current()
    {
        var environments = C4ZoomDeployment.Environments(Workspace, View("deployment-local"));

        // In the order the workspace declares them, the canonical one first.
        Assert.Equal(["Local machine", "Azure"], environments.Select(environment => environment.Name));

        var local = environments.Single(environment => environment.Name == "Local machine");
        Assert.True(local.Current);
        Assert.Equal("deployment-local", local.ViewKey);

        var azure = environments.Single(environment => environment.Name == "Azure");
        Assert.False(azure.Current);
        Assert.Equal("deployment-azure", azure.ViewKey);
        Assert.Equal(["Cloud Service", "Cloud Database"], azure.Runs);
    }

    [Fact]
    public void Any_other_view_has_no_environments()
    {
        Assert.Empty(C4ZoomDeployment.Environments(Workspace, View("containers-backlog")));
    }

    [Fact]
    public void A_scope_deployed_once_has_one_environment_and_so_nothing_to_switch_to()
    {
        var workspace = Workspace with { Views = [.. Workspace.Views.Where(view => view.Key != "deployment-azure")] };

        var environments = C4ZoomDeployment.Environments(workspace, View("deployment-local"));

        Assert.Single(environments);
    }

    // ---- the inspector -------------------------------------------------------------

    [Fact]
    public void A_deployment_node_is_described_with_the_container_instances_it_runs()
    {
        var details = C4ZoomDeployment.Details(Workspace, "pc.msix");

        Assert.NotNull(details);
        Assert.Equal("MSIX package", details.Name);
        Assert.Equal("Deployment Node", details.Kind);
        Assert.Equal("MSIX", details.Technology);
        Assert.Equal("Signed, sideloaded from GitHub Releases.", details.Description);
        Assert.Equal("Local machine", details.Environment);

        var run = Assert.Single(details.Runs);
        Assert.Equal("backlog.desktop", run.ContainerId);
        Assert.Equal("Desktop App", run.Name);
        Assert.Equal("containers-backlog", run.ContainersViewKey);
    }

    [Fact]
    public void A_node_runs_everything_beneath_it()
    {
        var details = C4ZoomDeployment.Details(Workspace, "pc");

        Assert.NotNull(details);
        Assert.Equal(["Desktop App", "Local Task Store"], details.Runs.Select(run => run.Name));
    }

    [Fact]
    public void A_node_with_no_description_reads_none_rather_than_an_empty_line()
    {
        Assert.Null(C4ZoomDeployment.Details(Workspace, "azure.app")?.Description);
    }

    [Fact]
    public void A_container_is_not_a_deployment_node()
    {
        Assert.Null(C4ZoomDeployment.Details(Workspace, "backlog.desktop"));
        Assert.Null(C4ZoomDeployment.Details(Workspace, null));
    }

    [Fact]
    public void An_instance_of_a_system_with_no_container_view_is_listed_and_links_nowhere()
    {
        var workspace = Workspace with { Views = [.. Workspace.Views.Where(view => view.Kind != C4ViewKind.Container)] };

        var run = Assert.Single(C4ZoomDeployment.Details(workspace, "pc.msix")!.Runs);

        Assert.Null(run.ContainersViewKey);
    }

    // ---- the picture ---------------------------------------------------------------

    [Fact]
    public void The_frames_of_deployment_nodes_are_the_selectable_ones()
    {
        var diagram = C4LayoutEngine.Build(Workspace, View("deployment-local"));

        var frames = C4ZoomDeployment.SelectableFrames(diagram);

        Assert.Contains(Alias("pc.msix"), frames);
        Assert.Contains(Alias("pc.root"), frames);
    }

    /// <summary>A deployment node is drawn round the instances it runs. The layout
    /// once looked an instance up as the container it draws, whose parent is its
    /// system, and so drew the instance bare and the node not at all.</summary>
    [Fact]
    public void A_deployment_node_is_drawn_as_a_frame_round_the_instances_it_runs()
    {
        var diagram = C4LayoutEngine.Build(Workspace, View("deployment-local"));

        var frame = Assert.Single(diagram.Boundaries, boundary => boundary.Label == "MSIX package");
        var card = Assert.Single(diagram.Nodes, node => node.Node.Name == "Desktop App");

        Assert.InRange(card.X, frame.X, frame.X + frame.Width - card.Width);
        Assert.InRange(card.Y, frame.Y, frame.Y + frame.Height - card.Height);
    }

    [Fact]
    public void A_selectable_frame_is_written_with_the_card_id_contract()
    {
        var diagram = C4LayoutEngine.Build(Workspace, View("deployment-local"));
        var options = new C4SvgOptions { SelectableBoundaryAliases = new HashSet<string>(StringComparer.Ordinal) { Alias("pc.msix") } };

        var svg = C4SvgWriter.Write(diagram, "zoom", options: options);

        Assert.Contains($"<g class=\"c4-diagram__boundary c4-diagram__boundary--selectable node\" id=\"zoom-{Alias("pc.msix")}\">", svg, StringComparison.Ordinal);
        Assert.DoesNotContain($"id=\"zoom-{Alias("pc.root")}\"", svg, StringComparison.Ordinal);
    }

    /// <summary>The C4 tab's explorer passes no options, and its frames must stay
    /// frames: a frame it cannot select drawn as a card would take its clicks.</summary>
    [Fact]
    public void Without_the_option_every_frame_is_drawn_as_before()
    {
        var diagram = C4LayoutEngine.Build(Workspace, View("deployment-local"));

        Assert.Equal(C4SvgWriter.Write(diagram, "zoom"), C4SvgWriter.Write(diagram, "zoom", options: C4SvgOptions.None));
        Assert.DoesNotContain("c4-diagram__boundary--selectable", C4SvgWriter.Write(diagram, "zoom"), StringComparison.Ordinal);
    }

    // ---- the tab -------------------------------------------------------------------

    private IRenderedComponent<C4ZoomExplorer> RenderOnDeployment()
    {
        var explorer = _context.Render<C4ZoomExplorer>(parameters => parameters
            .Add(c => c.Workspace, Workspace)
            .Add(c => c.HintDismissed, true));

        // Also offers one deployment view, the first by key; the Local machine tab is
        // then one press away whichever it was.
        explorer.FindAll("[data-testid='c4-zoom-also-link']").Single(link => link.TextContent.Trim() == "Deployment").Click();
        if (explorer.Instance.ViewKey != "deployment-local")
        {
            explorer.FindAll("[data-testid='c4-zoom-environment-tab']").Single(tab => tab.TextContent.Trim() == "Local machine").Click();
        }

        Assert.Equal("deployment-local", explorer.Instance.ViewKey);
        return explorer;
    }

    [Fact]
    public void A_deployment_view_puts_its_environments_behind_one_tab_list()
    {
        var explorer = RenderOnDeployment();

        var list = explorer.Find("[data-testid='c4-zoom-environments']");
        Assert.Equal("tablist", list.GetAttribute("role"));
        Assert.Equal("Deployment environment", list.GetAttribute("aria-label"));

        var tabs = explorer.FindAll("[data-testid='c4-zoom-environment-tab']");
        Assert.Equal(["Local machine", "Azure"], tabs.Select(tab => tab.TextContent.Trim()));
        Assert.Equal("true", tabs.Single(tab => tab.TextContent.Trim() == "Local machine").GetAttribute("aria-selected"));

        // The picture is the open tab's panel; the other panel is hidden and empty, and
        // neither panel is a Tab stop of its own.
        Assert.Single(explorer.FindAll("[data-testid='c4-zoom-frame']"));
        Assert.NotNull(explorer.Find("[role='tabpanel']:not([hidden]) [data-testid='c4-zoom-frame']"));
        var hidden = Assert.Single(explorer.FindAll("[role='tabpanel'][hidden]"));
        Assert.Empty(hidden.Children);
        Assert.All(explorer.FindAll("[role='tabpanel']"), panel => Assert.Equal("-1", panel.GetAttribute("tabindex")));
    }

    [Fact]
    public void Choosing_a_tab_switches_to_that_environment()
    {
        var explorer = RenderOnDeployment();

        explorer.FindAll("[data-testid='c4-zoom-environment-tab']").Single(tab => tab.TextContent.Trim() == "Azure").Click();

        Assert.Equal("deployment-azure", explorer.Instance.ViewKey);
        Assert.Equal("true", explorer.FindAll("[data-testid='c4-zoom-environment-tab']").Single(tab => tab.TextContent.Trim() == "Azure").GetAttribute("aria-selected"));
    }

    [Fact]
    public void Each_other_environment_is_a_dashed_door_that_switches_to_it()
    {
        var explorer = RenderOnDeployment();

        var door = explorer.Find("[data-testid='c4-deployment-door']");
        Assert.Equal("Azure", door.GetAttribute("data-environment"));
        Assert.Contains("Cloud Service, Cloud Database", door.TextContent, StringComparison.Ordinal);
        Assert.Contains("Open the Azure view", door.TextContent, StringComparison.Ordinal);

        door.Click();

        Assert.Equal("deployment-azure", explorer.Instance.ViewKey);
        Assert.Equal("Local machine", explorer.Find("[data-testid='c4-deployment-door']").GetAttribute("data-environment"));
    }

    [Fact]
    public async Task Selecting_a_deployment_node_opens_its_inspector()
    {
        var explorer = RenderOnDeployment();
        Assert.Empty(explorer.FindAll("[data-testid='c4-deployment-inspector']"));

        await explorer.InvokeAsync(() => explorer.Instance.NodeSelected(Alias("pc.msix"), "deployment-local"));

        var inspector = explorer.Find("[data-testid='c4-deployment-inspector']");
        Assert.Equal("Deployment Node · MSIX", inspector.QuerySelector("[data-testid='c4-deployment-inspector-kind']")!.TextContent);
        Assert.Equal("MSIX package", inspector.QuerySelector("[data-testid='c4-deployment-inspector-name']")!.TextContent);
        Assert.Equal("Signed, sideloaded from GitHub Releases.", inspector.QuerySelector("[data-testid='c4-deployment-inspector-description']")!.TextContent);

        var instance = Assert.Single(inspector.QuerySelectorAll("[data-testid='c4-deployment-instance']"));
        Assert.Contains("Desktop App", instance.TextContent, StringComparison.Ordinal);
        Assert.Contains(".NET MAUI Blazor Hybrid", instance.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_instance_links_to_the_container_view_with_its_container_selected()
    {
        var explorer = RenderOnDeployment();
        await explorer.InvokeAsync(() => explorer.Instance.NodeSelected(Alias("pc.msix"), "deployment-local"));

        explorer.Find("[data-testid='c4-deployment-instance-open']").Click();

        Assert.Equal("containers-backlog", explorer.Instance.ViewKey);
        Assert.Equal("backlog.desktop", explorer.Instance.SelectedElementId);
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-environments']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-deployment']"));
    }

    [Fact]
    public async Task Selecting_a_container_instance_opens_no_deployment_inspector()
    {
        var explorer = RenderOnDeployment();

        var instance = Workspace.Elements.Single(element => element.Kind == C4ElementKind.ContainerInstance && element.InstanceOfId == "backlog.desktop");

        await explorer.InvokeAsync(() => explorer.Instance.NodeSelected(Alias(instance.Id), "deployment-local"));

        Assert.Equal("backlog.desktop", explorer.Instance.SelectedElementId);
        Assert.Empty(explorer.FindAll("[data-testid='c4-deployment-inspector']"));
    }

    [Fact]
    public async Task Switching_environment_drops_the_selected_node()
    {
        var explorer = RenderOnDeployment();
        await explorer.InvokeAsync(() => explorer.Instance.NodeSelected(Alias("pc.msix"), "deployment-local"));

        explorer.Find("[data-testid='c4-deployment-door']").Click();

        Assert.Null(explorer.Instance.SelectedElementId);
        Assert.Empty(explorer.FindAll("[data-testid='c4-deployment-inspector']"));
    }

    [Fact]
    public void The_deployment_view_draws_its_frames_selectable()
    {
        var explorer = RenderOnDeployment();

        var svg = explorer.Find("[data-testid='c4-zoom-diagram'] svg");
        var frame = explorer.Find("g.c4-diagram__boundary--selectable");

        // The card id contract the viewer reads the alias back out of.
        Assert.Equal($"{svg.Id}-{Alias("pc.msix")}", frame.Id);
        Assert.Contains("node", frame.ClassList);
    }

    [Fact]
    public void A_scope_deployed_once_shows_no_tab_list_and_no_door()
    {
        var workspace = Workspace with { Views = [.. Workspace.Views.Where(view => view.Key != "deployment-azure")] };
        var explorer = _context.Render<C4ZoomExplorer>(parameters => parameters
            .Add(c => c.Workspace, workspace)
            .Add(c => c.HintDismissed, true));

        explorer.FindAll("[data-testid='c4-zoom-also-link']").Single(link => link.TextContent.Trim() == "Deployment").Click();

        Assert.Equal("deployment-local", explorer.Instance.ViewKey);
        Assert.Empty(explorer.FindAll("[data-testid='c4-zoom-environments']"));
        Assert.Empty(explorer.FindAll("[data-testid='c4-deployment-door']"));
        Assert.Single(explorer.FindAll("[data-testid='c4-zoom-frame']"));
    }
}
