using AngleSharp.Dom;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Extensions;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Devbook;
using Backlog.Desktop.UI.Devbook.Extensions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Backlog.Modules.DevPc.UI;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;
using Backlog.Desktop.UI.PullRequests;
using Backlog.Desktop.UI.Tasks;
using Backlog.SharedKernel.Ai;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The shell shows one surface at a time. The workspace is one of them: the main
/// view the view switch picks — Tasks or Roadmap — with the Inbox and the Devbook
/// beside it. Tools, the Dashboard, Sessions and Pull requests are takeovers: opening
/// one takes the screen and hides every other domain concern, and closing it puts
/// the reader back exactly where they were.
/// <para>
/// That last part is worth a test even though no code implements it. The surface
/// is a field of its own and neither the view nor the side panes are touched to open
/// one, so they are simply revealed again rather than saved and restored. A future
/// change that folds the surfaces into <c>GlobalPaneSelection</c> would pass every
/// markup assertion and quietly break this.
/// </para>
/// </summary>
public sealed class HomeWorkspaceSurfaceTests
{
    [Fact]
    public void Opening_tools_replaces_the_roadmap_and_every_pane()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        // Start on the roadmap, so the test also proves one takeover replaces
        // another rather than stacking on it.
        OpenTheRoadmap(component);

        component.Find("[data-testid='tools-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='tools-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='tools-panel']"));
            Assert.Empty(component.FindAll("[data-testid='devbook-layout']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        });
    }

    [Fact]
    public void Opening_the_dashboard_replaces_the_roadmap_and_every_pane()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        // Start on the roadmap, so the test also proves one takeover replaces
        // another rather than stacking on it.
        OpenTheRoadmap(component);

        component.Find("[data-testid='dashboard-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-panel']"));
            Assert.Empty(component.FindAll("[data-testid='devbook-layout']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        });
    }

    /// <summary>
    /// The session list is a takeover of its own again, with its own segment in the
    /// surface switcher — not a tab behind the Dashboard.
    /// </summary>
    [Fact]
    public void Opening_sessions_replaces_the_roadmap_and_every_pane()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        // Start on the roadmap, so the test also proves one takeover replaces
        // another rather than stacking on it.
        OpenTheRoadmap(component);

        OpenSessions(component);

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='sessions-surface'] [data-testid='sessions-panel']"));
            Assert.Empty(component.FindAll("[data-testid='dashboard-surface']"));
            Assert.Empty(component.FindAll("[data-testid='devbook-layout']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Single(component.FindAll("main"));

            Assert.Equal("true", component.Find("[data-testid='sessions-toggle-button']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='dashboard-toggle-button']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// <c>open_dashboard</c> arrives from an agent run, not from the reader, so it
    /// answers whether the Sessions pane is there to open and leaves the window where
    /// the reader put it. Navigating is always the reader's act: a run that starts
    /// mid-edit must not pull them off the entry they are typing in.
    /// </summary>
    [Fact]
    public async Task An_agent_asking_for_the_sessions_pane_does_not_navigate()
    {
        var activator = new SessionsSurfaceActivator();
        using var harness = CreateHarness(configureServices: services => services.AddSingleton(activator));
        var component = Render(harness);

        OpenTheRoadmap(component);

        var answer = await activator.ActivateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DeliverySurfaceActivation.Available, answer);
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Empty(component.FindAll("[data-testid='sessions-surface']"));
            Assert.Empty(component.FindAll("[data-testid='sessions-panel']"));
        });
    }

    /// <summary>The same from the workspace, where the reader spends most of their
    /// time: the task list stays on screen.</summary>
    [Fact]
    public async Task An_agent_asking_for_the_sessions_pane_leaves_the_workspace_on_screen()
    {
        var activator = new SessionsSurfaceActivator();
        using var harness = CreateHarness(configureServices: services => services.AddSingleton(activator));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='workspace']")));

        var answer = await activator.ActivateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DeliverySurfaceActivation.Available, answer);
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
            Assert.Empty(component.FindAll("[data-testid='sessions-surface']"));
        });
    }

    /// <summary>
    /// A session opened from a task is the reader's own click, so unlike
    /// <c>open_dashboard</c> it does navigate: the Sessions surface comes up on that
    /// session.
    /// </summary>
    [Fact]
    public async Task Opening_a_session_from_a_task_shows_the_sessions_surface_on_that_session()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindComponents<TasksPane>()));
        var tasks = component.FindComponent<TasksPane>();

        await component.InvokeAsync(() => tasks.Instance.OnOpenSession.InvokeAsync("session-1"));

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='sessions-surface']"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
            Assert.Equal("session-1", component.FindComponent<SessionsPane>().Instance.FocusSessionId);
        });
    }

    /// <summary>With the session list switched off there is nowhere to open it: the
    /// reader is told so and stays on the task.</summary>
    [Fact]
    public async Task Opening_a_session_from_a_task_with_sessions_switched_off_says_so_and_stays()
    {
        using var harness = CreateHarness(features => features.SetEnabled(SessionFeatures.Sessions, false));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindComponents<TasksPane>()));
        var tasks = component.FindComponent<TasksPane>();

        await component.InvokeAsync(() => tasks.Instance.OnOpenSession.InvokeAsync("session-1"));

        var toasts = harness.Context.Services.GetRequiredService<ToastChannel>();
        component.WaitForAssertion(() =>
        {
            var toast = Assert.Single(toasts.Visible);
            Assert.Equal("tasks-session-sessions-off", toast.TestId);
            Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
            Assert.Empty(component.FindAll("[data-testid='sessions-surface']"));
        });
    }

    /// <summary>
    /// The Dashboard is its overview alone: no tab strip, and no session list
    /// beside or behind it.
    /// </summary>
    [Fact]
    public void The_dashboard_shows_its_overview_without_a_tab_strip()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.Find("[data-testid='dashboard-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-panel']"));
            Assert.Empty(component.FindAll("[data-testid='dashboard-tabs']"));
            Assert.Empty(component.FindAll("[data-testid='dashboard-sessions-tab']"));
            Assert.Empty(component.FindAll("[data-testid='sessions-panel']"));
        });
    }

    /// <summary>
    /// During a takeover the view switch and the side-pane toggles stay in the
    /// header, because they are the way back: no option reads pressed, since nothing
    /// of the workspace is on screen. The view underneath is untouched: the switch
    /// comes back with the same view pressed.
    /// </summary>
    [Fact]
    public void A_takeover_keeps_the_view_switch_unpressed_and_a_view_option_returns_to_the_workspace()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='view-switch']")));

        foreach (var surface in new[] { "tools", "dashboard", "sessions", "pull-requests" })
        {
            component.Find($"[data-testid='{surface}-toggle-button']").Click();

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll($"[data-testid='{surface}-surface']"));
                Assert.NotEmpty(component.FindAll("[data-testid='view-switch']"));

                var tasks = component.Find("[data-testid='tasks-view-option']");
                Assert.Equal("false", tasks.GetAttribute("aria-pressed"));
                Assert.False(tasks.HasAttribute("disabled"));
                Assert.Equal("false", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));

                var devbook = component.Find("[data-testid='devbook-pane-option']");
                Assert.Equal("false", devbook.GetAttribute("aria-pressed"));
                Assert.Equal("Back to the workspace with Devbook open", devbook.GetAttribute("title"));
            });

            component.Find("[data-testid='tasks-view-option']").Click();

            component.WaitForAssertion(() =>
            {
                Assert.Empty(component.FindAll($"[data-testid='{surface}-surface']"));
                Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
                Assert.Equal("true", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
                Assert.Equal("false", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
            });
        }
    }

    /// <summary>
    /// The way back keeps the workspace as it was: pressing a side pane's toggle
    /// whose pane is already open closes the takeover and shows the workspace with
    /// that pane still open — it does not toggle the pane off.
    /// </summary>
    [Fact]
    public void Pressing_an_open_pane_during_a_takeover_returns_with_the_selection_unchanged()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        component.Find("[data-testid='devbook-pane-option']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
        });

        component.Find("[data-testid='dashboard-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='dashboard-surface']")));

        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='dashboard-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.Equal("true", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
            Assert.Equal("true", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='dashboard-toggle-button']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// A side pane that was not open opens beside the view the workspace was showing,
    /// closing the takeover on the way: the view is never replaced by a pane.
    /// </summary>
    [Fact]
    public void Pressing_a_closed_pane_during_a_takeover_opens_it_beside_the_view()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']")));
        component.Find("[data-testid='tools-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='tools-surface']")));

        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='tools-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.Equal("true", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
            Assert.Equal("true", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// The header's nav, left to right, as the design draws it: the Inbox toggle, the
    /// fused view switch, the Devbook toggle; then the fused work in progress —
    /// Sessions and Pull requests — and the loose Dashboard and Tools. No Tasks pane
    /// toggle, and no "Workspace" option anywhere.
    /// </summary>
    [Fact]
    public void The_nav_reads_inbox_views_devbook_then_the_work_in_progress_and_the_other_takeovers()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.InboxPane, true));
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            var nav = component.Find(".app-header__nav");
            var groups = nav.Children.Select(child => child.GetAttribute("data-testid")).ToList();
            Assert.Equal(["workspace-navigation", "work-in-progress-switcher", "workspace-surface-switcher"], groups);

            var workspace = component.Find("[data-testid='workspace-navigation']").Children
                .Select(child => child.GetAttribute("data-testid")).ToList();
            Assert.Equal(["inbox-pane-option", "view-switch", "devbook-pane-option"], workspace);

            Assert.Equal(
                ["tasks-view-option", "board-view-option", "calendar-view-option", "roadmap-view-option"],
                OptionIds(component, "view-switch"));
            Assert.Equal(
                ["sessions-toggle-button", "pull-requests-toggle-button"],
                OptionIds(component, "work-in-progress-switcher"));
            Assert.Equal(
                ["dashboard-toggle-button", "tools-toggle-button"],
                OptionIds(component, "workspace-surface-switcher"));

            // Fused where the design fuses, loose where it does not.
            Assert.DoesNotContain("header-group--loose", component.Find("[data-testid='view-switch']").ClassName);
            Assert.DoesNotContain("header-group--loose", component.Find("[data-testid='work-in-progress-switcher']").ClassName);
            Assert.Contains("header-group--loose", component.Find("[data-testid='workspace-surface-switcher']").ClassName);
            Assert.Contains("header-group__option--loose", component.Find("[data-testid='inbox-pane-option']").ClassName);
            Assert.Contains("header-group__option--loose", component.Find("[data-testid='devbook-pane-option']").ClassName);

            Assert.Empty(component.FindAll("[data-testid='backlog-pane-option']"));
            Assert.Empty(component.FindAll("[data-testid='global-pane-multiselect']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-toggle-button']"));
            Assert.Empty(component.FindAll("[data-testid='workspace-surface-option']"));
        });

        static List<string?> OptionIds(IRenderedComponent<Home> rendered, string group) =>
            [.. rendered.Find($"[data-testid='{group}']").Children.Select(option => option.GetAttribute("data-testid"))];
    }

    /// <summary>
    /// Ask AI answers from whichever area is open, so it stays offered across a
    /// takeover — the Dashboard is an area too. An open panel goes with its button and comes back
    /// with it: the flag behind it is not reset by a takeover. The sentence under
    /// the title names the one area on screen, because with one there are no chips.
    /// </summary>
    [Fact]
    public void Ask_ai_follows_the_open_areas_and_names_the_one_that_answers()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.AiAssistant, true));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='ai-assistant-panel']"));
            Assert.Equal("Answers from the Tasks content.", component.Find(".ai-panel__body").TextContent.Trim());
        });

        component.Find("[data-testid='dashboard-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='ai-assistant-panel']"));
            Assert.Equal("Answers from the Dashboard content.", component.Find(".ai-panel__body").TextContent.Trim());
        });

        // Sessions is its own surface and its own area.
        component.Find("[data-testid='sessions-toggle-button']").Click();
        component.WaitForAssertion(() =>
            Assert.Equal("Answers from the Sessions content.", component.Find(".ai-panel__body").TextContent.Trim()));

        // Pressing the Devbook's toggle leaves the takeover and opens it beside the
        // task list: two areas, and the Devbook, opened last, is the one asked.
        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']"));
            Assert.Equal("true", component.Find("[data-testid='ai-scope-devbook']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='ai-scope-tasks']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>The Inbox is an area like the others: opened beside the task list,
    /// it is the one asked by default, and the list is still a choice.</summary>
    [Fact]
    public void Ask_ai_offers_the_inbox_opened_beside_the_task_list()
    {
        using var harness = CreateHarness(features =>
        {
            features.SetEnabled(AppFeatures.AiAssistant, true);
            features.SetEnabled(AppFeatures.InboxPane, true);
        });
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-option']")));
        component.Find("[data-testid='inbox-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']"));
        });

        component.Find("[data-testid='ai-toggle-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Equal("true", component.Find("[data-testid='ai-scope-inbox']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='ai-scope-tasks']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// The scope chips exist only when there is a choice: none with one area, one
    /// per area with two or more, each carrying its area's test id. The area the
    /// reader opened last is pressed by default, and a press on another chip moves
    /// the choice.
    /// </summary>
    [Fact]
    public void Scope_chips_appear_with_two_areas_default_to_the_last_opened_and_follow_a_press()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.AiAssistant, true));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='ai-assistant-panel']"));
            Assert.Empty(component.FindAll("[data-testid='ai-scope']"));
        });

        OpenTheDevbookBeside(component);

        component.WaitForAssertion(() =>
        {
            var group = component.Find("[data-testid='ai-scope']");
            Assert.Equal("group", group.GetAttribute("role"));
            Assert.Equal("Ask about", group.GetAttribute("aria-label"));

            // Two chips, in the shell's fixed order — the workspace left to right — and
            // the Devbook, opened last, is the one pressed.
            Assert.Equal("false", component.Find("[data-testid='ai-scope-tasks']").GetAttribute("aria-pressed"));
            Assert.Equal("true", component.Find("[data-testid='ai-scope-devbook']").GetAttribute("aria-pressed"));
            Assert.Equal("Answers from the content of the area chosen above.", component.Find(".ai-panel__body").TextContent.Trim());
        });

        component.Find("[data-testid='ai-scope-tasks']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("true", component.Find("[data-testid='ai-scope-tasks']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='ai-scope-devbook']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>A question goes to the chosen area's source, and what the client
    /// receives as content is that source's body — here the plan's, headed the
    /// way the budget heads every body.</summary>
    [Fact]
    public void Asking_sends_the_chosen_sources_body_as_the_content()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.AiAssistant, true));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();
        OpenTheRoadmap(component);
        component.WaitForAssertion(() =>
            Assert.Equal("Answers from the Roadmap content.", component.Find(".ai-panel__body").TextContent.Trim()));

        component.Find("[data-testid='ai-question-input']").Input("What ships first?");
        component.Find("[data-testid='ai-ask-button']").Click();

        component.WaitForAssertion(() =>
        {
            var request = Assert.Single(harness.FoundryChat.Requests);
            Assert.Equal("What ships first?", request.Question);
            Assert.StartsWith("Roadmap: ", request.Content, StringComparison.Ordinal);
        });
    }

    /// <summary>A chip pressed for an area that then closes is a choice about
    /// nothing: the chips go with the second area, and the question goes to the
    /// one that is left.</summary>
    [Fact]
    public void A_pressed_chip_whose_area_closes_falls_back_to_the_area_still_open()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.AiAssistant, true));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();
        OpenTheDevbookBeside(component);
        component.Find("[data-testid='ai-scope-devbook']").Click();
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("[data-testid='ai-scope-devbook']").GetAttribute("aria-pressed")));

        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.Empty(component.FindAll("[data-testid='ai-scope']"));
            Assert.Equal("Answers from the Tasks content.", component.Find(".ai-panel__body").TextContent.Trim());
        });

        component.Find("[data-testid='ai-question-input']").Input("What now?");
        component.Find("[data-testid='ai-ask-button']").Click();

        component.WaitForAssertion(() =>
        {
            var request = Assert.Single(harness.FoundryChat.Requests);
            Assert.StartsWith("Tasks: ", request.Content, StringComparison.Ordinal);
        });
    }

    /// <summary>Two asks, two chips, two bodies: the chip pressed at the moment of
    /// asking is the source whose body goes.</summary>
    [Fact]
    public void Pressing_a_different_chip_changes_which_body_is_sent()
    {
        // A fixed Devbook body: the harness has no knowledge folder to read, and what
        // is pinned is which source the shell asks, not what the Devbook finds.
        using var harness = CreateHarness(
            features => features.SetEnabled(AppFeatures.AiAssistant, true),
            configureServices: services => services.AddScoped<IAiContentSource>(_ => new FixedSource("devbook", "Devbook")));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();
        OpenTheDevbookBeside(component);

        component.Find("[data-testid='ai-scope-tasks']").Click();
        component.Find("[data-testid='ai-question-input']").Input("First?");
        component.Find("[data-testid='ai-ask-button']").Click();
        component.WaitForAssertion(() => Assert.Single(harness.FoundryChat.Requests));

        component.Find("[data-testid='ai-scope-devbook']").Click();
        component.Find("[data-testid='ai-ask-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(2, harness.FoundryChat.Requests.Count);
            Assert.StartsWith("Tasks: ", harness.FoundryChat.Requests[0].Content, StringComparison.Ordinal);
            Assert.StartsWith("Devbook: ", harness.FoundryChat.Requests[1].Content, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A source reads a store, and a store can throw. That is a fact about the
    /// area rather than about the assistant, so it is said the way an assistant
    /// failure is — on the toast, in the area's name — and it never reaches the
    /// error boundary: the panel and the page are still there afterwards, and
    /// nothing was sent.
    /// </summary>
    [Fact]
    public void A_source_that_throws_is_reported_beside_the_question_and_the_page_survives()
    {
        using var harness = CreateHarness(
            features => features.SetEnabled(AppFeatures.AiAssistant, true),
            configureServices: services => services.AddScoped<IAiContentSource>(_ => new ThrowingSource("roadmap", "Roadmap")));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();
        OpenTheRoadmap(component);
        component.WaitForAssertion(() =>
            Assert.Equal("Answers from the Roadmap content.", component.Find(".ai-panel__body").TextContent.Trim()));

        component.Find("[data-testid='ai-question-input']").Input("Anything?");
        component.Find("[data-testid='ai-ask-button']").Click();

        var toasts = harness.Context.Services.GetRequiredService<ToastChannel>();
        component.WaitForAssertion(() =>
        {
            var toast = Assert.Single(toasts.Visible);
            Assert.Equal("Could not gather the Roadmap content: The plan file is locked.", toast.Message);
            Assert.Equal("ai-error", toast.TestId);
        });

        Assert.Empty(harness.FoundryChat.Requests);
        Assert.NotEmpty(component.FindAll("[data-testid='ai-assistant-panel']"));
        Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
        Assert.False(component.Find("[data-testid='ai-ask-button']").HasAttribute("disabled"));
    }

    /// <summary>
    /// The old panel sent the rows the status chips had left in view. The Tasks
    /// source sends the scope: an entry the chips hide is still in the body, and the
    /// body is the source's own rather than anything the shell composed.
    /// </summary>
    [Fact]
    public async Task The_tasks_body_sent_is_the_sources_body_and_not_the_filtered_rows()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.AiAssistant, true));
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));

        await component.InvokeAsync(async () =>
        {
            state.NewRow();
            state.OnRawTextInput(state.Rows[^1], "# Ready entry\n`task` `!ready`\n");
            await state.EndEditAsync(state.Rows[^1]);
            state.NewRow();
            state.OnRawTextInput(state.Rows[^1], "# Draft entry\n`task` `!draft`\n");
            await state.EndEditAsync(state.Rows[^1]);
            state.SetStatusFilter("draft");
        });

        Assert.Single(state.FilteredRows);

        component.Find("[data-testid='ai-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-question-input']")));
        component.Find("[data-testid='ai-question-input']").Input("What is ready?");
        component.Find("[data-testid='ai-ask-button']").Click();

        component.WaitForAssertion(() =>
        {
            var request = Assert.Single(harness.FoundryChat.Requests);
            Assert.StartsWith("Tasks: 2 entries.", request.Content, StringComparison.Ordinal);
            Assert.Contains("# Ready entry", request.Content, StringComparison.Ordinal);
            Assert.Contains("# Draft entry", request.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("Visible tasks", request.Content, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// One field with five states, so opening a takeover is the same act as closing
    /// whichever one was already there. There is no arrangement in which two are on
    /// screen, and the loop ends where it started to prove that reopening the first
    /// one is the same act rather than a special case.
    /// </summary>
    [Fact]
    public void No_two_surfaces_can_be_open_at_the_same_time()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        string[] surfaces = ["tools", "dashboard", "sessions", "pull-requests", "tools"];

        foreach (var surface in surfaces)
        {
            component.Find($"[data-testid='{surface}-toggle-button']").Click();

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll($"[data-testid='{surface}-surface']"));

                // Exactly one <main> on the page, which is what makes the exclusivity
                // structural rather than a rule this test happens to check pairwise.
                Assert.Single(component.FindAll("main"));
            });
        }
    }

    /// <summary>
    /// Opening a surface has to be remembered, not just shown, or the shell has
    /// nothing to reopen on the next launch.
    /// </summary>
    [Fact]
    public void Opening_a_surface_remembers_it_for_next_launch()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.Find("[data-testid='dashboard-toggle-button']").Click();
            component.WaitForAssertion(() => Assert.Equal("Dashboard", shellNavigation.LastSurface));

            // The name the sessions list has always been remembered by, so a file
            // written while it was a Dashboard tab reopens on it too.
            component.Find("[data-testid='sessions-toggle-button']").Click();
            component.WaitForAssertion(() => Assert.Equal("Sessions", shellNavigation.LastSurface));
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// Closing a surface is itself a surface choice — back to the workspace — so
    /// it has to overwrite whatever takeover was remembered, or the shell would
    /// reopen on a surface the reader deliberately left.
    /// </summary>
    [Fact]
    public void Closing_a_surface_remembers_the_workspace_instead()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.Find("[data-testid='tools-toggle-button']").Click();
            component.WaitForAssertion(() => Assert.Equal("Tools", shellNavigation.LastSurface));

            component.Find("[data-testid='tools-toggle-button']").Click();

            component.WaitForAssertion(() => Assert.Equal("Workspace", shellNavigation.LastSurface));
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// The bug this pins: the shell used to always open on the workspace panes,
    /// even when the reader had a takeover open when they last closed the app.
    /// </summary>
    [Fact]
    public void The_shell_reopens_on_the_surface_that_was_last_open()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            shellNavigation.SetLastSurface("Sessions");

            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='sessions-surface'] [data-testid='sessions-panel']"));
                Assert.Empty(component.FindAll("[data-testid='dashboard-surface']"));
                Assert.Empty(component.FindAll("[data-testid='workspace']"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// A name the current build does not recognise — an older or newer version's
    /// surface, or a hand-edited file — must never stop the app from opening.
    /// </summary>
    [Fact]
    public void An_unrecognised_remembered_surface_falls_back_to_the_workspace()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            shellNavigation.SetLastSurface("SomeFutureSurface");

            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='workspace']")));
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// Opening a side pane has to be remembered, not just shown, or a fresh shell
    /// instance — a relaunch, or the Router rebuilding this component after a trip
    /// to Settings — has nothing to reopen on.
    /// </summary>
    [Fact]
    public void Opening_a_side_pane_remembers_it_for_next_time()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.Find("[data-testid='devbook-pane-option']").Click();

            component.WaitForAssertion(() => Assert.Equal(["Devbook"], shellNavigation.LastEnabledPanes));
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// The bug this pins: navigating to Settings and back tears down and rebuilds
    /// this component — the Router disposes it on every route change — so a fresh
    /// <c>GlobalPaneSelection</c> field initializer used to reopen with no side pane
    /// no matter what the reader had open. A brand-new shell instance backed by the
    /// same store is the same situation without needing an actual Settings round trip
    /// in the test.
    /// </summary>
    [Fact]
    public void A_fresh_shell_instance_reopens_with_the_side_pane_that_was_last_open()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            shellNavigation.SetLastPanes(["Devbook"]);

            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
                Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// A file from before the view switch lists the task list among its panes —
    /// "Tasks", or "Backlog" before that. It names no pane now and is ignored: the
    /// Tasks view is showing, and the Inbox and the Devbook keep their state.
    /// </summary>
    [Theory]
    [InlineData("Backlog")]
    [InlineData("Tasks")]
    public void A_tasks_pane_from_before_the_view_switch_is_ignored_and_the_side_panes_keep_their_state(string legacyTasks)
    {
        var path = NewShellNavigationPath();

        try
        {
            WriteShellNavigation(path, $$"""
                { "lastSurface": "Workspace", "lastEnabledPanes": ["{{legacyTasks}}", "Inbox", "Devbook"] }
                """);
            var shellNavigation = new ShellNavigationStore(path);

            using var harness = CreateHarness(
                features => features.SetEnabled(AppFeatures.InboxPane, true),
                shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
                Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane']"));
                Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
                Assert.Equal("true", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
                Assert.Equal("true", component.Find("[data-testid='inbox-pane-option']").GetAttribute("aria-pressed"));
                Assert.Equal("true", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
            });

            // The next save writes the side panes alone.
            component.Find("[data-testid='inbox-pane-option']").Click();
            component.WaitForAssertion(() => Assert.Equal(["Devbook"], shellNavigation.LastEnabledPanes));
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// The "side panes only" state is gone with the Tasks pane toggle: a file that
    /// left the Inbox open on its own reopens with the Inbox beside the Tasks view,
    /// since one view is always showing.
    /// </summary>
    [Fact]
    public void A_side_panes_only_file_reopens_with_the_panes_beside_the_tasks_view()
    {
        var path = NewShellNavigationPath();

        try
        {
            WriteShellNavigation(path, """
                { "lastSurface": "Workspace", "lastEnabledPanes": ["Inbox"] }
                """);
            var shellNavigation = new ShellNavigationStore(path);

            using var harness = CreateHarness(
                features => features.SetEnabled(AppFeatures.InboxPane, true),
                shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
                Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane']"));
                Assert.Empty(component.FindAll("[data-testid='side-pane-stack']"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// The roadmap was a takeover before it was a view, and a file from then names it
    /// as the surface. It reopens on the Roadmap view in the workspace, with the
    /// remembered side panes beside it, and the shell never writes the old surface
    /// name back.
    /// </summary>
    [Fact]
    public void A_roadmap_surface_from_before_the_view_switch_reopens_as_the_roadmap_view()
    {
        var path = NewShellNavigationPath();

        try
        {
            WriteShellNavigation(path, """
                { "lastSurface": "Roadmap", "lastEnabledPanes": ["Devbook"] }
                """);
            var shellNavigation = new ShellNavigationStore(path);

            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='workspace'] [data-testid='roadmap-view'] [data-testid='roadmap-band']"));
                Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
                Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
                Assert.Equal("true", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
            });

            component.Find("[data-testid='dashboard-toggle-button']").Click();
            component.WaitForAssertion(() => Assert.Equal("Dashboard", shellNavigation.LastSurface));
            component.Find("[data-testid='dashboard-toggle-button']").Click();
            component.WaitForAssertion(() => Assert.Equal("Workspace", shellNavigation.LastSurface));

            Assert.DoesNotContain("\"lastSurface\": \"Roadmap\"", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Equal("Roadmap", new ShellNavigationStore(path).LastView);
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>Picking a view is remembered by its name, and a fresh shell instance
    /// reopens on it.</summary>
    [Fact]
    public void The_view_is_remembered_and_a_fresh_shell_instance_reopens_on_it()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            using (var harness = CreateHarness(shellNavigation: shellNavigation))
            {
                var component = Render(harness);
                OpenTheRoadmap(component);
                component.WaitForAssertion(() => Assert.Equal("Roadmap", shellNavigation.LastView));
            }

            using var reopened = CreateHarness(shellNavigation: new ShellNavigationStore(path));
            var again = Render(reopened);

            again.WaitForAssertion(() =>
            {
                Assert.NotEmpty(again.FindAll("[data-testid='roadmap-band']"));
                Assert.Equal("true", again.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// A name the current build does not recognise must never stop the app from
    /// opening, the same guarantee the surface gets.
    /// </summary>
    [Fact]
    public void An_unrecognised_remembered_pane_falls_back_to_the_default()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            shellNavigation.SetLastPanes(["SomeFuturePane"]);

            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']")));
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// A non-default pane selection has to survive a takeover. Devbook is turned
    /// on first precisely so the assertion is about the reader's choice rather than
    /// about the default the shell would fall back to anyway.
    /// </summary>
    [Fact]
    public void Closing_a_surface_restores_the_workspace_with_the_pane_selection_unchanged()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
        });

        component.Find("[data-testid='tools-toggle-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='tools-surface']"));
            Assert.Empty(component.FindAll("[data-testid='devbook-stack']"));
        });

        // The in-surface ✕ and the header toggle both close it; this is the ✕.
        component.Find("[data-testid='tools-panel'] button.btn--ghost").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='tools-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-layout']"));

            // Both panes are back, which is only true because opening the surface
            // never disabled either of them.
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
        });
    }

    /// <summary>
    /// The Board is a main view over the same Tasks pane: the filter bar stays,
    /// the list gives way to the columns, and the switch reads Board pressed. Its
    /// name is what the shell remembers, and the Columns choice is remembered
    /// beside it, so a fresh shell reopens on the Board grouped the same way.
    /// </summary>
    [Fact]
    public void The_board_view_lays_the_tasks_out_in_columns_under_the_filter_bar_and_is_remembered_with_its_columns()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            using (var harness = CreateHarness(shellNavigation: shellNavigation))
            {
                var component = Render(harness);

                component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='entry-list'], [data-testid='empty-state']")));
                component.Find("[data-testid='board-view-option']").Click();

                component.WaitForAssertion(() =>
                {
                    Assert.NotEmpty(component.FindAll("[data-testid='workspace'] [data-testid='backlog-pane'] .filter-bar"));
                    Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane'] [data-testid='task-board']"));
                    Assert.Empty(component.FindAll("[data-testid='entry-list']"));
                    Assert.Equal("true", component.Find("[data-testid='board-view-option']").GetAttribute("aria-pressed"));
                    Assert.Equal("false", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
                    Assert.Equal("Board", shellNavigation.LastView);
                    Assert.Equal("Status", shellNavigation.BoardColumns);
                });

                component.Find("#board-columns").Change("Priority");
                component.WaitForAssertion(() =>
                {
                    Assert.Equal("Priority", shellNavigation.BoardColumns);
                    Assert.Equal("Critical", component.Find("[data-testid='board-column-title']").TextContent);
                });

                component.Find("[data-testid='tasks-view-option']").Click();
                component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='task-board']")));
            }

            var reopenedNavigation = new ShellNavigationStore(path);
            reopenedNavigation.SetLastView("Board");
            using var reopened = CreateHarness(shellNavigation: reopenedNavigation);
            var again = Render(reopened);

            again.WaitForAssertion(() =>
            {
                Assert.NotEmpty(again.FindAll("[data-testid='task-board']"));
                Assert.Equal("true", again.Find("[data-testid='board-view-option']").GetAttribute("aria-pressed"));
                Assert.Equal("Priority", again.Find("#board-columns").GetAttribute("value"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// The Calendar is a main view over the same Tasks pane: the filter bar stays,
    /// the list gives way to the month, and the switch reads Calendar pressed. Its
    /// name is what the shell remembers, and a fresh shell reopens on it.
    /// </summary>
    [Fact]
    public void The_calendar_view_lays_the_tasks_out_on_a_month_under_the_filter_bar_and_is_remembered()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            using (var harness = CreateHarness(shellNavigation: shellNavigation))
            {
                var component = Render(harness);

                component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='entry-list'], [data-testid='empty-state']")));
                component.Find("[data-testid='calendar-view-option']").Click();

                component.WaitForAssertion(() =>
                {
                    Assert.NotEmpty(component.FindAll("[data-testid='workspace'] [data-testid='backlog-pane'] .filter-bar"));
                    Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane'] [data-testid='task-calendar']"));
                    Assert.Empty(component.FindAll("[data-testid='entry-list']"));
                    Assert.Equal("true", component.Find("[data-testid='calendar-view-option']").GetAttribute("aria-pressed"));
                    Assert.Equal("false", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
                    Assert.Equal("Calendar", shellNavigation.LastView);
                });

                component.Find("[data-testid='tasks-view-option']").Click();
                component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='task-calendar']")));
                component.Find("[data-testid='calendar-view-option']").Click();
                component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='task-calendar']")));
            }

            using var reopened = CreateHarness(shellNavigation: new ShellNavigationStore(path));
            var again = Render(reopened);

            again.WaitForAssertion(() =>
            {
                Assert.NotEmpty(again.FindAll("[data-testid='task-calendar']"));
                Assert.Equal("true", again.Find("[data-testid='calendar-view-option']").GetAttribute("aria-pressed"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// The roadmap is a main view, not a takeover: it shows in the workspace's own
    /// <c>main</c>, in the place of the task list, and the Tasks filter bar goes with
    /// the list. The view switch reads Roadmap pressed and Tasks not.
    /// </summary>
    [Fact]
    public void The_roadmap_view_replaces_the_task_list_inside_the_workspace()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll(".filter-bar"));
        });

        OpenTheRoadmap(component);

        component.WaitForAssertion(() =>
        {
            var main = Assert.Single(component.FindAll("main"));
            Assert.Equal("workspace", main.GetAttribute("data-testid"));
            Assert.NotNull(main.QuerySelector("[data-testid='devbook-layout'] > [data-testid='roadmap-view'] > [data-testid='roadmap-band']"));

            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-surface']"));
            Assert.Empty(component.FindAll(".filter-bar"));

            Assert.Equal("true", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// Exactly one view is always showing, so the view switch has no "off": pressing
    /// the pressed option again changes nothing, and the other option is the only way
    /// to another view.
    /// </summary>
    [Fact]
    public void Pressing_the_pressed_view_again_changes_nothing()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']")));
        component.Find("[data-testid='tasks-view-option']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Equal("true", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
        });

        OpenTheRoadmap(component);
        component.Find("[data-testid='roadmap-view-option']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        });

        component.Find("[data-testid='tasks-view-option']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
        });
    }

    /// <summary>
    /// The side panes open beside whichever view is showing: the Inbox to the left of
    /// the roadmap, the Devbook to its right, as they sit beside the task list.
    /// </summary>
    [Fact]
    public void The_inbox_and_devbook_open_beside_the_roadmap_view()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.InboxPane, true));
        var component = Render(harness);

        OpenTheRoadmap(component);
        component.Find("[data-testid='inbox-pane-option']").Click();
        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            var layout = component.Find("[data-testid='devbook-layout']");
            var columns = layout.Children
                .Select(child => child.GetAttribute("data-testid"))
                .ToList();

            Assert.Equal("inbox-pane", columns[0]);
            Assert.Equal("roadmap-view", columns[1]);
            Assert.Equal("side-pane-stack", columns[^1]);
            Assert.NotNull(layout.QuerySelector("[data-testid='side-pane-stack'] [data-testid='devbook-stack']"));
            Assert.Contains("devbook-layout--side-open", layout.ClassList);
            Assert.Contains("devbook-layout--inbox-before-backlog", layout.ClassList);
            Assert.Equal("true", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void The_roadmap_is_absent_when_its_feature_is_off()
    {
        using var harness = CreateHarness(features => features.SetEnabled(RoadmapFeatures.Roadmap, false));
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            // No option offering it: the flag decides whether the option exists. The
            // switch stays, with Tasks, the Board and the Calendar as its options.
            Assert.Empty(component.FindAll("[data-testid='roadmap-view-option']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));

            Assert.Equal(
                ["tasks-view-option", "board-view-option", "calendar-view-option"],
                component.Find("[data-testid='view-switch']").Children.Select(option => option.GetAttribute("data-testid")));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
        });
    }

    [Fact]
    public void A_surface_whose_feature_is_switched_off_falls_back_to_the_workspace()
    {
        using var harness = CreateHarness(features =>
        {
            features.SetEnabled(DashboardFeatures.Dashboard, false);
            features.SetEnabled(SessionFeatures.Sessions, false);
        });
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='dashboard-toggle-button']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-layout']"));
        });
    }

    /// <summary>
    /// Each segment is gated on its own feature: with the Dashboard off, Sessions
    /// is still offered and still opens its list.
    /// </summary>
    [Fact]
    public void With_only_the_dashboard_feature_off_sessions_is_still_offered()
    {
        using var harness = CreateHarness(features => features.SetEnabled(DashboardFeatures.Dashboard, false));
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='dashboard-toggle-button']"));
            Assert.NotEmpty(component.FindAll("[data-testid='sessions-toggle-button']"));
        });

        OpenSessions(component);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='sessions-surface']")));
    }

    /// <summary>
    /// The whole point of the flag: with it off there is no way in and nothing to
    /// find. The Dashboard itself is untouched.
    /// </summary>
    [Fact]
    public void With_the_sessions_feature_off_there_is_no_segment_and_no_list()
    {
        using var harness = CreateHarness(features => features.SetEnabled(SessionFeatures.Sessions, false));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='dashboard-toggle-button']")));
        Assert.Empty(component.FindAll("[data-testid='sessions-toggle-button']"));

        component.Find("[data-testid='dashboard-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-panel']"));
            Assert.Empty(component.FindAll("[data-testid='sessions-panel']"));

            // The other takeover is untouched: one flag, one area.
            Assert.NotEmpty(component.FindAll("[data-testid='tools-toggle-button']"));
        });
    }

    /// <summary>
    /// A remembered Sessions surface whose feature has since gone off falls back to
    /// the workspace, as every takeover does.
    /// </summary>
    [Fact]
    public void A_remembered_sessions_surface_falls_back_to_the_workspace_when_its_feature_is_off()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            shellNavigation.SetLastSurface("Sessions");

            using var harness = CreateHarness(
                features => features.SetEnabled(SessionFeatures.Sessions, false),
                shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
                Assert.Empty(component.FindAll("[data-testid='sessions-panel']"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// Closing is the ✕ inside the pane as well as the header's takeover group and
    /// pane strip, which is what keeps the header on screen while a takeover is open.
    /// </summary>
    [Fact]
    public void The_sessions_pane_closes_back_to_the_workspace()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenSessions(component);

        component.Find("[data-testid='sessions-panel'] button.btn--ghost").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='sessions-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-layout']"));
        });
    }

    /// <summary>
    /// The roadmap's option sits in the view switch beside Tasks, fused with it,
    /// unpressed until the reader chooses it. The shell opens on the Tasks view, so
    /// the roadmap is off screen until asked for, and it keeps its own Planning
    /// heading row once it is.
    /// </summary>
    [Fact]
    public void The_roadmap_option_starts_unpressed_in_the_view_switch()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            var option = component.Find("[data-testid='roadmap-view-option']");

            Assert.Equal("false", option.GetAttribute("aria-pressed"));
            Assert.Equal("roadmap-view", option.GetAttribute("aria-controls"));
            Assert.Equal("Roadmap", LabelWithoutFlag(option));
            Assert.False(option.HasAttribute("disabled"));

            Assert.Equal("view-switch", option.ParentElement?.GetAttribute("data-testid"));
            Assert.DoesNotContain("header-group__option--loose", option.ClassList);

            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
        });

        OpenTheRoadmap(component);

        component.WaitForAssertion(() =>
        {
            var band = component.Find("[data-testid='roadmap-band']");

            Assert.Equal("Planning", band.QuerySelector(".roadmap-band__eyebrow")!.TextContent.Trim());
            Assert.Equal("Roadmap", component.Find("#roadmap-band-title").TextContent.Trim());
        });
    }

    /// <summary>
    /// A takeover opened from the Roadmap view closes back onto it: the view is kept
    /// underneath, as the side panes are.
    /// </summary>
    [Fact]
    public void Closing_a_takeover_opened_from_the_roadmap_returns_to_the_roadmap()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenTheRoadmap(component);
        component.Find("[data-testid='dashboard-toggle-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-surface']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Equal("false", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
        });

        component.Find("[data-testid='dashboard-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='dashboard-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Equal("true", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// A resize may not touch the view. The side panes have a viewport-driven
    /// capacity and trim themselves to fit; the view is outside that selection, so a
    /// narrowed window can never close it. Driven through the shell's own capacity
    /// entry point — the path the window's resize listener calls.
    /// </summary>
    [Fact]
    public async Task A_window_resize_never_closes_the_roadmap()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenTheRoadmap(component);

        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(1));
        AssertOnTheRoadmap(component);

        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(3));
        AssertOnTheRoadmap(component);

        static void AssertOnTheRoadmap(IRenderedComponent<Home> rendered) =>
            rendered.WaitForAssertion(() =>
            {
                Assert.NotEmpty(rendered.FindAll("[data-testid='roadmap-band']"));
                Assert.Equal("true", rendered.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
            });
    }

    /// <summary>
    /// The views are not panes, and nothing may quietly make one a pane again:
    /// folding a view into <see cref="GlobalPane"/> would hand it the side panes'
    /// capacity rule, and a narrowed window would start evicting it. A tripwire on a
    /// later tidy-up rather than a test of behaviour, which is why it counts members.
    /// </summary>
    [Fact]
    public void The_side_panes_are_the_inbox_and_the_devbook_and_no_view()
    {
        GlobalPane[] expected = [GlobalPane.Inbox, GlobalPane.Devbook];

        Assert.Equal(expected, Enum.GetValues<GlobalPane>());
    }

    /// <summary>
    /// The feature and the chosen view are independent: turning the feature off
    /// shows the Tasks view and takes the option away, and turning it back on
    /// returns the reader to the roadmap they were on, because the choice was
    /// never reset.
    /// </summary>
    [Fact]
    public void The_roadmap_survives_its_feature_going_off_and_back_on()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var features = (AppFeatureSettingsStore)harness.Context.Services.GetRequiredService<IAppFeatureSettings>();

        OpenTheRoadmap(component);

        _ = features.SetEnabled(RoadmapFeatures.Roadmap, false);

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='roadmap-view-option']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Equal("true", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
        });

        _ = features.SetEnabled(RoadmapFeatures.Roadmap, true);

        component.WaitForAssertion(() =>
        {
            Assert.Equal("true", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
        });
    }

    /// <summary>
    /// The maturity flag follows an unfinished feature out of the settings screen
    /// and onto the control that leads to it.
    ///
    /// <para>Inbox is the catalog's <c>Dev</c> feature and ships off, which makes
    /// it the pair worth testing: the option only exists in the header once the
    /// feature is on, and the badge only exists once the option does.</para>
    /// </summary>
    [Fact]
    public void An_enabled_unfinished_feature_carries_its_flag_into_the_header()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.InboxPane, true));
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            var badge = component.Find("[data-testid='inbox-feature-status']");

            Assert.Contains("badge--feature-dev", badge.ClassList);
            Assert.Equal("DEV", badge.TextContent.ToUpperInvariant());

            // On the control itself, not floating near it.
            Assert.Equal(
                component.Find("[data-testid='inbox-pane-option']"),
                badge.ParentElement);
        });
    }

    /// <summary>A released feature is the ordinary case and the header stays quiet
    /// about it — otherwise the flag would be wallpaper rather than a warning.
    /// Devbook and Roadmap are the released ones of the four enabled here; the
    /// other two are Dev and are the control group.</summary>
    [Fact]
    public void A_released_feature_adds_no_flag_to_the_header()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            // Present and unflagged.
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']"));
            Assert.Empty(component.FindAll("[data-testid='devbook-feature-status']"));
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-view-option']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-feature-status']"));

            // Present and flagged — proving the absence above is the status
            // talking rather than the badge being broken everywhere.
            Assert.Single(component.FindAll("[data-testid='tools-feature-status']"));
            Assert.Single(component.FindAll("[data-testid='dashboard-feature-status']"));
        });
    }

    /// <summary>
    /// A side pane opens beside the view on a plain press and closes on the next:
    /// there is no switch to make, because the view is not a pane, and no modifier to
    /// learn. Closing the last side pane is allowed — the view is still on screen —
    /// so neither toggle is ever disabled.
    /// </summary>
    [Fact]
    public void A_side_pane_toggle_opens_it_beside_the_view_and_closes_it_again()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Equal("true", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
            Assert.False(component.Find("[data-testid='devbook-pane-option']").HasAttribute("disabled"));
        });

        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Equal("false", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>The tooltip says what the press will do: open the pane beside the
    /// view on screen, or close it.</summary>
    [Fact]
    public void The_side_pane_tooltip_says_what_the_press_will_do()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
            Assert.Equal("Open Devbook beside Tasks", component.Find("[data-testid='devbook-pane-option']").GetAttribute("title")));

        OpenTheRoadmap(component);
        component.WaitForAssertion(() =>
            Assert.Equal("Open Devbook beside Roadmap", component.Find("[data-testid='devbook-pane-option']").GetAttribute("title")));

        component.Find("[data-testid='devbook-pane-option']").Click();
        component.WaitForAssertion(() =>
            Assert.Equal("Close Devbook", component.Find("[data-testid='devbook-pane-option']").GetAttribute("title")));
    }

    /// <summary>
    /// A window too narrow for the view and two side panes has to drop one, and it
    /// drops the first in the stable order — the Inbox before the Devbook. The view
    /// itself is never dropped. This is the resize path rather than the press path.
    /// </summary>
    [Fact]
    public async Task A_narrowed_window_drops_the_first_side_pane_in_the_stable_order()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.InboxPane, true));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-option']")));
        component.Find("[data-testid='inbox-pane-option']").Click();
        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
        });

        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(2));

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='inbox-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
        });
    }

    /// <summary>
    /// A disposed shell takes its registration out of the resizer with it.
    /// <para>
    /// The resizer keeps a map of owners and invokes every one of them on each
    /// window resize. Home registers on its first render and used to leave the
    /// registration behind, so a shell that had been navigated away from was still
    /// in the map: the next resize called into a disposed component and threw. The
    /// registration is keyless — Home's own <c>initialize</c> passes no key — so
    /// the unregister is asserted keyless too, because a key here would delete
    /// nothing and leave the dead owner in place.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Disposing_the_shell_unregisters_its_pane_owner_from_the_resizer()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        // Both calls are filtered on their argument count, because the backlog
        // pane's SplitPane registers through the same two interop names. It passes
        // a minted key; the shell passes none, so the shell's registration is the
        // one-argument initialize and the shell's unregister is the argument-less
        // dispose. Counting the identifier alone would pass on the split's calls.
        component.WaitForAssertion(() =>
            Assert.Single(
                harness.Context.JSInterop.Invocations["backlogPaneResizer.initialize"],
                invocation => invocation.Arguments.Count == 1));

        await harness.Context.DisposeAsync();

        Assert.Single(
            harness.Context.JSInterop.Invocations["backlogPaneResizer.dispose"],
            invocation => invocation.Arguments.Count == 0);
    }

    /// <summary>An option's own label, with any maturity flag inside it removed.
    /// The badge is a child of the control rather than a sibling, so plain
    /// TextContent now returns "Roadmap dev".</summary>
    private static string LabelWithoutFlag(IElement option)
    {
        var flag = option.QuerySelector("[class*='badge--feature']")?.TextContent;

        return (string.IsNullOrEmpty(flag) ? option.TextContent : option.TextContent.Replace(flag, string.Empty)).Trim();
    }

    /// <summary>
    /// The AI panel header is the library SectionHeader too, and it stays a div:
    /// the panel is already a section named by this heading, so the header it had
    /// was never a landmark of its own and adopting the component was not allowed
    /// to make it one.
    /// </summary>
    [Fact]
    public void The_ai_panel_header_is_the_shared_component_and_stays_a_div()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.AiAssistant, true));

        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            var header = component.Find(".ai-panel__header");

            Assert.Equal("DIV", header.TagName);
            Assert.Equal("ai-panel__header", header.GetAttribute("class"));

            var text = header.Children[0];
            Assert.Null(text.GetAttribute("class"));
            Assert.Equal("ai-panel__eyebrow", text.Children[0].GetAttribute("class"));

            // Styled by element in app.css, so the heading carries no class - and
            // the id the panel aria-labelledby points at is on the heading.
            var heading = text.Children[1];
            Assert.Equal("H2", heading.TagName);
            Assert.Null(heading.GetAttribute("class"));
            Assert.Equal("ai-assistant-title", heading.GetAttribute("id"));

            // The settings link is a direct child, as it was before the swap.
            Assert.Equal("A", header.Children[1].TagName);
            Assert.Equal("ai-panel__settings", header.Children[1].GetAttribute("class"));
        });
    }

    /// <summary>
    /// The pull requests list is a takeover of its own, with its own segment right
    /// after Sessions, and the way back is the pane's ✕ as much as the header's
    /// pane options.
    /// </summary>
    [Fact]
    public void Opening_pull_requests_replaces_every_pane_and_its_close_returns_to_them()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenPullRequests(component);

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='pull-requests-surface'] [data-testid='pull-requests-panel']"));
            Assert.Empty(component.FindAll("[data-testid='sessions-surface']"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
            Assert.Single(component.FindAll("main"));
            Assert.Equal("true", component.Find("[data-testid='pull-requests-toggle-button']").GetAttribute("aria-pressed"));

            // Next to Sessions in the work in progress, as the same context's second list.
            var options = component.FindAll("[data-testid='work-in-progress-switcher'] > *")
                .Select(option => option.GetAttribute("data-testid"))
                .ToList();
            Assert.Equal(options.IndexOf("sessions-toggle-button") + 1, options.IndexOf("pull-requests-toggle-button"));
        });

        component.Find("[data-testid='pull-requests-panel'] button.btn--ghost").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='pull-requests-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-layout']"));
        });
    }

    /// <summary>The flag is the whole gate: off, there is no segment and no list, and
    /// a surface remembered from when it was on reopens on the workspace instead.</summary>
    [Fact]
    public void With_the_pull_requests_feature_off_there_is_no_segment_and_no_list()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            shellNavigation.SetLastSurface("PullRequests");

            using var harness = CreateHarness(
                features => features.SetEnabled(SessionFeatures.PullRequests, false),
                shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
                Assert.NotEmpty(component.FindAll("[data-testid='sessions-toggle-button']"));
                Assert.Empty(component.FindAll("[data-testid='pull-requests-toggle-button']"));
                Assert.Empty(component.FindAll("[data-testid='pull-requests-panel']"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    [Fact]
    public void The_shell_reopens_on_a_remembered_pull_requests_surface()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            shellNavigation.SetLastSurface("PullRequests");

            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='pull-requests-surface'] [data-testid='pull-requests-panel']"));
                Assert.Empty(component.FindAll("[data-testid='workspace']"));
            });

            component.Find("[data-testid='tasks-view-option']").Click();
            component.WaitForAssertion(() => Assert.Equal("Workspace", shellNavigation.LastSurface));
            component.Find("[data-testid='pull-requests-toggle-button']").Click();

            component.WaitForAssertion(() => Assert.Equal("PullRequests", shellNavigation.LastSurface));
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// The pull requests list asks the shell which entry recorded a pull request, and
    /// the shell answers from the entry's own pull request links — <c>owner/name</c>
    /// and number, the repository compared without regard to case — by stored id.
    /// </summary>
    [Fact]
    public void The_entry_that_recorded_a_pull_request_is_the_pull_request_s_task()
    {
        using var harness = CreateHarness(seed: PlanEntryText);
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        component.WaitForState(() => state.Rows.FirstOrDefault()?.Id is not null);

        var row = state.Rows[0];
        row.PullRequestLinks = [new EntryPullRequestLink("JSdotNet/Backlog", 712)];

        OpenPullRequests(component);

        var pane = component.FindComponent<PullRequestsPane>().Instance;

        Assert.NotNull(pane.PullRequestTask);

        var task = pane.PullRequestTask!(PullRequestSubject("jsdotnet/backlog", 712), false);

        Assert.NotNull(task);
        Assert.Equal(DeliveryRunReferenceKind.Task, task!.Kind);
        Assert.Equal(row.Id, task.EntryId);
        Assert.Equal("Read delivery run files", task.Label);

        Assert.Null(pane.PullRequestTask(PullRequestSubject("JSdotNet/Backlog", 713), false));
        Assert.Null(pane.PullRequestTask(PullRequestSubject("JSdotNet/Archify", 712), false));

        // An open local entry is one of mine, so Mine asks of it too.
        Assert.Equal(row.Id, pane.PullRequestTask(PullRequestSubject("JSdotNet/Backlog", 712), true)?.EntryId);

        // And opening it is the shell's ordinary way to an entry.
        component.InvokeAsync(() => pane.OnOpenTask.InvokeAsync(task));

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='pull-requests-panel']"));
            Assert.Contains("Read delivery run files", Assert.Single(component.FindAll(".task-item--selected")).TextContent);
        });
    }

    /// <summary>
    /// A linked task's external key — the Jira key in spec-manager's display key —
    /// relates a pull request that names it in its title or branch, and a task that
    /// is done still leads there but no longer counts toward Mine.
    /// </summary>
    [Fact]
    public void A_linked_tasks_external_key_in_a_pull_request_relates_it_and_a_done_task_is_not_mine()
    {
        using var harness = CreateHarness(seed: PlanEntryText);
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        component.WaitForState(() => state.Rows.FirstOrDefault()?.Id is not null);

        var row = state.Rows[0];
        row.SourceRef = new SourceRef(
            "spec-manager", "finance", "item-123", "https://example.test/items/123", "#123 · FIN-8428",
            assignee: null, sourceState: "open", sourceUpdatedAt: DateTimeOffset.UnixEpoch);

        OpenPullRequests(component);

        var pane = component.FindComponent<PullRequestsPane>().Instance;
        var named = PullRequestSubject("JSdotNet/Backlog", 900, title: "FIN-8428: reconcile the ledger");
        var branch = PullRequestSubject("JSdotNet/Backlog", 901, head: "feature/fin-8428-ledger");
        var longer = PullRequestSubject("JSdotNet/Backlog", 902, title: "FIN-84281 something else");

        Assert.Equal(row.Id, pane.PullRequestTask!(named, false)?.EntryId);
        Assert.Equal(row.Id, pane.PullRequestTask(branch, true)?.EntryId);
        Assert.Null(pane.PullRequestTask(longer, false));

        // Done in the entry's own text, then any change the shell hears of — which is
        // what rebuilds its matcher, since a row changes in place.
        row.RawText = row.RawText.Replace("`!ready`", "`!done`", StringComparison.Ordinal);
        Assert.Equal(EntryStatus.Done, row.PreviewStatus);
        component.InvokeAsync(() => state.SetSelectionMode(true));

        component.WaitForAssertion(() =>
        {
            Assert.Equal(row.Id, pane.PullRequestTask(named, false)?.EntryId);
            Assert.Null(pane.PullRequestTask(named, true));
        });
    }

    private static PullRequestTaskSubject PullRequestSubject(string repository, int number, string title = "A pull request", string head = "feature") =>
        new(repository, number, title, head, [], SessionId: null);

    /// <summary>A session opened from the pull requests list is the session list on
    /// that session, as it is from a task.</summary>
    [Fact]
    public async Task Opening_a_session_from_the_pull_requests_list_shows_the_sessions_surface_on_it()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenPullRequests(component);

        var pane = component.FindComponent<PullRequestsPane>().Instance;

        await component.InvokeAsync(() => pane.OnOpenSession.InvokeAsync("session-1"));

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='pull-requests-surface']"));
            Assert.Equal("session-1", component.FindComponent<SessionsPane>().Instance.FocusSessionId);
        });
    }

    /// <summary>
    /// Escape closes a takeover even after focus has left it for <c>&lt;body&gt;</c>.
    /// <para>
    /// A focused Refresh button that goes disabled while its read runs drops focus
    /// to the body, outside the surface's <c>@onkeydown</c>, and the surface stayed
    /// open on Escape. The shell now registers with a document listener that hears
    /// that press and calls back; bUnit has no document, so the callback is invoked
    /// the way the script invokes it, and the surface marker the script looks for
    /// is asserted beside it.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("tools-toggle-button", "tools-surface")]
    [InlineData("dashboard-toggle-button", "dashboard-surface")]
    [InlineData("sessions-toggle-button", "sessions-surface")]
    [InlineData("pull-requests-toggle-button", "pull-requests-surface")]
    public async Task Escape_heard_at_the_document_closes_the_takeover(string toggle, string surface)
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
            Assert.Single(
                harness.Context.JSInterop.Invocations["backlogTakeoverEscape.register"],
                invocation => invocation.Arguments.Count == 1));

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll($"[data-testid='{toggle}']")));
        component.Find($"[data-testid='{toggle}']").Click();
        component.WaitForAssertion(() =>
            Assert.True(component.Find($"[data-testid='{surface}']").HasAttribute("data-escape-closes")));

        await component.InvokeAsync(component.Instance.CloseSurfaceOnEscapeAsync);

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll($"[data-testid='{surface}']"));
            Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
        });
    }

    /// <summary>The Roadmap is a view, not a takeover, and keeps Escape for its own
    /// grabbed bars and dialogs: nothing on it carries the marker for the document
    /// listener, and the callback leaves it on screen.</summary>
    [Fact]
    public async Task Escape_heard_at_the_document_leaves_the_roadmap_open()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenTheRoadmap(component);

        Assert.Empty(component.FindAll("[data-escape-closes]"));

        await component.InvokeAsync(component.Instance.CloseSurfaceOnEscapeAsync);

        Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
        Assert.Equal("true", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task Disposing_the_shell_unregisters_its_escape_listener()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
            Assert.NotEmpty(harness.Context.JSInterop.Invocations["backlogTakeoverEscape.register"]));

        await harness.Context.DisposeAsync();

        Assert.Single(harness.Context.JSInterop.Invocations["backlogTakeoverEscape.unregister"]);
    }

    /// <summary>Presses the Pull requests segment and waits for the list to take the
    /// screen.</summary>
    private static void OpenPullRequests(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='pull-requests-toggle-button']")));
        component.Find("[data-testid='pull-requests-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='pull-requests-panel']")));
    }

    /// <summary>Presses the Sessions segment and waits for the list to take the
    /// screen.</summary>
    private static void OpenSessions(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='sessions-toggle-button']")));
        component.Find("[data-testid='sessions-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='sessions-panel']")));
    }

    /// <summary>Presses the roadmap's option in the view switch and waits for it to
    /// show — through the same affordance the reader has, rather than by reaching
    /// into shell state.</summary>
    private static void OpenTheRoadmap(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='roadmap-view-option']")));
        component.Find("[data-testid='roadmap-view-option']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']")));
    }

    /// <summary>Opens the Devbook beside the task list, so two areas are on screen
    /// and Ask AI offers a chip for each.</summary>
    private static void OpenTheDevbookBeside(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        component.Find("[data-testid='devbook-pane-option']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.NotEmpty(component.FindAll("[data-testid='ai-scope-devbook']"));
        });
    }

    /// <summary>Writes a navigation file the way an earlier build left it, so the
    /// shell reads a shape this build would never write itself.</summary>
    private static void WriteShellNavigation(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private static string NewShellNavigationPath() =>
        Path.Combine(Path.GetTempPath(), "backlog-workspace-surface-tests", Guid.NewGuid().ToString("n"), "shell", "shell-navigation.json");

    private static void DeleteShellNavigationDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null) return;

        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }

    private static IRenderedComponent<Home> Render(Harness harness)
    {
        harness.Context.JSInterop.Mode = JSRuntimeMode.Loose;
        return harness.Context.Render<Home>();
    }

    /// <summary>
    /// The sessions list hands an entry back to the workspace: a delivery run names
    /// the Backlog entry it was started from, and pressing it leaves the surface,
    /// puts the task list on screen and selects that entry.
    /// <para>
    /// Wired here rather than in the pane because an entry belongs to Tasks and the
    /// pane holds sessions; the shell is the only place that knows both, which is
    /// exactly what this asserts.
    /// </para>
    /// </summary>
    [Fact]
    public void A_run_s_backlog_entry_opens_in_the_task_list()
    {
        using var harness = CreateHarness(seed: PlanEntryText);
        var component = Render(harness);

        OpenSessions(component);

        var pane = component.FindComponent<SessionsPane>().Instance;

        Assert.True(pane.OnOpenTask.HasDelegate);

        // The plan as the marker states it — bare, where the app stores the tag with
        // its sigil — so this is the spelling the shell has to reconcile.
        component.InvokeAsync(() => pane.OnOpenTask.InvokeAsync(
            new DeliveryRunReference(DeliveryRunReferenceKind.Task, "delivery-run-reader", "Plan backlog-mcp-server", null, null, "backlog-mcp-server")));

        component.WaitForAssertion(() =>
        {
            // Off the surface and back to the panes, with the task list among them.
            Assert.Empty(component.FindAll("[data-testid='sessions-panel']"));

            var selected = Assert.Single(component.FindAll(".task-item--selected"));

            Assert.Contains("Read delivery run files", selected.TextContent);
        });
    }

    /// <summary>
    /// A run started from an entry copied out of the app names it by its stored id and
    /// nothing else, and that id is what the shell opens it by.
    /// </summary>
    [Fact]
    public void A_run_s_entry_named_by_id_opens_in_the_task_list()
    {
        using var harness = CreateHarness(seed: PlanEntryText);
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();
        component.WaitForState(() => state.Rows.FirstOrDefault()?.Id is not null);

        var id = state.Rows[0].Id!.Value;

        OpenSessions(component);

        var pane = component.FindComponent<SessionsPane>().Instance;

        component.InvokeAsync(() => pane.OnOpenTask.InvokeAsync(
            new DeliveryRunReference(DeliveryRunReferenceKind.Task, "Read delivery run files", null, null, null, EntryId: id)));

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='sessions-panel']"));
            Assert.Contains("Read delivery run files", Assert.Single(component.FindAll(".task-item--selected")).TextContent);
        });
    }

    /// <summary>
    /// The sessions list asks the shell which entry linked a session, and the shell
    /// answers from the entry's own session links — by stored id, so opening it is the
    /// surest match there is. A session no entry linked has none.
    /// </summary>
    [Fact]
    public void The_entry_that_linked_a_session_is_the_session_s_task()
    {
        using var harness = CreateHarness(seed: PlanEntryText);
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();

        component.WaitForState(() => state.Rows.FirstOrDefault()?.Id is not null);

        var row = state.Rows[0];
        row.SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", "1d32704d-c565-4c8a-bb64-b96b01a6f701")];

        OpenSessions(component);

        var pane = component.FindComponent<SessionsPane>().Instance;

        Assert.NotNull(pane.SessionTask);

        var task = pane.SessionTask!("1D32704D-c565-4c8a-bb64-b96b01a6f701");

        Assert.NotNull(task);
        Assert.Equal(DeliveryRunReferenceKind.Task, task!.Kind);
        Assert.Equal(row.Id, task.EntryId);
        Assert.Equal("Read delivery run files", task.Label);

        Assert.Null(pane.SessionTask("another-session"));
    }

    /// <summary>An entry as an import leaves it: the plan tag with its sigil, and
    /// the item's id within that plan.</summary>
    private const string PlanEntryText =
        "# Read delivery run files\n`prompt` `!ready` `+backlog-mcp-server` `id:delivery-run-reader`\n";

    /// <summary>An entry this workspace does not hold is said out loud: the run was
    /// real and the entry is somewhere, just not here.</summary>
    [Fact]
    public void An_entry_this_workspace_does_not_hold_is_reported_rather_than_ignored()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenSessions(component);

        var pane = component.FindComponent<SessionsPane>().Instance;

        component.InvokeAsync(() => pane.OnOpenTask.InvokeAsync(
            new DeliveryRunReference(DeliveryRunReferenceKind.Task, "nowhere", "Plan other-plan", null, null, "other-plan")));

        component.WaitForAssertion(() =>
        {
            // Read off the channel rather than off the screen: the tray lives in the
            // layout, which these tests do not render.
            var notice = Assert.Single(harness.Context.Services.GetRequiredService<IToastChannel>().Visible);

            Assert.Contains("other-plan", notice.Message);
            Assert.Contains("nowhere", notice.Message);
            Assert.Equal("sessions-plan-entry-missing", notice.TestId);

            // And the reader is left where they were, rather than on a task list that
            // does not hold what they asked for.
            Assert.NotEmpty(component.FindAll("[data-testid='sessions-panel']"));
        });
    }

    private static Harness CreateHarness(
        Action<AppFeatureSettingsStore>? configureFeatures = null,
        ShellNavigationStore? shellNavigation = null,
        string? seed = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-workspace-surface-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));

        if (seed is not null)
        {
            // Written through the real use case, so the entry carries the plan and
            // the item id exactly as an import leaves them.
            var saved = TasksTestHost.EntriesFor(store).SaveFromTextAsync(null, seed, 0).GetAwaiter().GetResult();

            Assert.True(saved.IsSuccess);
        }

        var gitHubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var featureSettings = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        shellNavigation ??= new ShellNavigationStore(Path.Combine(root, "shell", "shell-navigation.json"));

        // The three surfaces under test are on; everything that would put extra
        // chrome or a network call in the way is off.
        _ = featureSettings.SetEnabled(RoadmapFeatures.Roadmap, true);
        _ = featureSettings.SetEnabled(DashboardFeatures.Dashboard, true);
        _ = featureSettings.SetEnabled(DevPcFeatures.SystemTools, true);
        _ = featureSettings.SetEnabled(SessionFeatures.Sessions, true);
        _ = featureSettings.SetEnabled(SessionFeatures.PullRequests, true);
        _ = featureSettings.SetEnabled(DevbookFeatures.RepositoryDevbook, true);
        _ = featureSettings.SetEnabled(AppFeatures.InboxPane, false);
        _ = featureSettings.SetEnabled(AppFeatures.AiAssistant, false);
        _ = featureSettings.SetEnabled(AppFeatures.FeedbackReporting, false);
        _ = featureSettings.SetEnabled(TasksFeatures.GitHubIntegration, false);

        configureFeatures?.Invoke(featureSettings);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories);
        var configuredRepository = repository with
        {
            CloneDirectory = RepositoryRoot.Root.FullName,
            DevbookFolders = DevbookFolderSetting.Defaults()
        };
        Assert.Null(gitHubSettings.SetRepositories([configuredRepository]));

        var gitHub = new GitHubIntegration(gitHubSettings, new StubGitHubClient(), new StubProbe());

        // The devbook-only composition, which answers an unscoped ask with the
        // first configured repository. These tests are about the pane surface —
        // pins, switching, what a relaunch reopens on — and Devbook is the
        // released pane they do it with; scoping a repository first in every one
        // of them would be a second gesture about a different thing. The
        // application hosts compose the other way and offer the pane only once
        // a repository is scoped; HomeDevbookPaneTests pins that.
        var devbookFolderSource = new DevbookFolderSource(gitHubSettings);

        var context = new BunitContext();
        context.Services.AddSingleton(store);
        context.Services.AddSingleton<IAppFeatureSettings>(featureSettings);
        context.Services.AddSingleton(shellNavigation);
        context.Services.AddSingleton(gitHubSettings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton(new FeedbackReporter(gitHub));
        var foundryChat = new StubAzureFoundryChatClient();
        context.Services.AddSingleton<IAzureFoundryChatClient>(foundryChat);
        context.Services.AddSingleton<IDevToolService, UnsupportedDevToolService>();

        // The sessions takeover, with nothing on the machine behind it. A shell test
        // asking whether the takeover replaced the panes should not also be reading
        // whatever the person running it had been doing that morning — and a pane that
        // only worked with rows in it would fail here, which is the point.
        context.Services.AddSingleton<IAgentSessionSource>(new EmptySessionSource());
        // And no delivery runs behind it, for the same reason.
        context.Services.AddSingleton<IDeliveryRunSource>(new EmptyRunSource());
        context.Services.AddSingleton<IAppUpdateService, UnsupportedAppUpdateService>();
        context.Services.AddSingleton<IDevbookFolderSource>(devbookFolderSource);
        // The Roadmap module the way a host wires it: a real plan document under the
        // same storage root, so the band draws what was stored rather than a fixture.
        context.Services.AddSingleton<IRoadmapPlanning>(sp =>
            TasksTestHost.PlanningFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
        // The band gathers an item's linked and tagged work through this port before it
        // opens the editor, so a host that composes the band composes the rollup with it.
        context.Services.AddSingleton<IRoadmapItemRollup>(sp =>
            new Backlog.Infrastructure.FileSystem.Roadmap.RoadmapItemRollupService(
                TasksTestHost.EntriesFor(sp.GetRequiredService<WorkspaceSettingsStore>()),
                () => sp.GetRequiredService<WorkspaceSettingsStore>().RootDirectory));
        context.Services.AddSingleton<IImportedPlanSource>(sp =>
            TasksTestHost.ImportedPlansFor(sp.GetRequiredService<WorkspaceSettingsStore>()));
        context.Services.AddSingleton<IRoadmapWorkChanges>(TasksTestHost.WorkChanges());
        context.Services.AddSingleton(TasksTestHost.UntouchedPace());
        context.Services.AddSingleton<IRoadmapActualHours>(new ScriptedActualHours());
        context.Services.AddSingleton<IRoadmapViewPreferences>(new Backlog.Infrastructure.FileSystem.Roadmap.RoadmapViewPreferences(null));
        context.Services.AddSingleton<DesignDevbookProvider>();
        context.Services.AddSingleton<AiDevbookProvider>();
        context.Services.AddSingleton<TechnologyDevbookService>();
        context.Services.AddSingleton<InstructionSourceDiscovery>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<Arc42DevbookStore>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();
        context.Services.AddSingleton<DevbookScope>();
        context.Services.AddSingleton<DevbookUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        // The dashboard takeover, with no provider behind it — see DashboardTestHost.
        _ = context.Services.AddUnavailableDashboard("backlog", "backlog-ide");
        context.Services.AddScoped(sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));
        context.Services.AddScoped(sp => TasksTestHost.StateFor(
            sp.GetRequiredService<WorkspaceSettingsStore>(),
            sp.GetRequiredService<GitHubIntegration>(),
            TasksCopilotCli.Unavailable,
            toasts: sp.GetRequiredService<IToastChannel>()));
        // Home answers the Inbox's Capture button through the module's runner and
        // injects it hard, so a host that renders Home composes the module and
        // picks where its sources are kept, the same as the application hosts do.
        context.Services.AddSingleton<ICaptureSourceSettings>(
            new CaptureSourcesSettingsStore(Path.Combine(root, "capture", "capture-sources.json")));
        context.Services.AddSingleton<ICaptureRunLog>(
            new CaptureRunLogStore(Path.Combine(root, "capture", "capture-runs.json")));
        context.Services.AddSingleton<ICaptureTargetLedger>(
            new CaptureTargetLedgerStore(Path.Combine(root, "capture", "capture-targets.json")));
        context.Services.AddCaptureModule();
        InboxTestHost.AddCaptureDelivery(context.Services);

        TasksTestHost.AddToastChannel(context.Services);
        // The Inbox pane the shell now composes: its state over the in-memory
        // module, the same terms as the Tasks state above.
        _ = InboxTestHost.AddInboxState(context.Services);

        // A test's own registrations go first, so a source it registers for an
        // area is the one the shell takes for that area — the shell keeps the
        // first registration per key.
        configureServices?.Invoke(context.Services);

        // Every area's Ask AI source, the way the application hosts compose them,
        // so the shell has one to offer for each area a test can open. The two
        // whose ports this harness does not otherwise carry — the devbook index
        // and the GitHub activity — get the real adapter over the test clone and
        // an unavailable provider respectively, which is the state both are in on
        // any machine running these tests.
        context.Services.AddTasksAiContentSource();
        context.Services.AddInboxAiContentSource();
        context.Services.AddSingleton<IDevbookSearch>(sp =>
            new DevbookFullTextSearch(sp.GetRequiredService<IDevbookFolderSource>()));
        context.Services.AddDevbookAiContentSource();
        context.Services.AddRoadmapAiContentSource();
        context.Services.AddSingleton<IActivitySource>(new UnavailableActivitySource());
        context.Services.AddScoped<IAiContentSource>(sp => new DashboardAiContentSource(
            sp.GetRequiredService<IActivitySource>(),
            sp.GetRequiredService<IRepositoryDirectory>(),
            sp.GetRequiredService<DashboardScopeInView>(),
            TimeProvider.System));
        context.Services.AddScoped<IAiContentSource, SessionsAiContentSource>();
        context.Services.AddToolsAiContentSource();

        return new Harness(root, context, foundryChat);
    }

    private sealed record Harness(string Root, BunitContext Context, StubAzureFoundryChatClient FoundryChat) : IDisposable
    {
        public void Dispose()
        {
            Context.Dispose();
            TestDatabases.Release(Root);

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class EmptySessionSource : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentSessionCatalog.Empty);
    }

    private sealed class EmptyRunSource : IDeliveryRunSource
    {
        public Task<DeliveryRunCatalog> GetRunsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(DeliveryRunCatalog.Empty);
    }

    private sealed class ThrowingSource(string areaKey, string areaTitle) : IAiContentSource
    {
        public string AreaKey => areaKey;

        public string AreaTitle => areaTitle;

        public Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default) =>
            throw new IOException("The plan file is locked.");
    }

    private sealed class FixedSource(string areaKey, string areaTitle) : IAiContentSource
    {
        public string AreaKey => areaKey;

        public string AreaTitle => areaTitle;

        public Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AiContent(areaKey, $"{areaTitle}: one chapter.", 1, 1, false));
    }

    private sealed class UnavailableActivitySource : IActivitySource
    {
        public Task<InsightAvailability> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(InsightAvailability.Unavailable(DashboardTestHost.UnavailableReason));

        public Task<ActivityReport> GetActivityAsync(
            IReadOnlyList<DashboardRepository> repositories,
            DateTimeOffset from,
            DateTimeOffset to,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ActivityReport.Empty);
    }
}
