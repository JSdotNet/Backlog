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
using Backlog.Modules.Sessions.UI;
using Backlog.SharedKernel.Ai;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The shell shows one surface at a time. Tools and the Dashboard are whole
/// domains rather than panes: opening either takes the screen and hides every
/// other domain concern — the roadmap band included — and closing it puts the
/// reader back exactly where they were. The session list is the Dashboard's second
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
    public void Opening_tools_hides_the_roadmap_band_and_every_pane()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        // The shell opens with the band collapsed, so a test about the takeover
        // removing it has to put it on screen first or it asserts nothing.
        ShowTheBand(component);

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
    public void Opening_the_dashboard_hides_the_roadmap_band_and_every_pane()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        // The shell opens with the band collapsed, so a test about the takeover
        // removing it has to put it on screen first or it asserts nothing.
        ShowTheBand(component);

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
    /// The session list is reached through the Dashboard: its segment, then the
    /// Sessions tab. Switching tabs swaps which module's pane is in front and
    /// nothing else — the surface, the landmark and the hidden workspace are the
    /// same as on the overview.
    /// </summary>
    [Fact]
    public void Opening_the_sessions_tab_shows_the_session_list_in_the_dashboard_surface()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        // The shell opens with the band collapsed, so a test about the takeover
        // removing it has to put it on screen first or it asserts nothing.
        ShowTheBand(component);

        OpenTheSessionsTab(component);

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-surface'] [data-testid='sessions-panel']"));
            Assert.Empty(component.FindAll("[data-testid='dashboard-panel']"));
            Assert.Empty(component.FindAll("[data-testid='devbook-layout']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Single(component.FindAll("main"));

            var tab = component.Find("[data-testid='dashboard-sessions-tab']");
            Assert.Equal("true", tab.GetAttribute("aria-selected"));
            Assert.Equal("false", component.Find("[data-testid='dashboard-overview-tab']").GetAttribute("aria-selected"));
        });
    }

    /// <summary>
    /// The Dashboard opens on its overview, with the strip offering both tabs:
    /// two tabs, one strip, and the overview in front until the reader asks
    /// otherwise.
    /// </summary>
    [Fact]
    public void The_dashboard_opens_on_its_overview_with_both_tabs_offered()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.Find("[data-testid='dashboard-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            var strip = component.Find("[data-testid='dashboard-tabs']");
            Assert.Equal("tablist", strip.GetAttribute("role"));
            Assert.Equal("NAV", strip.TagName);
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-overview-tab']"));
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-sessions-tab']"));
            Assert.Equal("true", component.Find("[data-testid='dashboard-overview-tab']").GetAttribute("aria-selected"));

            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-panel']"));
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

        foreach (var surface in new[] { "tools", "dashboard" })
        {
            component.Find($"[data-testid='{surface}-toggle-button']").Click();

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll($"[data-testid='{surface}-surface']"));
                Assert.Empty(component.FindAll("[data-testid='global-pane-multiselect']"));
                Assert.Empty(component.FindAll("[data-testid='roadmap-band-toggle']"));
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

        // The Sessions tab is the other context on the same surface, and it is the
        // area that answers while it is in front.
        component.Find("[data-testid='dashboard-sessions-tab']").Click();
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

        ShowTheBand(component);

        component.WaitForAssertion(() =>
        {
            var group = component.Find("[data-testid='ai-scope']");
            Assert.Equal("group", group.GetAttribute("role"));
            Assert.Equal("Ask about", group.GetAttribute("aria-label"));

            // Two chips, in the shell's fixed order — the panes, then the band —
            // and the band, opened last, is the one pressed.
            Assert.Equal("false", component.Find("[data-testid='ai-scope-tasks']").GetAttribute("aria-pressed"));
            Assert.Equal("true", component.Find("[data-testid='ai-scope-roadmap']").GetAttribute("aria-pressed"));
            Assert.Equal("Answers from the content of the area chosen above.", component.Find(".ai-panel__body").TextContent.Trim());
        });

        component.Find("[data-testid='ai-scope-tasks']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("true", component.Find("[data-testid='ai-scope-tasks']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='ai-scope-roadmap']").GetAttribute("aria-pressed"));
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
        ShowTheBand(component);
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("[data-testid='ai-scope-roadmap']").GetAttribute("aria-pressed")));

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
        ShowTheBand(component);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-scope-roadmap']")));
        component.Find("[data-testid='ai-scope-roadmap']").Click();
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("[data-testid='ai-scope-roadmap']").GetAttribute("aria-pressed")));

        component.Find("[data-testid='roadmap-band-toggle']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
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
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.AiAssistant, true));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();
        ShowTheBand(component);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-scope-tasks']")));

        component.Find("[data-testid='ai-scope-tasks']").Click();
        component.Find("[data-testid='ai-question-input']").Input("First?");
        component.Find("[data-testid='ai-ask-button']").Click();
        component.WaitForAssertion(() => Assert.Single(harness.FoundryChat.Requests));

        component.Find("[data-testid='ai-scope-roadmap']").Click();
        component.Find("[data-testid='ai-ask-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(2, harness.FoundryChat.Requests.Count);
            Assert.StartsWith("Tasks: ", harness.FoundryChat.Requests[0].Content, StringComparison.Ordinal);
            Assert.StartsWith("Roadmap: ", harness.FoundryChat.Requests[1].Content, StringComparison.Ordinal);
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
        ShowTheBand(component);
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("[data-testid='ai-scope-roadmap']").GetAttribute("aria-pressed")));

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
    /// One field with four states, so opening a takeover is the same act as closing
    /// whichever one was already there. There is no arrangement in which two are on
    /// screen, and the loop ends where it started to prove that reopening the first
    /// one is the same act rather than a special case.
    /// </summary>
    [Fact]
    public void No_two_surfaces_can_be_open_at_the_same_time()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        string[] surfaces = ["tools", "dashboard", "tools"];

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

            // The tab travels with the surface, under the name the sessions list has
            // always been remembered by — so a file written by a build where it was
            // a surface of its own reads the same either way.
            component.Find("[data-testid='dashboard-sessions-tab']").Click();
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
                Assert.NotEmpty(component.FindAll("[data-testid='dashboard-surface']"));
                Assert.NotEmpty(component.FindAll("[data-testid='sessions-panel']"));
                Assert.Equal("true", component.Find("[data-testid='dashboard-sessions-tab']").GetAttribute("aria-selected"));
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
    public void The_roadmap_band_renders_above_the_panes_once_it_is_shown()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        ShowTheBand(component);

        component.WaitForAssertion(() =>
        {
            var workspace = component.Find("[data-testid='workspace']");
            Assert.DoesNotContain("workspace--no-roadmap", workspace.GetAttribute("class"));

            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band-content']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-layout']"));
        });

        // The band above, the panes below — the order the layout depends on.
        var bandAt = component.Markup.IndexOf("data-testid=\"roadmap-band\"", StringComparison.Ordinal);
        var panesAt = component.Markup.IndexOf("data-testid=\"devbook-layout\"", StringComparison.Ordinal);

        Assert.True(bandAt >= 0 && panesAt > bandAt);
    }

    [Fact]
    public void The_roadmap_band_is_absent_when_its_feature_is_off()
    {
        using var harness = CreateHarness(features => features.SetEnabled(RoadmapFeatures.Roadmap, false));
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));

            // And no option offering one. The flag decides whether the option exists;
            // the option decides whether the band is on screen.
            Assert.Empty(component.FindAll("[data-testid='roadmap-band-toggle']"));

            // The strip itself stays, because the panes still need it.
            Assert.NotEmpty(component.FindAll("[data-testid='global-pane-multiselect']"));

            // No band means no empty track left where it would have been.
            var workspace = component.Find("[data-testid='workspace']");
            Assert.Contains("workspace--no-roadmap", workspace.GetAttribute("class"));

            Assert.NotEmpty(component.FindAll("[data-testid='devbook-layout']"));
        });
    }

    [Fact]
    public void A_surface_whose_feature_is_switched_off_falls_back_to_the_workspace()
    {
        // Both features that share the Dashboard surface, or the segment would
        // still be offered for the one still on.
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
    /// The Dashboard surface is shared by two features, and the segment is offered
    /// while either has something to show. With only the overview off, the segment
    /// opens straight onto the session list with no strip — one tab is no choice.
    /// </summary>
    [Fact]
    public void With_only_the_dashboard_feature_off_the_segment_opens_the_session_list_alone()
    {
        using var harness = CreateHarness(features => features.SetEnabled(DashboardFeatures.Dashboard, false));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='dashboard-toggle-button']")));
        component.Find("[data-testid='dashboard-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='sessions-panel']"));
            Assert.Empty(component.FindAll("[data-testid='dashboard-panel']"));
            Assert.Empty(component.FindAll("[data-testid='dashboard-tabs']"));
        });
    }

    /// <summary>
    /// The whole point of the flag: with it off there is no way in and nothing to
    /// find. One key gates both halves — whether the dashboard offers the tab, and
    /// whether the list may be shown — so this is the same assertion twice on
    /// purpose. The Dashboard itself is untouched, and with one tab left it draws
    /// no strip.
    /// </summary>
    [Fact]
    public void With_the_sessions_feature_off_there_is_no_tab_and_no_list()
    {
        using var harness = CreateHarness(features => features.SetEnabled(SessionFeatures.Sessions, false));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='dashboard-toggle-button']")));
        component.Find("[data-testid='dashboard-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-panel']"));
            Assert.Empty(component.FindAll("[data-testid='dashboard-tabs']"));
            Assert.Empty(component.FindAll("[data-testid='dashboard-sessions-tab']"));
            Assert.Empty(component.FindAll("[data-testid='sessions-panel']"));

            // The other takeover is untouched: one flag, one area.
            Assert.NotEmpty(component.FindAll("[data-testid='tools-toggle-button']"));
        });
    }

    /// <summary>
    /// A remembered Sessions tab whose feature has since gone off must not leave the
    /// Dashboard blank: the overview is what is left, so the overview is shown.
    /// </summary>
    [Fact]
    public void A_remembered_sessions_tab_falls_back_to_the_overview_when_its_feature_is_off()
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
                Assert.NotEmpty(component.FindAll("[data-testid='dashboard-surface']"));
                Assert.NotEmpty(component.FindAll("[data-testid='dashboard-panel']"));
                Assert.Empty(component.FindAll("[data-testid='sessions-panel']"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// Closing the surface and coming back lands on the tab the reader left — the
    /// tab is part of where they were, not a setting the ✕ resets.
    /// </summary>
    [Fact]
    public void Closing_the_dashboard_keeps_its_tab_for_the_way_back()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenTheSessionsTab(component);

        component.Find("[data-testid='workspace-surface-option']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='workspace']")));

        component.Find("[data-testid='dashboard-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='sessions-panel']"));
            Assert.Equal("true", component.Find("[data-testid='dashboard-sessions-tab']").GetAttribute("aria-selected"));
        });
    }

    /// <summary>
    /// Closing is the ✕ inside the pane as well as the header switcher, which is
    /// what keeps the header on screen while a takeover is open. The ✕ on the
    /// sessions list closes the whole surface — it is not a way back to the
    /// overview tab.
    /// </summary>
    [Fact]
    public void The_sessions_pane_closes_the_dashboard_back_to_the_workspace()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenTheSessionsTab(component);

        component.Find("[data-testid='sessions-panel'] button.btn--ghost").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='dashboard-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-layout']"));
        });
    }

    /// <summary>
    /// The band ships collapsed, and its header option says so. Nothing on the band
    /// itself controls it: the only affordance is the option in the header strip,
    /// pointed at the band's landmark through <c>aria-controls</c>, so there is one
    /// affordance for one thing rather than a header option and a chevron competing.
    /// <para>
    /// The option is what proves the band is collapsed rather than missing. An absent
    /// band with no option would be the feature being off; an absent band with an
    /// unpressed option offering it is the default this test pins.
    /// </para>
    /// </summary>
    [Fact]
    public void The_roadmap_band_starts_collapsed_and_its_header_option_is_unpressed()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            var option = component.Find("[data-testid='roadmap-band-toggle']");

            Assert.Equal("false", option.GetAttribute("aria-pressed"));
            Assert.Equal("roadmap-band", option.GetAttribute("aria-controls"));
            // The label, with the maturity flag taken back out: Roadmap is a Dev
            // feature and is on in this harness, so the option now holds a badge
            // as well as its name.
            Assert.Equal("Roadmap", LabelWithoutFlag(option));

            // No capacity rule, so unlike the three panes it is never blocked.
            Assert.False(option.HasAttribute("disabled"));

            // Its own control, outside the panes strip: on or off, touching no pane,
            // so it wears the loose shape rather than sitting fused among options
            // that switch. It comes first because the band is above the panes on
            // screen.
            Assert.Null(option.Closest("[data-testid='global-pane-multiselect']"));
            Assert.Contains("header-group__option--loose", option.ClassList);
            Assert.Equal("NAV", option.ParentElement?.TagName);
            Assert.Equal("global-pane-multiselect", option.NextElementSibling?.GetAttribute("data-testid"));

            // Nothing of the band on screen, and no empty track where it would go.
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band-content']"));
            Assert.Contains("workspace--no-roadmap",
                component.Find("[data-testid='workspace']").GetAttribute("class"));
        });

        ShowTheBand(component);

        component.WaitForAssertion(() =>
        {
            var band = component.Find("[data-testid='roadmap-band']");

            Assert.Equal("true", component.Find("[data-testid='roadmap-band-toggle']").GetAttribute("aria-pressed"));
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band-content']"));
            Assert.Equal("Planning", band.QuerySelector(".roadmap-band__eyebrow")!.TextContent.Trim());
            Assert.Equal("Roadmap", component.Find("#roadmap-band-title").TextContent.Trim());
        });
    }

    /// <summary>
    /// Hiding is binary: the band leaves the DOM entirely and its grid track goes
    /// with it, through the same <c>workspace--no-roadmap</c> variant the feature flag
    /// uses. There is no smaller band left behind, so there is no second grid variant
    /// and nothing on screen to fold.
    /// </summary>
    [Fact]
    public void Turning_the_roadmap_option_off_removes_the_band_and_its_track()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        ShowTheBand(component);
        component.Find("[data-testid='roadmap-band-toggle']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band-content']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band-empty-state']"));

            Assert.Contains("workspace--no-roadmap",
                component.Find("[data-testid='workspace']").GetAttribute("class"));

            // The option stays, unpressed and enabled, because it is the way back.
            var option = component.Find("[data-testid='roadmap-band-toggle']");

            Assert.Equal("false", option.GetAttribute("aria-pressed"));
            Assert.False(option.HasAttribute("disabled"));

            // The panes are untouched: the band was never competing with them.
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-layout']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
        });
    }

    [Fact]
    public void Turning_the_roadmap_option_on_again_restores_the_band()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        ShowTheBand(component);

        component.Find("[data-testid='roadmap-band-toggle']").Click();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='roadmap-band']")));

        component.Find("[data-testid='roadmap-band-toggle']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band-content']"));
            Assert.Equal("true", component.Find("[data-testid='roadmap-band-toggle']").GetAttribute("aria-pressed"));

            Assert.DoesNotContain("workspace--no-roadmap",
                component.Find("[data-testid='workspace']").GetAttribute("class"));
        });
    }

    /// <summary>
    /// A resize may not touch the band, and this is the guarantee that keeping it out
    /// of <see cref="GlobalPaneSelection"/> buys. That selection has a viewport-driven
    /// capacity and trims itself to fit; a band folded into it would be evictable by
    /// window width, which is a horizontal rule applied to a horizontal row that
    /// competes with nothing. The resize is driven through the shell's own capacity
    /// entry point — the path the window's resize listener calls from JavaScript —
    /// because that is the only thing a resize gets to say here.
    /// </summary>
    [Fact]
    public async Task A_window_resize_never_changes_whether_the_band_is_shown()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        ShowTheBand(component);

        // Shown: down to a single-pane window and back again.
        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(1));
        AssertShown(component);

        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(3));
        AssertShown(component);

        // And hidden, which is the direction a capacity trim could not accidentally
        // get right: the band has to stay gone rather than reappearing on a resize.
        component.Find("[data-testid='roadmap-band-toggle']").Click();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='roadmap-band']")));

        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(1));
        AssertHidden(component);

        await component.InvokeAsync(() => component.Instance.SetGlobalPaneCapacityAsync(3));
        AssertHidden(component);

        static void AssertShown(IRenderedComponent<Home> rendered) =>
            rendered.WaitForAssertion(() =>
            {
                Assert.NotEmpty(rendered.FindAll("[data-testid='roadmap-band']"));
                Assert.Equal("true", rendered.Find("[data-testid='roadmap-band-toggle']").GetAttribute("aria-pressed"));
                Assert.DoesNotContain("workspace--no-roadmap",
                    rendered.Find("[data-testid='workspace']").GetAttribute("class"));
            });

        static void AssertHidden(IRenderedComponent<Home> rendered) =>
            rendered.WaitForAssertion(() =>
            {
                Assert.Empty(rendered.FindAll("[data-testid='roadmap-band']"));
                Assert.Equal("false", rendered.Find("[data-testid='roadmap-band-toggle']").GetAttribute("aria-pressed"));
                Assert.Contains("workspace--no-roadmap",
                    rendered.Find("[data-testid='workspace']").GetAttribute("class"));
            });
    }

    /// <summary>
    /// The band is a row above the panes, not a fourth pane, and nothing may quietly
    /// make it one. Folding it into <see cref="GlobalPane"/> would hand it a
    /// viewport-driven capacity and the "one is always on screen" invariant, neither
    /// of which is true of a horizontal band — a narrowed window would start evicting
    /// the reader's roadmap through <c>TrimToCapacity</c>. This is a tripwire on a
    /// later tidy-up rather than a test of behaviour, which is why it counts members
    /// rather than exercising anything.
    /// </summary>
    [Fact]
    public void The_global_pane_enum_still_describes_three_panes_and_no_band()
    {
        GlobalPane[] expected = [GlobalPane.Inbox, GlobalPane.Tasks, GlobalPane.Devbook];

        Assert.Equal(expected, Enum.GetValues<GlobalPane>());
    }

    /// <summary>
    /// The fold survives a takeover, and this is the test that makes shell-owned
    /// visibility load-bearing rather than a preference.
    /// <para>
    /// A takeover does not hide the band, it removes it:
    /// <see cref="Opening_tools_hides_the_roadmap_band_and_every_pane"/> asserts the
    /// band is absent from the DOM, not merely off screen. So <c>RoadmapBand</c> is
    /// disposed on the way in and constructed afresh on the way out, and a private
    /// <c>bool</c> inside it would come back at its default — collapsed — on every
    /// round trip. Nothing in the markup would look wrong; the reader would just find
    /// the band gone each time they came out of Tools, having asked for it. Holding
    /// the state on the shell, which outlives the band, is what prevents that, and
    /// this pins it.
    /// </para>
    /// <para>
    /// Shown rather than hidden is the state worth pinning now that the band ships
    /// collapsed: hidden is the default, so a band holding its own field would come
    /// back hidden and the assertion would pass for the wrong reason.
    /// </para>
    /// </summary>
    [Fact]
    public void A_shown_band_stays_shown_across_a_takeover_round_trip()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        ShowTheBand(component);

        // In: the pane layout that would have held the band goes with it.
        component.Find("[data-testid='tools-toggle-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='tools-surface']"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
        });

        // Out, through the header toggle rather than the in-surface ✕ — either closes
        // it, and the other round-trip test above covers the ✕.
        component.Find("[data-testid='tools-toggle-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='tools-surface']"));

            // The workspace is back and the band with it, drawn from state that was
            // never the band's own.
            Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Equal("true", component.Find("[data-testid='roadmap-band-toggle']").GetAttribute("aria-pressed"));

            Assert.DoesNotContain("workspace--no-roadmap",
                component.Find("[data-testid='workspace']").GetAttribute("class"));
        });
    }

    /// <summary>
    /// The feature flag and the header option are independent. The flag decides
    /// whether there is an option at all; the option decides whether the band is on
    /// screen. So switching the flag off leaves the option's state retained but
    /// unobservable, and switching it back on restores what the reader left rather
    /// than resetting it to the collapsed default.
    /// </summary>
    [Fact]
    public void The_bands_visibility_survives_its_feature_going_off_and_back_on()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var features = (AppFeatureSettingsStore)harness.Context.Services.GetRequiredService<IAppFeatureSettings>();

        ShowTheBand(component);

        _ = features.SetEnabled(RoadmapFeatures.Roadmap, false);

        component.WaitForAssertion(() =>
        {
            // The option goes with the feature: no band to offer, so nothing to offer.
            Assert.Empty(component.FindAll("[data-testid='roadmap-band-toggle']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));

            Assert.Contains("workspace--no-roadmap",
                component.Find("[data-testid='workspace']").GetAttribute("class"));
        });

        _ = features.SetEnabled(RoadmapFeatures.Roadmap, true);

        component.WaitForAssertion(() =>
        {
            // Back to shown, which is where the reader left it — not to the default.
            Assert.Equal("true", component.Find("[data-testid='roadmap-band-toggle']").GetAttribute("aria-pressed"));
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));

            Assert.DoesNotContain("workspace--no-roadmap",
                component.Find("[data-testid='workspace']").GetAttribute("class"));
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
    /// above an option but the option, and no roadmap toggle — that is a band, not a
    /// pane, and stands outside. Every one of the three is a direct child of the
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
            Assert.Empty(component.Find("[data-testid='global-pane-multiselect']").QuerySelectorAll("[data-testid='roadmap-band-toggle']"));
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

    /// <summary>The Dashboard segment, then its Sessions tab: the two presses that
    /// put the session list on screen, and the only way there.</summary>
    private static void OpenTheSessionsTab(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='dashboard-toggle-button']")));
        component.Find("[data-testid='dashboard-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='dashboard-sessions-tab']")));
        component.Find("[data-testid='dashboard-sessions-tab']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='sessions-panel']")));
    }

    /// <summary>Presses the header option and waits for the band to arrive. The
    /// shell opens collapsed, so every test about a band on screen starts here —
    /// through the same affordance the reader has, rather than by reaching into
    /// shell state.</summary>
    private static void ShowTheBand(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band-toggle']")));
        component.Find("[data-testid='roadmap-band-toggle']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']")));
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

    private static Harness CreateHarness(
        Action<AppFeatureSettingsStore>? configureFeatures = null,
        ShellNavigationStore? shellNavigation = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-workspace-surface-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
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
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentSessionCatalog.Empty);
    }

    private sealed class ThrowingSource(string areaKey, string areaTitle) : IAiContentSource
    {
        public string AreaKey => areaKey;

        public string AreaTitle => areaTitle;

        public Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default) =>
            throw new IOException("The plan file is locked.");
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
