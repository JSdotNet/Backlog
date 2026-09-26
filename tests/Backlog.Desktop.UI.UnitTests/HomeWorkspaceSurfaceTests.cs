using AngleSharp.Dom;
using Backlog.Modules.Capture.Abstractions.Services;
using Backlog.Modules.Capture.Extensions;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI;
using Backlog.Modules.DevPc.UI;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;
using Backlog.Desktop.UI.Tasks;
using Backlog.SharedKernel.Ai;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The shell shows one surface at a time. The Roadmap, Tools and the Dashboard
/// are whole domains rather than panes: opening one takes the screen and hides
/// every other domain concern, and closing it puts the reader back exactly where
/// they were. The session list is the Dashboard's second
/// tab rather than a surface of its own, so it is reached through the Dashboard
/// segment and then the tab strip.
/// <para>
/// That last part is worth a test even though no code implements it. The surface
/// is a field of its own and the pane selection is never touched to open one, so
/// the selection is simply revealed again rather than saved and restored. A future
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
    /// The header configures what is on screen, so during a takeover the strip
    /// that configures the workspace has nothing to act on and is not offered.
    /// The pane selection underneath is untouched: the strip comes back with the
    /// same pane pressed that it left with.
    /// </summary>
    [Fact]
    public void A_takeover_takes_the_sections_strip_out_of_the_header_and_the_way_back_restores_it()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='global-pane-multiselect']")));

        foreach (var surface in new[] { "roadmap", "tools", "dashboard", "sessions" })
        {
            component.Find($"[data-testid='{surface}-toggle-button']").Click();

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll($"[data-testid='{surface}-surface']"));
                Assert.Empty(component.FindAll("[data-testid='global-pane-multiselect']"));
                Assert.Empty(component.FindAll("[data-testid='backlog-pane-option']"));
                Assert.Empty(component.FindAll("[data-testid='devbook-pane-option']"));

                // The switcher is what brings the reader back, so it stays.
                Assert.NotEmpty(component.FindAll("[data-testid='workspace-surface-switcher']"));
            });

            component.Find("[data-testid='workspace-surface-option']").Click();

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='global-pane-multiselect']"));
                Assert.Equal("true", component.Find("[data-testid='backlog-pane-option']").GetAttribute("aria-pressed"));
            });
        }
    }

    /// <summary>
    /// Ask AI answers from whichever area is open, so it stays offered across a
    /// takeover — the Dashboard is an area too — and while the task list is closed
    /// in favour of another pane. An open panel goes with its button and comes back
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

        component.Find("[data-testid='workspace-surface-option']").Click();

        // Devbook alone on screen: the list is closed, and the devbook answers.
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']"));
            Assert.Equal("Answers from the Devbook content.", component.Find(".ai-panel__body").TextContent.Trim());
        });
    }

    /// <summary>The Inbox is an area like the others: with it open and the task
    /// list closed, the button is still there and the Inbox is what answers.</summary>
    [Fact]
    public void Ask_ai_is_offered_with_the_inbox_open_and_the_task_list_closed()
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
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']"));
        });

        component.Find("[data-testid='ai-toggle-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='ai-scope']"));
            Assert.Equal("Answers from the Inbox content.", component.Find(".ai-panel__body").TextContent.Trim());
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

            // Two chips, in the shell's fixed order — the panes left to right — and
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

        string[] surfaces = ["tools", "dashboard", "sessions", "tools"];

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
    /// Switching to a pane has to be remembered, not just shown, or a fresh shell
    /// instance — a relaunch, or the Router rebuilding this component after a trip
    /// to Settings — has nothing to reopen on.
    /// </summary>
    [Fact]
    public void Switching_to_a_pane_remembers_it_for_next_time()
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
    /// <c>GlobalPaneSelection</c> field initializer used to reopen on Backlog no
    /// matter what the reader had been looking at. A brand-new shell instance backed
    /// by the same store is the same situation without needing an actual Settings
    /// round trip in the test.
    /// </summary>
    [Fact]
    public void A_fresh_shell_instance_reopens_on_the_pane_that_was_last_open()
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
                Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// Two panes the reader had open beside each other come back beside each other:
    /// the restore is the arrangement, not just the last pane pressed.
    /// </summary>
    [Fact]
    public void A_fresh_shell_instance_restores_two_open_panes()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            shellNavigation.SetLastPanes(["Backlog", "Devbook"]);

            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
                Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
                Assert.Equal("true", component.Find("[data-testid='backlog-pane-option']").GetAttribute("aria-pressed"));
                Assert.Equal("true", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
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
    /// <para>
    /// Devbook is opened with Ctrl held, because a plain press would switch to it and
    /// close Tasks. The two-pane arrangement this test is about is something the
    /// reader has to ask for, and the modifier is how.
    /// </para>
    /// </summary>
    [Fact]
    public void Closing_a_surface_restores_the_workspace_with_the_pane_selection_unchanged()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        component.Find("[data-testid='devbook-pane-option']").Click(new MouseEventArgs { CtrlKey = true });

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

    [Fact]
    public void The_roadmap_takes_the_screen_in_place_of_the_panes()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenTheRoadmap(component);

        component.WaitForAssertion(() =>
        {
            var surface = component.Find("[data-testid='roadmap-surface']");
            Assert.Equal("MAIN", surface.TagName);
            Assert.NotNull(surface.QuerySelector("[data-testid='roadmap-band']"));

            // Never beside the task list: the workspace, and every pane with it, is gone.
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
            Assert.Empty(component.FindAll("[data-testid='devbook-layout']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Empty(component.FindAll("[data-testid='global-pane-multiselect']"));

            Assert.Equal("true", component.Find("[data-testid='roadmap-toggle-button']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='workspace-surface-option']").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void The_roadmap_is_absent_when_its_feature_is_off()
    {
        using var harness = CreateHarness(features => features.SetEnabled(RoadmapFeatures.Roadmap, false));
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            // No option offering it: the flag decides whether the option exists.
            Assert.Empty(component.FindAll("[data-testid='roadmap-toggle-button']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));

            Assert.NotEmpty(component.FindAll("[data-testid='global-pane-multiselect']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-layout']"));
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
    /// Closing is the ✕ inside the pane as well as the header switcher, which is
    /// what keeps the header on screen while a takeover is open.
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
    /// The roadmap is offered as one more surface: its option sits in the surface
    /// switcher, loose like its neighbours, unpressed until the reader chooses it, and
    /// never blocked — it has no capacity rule, because it competes with nothing for
    /// width. The shell opens on the workspace, so the roadmap is off screen until
    /// asked for.
    /// </summary>
    [Fact]
    public void The_roadmap_option_starts_unpressed_in_the_surface_switcher()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            var option = component.Find("[data-testid='roadmap-toggle-button']");

            Assert.Equal("false", option.GetAttribute("aria-pressed"));
            Assert.Equal("roadmap-surface", option.GetAttribute("aria-controls"));
            Assert.Equal("Roadmap", LabelWithoutFlag(option));
            Assert.False(option.HasAttribute("disabled"));

            Assert.NotNull(option.Closest("[data-testid='workspace-surface-switcher']"));
            Assert.Null(option.Closest("[data-testid='global-pane-multiselect']"));
            Assert.Contains("header-group__option--loose", option.ClassList);

            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
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
    /// Both ways back land on the workspace with the panes the reader left: a second
    /// press on the roadmap's own option, and the Workspace option. The pane selection
    /// was never touched to open the roadmap, so there is nothing to restore.
    /// </summary>
    [Theory]
    [InlineData("roadmap-toggle-button")]
    [InlineData("workspace-surface-option")]
    public void Leaving_the_roadmap_returns_to_the_workspace_as_it_was(string wayBack)
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenTheRoadmap(component);
        component.Find($"[data-testid='{wayBack}']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='roadmap-surface']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));

            Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Equal("true", component.Find("[data-testid='backlog-pane-option']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='roadmap-toggle-button']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// A resize may not touch the roadmap. The pane selection has a viewport-driven
    /// capacity and trims itself to fit; the roadmap is a surface, outside that
    /// selection, so a narrowed window can never close it. Driven through the shell's
    /// own capacity entry point — the path the window's resize listener calls.
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
                Assert.Equal("true", rendered.Find("[data-testid='roadmap-toggle-button']").GetAttribute("aria-pressed"));
            });
    }

    /// <summary>
    /// The roadmap is a surface, not a fourth pane, and nothing may quietly make it
    /// one. Folding it into <see cref="GlobalPane"/> would hand it a viewport-driven
    /// capacity and the "one is always on screen" invariant — a narrowed window would
    /// start evicting the reader's roadmap through <c>TrimToCapacity</c>. A tripwire on
    /// a later tidy-up rather than a test of behaviour, which is why it counts members.
    /// </summary>
    [Fact]
    public void The_global_pane_enum_still_describes_three_panes_and_no_roadmap()
    {
        GlobalPane[] expected = [GlobalPane.Inbox, GlobalPane.Tasks, GlobalPane.Devbook];

        Assert.Equal(expected, Enum.GetValues<GlobalPane>());
    }

    /// <summary>
    /// The feature and the surface are independent, the way they are for every
    /// takeover: turning the feature off puts the reader on the workspace and takes
    /// the option away, and turning it back on returns them to the roadmap they were
    /// on, because the surface field was never reset.
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
            Assert.Empty(component.FindAll("[data-testid='roadmap-toggle-button']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
        });

        _ = features.SetEnabled(RoadmapFeatures.Roadmap, true);

        component.WaitForAssertion(() =>
        {
            Assert.Equal("true", component.Find("[data-testid='roadmap-toggle-button']").GetAttribute("aria-pressed"));
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
    /// Devbook is the released one of the four enabled here; the other three
    /// were moved to Dev and are the control group.</summary>
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

            // Present and flagged — proving the absence above is the status
            // talking rather than the badge being broken everywhere.
            Assert.Single(component.FindAll("[data-testid='roadmap-feature-status']"));
            Assert.Single(component.FindAll("[data-testid='tools-feature-status']"));
            Assert.Single(component.FindAll("[data-testid='dashboard-feature-status']"));
        });
    }

    /// <summary>
    /// Pressing a section is asking to look at it. The pane that was there makes way,
    /// because a reader who wanted both would have said so — with Ctrl held, which is
    /// the fact after this one.
    /// </summary>
    [Fact]
    public void Switching_to_a_pane_closes_the_one_it_replaces()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']")));

        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Equal("false", component.Find("[data-testid='backlog-pane-option']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// "This one too." The modifier press opens the pane beside the open one rather
    /// than in its place — Ctrl here, Cmd on macOS, the convention the repository
    /// scope in the same header already follows. Both options read pressed, because
    /// both panes are on screen.
    /// </summary>
    [Fact]
    public void A_modifier_press_opens_the_pane_beside_the_open_one()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        component.Find("[data-testid='devbook-pane-option']").Click(new MouseEventArgs { CtrlKey = true });

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Equal("true", component.Find("[data-testid='backlog-pane-option']").GetAttribute("aria-pressed"));
            Assert.Equal("true", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void The_meta_key_opens_beside_too()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        component.Find("[data-testid='devbook-pane-option']").Click(new MouseEventArgs { MetaKey = true });

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
        });
    }

    /// <summary>
    /// "This one too" has no meaning for a pane already on screen, so a press on an
    /// open pane closes it whether or not the modifier is held — the same act the
    /// plain press has always been.
    /// </summary>
    [Fact]
    public void Pressing_an_open_pane_closes_it_whatever_the_reader_held()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        component.Find("[data-testid='devbook-pane-option']").Click(new MouseEventArgs { CtrlKey = true });
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']")));

        component.Find("[data-testid='backlog-pane-option']").Click(new MouseEventArgs { CtrlKey = true });

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
        });
    }

    /// <summary>
    /// In a window that fits one pane there is no beside, so the modifier press is
    /// the switch the plain press would have been — and the tooltip stops offering
    /// a modifier that does nothing.
    /// </summary>
    [Fact]
    public async Task A_single_pane_window_turns_the_modifier_press_into_a_switch()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
            Assert.Contains("Ctrl+click", component.Find("[data-testid='devbook-pane-option']").GetAttribute("title")));

        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(1));

        component.WaitForAssertion(() =>
            Assert.DoesNotContain("Ctrl+click", component.Find("[data-testid='devbook-pane-option']").GetAttribute("title")));

        component.Find("[data-testid='devbook-pane-option']").Click(new MouseEventArgs { CtrlKey = true });

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        });
    }

    /// <summary>
    /// The tooltip is the one place the modifier is named, so it says what the press
    /// will do in every state: switch, with the modifier offered; close; or nothing,
    /// because the pane is the last one and its option is disabled to say so.
    /// </summary>
    [Fact]
    public void The_option_tooltip_says_what_the_press_will_do()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            Assert.Equal(
                "Switch to Devbook — Ctrl+click to open it beside the open panes",
                component.Find("[data-testid='devbook-pane-option']").GetAttribute("title"));
            Assert.Equal("Tasks is the only pane open", component.Find("[data-testid='backlog-pane-option']").GetAttribute("title"));
            Assert.True(component.Find("[data-testid='backlog-pane-option']").HasAttribute("disabled"));
        });

        component.Find("[data-testid='devbook-pane-option']").Click(new MouseEventArgs { CtrlKey = true });

        component.WaitForAssertion(() =>
        {
            Assert.Equal("Close Tasks", component.Find("[data-testid='backlog-pane-option']").GetAttribute("title"));
            Assert.Equal("Close Devbook", component.Find("[data-testid='devbook-pane-option']").GetAttribute("title"));
            Assert.False(component.Find("[data-testid='backlog-pane-option']").HasAttribute("disabled"));
        });
    }

    /// <summary>
    /// The strip holds bare pane options only: no rail, no cell, nothing beside or
    /// above an option but the option, and no roadmap option — that is a surface,
    /// not a pane, and sits in the surface switcher. Every one of the three is a direct child of the
    /// group, which is what the fused hairlines key on.
    /// </summary>
    [Fact]
    public void Every_option_stands_bare_in_the_strip()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.InboxPane, true));
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            var options = component.FindAll("[data-testid$='-pane-option']");

            Assert.Equal(3, options.Count);
            Assert.Empty(component.Find("[data-testid='global-pane-multiselect']").QuerySelectorAll("[data-testid='roadmap-toggle-button']"));
            Assert.All(options, option =>
                Assert.Equal("global-pane-multiselect", option.ParentElement?.GetAttribute("data-testid")));
            Assert.Empty(component.FindAll("[data-testid$='-pane-pin']"));
        });
    }

    /// <summary>
    /// A window too narrow for both has to drop one, and it drops the first in the
    /// stable order — Tasks before Devbook — the same order every trim takes. This is
    /// the resize path rather than the switch path.
    /// </summary>
    [Fact]
    public async Task A_narrowed_window_drops_the_first_pane_in_the_stable_order()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        component.Find("[data-testid='devbook-pane-option']").Click(new MouseEventArgs { CtrlKey = true });

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
        });

        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(1));

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
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
        var flag = option.QuerySelector("[class*='badge--feature']")?.TextContent ?? string.Empty;

        return option.TextContent.Replace(flag, string.Empty).Trim();
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

    /// <summary>Presses the Sessions segment and waits for the list to take the
    /// screen.</summary>
    private static void OpenSessions(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='sessions-toggle-button']")));
        component.Find("[data-testid='sessions-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='sessions-panel']")));
    }

    /// <summary>Presses the roadmap's option in the surface switcher and waits for
    /// it to take the screen — through the same affordance the reader has, rather
    /// than by reaching into shell state.</summary>
    private static void OpenTheRoadmap(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='roadmap-toggle-button']")));
        component.Find("[data-testid='roadmap-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']")));
    }

    /// <summary>Opens the Devbook beside the task list with the modifier press, so
    /// two areas are on screen and Ask AI offers a chip for each.</summary>
    private static void OpenTheDevbookBeside(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        component.Find("[data-testid='devbook-pane-option']").Click(new MouseEventArgs { CtrlKey = true });
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.NotEmpty(component.FindAll("[data-testid='ai-scope-devbook']"));
        });
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
        _ = featureSettings.SetEnabled(DevbookFeatures.DevbookSections, true);
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

    /// <summary>Answers every question with the same line, and keeps what it was
    /// asked: which body the shell handed over is the thing the scope tests are
    /// about.</summary>
    private sealed class StubAzureFoundryChatClient : IAzureFoundryChatClient
    {
        public List<AzureFoundryChatRequest> Requests { get; } = [];

        public Task<AzureFoundryChatResponse> AskAsync(AzureFoundryChatRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new AzureFoundryChatResponse("Not used in this test."));
        }

        public Task<AzureFoundryPlanResponse> DraftPlanAsync(AzureFoundryPlanRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AzureFoundryPlanResponse("# Not used in this test."));
    }

    private sealed class StubGitHubClient : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }
}
