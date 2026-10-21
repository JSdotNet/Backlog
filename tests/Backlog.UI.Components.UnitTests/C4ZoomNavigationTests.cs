namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The C4 zoom tab's navigation, against the logic it is built on: where the tab opens,
/// what each zoom bar step names and where it goes, and where zooming in and out lands.
/// <para>
/// The cases are the requirement's own (<c>.devbook/domain/devbook/requirements.md</c>,
/// "The zoom bar names each level's element" and "Zooming moves one level and keeps
/// the selection"), on a workspace shaped like the one they are written about.
/// </para>
/// </summary>
public sealed class C4ZoomNavigationTests
{
    internal const string Source = """
        workspace "Backlog" "For the zoom tests" {
            !identifiers hierarchical
            model {
                me = person "ME" "The owner"
                backlog = softwareSystem "Prompt Backlog" "Local-first work management" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI Blazor Hybrid" {
                        shell = component "Shell" "Holds the panes" "Blazor"
                        devbook = component "Devbook" "Draws the knowledge folders" "Razor"
                    }
                    cloud = container "Cloud Database" "Sync state" "Cosmos DB" "Database"
                }
                github = softwareSystem "GitHub" "Issues and webhooks" "External"
                me -> backlog.desktop "Captures work"
                backlog.desktop.shell -> backlog.desktop.devbook "Offers the pane"
                backlog.desktop -> backlog.cloud "Syncs"
                backlog.desktop -> github "Syncs issues" "HTTPS"

                deploymentEnvironment "Local machine" {
                    pc = deploymentNode "Windows PC" "" "Windows 11" {
                        containerInstance backlog.desktop
                    }
                }
            }
            views {
                systemLandscape "landscape" "Everything" { include * }
                systemContext backlog "context-backlog" "System context" { include * }
                container backlog "containers-backlog" "Containers" { include * }
                component backlog.desktop "components-desktop" "Desktop App components" { include * }
                dynamic backlog "capture-to-issue" "Capture to issue" {
                    me -> backlog.desktop "Captures an entry"
                    backlog.desktop -> github "Creates the issue"
                }
                deployment backlog "Local machine" "deployment-local" "Local machine" { include * }
            }
        }
        """;

    internal static readonly C4Workspace Workspace = C4DslReader.Read(Source);

    private static C4View View(string key) => Workspace.View(key) ?? throw new InvalidOperationException(key);

    private static C4ZoomStep Step(C4ZoomBarState bar, C4ZoomLevel level) => bar.Steps.Single(step => step.Level == level);

    [Fact]
    public void The_test_workspace_parses_clean() => Assert.Empty(Workspace.Problems);

    [Fact]
    public void The_tab_opens_on_the_system_context_view()
    {
        Assert.Equal("context-backlog", C4ZoomNavigation.StartView(Workspace)?.Key);
    }

    [Fact]
    public void A_workspace_without_a_context_view_opens_on_its_first_view()
    {
        var workspace = Workspace with { Views = [.. Workspace.Views.Where(view => view.Kind != C4ViewKind.SystemContext)] };

        Assert.Equal("landscape", C4ZoomNavigation.StartView(workspace)?.Key);
    }

    // ---- the zoom bar ------------------------------------------------------------

    [Fact]
    public void On_the_context_view_the_system_fills_steps_one_and_two_and_the_rest_wait()
    {
        var bar = C4ZoomNavigation.Bar(Workspace, View("context-backlog"), null);

        Assert.Equal([C4ZoomLevel.Context, C4ZoomLevel.Containers, C4ZoomLevel.Components, C4ZoomLevel.Code], bar.Steps.Select(step => step.Level));

        var context = Step(bar, C4ZoomLevel.Context);
        Assert.True(context.Current);
        Assert.False(context.Enabled);
        Assert.Equal("Prompt Backlog", context.Scope);

        var containers = Step(bar, C4ZoomLevel.Containers);
        Assert.Equal("Prompt Backlog", containers.Scope);
        Assert.True(containers.Enabled);
        Assert.Equal("containers-backlog", containers.ViewKey);

        var components = Step(bar, C4ZoomLevel.Components);
        Assert.True(components.Waiting);
        Assert.False(components.Enabled);
        Assert.Equal(C4ZoomNavigation.PickContainer, components.Placeholder);

        var code = Step(bar, C4ZoomLevel.Code);
        Assert.True(code.Waiting);
        Assert.False(code.Enabled);
        Assert.Equal(C4ZoomNavigation.PickComponent, code.Placeholder);
    }

    [Fact]
    public void Selecting_a_container_fills_step_three_and_step_four_still_waits()
    {
        var bar = C4ZoomNavigation.Bar(Workspace, View("containers-backlog"), "backlog.desktop");

        Assert.True(Step(bar, C4ZoomLevel.Containers).Current);

        var components = Step(bar, C4ZoomLevel.Components);
        Assert.Equal("Desktop App", components.Scope);
        Assert.True(components.Enabled);
        Assert.Equal("components-desktop", components.ViewKey);

        var code = Step(bar, C4ZoomLevel.Code);
        Assert.Equal(C4ZoomNavigation.PickComponent, code.Placeholder);
        Assert.False(code.Enabled);
    }

    /// <summary>A container with no component view names itself on step 3 and still
    /// cannot be opened: an offer is a promise.</summary>
    [Fact]
    public void A_container_with_no_component_view_names_step_three_but_leaves_it_shut()
    {
        var components = Step(C4ZoomNavigation.Bar(Workspace, View("containers-backlog"), "backlog.cloud"), C4ZoomLevel.Components);

        Assert.Equal("Cloud Database", components.Scope);
        Assert.False(components.Waiting);
        Assert.False(components.Enabled);
    }

    [Fact]
    public void Selecting_a_component_with_no_code_folder_names_step_four_which_stays_grey()
    {
        var bar = C4ZoomNavigation.Bar(Workspace, View("components-desktop"), "backlog.desktop.devbook");

        Assert.True(Step(bar, C4ZoomLevel.Components).Current);
        Assert.Equal("Desktop App", Step(bar, C4ZoomLevel.Components).Scope);

        var code = Step(bar, C4ZoomLevel.Code);
        Assert.Equal("Devbook", code.Scope);
        Assert.False(code.Enabled);

        // A host that has a code source, but none for this component, says the same.
        Assert.False(Step(C4ZoomNavigation.Bar(Workspace, View("components-desktop"), "backlog.desktop.devbook", _ => false), C4ZoomLevel.Code).Enabled);
    }

    /// <summary>Requirement "Code is drawn from the source on demand": a component whose
    /// building block names code folders opens level 4.</summary>
    [Fact]
    public void Selecting_a_component_with_a_code_folder_lets_step_four_open_its_code()
    {
        var bar = C4ZoomNavigation.Bar(Workspace, View("components-desktop"), "backlog.desktop.devbook", HasDevbookCode);

        var code = Step(bar, C4ZoomLevel.Code);
        Assert.True(code.Enabled);
        Assert.Equal("Devbook", code.Scope);
        Assert.Equal(C4ZoomNavigation.CodeKey("backlog.desktop.devbook"), code.ViewKey);
        Assert.Equal("backlog.desktop.devbook", code.SelectId);
        Assert.True(Step(bar, C4ZoomLevel.Components).Current);
    }

    /// <summary>On the Code level step 4 is ringed, and every step behind it jumps back —
    /// step 3 to the components with the component still selected.</summary>
    [Fact]
    public void On_the_code_level_step_four_is_ringed_and_the_steps_behind_it_jump_back()
    {
        var bar = C4ZoomNavigation.Bar(Workspace, View("components-desktop"), "backlog.desktop.devbook", HasDevbookCode, codeOpen: true);

        Assert.True(Step(bar, C4ZoomLevel.Code).Current);
        Assert.False(Step(bar, C4ZoomLevel.Code).Enabled);

        var components = Step(bar, C4ZoomLevel.Components);
        Assert.False(components.Current);
        Assert.True(components.Enabled);
        Assert.Equal("components-desktop", components.ViewKey);
        Assert.Equal("backlog.desktop.devbook", components.SelectId);

        var containers = Step(bar, C4ZoomLevel.Containers);
        Assert.True(containers.Enabled);
        Assert.Equal("backlog.desktop", containers.SelectId);
    }

    /// <summary>The code being open says nothing about a component with none: the bar
    /// stays on the components level.</summary>
    [Fact]
    public void Code_open_for_a_component_without_code_leaves_the_components_step_current()
    {
        var bar = C4ZoomNavigation.Bar(Workspace, View("components-desktop"), "backlog.desktop.shell", HasDevbookCode, codeOpen: true);

        Assert.True(Step(bar, C4ZoomLevel.Components).Current);
        Assert.False(Step(bar, C4ZoomLevel.Code).Enabled);
    }

    private static bool HasDevbookCode(string elementId) =>
        string.Equals(elementId, "backlog.desktop.devbook", StringComparison.OrdinalIgnoreCase);

    /// <summary>Steps behind the reader always work, and each leaves the element the
    /// reader is coming out of selected.</summary>
    [Fact]
    public void Earlier_steps_jump_back_with_the_element_zoomed_out_of_selected()
    {
        var bar = C4ZoomNavigation.Bar(Workspace, View("components-desktop"), null);

        var containers = Step(bar, C4ZoomLevel.Containers);
        Assert.True(containers.Enabled);
        Assert.Equal("containers-backlog", containers.ViewKey);
        Assert.Equal("backlog.desktop", containers.SelectId);

        var context = Step(bar, C4ZoomLevel.Context);
        Assert.True(context.Enabled);
        Assert.Equal("context-backlog", context.ViewKey);
        Assert.Equal("backlog", context.SelectId);
    }

    [Fact]
    public void Landscape_dynamic_and_deployment_are_offered_beside_the_bar_and_are_never_steps()
    {
        var bar = C4ZoomNavigation.Bar(Workspace, View("containers-backlog"), null);

        Assert.Equal(["Landscape", "Dynamic", "Deployment"], bar.Also.Select(also => also.Label));
        Assert.Equal(["landscape", "capture-to-issue", "deployment-local"], bar.Also.Select(also => also.ViewKey));
        Assert.All(bar.Also, also => Assert.False(also.Current));
        Assert.Equal(4, bar.Steps.Count);
    }

    [Fact]
    public void On_a_supplementary_view_no_step_is_current_and_its_link_is()
    {
        var bar = C4ZoomNavigation.Bar(Workspace, View("capture-to-issue"), null);

        Assert.DoesNotContain(bar.Steps, step => step.Current);
        Assert.True(bar.Also.Single(also => also.Kind == C4ViewKind.Dynamic).Current);
        Assert.Equal("Prompt Backlog", Step(bar, C4ZoomLevel.Context).Scope);
    }

    [Fact]
    public void On_the_landscape_the_steps_wait_for_a_system()
    {
        var bar = C4ZoomNavigation.Bar(Workspace, View("landscape"), null);

        Assert.All(bar.Steps.Take(2), step => Assert.Equal(C4ZoomNavigation.PickSystem, step.Placeholder));

        var selected = C4ZoomNavigation.Bar(Workspace, View("landscape"), "backlog");
        Assert.Equal("Prompt Backlog", Step(selected, C4ZoomLevel.Context).Scope);
        Assert.True(Step(selected, C4ZoomLevel.Context).Enabled);
    }

    // ---- zooming -----------------------------------------------------------------

    [Fact]
    public void Zooming_into_the_system_on_its_context_opens_its_containers()
    {
        Assert.Equal("containers-backlog", C4ZoomNavigation.ZoomInView(Workspace, View("context-backlog"), "backlog"));
    }

    [Fact]
    public void Zooming_into_a_container_opens_its_components()
    {
        Assert.Equal("components-desktop", C4ZoomNavigation.ZoomInView(Workspace, View("containers-backlog"), "backlog.desktop"));
    }

    [Fact]
    public void Zooming_into_a_system_on_the_landscape_opens_its_context_which_is_level_one()
    {
        Assert.Equal("context-backlog", C4ZoomNavigation.ZoomInView(Workspace, View("landscape"), "backlog"));
    }

    [Fact]
    public void An_element_with_no_deeper_view_zooms_nowhere()
    {
        Assert.Null(C4ZoomNavigation.ZoomInView(Workspace, View("containers-backlog"), "backlog.cloud"));
        Assert.Null(C4ZoomNavigation.ZoomInView(Workspace, View("components-desktop"), "backlog.desktop.devbook"));
        Assert.Null(C4ZoomNavigation.ZoomInView(Workspace, View("context-backlog"), "github"));
    }

    [Fact]
    public void Zooming_out_of_components_opens_the_containers_with_that_container_selected()
    {
        Assert.Equal(new C4ZoomTarget("containers-backlog", "backlog.desktop"), C4ZoomNavigation.ZoomOut(Workspace, View("components-desktop")));
    }

    [Fact]
    public void Zooming_out_of_containers_opens_the_context_with_the_system_selected()
    {
        Assert.Equal(new C4ZoomTarget("context-backlog", "backlog"), C4ZoomNavigation.ZoomOut(Workspace, View("containers-backlog")));
    }

    [Fact]
    public void Zooming_out_of_the_context_opens_the_landscape_and_the_landscape_is_the_top()
    {
        Assert.Equal(new C4ZoomTarget("landscape", "backlog"), C4ZoomNavigation.ZoomOut(Workspace, View("context-backlog")));
        Assert.Null(C4ZoomNavigation.ZoomOut(Workspace, View("landscape")));
    }

    [Fact]
    public void Zooming_out_of_a_view_beside_the_containers_opens_the_containers()
    {
        Assert.Equal("containers-backlog", C4ZoomNavigation.ZoomOut(Workspace, View("capture-to-issue"))?.ViewKey);
        Assert.Equal("containers-backlog", C4ZoomNavigation.ZoomOut(Workspace, View("deployment-local"))?.ViewKey);
    }
}
