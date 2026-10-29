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
using Backlog.Modules.Progress.UI;
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
/// view the view switch picks — Tasks or Roadmap — alone. Tools, the Dashboard,
/// Sessions, Pull requests, the Inbox and the Devbook are takeovers: opening one
/// takes the screen and hides every other domain concern, and closing it puts the
/// reader back exactly where they were.
/// <para>
/// That last part is worth a test even though no code implements it. The surface
/// is a field of its own and the view is not touched to open one, so it is simply
/// revealed again rather than saved and restored. A future change that folds the
/// surfaces into the view would pass every markup assertion and quietly break this.
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
            Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
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
            Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
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
            Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
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

    /// <summary>
    /// A popped-out window's Show in main window (local ADR 0041 §4) reaches this
    /// shell through <see cref="MainWindowSurfaces"/>: the main window shows the
    /// surface it names, and answers that it did. A surface it cannot show is
    /// answered no, and a shell that is gone is no longer asked.
    /// </summary>
    [Fact]
    public async Task Show_in_main_window_from_a_popped_out_window_shows_that_surface_here()
    {
        var mainWindow = new MainWindowSurfaces();
        using var harness = CreateHarness(configureServices: services => services.AddSingleton(mainWindow));
        var component = Render(harness);

        OpenTheRoadmap(component);
        Assert.True(mainWindow.Attached);

        Assert.True(await mainWindow.ShowAsync(new SurfaceRequest("devbook", new Dictionary<string, string>())));
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-surface']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
        });

        Assert.False(await mainWindow.ShowAsync(new SurfaceRequest("no-such-surface", new Dictionary<string, string>())));

        await harness.Context.DisposeComponentsAsync();
        Assert.False(mainWindow.Attached);
    }

    /// <summary>
    /// The Devbook's ⧉ Open in new window button (local ADR 0041 §4) asks the host
    /// for one more window on every press, and hands that window the one thing of
    /// this window's the Devbook reads by: its scope. This window stays as it was.
    /// </summary>
    [Fact]
    public void The_devbooks_pop_out_hands_its_scope_over_and_every_press_opens_another_window()
    {
        var windows = new OpeningShellWindows();
        using var harness = CreateHarness(configureServices: services => services.AddSingleton<IShellWindows>(windows));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.Single(component.FindAll("[data-testid='devbook-pop-out']")));

        var item = component.Find("[data-testid='devbook-header-item']");
        Assert.Contains("header-surface-item", item.GetAttribute("class"), StringComparison.Ordinal);
        Assert.NotNull(item.QuerySelector("[data-testid='devbook-pane-option']"));

        var popOut = component.Find("[data-testid='devbook-pop-out']");
        Assert.Equal("BUTTON", popOut.TagName);
        Assert.Equal("Open Devbook in a new window", popOut.GetAttribute("aria-label"));

        // Scoped to the one repository, so the Devbook here reads it — and so does
        // the window the ⧉ opens.
        var alias = Assert.Single(harness.Context.Services.GetRequiredService<GitHubSettingsStore>().Current.Repositories).Alias;
        component.Find("[data-testid='repository-filter-option']").Click();
        popOut = component.Find("[data-testid='devbook-pop-out']");
        Assert.Equal(popOut.GetAttribute("aria-label"), popOut.GetAttribute("title"));
        Assert.Equal("⧉", popOut.QuerySelector("[aria-hidden='true']")!.TextContent);

        popOut.Click();
        component.Find("[data-testid='devbook-pop-out']").Click();

        Assert.Equal(2, windows.Opened.Count);
        Assert.All(windows.Opened, opened =>
        {
            Assert.Equal("devbook", opened.SurfaceId);
            // The scope this window hands its Devbook, and nothing else of this window's.
            Assert.Equal([DevbookSurfaceQuery.Scope], opened.Query.Keys);
            Assert.Equal(alias, opened.Query[DevbookSurfaceQuery.Scope]);
        });

        // This window is unchanged: the Devbook did not take its screen.
        Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
        Assert.Equal("false", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
    }

    /// <summary>
    /// Every header item the window draws for a catalog surface pops out, not the
    /// Devbook alone (local ADR 0041 §4, "each shell header item"): the Inbox, the
    /// views, the work in progress and the other takeovers each stand in an item of
    /// their own beside a ⧉ named as the option is. A press opens a window on that
    /// surface with no query — only the Devbook hands a scope over — and this window
    /// keeps its view, with nothing taking its screen.
    /// </summary>
    [Fact]
    public void Every_header_item_pops_out_its_own_surface_and_leaves_this_window_as_it_was()
    {
        var windows = new OpeningShellWindows();
        using var harness = CreateHarness(
            features => features.SetEnabled(AppFeatures.InboxPane, true),
            configureServices: services => services.AddSingleton<IShellWindows>(windows));
        var component = Render(harness);

        (string Surface, string Option, string Name)[] items =
        [
            ("inbox", "inbox-pane-option", "Inbox"),
            ("tasks", "tasks-view-option", "Tasks"),
            ("board", "board-view-option", "Board"),
            ("calendar", "calendar-view-option", "Calendar"),
            ("roadmap", "roadmap-view-option", "Roadmap"),
            ("devbook", "devbook-pane-option", "Devbook"),
            ("goals", "goals-view-option", "Goals"),
            ("sessions", "sessions-toggle-button", "Sessions"),
            ("pull-requests", "pull-requests-toggle-button", "Pull requests"),
            ("dashboard", "dashboard-toggle-button", "Dashboard"),
            ("tools", "tools-toggle-button", "Tools"),
        ];

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pop-out']")));

        foreach (var (surface, option, name) in items)
        {
            var item = component.Find($"[data-testid='{surface}-header-item']");
            Assert.Contains("header-surface-item", item.ClassList);
            Assert.Equal(option, item.FirstElementChild?.GetAttribute("data-testid"));

            var popOut = item.QuerySelector($"[data-testid='{surface}-pop-out']");
            Assert.NotNull(popOut);
            Assert.Equal("BUTTON", popOut!.TagName);
            Assert.Equal($"Open {name} in a new window", popOut.GetAttribute("aria-label"));
            Assert.Equal(popOut.GetAttribute("aria-label"), popOut.GetAttribute("title"));
        }

        // Nothing the nav draws goes without one: every option stands in an item
        // that carries its ⧉, and there is one ⧉ per item.
        var options = component.FindAll(".app-header__nav .header-group__option");
        Assert.Equal(items.Length, options.Count);
        Assert.All(options, option =>
            Assert.NotNull(option.Closest(".header-surface-item")?.QuerySelector(".header-surface-item__pop")));
        Assert.Equal(items.Length, component.FindAll(".header-surface-item__pop").Count);

        component.Find("[data-testid='roadmap-pop-out']").Click();
        component.Find("[data-testid='tasks-pop-out']").Click();
        component.Find("[data-testid='inbox-pop-out']").Click();

        Assert.Equal(["roadmap", "tasks", "inbox"], windows.Opened.Select(opened => opened.SurfaceId));
        Assert.All(windows.Opened, opened => Assert.Empty(opened.Query));

        // This window is unchanged: Tasks is still its view, and no takeover took it.
        Assert.Equal("true", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
        Assert.Equal("false", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
        Assert.Equal("false", component.Find("[data-testid='inbox-pane-option']").GetAttribute("aria-pressed"));
        Assert.Empty(component.FindAll("[data-testid='inbox-surface']"));
    }

    /// <summary>A host that opens no windows offers no ⧉ at all.</summary>
    [Fact]
    public void A_host_with_no_windows_to_open_offers_no_pop_out()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        // The items are there all the same, so the header keeps one shape per host.
        Assert.NotEmpty(component.FindAll("[data-testid='tasks-header-item']"));
        Assert.Empty(component.FindAll(".header-surface-item__pop"));
    }

    private sealed class OpeningShellWindows : IShellWindows
    {
        public List<(string SurfaceId, IReadOnlyDictionary<string, string> Query)> Opened { get; } = [];

        public void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null) =>
            Opened.Add((surfaceId, query ?? new Dictionary<string, string>()));

        public void Close(string windowKey) => throw new NotSupportedException("Home closes no window.");

        public void SetTitle(string windowKey, string title) => throw new NotSupportedException("Home titles no window.");

        public void ActivateMainWindow() { }
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
    /// The Dashboard is its overview alone: no Dashboard/Sessions tab strip around
    /// it, and no session list beside or behind it. The pane's own section tabs are
    /// inside the pane and are not that strip.
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
    /// During a takeover the view switch and the Devbook toggle stay in the
    /// header — the view options are the way back: no option reads pressed, since
    /// nothing of the workspace is on screen. The view underneath is untouched: the
    /// switch comes back with the same view pressed.
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
                Assert.Equal("Open the Devbook", devbook.GetAttribute("title"));
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
    /// The Devbook takes the whole screen in place of the main view (local ADR 0022,
    /// amendment of 2026-10-15): nothing of the workspace renders beside it, its
    /// option reads pressed and no view does, and a second press returns to the view
    /// that was showing, kept underneath — here the Board.
    /// </summary>
    [Fact]
    public void The_devbook_takes_the_screen_over_the_view_and_a_second_press_returns_to_it()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='board-view-option']")));
        component.Find("[data-testid='board-view-option']").Click();
        component.WaitForAssertion(() =>
            Assert.Equal("Open the Devbook", component.Find("[data-testid='devbook-pane-option']").GetAttribute("title")));

        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            var surface = component.Find("[data-testid='devbook-surface']");
            Assert.Equal("MAIN", surface.TagName);
            Assert.NotNull(surface.QuerySelector("[data-testid='devbook-stack']"));
            Assert.Single(component.FindAll("main"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));

            var option = component.Find("[data-testid='devbook-pane-option']");
            Assert.Equal("true", option.GetAttribute("aria-pressed"));
            Assert.Equal("devbook-surface", option.GetAttribute("aria-controls"));
            Assert.Equal("Back to Board", option.GetAttribute("title"));
            Assert.Equal("false", component.Find("[data-testid='board-view-option']").GetAttribute("aria-pressed"));
        });

        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
            Assert.Empty(component.FindAll("[data-testid='devbook-stack']"));
            Assert.NotEmpty(component.FindAll("[data-testid='workspace'] [data-testid='backlog-pane']"));
            Assert.Equal("true", component.Find("[data-testid='board-view-option']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// Every other way back from the Devbook returns to the view underneath: a view
    /// option, Escape on the surface, Escape heard at the document, and another
    /// takeover's option — which gives that takeover the screen instead, and whose
    /// own way back then shows the view.
    /// </summary>
    [Theory]
    [InlineData("view-option")]
    [InlineData("escape")]
    [InlineData("escape-at-document")]
    [InlineData("another-takeover")]
    public async Task Each_way_back_from_the_devbook_returns_to_the_view_underneath(string wayBack)
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenTheRoadmap(component);
        component.Find("[data-testid='devbook-pane-option']").Click();
        component.WaitForAssertion(() =>
            Assert.True(component.Find("[data-testid='devbook-surface']").HasAttribute("data-escape-closes")));

        switch (wayBack)
        {
            case "view-option":
                component.Find("[data-testid='roadmap-view-option']").Click();
                break;
            case "escape":
                await component.Find("[data-testid='devbook-surface']").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
                break;
            case "escape-at-document":
                await component.InvokeAsync(component.Instance.CloseSurfaceOnEscapeAsync);
                break;
            case "another-takeover":
                component.Find("[data-testid='dashboard-toggle-button']").Click();
                component.WaitForAssertion(() =>
                {
                    Assert.NotEmpty(component.FindAll("[data-testid='dashboard-surface']"));
                    Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
                    Assert.Equal("false", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
                });
                component.Find("[data-testid='dashboard-toggle-button']").Click();
                break;
        }

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
            Assert.Empty(component.FindAll("[data-testid='dashboard-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='workspace'] [data-testid='roadmap-band']"));
            Assert.Equal("true", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// The Devbook closes on its own close button, as every takeover does
    /// (interaction-guidelines.md, "Every way back works"): the ✕ on its header row
    /// returns to the view underneath, and that is remembered.
    /// </summary>
    [Fact]
    public void The_devbooks_close_button_returns_to_the_view_underneath()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='calendar-view-option']")));
            component.Find("[data-testid='calendar-view-option']").Click();
            OpenTheDevbook(component);

            var close = component.Find("[data-testid='devbook-surface'] [data-testid='devbook-close']");
            Assert.Equal("Close the Devbook", close.GetAttribute("aria-label"));
            close.Click();

            component.WaitForAssertion(() =>
            {
                Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
                Assert.NotEmpty(component.FindAll("[data-testid='workspace'] [data-testid='backlog-pane']"));
                Assert.Equal("true", component.Find("[data-testid='calendar-view-option']").GetAttribute("aria-pressed"));
                Assert.Equal("false", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
                Assert.Null(shellNavigation.LastSurface);
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// The Devbook's option during another takeover gives the Devbook the screen in
    /// that takeover's place — takeovers are exclusive — rather than returning to the
    /// workspace first.
    /// </summary>
    [Fact]
    public void Pressing_the_devbook_during_another_takeover_gives_it_the_screen()
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
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-surface'] [data-testid='devbook-stack']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Equal("true", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='tools-toggle-button']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>
    /// The header's nav, left to right, as the design draws it: the Inbox toggle, the
    /// fused view switch, the Devbook toggle; then the fused work in progress —
    /// Goals, Sessions and Pull requests — and the loose Dashboard and Tools. No Tasks pane
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
            // Each option stands in its header item, beside its ⧉ where the host
            // opens windows (local ADR 0041 §4).
            Assert.Equal(["inbox-header-item", "view-switch", "devbook-header-item"], workspace);
            Assert.Equal("inbox-pane-option",
                component.Find("[data-testid='inbox-header-item']").FirstElementChild?.GetAttribute("data-testid"));
            Assert.Equal("devbook-pane-option",
                component.Find("[data-testid='devbook-header-item']").FirstElementChild?.GetAttribute("data-testid"));

            Assert.Equal(
                ["tasks-view-option", "board-view-option", "calendar-view-option", "roadmap-view-option"],
                GroupOptionIds(component, "view-switch"));
            Assert.Equal(
                ["goals-view-option", "sessions-toggle-button", "pull-requests-toggle-button"],
                GroupOptionIds(component, "work-in-progress-switcher"));
            Assert.Equal(
                ["dashboard-toggle-button", "tools-toggle-button"],
                GroupOptionIds(component, "workspace-surface-switcher"));

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

        // The Devbook takes the screen in the takeover's place, and is the one area
        // asked: the task list under it is not on offer.
        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']"));
            Assert.Equal("Answers from the Devbook content.", component.Find(".ai-panel__body").TextContent.Trim());
        });
    }

    /// <summary>The Inbox is an area like the others: with the screen to itself it is
    /// the one area asked, and the task list under it is not on offer.</summary>
    [Fact]
    public void Ask_ai_offers_the_inbox_alone_while_it_has_the_screen()
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
            Assert.Equal("Answers from the Inbox content.", component.Find(".ai-panel__body").TextContent.Trim()));
    }

    /// <summary>
    /// The Devbook is an area like the others: with the screen to itself it is the
    /// one area asked, and its body is what is sent. Back on
    /// the view, the question goes to the view's area again.
    /// </summary>
    [Fact]
    public void Ask_ai_asks_the_devbook_while_it_has_the_screen_and_the_view_after()
    {
        // A fixed Devbook body: the harness has no devbook folder to read, and what
        // is pinned is which source the shell asks, not what the Devbook finds.
        using var harness = CreateHarness(
            features => features.SetEnabled(AppFeatures.AiAssistant, true),
            configureServices: services => services.AddScoped<IAiContentSource>(_ => new FixedSource("devbook", "Devbook")));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();
        OpenTheDevbook(component);

        component.WaitForAssertion(() =>
            Assert.Equal("Answers from the Devbook content.", component.Find(".ai-panel__body").TextContent.Trim()));

        component.Find("[data-testid='ai-question-input']").Input("First?");
        component.Find("[data-testid='ai-ask-button']").Click();
        component.WaitForAssertion(() => Assert.Single(harness.FoundryChat.Requests));

        component.Find("[data-testid='devbook-pane-option']").Click();
        component.WaitForAssertion(() =>
            Assert.Equal("Answers from the Tasks content.", component.Find(".ai-panel__body").TextContent.Trim()));

        component.Find("[data-testid='ai-ask-button']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(2, harness.FoundryChat.Requests.Count);
            Assert.StartsWith("Devbook: ", harness.FoundryChat.Requests[0].Content, StringComparison.Ordinal);
            Assert.StartsWith("Tasks: ", harness.FoundryChat.Requests[1].Content, StringComparison.Ordinal);
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
            component.WaitForAssertion(() => Assert.Equal("dashboard", shellNavigation.LastSurface));

            // The name the sessions list has always been remembered by, so a file
            // written while it was a Dashboard tab reopens on it too.
            component.Find("[data-testid='sessions-toggle-button']").Click();
            component.WaitForAssertion(() => Assert.Equal("sessions", shellNavigation.LastSurface));
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
            component.WaitForAssertion(() => Assert.Equal("tools", shellNavigation.LastSurface));

            component.Find("[data-testid='tools-toggle-button']").Click();

            component.WaitForAssertion(() => Assert.Null(shellNavigation.LastSurface));
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
    /// The Devbook is remembered as the surface, like the other takeovers: a shell
    /// built over the file again — a relaunch, or the Router rebuilding this
    /// component after a trip to Settings — reopens on it over the view it was opened
    /// from, and leaving it is remembered too. Nothing is written as a side pane.
    /// </summary>
    [Fact]
    public void Opening_the_devbook_reopens_it_after_a_restart()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            using (var harness = CreateHarness(shellNavigation: shellNavigation))
            {
                var component = Render(harness);
                OpenTheRoadmap(component);
                OpenTheDevbook(component);
                component.WaitForAssertion(() => Assert.Equal("devbook", shellNavigation.LastSurface));
            }

            Assert.DoesNotContain("lastEnabledPanes", ShellNavigationStoreTests.Written(path), StringComparison.Ordinal);

            var restarted = new ShellNavigationStore(path);
            using var again = CreateHarness(shellNavigation: restarted);
            var reopened = Render(again);

            reopened.WaitForAssertion(() =>
            {
                Assert.NotEmpty(reopened.FindAll("[data-testid='devbook-surface'] [data-testid='devbook-stack']"));
                Assert.Empty(reopened.FindAll("[data-testid='workspace']"));
                Assert.Equal("true", reopened.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
            });

            reopened.Find("[data-testid='devbook-pane-option']").Click();
            reopened.WaitForAssertion(() =>
            {
                Assert.Null(restarted.LastSurface);
                Assert.NotEmpty(reopened.FindAll("[data-testid='workspace'] [data-testid='roadmap-band']"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// The Inbox is remembered as the surface, like the other takeovers: a shell
    /// built over the file again — a relaunch — reopens on it, and leaving it is
    /// remembered too.
    /// </summary>
    [Fact]
    public void Opening_the_inbox_reopens_it_after_a_restart()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            using (var harness = CreateHarness(
                features => features.SetEnabled(AppFeatures.InboxPane, true),
                shellNavigation: shellNavigation))
            {
                var component = Render(harness);
                component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='inbox-pane-option']")));
                component.Find("[data-testid='inbox-pane-option']").Click();
                component.WaitForAssertion(() => Assert.Equal("inbox", shellNavigation.LastSurface));
            }

            var restarted = new ShellNavigationStore(path);
            using var again = CreateHarness(
                features => features.SetEnabled(AppFeatures.InboxPane, true),
                shellNavigation: restarted);
            var reopened = Render(again);

            reopened.WaitForAssertion(() =>
                Assert.NotEmpty(reopened.FindAll("[data-testid='inbox-surface'] [data-testid='inbox-pane']")));

            reopened.Find("[data-testid='inbox-pane-option']").Click();
            reopened.WaitForAssertion(() => Assert.Null(restarted.LastSurface));
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// The Devbook was a side pane before it took the screen: a file that left it
    /// open over the workspace — as <c>Devbook</c>, or <c>Knowledge</c> from before
    /// the rename — reopens on the Devbook surface over the remembered view, with no
    /// pane row anywhere, and leaving it shows that view.
    /// </summary>
    [Theory]
    [InlineData("Devbook")]
    [InlineData("Knowledge")]
    public void A_devbook_pane_from_before_the_devbook_took_the_screen_reopens_as_the_devbook_surface(string pane)
    {
        var path = NewShellNavigationPath();

        try
        {
            WriteShellNavigation(path, $$"""
                { "lastSurface": "Workspace", "lastView": "Calendar", "lastEnabledPanes": ["{{pane}}"] }
                """);
            var shellNavigation = new ShellNavigationStore(path);

            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='devbook-surface'] [data-testid='devbook-stack']"));
                Assert.Empty(component.FindAll("[data-testid='workspace']"));
                Assert.Single(component.FindAll("main"));
            });

            component.Find("[data-testid='devbook-pane-option']").Click();
            component.WaitForAssertion(() =>
            {
                Assert.Equal("true", component.Find("[data-testid='calendar-view-option']").GetAttribute("aria-pressed"));
                Assert.Empty(component.FindAll("[data-testid='devbook-stack']"));
                Assert.Null(shellNavigation.LastSurface);
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>A Devbook pane hidden under a stored takeover was not what the reader
    /// saw: the takeover reopens, and leaving it shows the view with nothing beside
    /// it.</summary>
    [Fact]
    public void A_devbook_pane_under_another_takeover_reopens_on_that_takeover()
    {
        var path = NewShellNavigationPath();

        try
        {
            WriteShellNavigation(path, """
                { "lastSurface": "Dashboard", "lastEnabledPanes": ["Devbook"] }
                """);
            var shellNavigation = new ShellNavigationStore(path);

            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='dashboard-surface']"));
                Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
            });

            component.Find("[data-testid='dashboard-toggle-button']").Click();
            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='workspace'] [data-testid='backlog-pane']"));
                Assert.Empty(component.FindAll("[data-testid='devbook-stack']"));
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
    /// Tasks view is under the Inbox, which was open over the workspace and so
    /// reopens as its surface — over the Devbook beside it, which the Inbox covered
    /// from the day it took the screen. Leaving the Inbox shows the Tasks view alone.
    /// </summary>
    [Theory]
    [InlineData("Backlog")]
    [InlineData("Tasks")]
    public void A_tasks_pane_from_before_the_view_switch_is_ignored_and_the_inbox_reopens(string legacyTasks)
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
                Assert.NotEmpty(component.FindAll("[data-testid='inbox-surface'] [data-testid='inbox-pane']"));
                Assert.Equal("true", component.Find("[data-testid='inbox-pane-option']").GetAttribute("aria-pressed"));
            });

            component.Find("[data-testid='inbox-pane-option']").Click();
            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
                Assert.Empty(component.FindAll("[data-testid='devbook-stack']"));
                Assert.Equal("true", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
                Assert.Equal("false", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
                Assert.Null(shellNavigation.LastSurface);
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// The Inbox was a side pane before it took the screen: a file that left it open
    /// over the workspace reopens on the Inbox surface, with no pane row beside it.
    /// </summary>
    [Fact]
    public void An_inbox_pane_from_before_the_inbox_took_the_screen_reopens_as_the_inbox_surface()
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
                Assert.NotEmpty(component.FindAll("[data-testid='inbox-surface'] [data-testid='inbox-pane']"));
                Assert.Empty(component.FindAll("[data-testid='workspace']"));
                Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// The roadmap was a takeover before it was a view, and a file from then names it
    /// as the surface. It reopens on the Roadmap view in the workspace — the Devbook
    /// pane it listed was hidden under the takeover and does not take the screen —
    /// and the shell never writes the old surface name back.
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
                Assert.Empty(component.FindAll("[data-testid='devbook-stack']"));
                Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
                Assert.Equal("true", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
            });

            component.Find("[data-testid='dashboard-toggle-button']").Click();
            component.WaitForAssertion(() => Assert.Equal("dashboard", shellNavigation.LastSurface));
            component.Find("[data-testid='dashboard-toggle-button']").Click();
            component.WaitForAssertion(() => Assert.Null(shellNavigation.LastSurface));

            Assert.DoesNotContain("\"lastSurface\": \"Roadmap\"", ShellNavigationStoreTests.Written(path), StringComparison.Ordinal);
            Assert.Equal("roadmap", new ShellNavigationStore(path).LastView);
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
                component.WaitForAssertion(() => Assert.Equal("roadmap", shellNavigation.LastView));
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
    /// A pane name the current build does not recognise must never stop the app from
    /// opening, the same guarantee the surface gets.
    /// </summary>
    [Fact]
    public void An_unrecognised_remembered_pane_falls_back_to_the_default()
    {
        var path = NewShellNavigationPath();

        try
        {
            WriteShellNavigation(path, """
                { "lastSurface": "Workspace", "lastEnabledPanes": ["SomeFuturePane"] }
                """);
            var shellNavigation = new ShellNavigationStore(path);

            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='workspace'] [data-testid='backlog-pane']")));
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// A non-default view has to survive a takeover. The Board is picked first
    /// precisely so the assertion is about the reader's choice rather than about the
    /// default the shell would fall back to anyway.
    /// </summary>
    [Fact]
    public void Closing_a_surface_restores_the_workspace_with_the_view_unchanged()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='board-view-option']")));
        component.Find("[data-testid='board-view-option']").Click();
        component.WaitForAssertion(() =>
            Assert.Equal("true", component.Find("[data-testid='board-view-option']").GetAttribute("aria-pressed")));

        component.Find("[data-testid='tools-toggle-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='tools-surface']"));
            Assert.Empty(component.FindAll("[data-testid='backlog-pane']"));
        });

        // The in-surface ✕ and the header toggle both close it; this is the ✕.
        component.Find("[data-testid='tools-panel'] button.btn--ghost").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='tools-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='workspace'] [data-testid='backlog-pane']"));
            Assert.Equal("true", component.Find("[data-testid='board-view-option']").GetAttribute("aria-pressed"));
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
                    Assert.Equal("board", shellNavigation.LastView);
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
                    Assert.Equal("calendar", shellNavigation.LastView);
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
    /// The Calendar's Month | Week range is remembered beside the view, as the Board's
    /// columns are: a fresh shell reopens the Calendar on Week, and picking Month again
    /// takes the key back out. Only the range is kept, so the week shown is today's.
    /// </summary>
    [Fact]
    public void The_calendar_range_is_remembered_and_a_fresh_shell_reopens_on_it()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            using (var harness = CreateHarness(shellNavigation: shellNavigation))
            {
                var component = Render(harness);

                component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='calendar-view-option']")));
                component.Find("[data-testid='calendar-view-option']").Click();
                component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='task-calendar-range-week']")));
                Assert.Equal(ShellNavigationStore.DefaultCalendarRange, shellNavigation.CalendarRange);

                component.Find("[data-testid='task-calendar-range-week']").Click();
                component.WaitForAssertion(() =>
                {
                    Assert.Equal("Week", shellNavigation.CalendarRange);
                    Assert.Equal("true", component.Find("[data-testid='task-calendar-range-week']").GetAttribute("aria-pressed"));
                });
            }

            var today = DateOnly.FromDateTime(DateTime.Now);
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var reopenedNavigation = new ShellNavigationStore(path);
            using (var reopened = CreateHarness(shellNavigation: reopenedNavigation))
            {
                var again = Render(reopened);

                again.WaitForAssertion(() =>
                {
                    Assert.Equal("true", again.Find("[data-testid='calendar-view-option']").GetAttribute("aria-pressed"));
                    Assert.Equal("true", again.Find("[data-testid='task-calendar-range-week']").GetAttribute("aria-pressed"));
                    Assert.Equal(7, again.FindAll("[data-testid='task-calendar'] [data-calendar-day]").Count);
                    Assert.NotEmpty(again.FindAll($"[data-calendar-day='{monday:yyyy-MM-dd}']"));
                });

                again.Find("[data-testid='task-calendar-range-month']").Click();
                again.WaitForAssertion(() => Assert.Equal(ShellNavigationStore.DefaultCalendarRange, reopenedNavigation.CalendarRange));
            }

            Assert.DoesNotContain("calendarRange", ShellNavigationStoreTests.Written(path), StringComparison.Ordinal);
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// Every view option and every work-in-progress option leads with a glyph. The
    /// glyph is decoration, hidden from assistive technology and out of the tab
    /// order, so the option's name is still its word alone
    /// (accessibility.md#iconography-accessibility).
    /// </summary>
    [Theory]
    [InlineData("tasks-view-option", "list-icon", "Tasks")]
    [InlineData("board-view-option", "columns-3-icon", "Board")]
    [InlineData("calendar-view-option", "calendar-icon", "Calendar")]
    [InlineData("roadmap-view-option", "chart-gantt-icon", "Roadmap")]
    [InlineData("goals-view-option", "target-icon", "Goals")]
    [InlineData("sessions-toggle-button", "square-terminal-icon", "Sessions")]
    [InlineData("pull-requests-toggle-button", "git-pull-request-icon", "Pull requests")]
    [InlineData("inbox-pane-option", "inbox-icon", "Inbox")]
    [InlineData("dashboard-toggle-button", "layout-dashboard-icon", "Dashboard")]
    public void Each_header_option_leads_with_a_hidden_glyph_and_keeps_its_word_as_its_name(
        string option, string glyph, string name)
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.InboxPane, true));
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            var button = component.Find($"[data-testid='{option}']");
            var icon = Assert.IsAssignableFrom<IElement>(button.FirstElementChild);

            Assert.Equal("svg", icon.LocalName);
            Assert.Contains(glyph, icon.ClassList);
            Assert.Equal("true", icon.GetAttribute("aria-hidden"));
            Assert.Equal("false", icon.GetAttribute("focusable"));
            Assert.Equal("16", icon.GetAttribute("width"));
            Assert.Null(button.GetAttribute("aria-label"));

            // The name a screen reader reads: the text, which the glyph adds none to.
            // A feature flag's badge may follow the word, so it is the start that counts.
            Assert.StartsWith(name, button.TextContent.Trim(), StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The header stays one row by folding the options' words away in tiers: the
    /// Dashboard first, then the work in progress, then the Inbox, then the view
    /// switch. Each word wears its tier, and the option's title names it, so a
    /// folded option still says what it is on hover.
    /// </summary>
    [Theory]
    [InlineData("dashboard-toggle-button", "Dashboard", 1)]
    [InlineData("goals-view-option", "Goals", 2)]
    [InlineData("sessions-toggle-button", "Sessions", 2)]
    [InlineData("pull-requests-toggle-button", "Pull requests", 2)]
    [InlineData("inbox-pane-option", "Inbox", 3)]
    [InlineData("tasks-view-option", "Tasks", 4)]
    [InlineData("board-view-option", "Board", 4)]
    [InlineData("calendar-view-option", "Calendar", 4)]
    [InlineData("roadmap-view-option", "Roadmap", 4)]
    public void Each_header_option_word_folds_in_its_tier(string option, string name, int tier)
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.InboxPane, true));
        var component = Render(harness);

        component.WaitForAssertion(() =>
        {
            var button = component.Find($"[data-testid='{option}']");
            var label = button.QuerySelector(".header-nav-label");

            Assert.NotNull(label);
            Assert.Equal(name, label!.TextContent);
            Assert.Contains($"header-nav-label--tier-{tier}", label.ClassList);
            Assert.False(string.IsNullOrWhiteSpace(button.GetAttribute("title")));
        });
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
            Assert.NotNull(main.QuerySelector(":scope > [data-testid='roadmap-view'] > [data-testid='roadmap-band']"));

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
    /// The Devbook and the Inbox each take the whole screen over the roadmap, never
    /// beside it, and each hands the screen to the other: the Inbox's option during
    /// the Devbook gives the Inbox the screen, and its option pressed again returns
    /// to the roadmap underneath, with nothing beside it.
    /// </summary>
    [Fact]
    public void The_devbook_and_the_inbox_each_take_the_screen_over_the_roadmap()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.InboxPane, true));
        var component = Render(harness);

        OpenTheRoadmap(component);
        component.Find("[data-testid='devbook-pane-option']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-surface'] [data-testid='devbook-stack']"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
            Assert.Empty(component.FindAll("[data-testid='roadmap-view']"));
            Assert.Equal("false", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
        });

        component.Find("[data-testid='inbox-pane-option']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='inbox-surface'] [data-testid='inbox-pane']"));
            Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
            Assert.Empty(component.FindAll("[data-testid='workspace']"));
            Assert.Equal("true", component.Find("[data-testid='inbox-pane-option']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
        });

        component.Find("[data-testid='inbox-pane-option']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='inbox-pane']"));
            Assert.NotEmpty(component.FindAll("[data-testid='workspace'] > [data-testid='roadmap-view']"));
            Assert.Empty(component.FindAll("[data-testid='devbook-stack']"));
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
                GroupOptionIds(component, "view-switch"));
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
            Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
        });
    }

    /// <summary>
    /// A shell remembered on the Inbox reopens on the workspace while the Inbox
    /// flag is off: there is no option to leave it by, so it must not have the
    /// screen. The stored surface is kept for when the flag comes back.
    /// </summary>
    [Fact]
    public void A_stored_inbox_surface_with_its_feature_off_shows_the_workspace()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            shellNavigation.SetLastSurface("Inbox");

            using var harness = CreateHarness(
                features => features.SetEnabled(AppFeatures.InboxPane, false),
                shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='workspace'] [data-testid='backlog-pane']"));
                Assert.Empty(component.FindAll("[data-testid='inbox-surface']"));
                Assert.Empty(component.FindAll("[data-testid='inbox-pane-option']"));
                Assert.Equal("true", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
            });
            Assert.Equal("inbox", shellNavigation.LastSurface);
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>
    /// Turning the Devbook's flag off while it has the screen returns the reader to
    /// the view underneath, with no Devbook option left to press; it is remembered
    /// so, as the side pane closed for the same reason, and the flag coming back
    /// offers the option without taking the screen by itself.
    /// </summary>
    [Fact]
    public void Turning_the_devbook_flag_off_while_it_has_the_screen_returns_to_the_workspace()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);
            var features = (AppFeatureSettingsStore)harness.Context.Services.GetRequiredService<IAppFeatureSettings>();

            OpenTheRoadmap(component);
            OpenTheDevbook(component);

            _ = features.SetEnabled(DevbookFeatures.RepositoryDevbook, false);

            component.WaitForAssertion(() =>
            {
                Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
                Assert.Empty(component.FindAll("[data-testid='devbook-pane-option']"));
                Assert.NotEmpty(component.FindAll("[data-testid='workspace'] [data-testid='roadmap-band']"));
                Assert.Equal("true", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));
                Assert.Null(shellNavigation.LastSurface);
            });

            _ = features.SetEnabled(DevbookFeatures.RepositoryDevbook, true);

            component.WaitForAssertion(() =>
            {
                Assert.Equal("false", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
                Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
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
            Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
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

            // In the switch, in its own header item (local ADR 0041 §4).
            Assert.Equal("roadmap-header-item", option.ParentElement?.GetAttribute("data-testid"));
            Assert.Equal("view-switch", option.ParentElement?.ParentElement?.GetAttribute("data-testid"));
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
    /// underneath.
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
    /// A resize may not touch the view. The side panes had a viewport-driven
    /// capacity the window's resize listener set; they are gone, and with them the
    /// shell's registration with that listener, so no window width reaches the
    /// shell at all and the roadmap stays on screen.
    /// </summary>
    [Fact]
    public void A_window_resize_never_closes_the_roadmap()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        OpenTheRoadmap(component);

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Equal("true", component.Find("[data-testid='roadmap-view-option']").GetAttribute("aria-pressed"));

            // The Tasks pane's SplitPane registers through the same interop name with
            // a minted key; the shell's registration was the keyless, one-argument one.
            Assert.DoesNotContain(
                harness.Context.JSInterop.Invocations,
                invocation => invocation.Identifier == "backlogPaneResizer.initialize" && invocation.Arguments.Count == 1);
        });
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
    /// Devbook, Roadmap and Dashboard are the released ones of the four enabled
    /// here; Tools is Dev and is the control group.</summary>
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
            Assert.NotEmpty(component.FindAll("[data-testid='dashboard-toggle-button']"));
            Assert.Empty(component.FindAll("[data-testid='dashboard-feature-status']"));

            // Present and flagged — proving the absence above is the status
            // talking rather than the badge being broken everywhere.
            Assert.Single(component.FindAll("[data-testid='tools-feature-status']"));
        });
    }

    /// <summary>
    /// The Devbook's toggle is a plain on-or-off: a press with Ctrl held does what a
    /// plain press does, and the toggle is never disabled — the shell showing no
    /// Devbook is its ordinary state.
    /// </summary>
    [Fact]
    public void The_devbook_toggle_takes_no_modifier_and_is_never_disabled()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        Assert.False(component.Find("[data-testid='devbook-pane-option']").HasAttribute("disabled"));

        component.Find("[data-testid='devbook-pane-option']").Click(new MouseEventArgs { CtrlKey = true });

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-surface']"));
            Assert.Equal("true", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
            Assert.False(component.Find("[data-testid='devbook-pane-option']").HasAttribute("disabled"));
        });

        component.Find("[data-testid='devbook-pane-option']").Click(new MouseEventArgs { CtrlKey = true });

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='backlog-pane']"));
            Assert.Equal("false", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));
        });
    }

    /// <summary>The tooltip says what the press will do: open the Devbook, or go back
    /// to the view underneath, named.</summary>
    [Fact]
    public void The_devbook_tooltip_says_what_the_press_will_do()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() =>
            Assert.Equal("Open the Devbook", component.Find("[data-testid='devbook-pane-option']").GetAttribute("title")));

        OpenTheRoadmap(component);
        component.WaitForAssertion(() =>
            Assert.Equal("Open the Devbook", component.Find("[data-testid='devbook-pane-option']").GetAttribute("title")));

        component.Find("[data-testid='devbook-pane-option']").Click();
        component.WaitForAssertion(() =>
            Assert.Equal("Back to Roadmap", component.Find("[data-testid='devbook-pane-option']").GetAttribute("title")));
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
            var options = GroupOptionIds(component, "work-in-progress-switcher");
            Assert.Equal(options.IndexOf("sessions-toggle-button") + 1, options.IndexOf("pull-requests-toggle-button"));
        });

        component.Find("[data-testid='pull-requests-panel'] button.btn--ghost").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='pull-requests-surface']"));
            Assert.NotEmpty(component.FindAll("[data-testid='workspace']"));
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
            component.WaitForAssertion(() => Assert.Null(shellNavigation.LastSurface));
            component.Find("[data-testid='pull-requests-toggle-button']").Click();

            component.WaitForAssertion(() => Assert.Equal("pull-requests", shellNavigation.LastSurface));
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

    // --- What a change draws again ------------------------------------------
    //
    // Every keystroke in an entry's body and every move of the selection is a
    // change the Tasks state announces, and the shell hears every one. The
    // header draws nothing those change, so the shell is not drawn again for
    // them; the Tasks pane listens for itself and is. Each change is made on the
    // renderer's thread, as the editor's own ValueChanged makes it.

    /// <summary>Five keystrokes in the open entry's Markdown: five redraws of the
    /// pane that shows the entry, none of the shell around it.</summary>
    [Fact]
    public async Task Typing_in_the_open_entry_redraws_the_pane_and_not_the_shell()
    {
        using var harness = CreateHarness(seed: PlanEntryText);
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();
        component.WaitForState(() => state.Rows.FirstOrDefault()?.Id is not null);

        var row = state.Rows[0];
        await component.InvokeAsync(() => state.SelectAsync(row));
        var (shellBefore, paneBefore) = (ShellRenderCount.Shell(component), ShellRenderCount.TasksPane(component));

        foreach (var body in new[] { "R", "Re", "Rea", "Read", "Read it" })
        {
            await component.InvokeAsync(() => state.ChangeBody(row, body));
        }

        Assert.Equal("Read it", row.PreviewBody.Trim());
        Assert.Equal(shellBefore, ShellRenderCount.Shell(component));
        // One pane redraw per keystroke, not the two it took while the shell redrew
        // too. The debounced save runs on the real clock and may land under load,
        // adding a redraw, so the count is a range below the old two-per-change.
        Assert.InRange(ShellRenderCount.TasksPane(component), paneBefore + 5, paneBefore + 9);
    }

    /// <summary>Opening one entry after another moves the selection the pane draws
    /// and nothing in the header.</summary>
    [Fact]
    public async Task Moving_the_selection_redraws_the_pane_and_not_the_shell()
    {
        using var harness = CreateHarness(seed: PlanEntryText, moreSeeds: ["# Second entry\n`task` `!ready`\n"]);
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();
        component.WaitForState(() => state.Rows.Count == 2 && state.Rows.All(row => row.Id is not null));

        var (shellBefore, paneBefore) = (ShellRenderCount.Shell(component), ShellRenderCount.TasksPane(component));

        await component.InvokeAsync(() => state.SelectAsync(state.Rows[0]));
        await component.InvokeAsync(() => state.SelectAsync(state.Rows[1]));

        Assert.Contains("Second entry", component.Find(".task-item--selected").TextContent, StringComparison.Ordinal);
        Assert.Equal(shellBefore, ShellRenderCount.Shell(component));
        Assert.InRange(ShellRenderCount.TasksPane(component), paneBefore + 2, paneBefore + 3);
    }

    /// <summary>
    /// The session and pull request lists read their tasks through the shell's
    /// delegates and do not listen to the Tasks state themselves, so while either is
    /// on screen every change the shell hears still draws it again — a row's link or
    /// status that moved in place reaches the list's cells.
    /// </summary>
    [Fact]
    public async Task A_tasks_change_under_the_sessions_or_pull_requests_list_redraws_it()
    {
        using var harness = CreateHarness(seed: PlanEntryText);
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();
        component.WaitForState(() => state.Rows.FirstOrDefault()?.Id is not null);

        OpenSessions(component);
        var sessions = component.FindComponent<SessionsPane>();
        var sessionsBefore = sessions.RenderCount;

        await component.InvokeAsync(() => state.SetSelectionMode(true));

        component.WaitForAssertion(() => Assert.True(sessions.RenderCount > sessionsBefore));

        OpenPullRequests(component);
        var pullRequests = component.FindComponent<PullRequestsPane>();
        var pullRequestsBefore = pullRequests.RenderCount;

        await component.InvokeAsync(() => state.SetSelectionMode(false));

        component.WaitForAssertion(() => Assert.True(pullRequests.RenderCount > pullRequestsBefore));
    }

    /// <summary>The question box holds its own text: typing a question, keys and
    /// all, draws the box again and not the shell around it — and the question
    /// typed is still the one that is asked.</summary>
    [Fact]
    public void Typing_a_question_redraws_the_box_and_not_the_shell()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.AiAssistant, true));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();
        component.WaitForElement("[data-testid='ai-question-input']");
        var shellBefore = ShellRenderCount.Shell(component);

        foreach (var typed in new[] { "W", "Wh", "Wha", "What", "What?" })
        {
            var input = component.Find("[data-testid='ai-question-input']");
            input.KeyDown(new KeyboardEventArgs { Key = typed[^1..] });
            input.Input(typed);
        }

        Assert.Equal(shellBefore, ShellRenderCount.Shell(component));

        component.Find("[data-testid='ai-ask-button']").Click();

        component.WaitForAssertion(() => Assert.Equal("What?", Assert.Single(harness.FoundryChat.Requests).Question));
    }

    /// <summary>Ctrl+Enter in the box asks, as the button does, with the question
    /// typed.</summary>
    [Fact]
    public void Ctrl_enter_in_the_question_box_asks_the_typed_question()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.AiAssistant, true));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();
        component.WaitForElement("[data-testid='ai-question-input']").Input("What is next?");

        component.Find("[data-testid='ai-question-input']").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        component.WaitForAssertion(() => Assert.Equal("What is next?", Assert.Single(harness.FoundryChat.Requests).Question));
    }

    /// <summary>A question typed and left is still there when the panel opens
    /// again: closing the panel ends an ask, not the question.</summary>
    [Fact]
    public void A_question_typed_survives_closing_and_reopening_the_panel()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.AiAssistant, true));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();
        component.WaitForElement("[data-testid='ai-question-input']").Input("Kept for later");

        component.Find("[data-testid='ai-toggle-button']").Click();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='ai-question-input']")));
        component.Find("[data-testid='ai-toggle-button']").Click();

        component.WaitForAssertion(() => Assert.Equal("Kept for later", component.Find("[data-testid='ai-question-input']").GetAttribute("value")));
    }

    /// <summary>The question a dashboard part's Ask AI begins.</summary>
    private const string ThroughputPrefix = "About the \"Throughput\" chart: ";

    /// <summary>
    /// A dashboard part's Ask AI opens the panel on the Dashboard with a question
    /// that names the part, takes focus to the box, and the question asked goes to
    /// the Dashboard's source.
    /// </summary>
    [Fact]
    public void A_dashboard_parts_ask_ai_opens_the_panel_on_the_dashboard_with_a_question_naming_the_part()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.AiAssistant, true));
        var component = Render(harness);
        harness.Context.JSInterop.SetupVoid("backlogFocus", _ => true);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='dashboard-toggle-button']")));
        component.Find("[data-testid='dashboard-toggle-button']").Click();
        component.WaitForElement("[data-testid='dashboard-headline-ask-ai']");
        Assert.Empty(component.FindAll("[data-testid='ai-assistant-panel']"));

        component.Find("[data-testid='dashboard-headline-ask-ai']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='ai-assistant-panel']"));
            Assert.Equal("Answers from the Dashboard content.", component.Find(".ai-panel__body").TextContent.Trim());
            Assert.Equal(ThroughputPrefix, component.Find("[data-testid='ai-question-input']").GetAttribute("value"));
            Assert.Contains(
                harness.Context.JSInterop.Invocations["backlogFocus"],
                invocation => Equals(invocation.Arguments[0], "ai-question-input"));
        });

        component.Find("[data-testid='ai-ask-button']").Click();

        component.WaitForAssertion(() =>
        {
            var request = Assert.Single(harness.FoundryChat.Requests);
            Assert.Equal(ThroughputPrefix, request.Question);
            Assert.StartsWith("Dashboard (", request.Content, StringComparison.Ordinal);
        });
    }

    /// <summary>An answer already in the panel was to another question, so a part's
    /// Ask AI clears it as it begins the new one.</summary>
    [Fact]
    public void A_dashboard_parts_ask_ai_clears_an_earlier_answer()
    {
        using var harness = CreateHarness(features => features.SetEnabled(AppFeatures.AiAssistant, true));
        var component = Render(harness);
        harness.Context.JSInterop.SetupVoid("backlogFocus", _ => true);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='dashboard-toggle-button']")));
        component.Find("[data-testid='dashboard-toggle-button']").Click();
        component.WaitForElement("[data-testid='dashboard-headline-ask-ai']");
        component.Find("[data-testid='ai-toggle-button']").Click();
        component.WaitForElement("[data-testid='ai-question-input']").Input("What changed?");
        component.Find("[data-testid='ai-ask-button']").Click();
        component.WaitForElement("[data-testid='ai-answer']");

        component.Find("[data-testid='dashboard-headline-ask-ai']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='ai-answer']"));
            Assert.Equal(ThroughputPrefix, component.Find("[data-testid='ai-question-input']").GetAttribute("value"));
        });
    }

    /// <summary>An ask still in flight ends when a part's Ask AI is pressed: its
    /// answer would otherwise land under the new question and read as the answer to
    /// it. The Ask button is offered again at once.</summary>
    [Fact]
    public void A_dashboard_parts_ask_ai_ends_an_ask_in_flight()
    {
        var hanging = new HangingSource("dashboard", "Dashboard");
        using var harness = CreateHarness(
            features => features.SetEnabled(AppFeatures.AiAssistant, true),
            configureServices: services => services.AddScoped<IAiContentSource>(_ => hanging));
        var component = Render(harness);
        harness.Context.JSInterop.SetupVoid("backlogFocus", _ => true);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='dashboard-toggle-button']")));
        component.Find("[data-testid='dashboard-toggle-button']").Click();
        component.WaitForElement("[data-testid='dashboard-headline-ask-ai']");
        component.Find("[data-testid='ai-toggle-button']").Click();
        component.WaitForElement("[data-testid='ai-question-input']").Input("What changed?");
        component.Find("[data-testid='ai-ask-button']").Click();
        component.WaitForAssertion(() =>
        {
            Assert.True(hanging.Started.Task.IsCompleted);
            Assert.True(component.Find("[data-testid='ai-ask-button']").HasAttribute("disabled"));
        });

        component.Find("[data-testid='dashboard-headline-ask-ai']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.True(hanging.Started.Task.Result.IsCancellationRequested);
            Assert.False(component.Find("[data-testid='ai-ask-button']").HasAttribute("disabled"));
            Assert.Equal(ThroughputPrefix, component.Find("[data-testid='ai-question-input']").GetAttribute("value"));
        });
        Assert.Empty(harness.FoundryChat.Requests);
    }

    /// <summary>With the assistant switched off nothing could answer, so no part
    /// offers Ask AI.</summary>
    [Fact]
    public void No_dashboard_part_offers_ask_ai_while_the_assistant_is_off()
    {
        using var harness = CreateHarness();
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='dashboard-toggle-button']")));
        component.Find("[data-testid='dashboard-toggle-button']").Click();
        component.WaitForElement("[data-testid='dashboard-headline-refresh']");

        Assert.Empty(component.FindAll("[data-testid$='-ask-ai']"));
    }

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

    private const string InProgressEntryText = "# Wire the In progress view\n`prompt` `!in-progress`\n";

    /// <summary>A task's side panel carries its sessions and pull requests while either
    /// feature is on.</summary>
    [Fact]
    public async Task A_tasks_side_panel_carries_its_sessions_and_pull_requests()
    {
        using var harness = CreateHarness(seed: InProgressEntryText);
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();
        component.WaitForState(() => state.Rows.FirstOrDefault()?.Id is not null);

        await component.InvokeAsync(() => state.SelectAsync(state.Rows[0]));

        component.WaitForAssertion(() =>
            Assert.NotEmpty(component.FindAll("[data-testid='entry-detail'] [data-testid='entry-linked-work']")));
    }

    /// <summary>
    /// Goals leads the work in progress group, always offered: its word and title are
    /// Goals, a target leads it, it carries no feature badge, and a press shows the Goals
    /// view, with no Now board, and remembers it as <c>Goals</c>.
    /// </summary>
    [Fact]
    public void Goals_leads_the_work_in_progress_group()
    {
        using var harness = CreateHarness(seed: InProgressEntryText);
        var component = Render(harness);

        component.WaitForAssertion(() =>
            Assert.Equal(
                ["goals-view-option", "sessions-toggle-button", "pull-requests-toggle-button"],
                GroupOptionIds(component, "work-in-progress-switcher")));

        var option = component.Find("[data-testid='goals-view-option']");
        Assert.Equal("Goals", option.GetAttribute("title"));
        Assert.Equal("goals-view", option.GetAttribute("aria-controls"));
        Assert.StartsWith("Goals", option.TextContent.Trim(), StringComparison.Ordinal);
        Assert.Contains("target-icon", Assert.IsAssignableFrom<IElement>(option.FirstElementChild).ClassList);
        Assert.Empty(component.FindAll("[data-testid='goals-feature-status']"));
        Assert.Empty(component.FindAll("[data-testid='in-progress-view-option']"));

        option.Click();

        component.WaitForAssertion(() =>
        {
            Assert.NotEmpty(component.FindAll("[data-testid='workspace'] [data-testid='goals-view']"));
            Assert.Empty(component.FindAll("[data-testid='in-progress-view']"));
            Assert.Empty(component.FindAll("[data-testid='in-progress-board']"));
            Assert.Equal("true", component.Find("[data-testid='goals-view-option']").GetAttribute("aria-pressed"));
            Assert.Equal("false", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
        });

        Assert.Equal("goals", harness.Context.Services.GetRequiredService<ShellNavigationStore>().LastView);
    }

    /// <summary>The Goals view needs neither list: with Sessions and Pull requests both
    /// off the group still renders, holding Goals only.</summary>
    [Fact]
    public void Goals_is_offered_without_sessions_or_pull_requests()
    {
        using var harness = CreateHarness(
            features =>
            {
                features.SetEnabled(SessionFeatures.Sessions, false);
                features.SetEnabled(SessionFeatures.PullRequests, false);
            });
        var component = Render(harness);

        component.WaitForAssertion(() =>
            Assert.Equal(["goals-view-option"], GroupOptionIds(component, "work-in-progress-switcher")));

        component.Find("[data-testid='goals-view-option']").Click();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='goals-view']")));
    }

    /// <summary>
    /// The Goals view in the shell draws the board from the Progress ports over the
    /// shell's own task list: the goals and the stand-up aside, and the task in progress
    /// that no goal holds under No goal. Without the ports — every other test here —
    /// the view says the module is not set up rather than drawing nothing.
    /// </summary>
    [Fact]
    public void The_goals_view_draws_the_board_over_the_shells_tasks()
    {
        using var harness = CreateHarness(
            seed: InProgressEntryText,
            configureServices: services =>
            {
                services.AddSingleton<Backlog.Modules.Progress.Abstractions.Services.IStandUp>(new GoalsHostTests.FakeStandUp());
                services.AddSingleton<Backlog.Modules.Progress.Abstractions.Services.IGoals>(new GoalsHostTests.FakeGoals());
                services.AddSingleton<Backlog.Modules.Progress.Abstractions.Services.IImpediments>(new GoalsHostTests.FakeImpediments());
                services.AddSingleton<Backlog.Modules.Progress.Abstractions.Services.IIterationSettings>(new GoalsHostTests.FakeIterationSettings());
                services.AddSingleton<Backlog.Modules.Progress.Abstractions.Services.IGoalPullRequestReader>(new GoalsHostTests.FakePullRequests());
            });
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='goals-view-option']")));
        component.Find("[data-testid='goals-view-option']").Click();

        component.WaitForAssertion(() =>
        {
            var view = component.Find("[data-testid='goals-view']");
            Assert.Equal("Goals", view.QuerySelector("h1")!.TextContent);
            Assert.NotNull(view.QuerySelector("aside[aria-label='Stand-up']"));
            Assert.Contains("Wire the In progress view", view.QuerySelector("[data-testid='goals-board-no-goal']")!.TextContent);
            Assert.Null(view.QuerySelector("[data-testid='goals-view-empty']"));
        });
    }

    /// <summary>A file from before the rename reopens on the Goals view: the retired
    /// name is the same view.</summary>
    [Fact]
    public void A_stored_in_progress_view_reopens_on_goals()
    {
        var path = NewShellNavigationPath();

        try
        {
            var shellNavigation = new ShellNavigationStore(path);
            shellNavigation.SetLastView("InProgress");

            using var harness = CreateHarness(shellNavigation: shellNavigation);
            var component = Render(harness);

            component.WaitForAssertion(() =>
            {
                Assert.NotEmpty(component.FindAll("[data-testid='goals-view']"));
                Assert.Equal("true", component.Find("[data-testid='goals-view-option']").GetAttribute("aria-pressed"));
            });
        }
        finally
        {
            DeleteShellNavigationDirectory(path);
        }
    }

    /// <summary>Ask AI follows the Goals view as it follows every area: on screen, a
    /// question goes to the source composed for <c>goals</c>.</summary>
    [Fact]
    public void Ask_ai_asks_the_goals_area_while_the_goals_view_is_on_screen()
    {
        using var harness = CreateHarness(
            features => features.SetEnabled(AppFeatures.AiAssistant, true),
            configureServices: services => services.AddScoped<IAiContentSource>(_ => new FixedSource("goals", "Goals")));
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='goals-view-option']")));
        component.Find("[data-testid='goals-view-option']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();

        component.WaitForAssertion(() =>
            Assert.Equal("Answers from the Goals content.", component.Find(".ai-panel__body").TextContent.Trim()));
    }

    /// <summary>With the desktop's own Goals source composed over the stand-up port, Ask AI
    /// is offered on the Goals view and a question carries the stand-up as its content.</summary>
    [Fact]
    public void Ask_ai_is_offered_on_the_goals_view_and_sends_the_stand_up()
    {
        using var harness = CreateHarness(
            features => features.SetEnabled(AppFeatures.AiAssistant, true),
            configureServices: services =>
            {
                services.AddSingleton<Backlog.Modules.Progress.Abstractions.Services.IStandUp>(new GoalsHostTests.FakeStandUp());
                services.AddGoalsAiContentSource();
            });
        var component = Render(harness);

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='goals-view-option']")));
        component.Find("[data-testid='goals-view-option']").Click();
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='ai-toggle-button']")));
        component.Find("[data-testid='ai-toggle-button']").Click();
        component.WaitForAssertion(() =>
            Assert.Equal("Answers from the Goals content.", component.Find(".ai-panel__body").TextContent.Trim()));

        component.Find("[data-testid='ai-question-input']").Input("What is at risk?");
        component.Find("[data-testid='ai-ask-button']").Click();

        component.WaitForAssertion(() =>
        {
            var request = Assert.Single(harness.FoundryChat.Requests);
            Assert.StartsWith("Goals (stand-up: ", request.Content, StringComparison.Ordinal);
            Assert.Contains("Goal: Customers get paid faster", request.Content, StringComparison.Ordinal);
            Assert.Contains("Impediment: Bank sample files missing, escalated", request.Content, StringComparison.Ordinal);
        });
    }

    /// <summary>The test ids of a header group's options, in order. Each member of a group is a header
    /// item (local ADR 0041 §4), and its option leads it.</summary>
    private static List<string?> GroupOptionIds(IRenderedComponent<Home> rendered, string group) =>
        [.. rendered.Find($"[data-testid='{group}']").Children.Select(item =>
        {
            Assert.Contains("header-surface-item", item.ClassList);
            return item.FirstElementChild?.GetAttribute("data-testid");
        })];

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

    /// <summary>
    /// A roadmap item's "Open in Tasks" puts the Tasks view on screen with the item's
    /// plan chip pressed and every entry the roadmap gathers under the item listed — the
    /// task it links, and whatever wears its tag with or without the <c>+</c>, wherever
    /// it is filed (roadmap/domain.md#roadmap-item-gathering). The list is scoped to
    /// every repository, remembered as a press on a scope chip is. The roadmap raises the
    /// request and the shell answers it.
    /// <para>
    /// The defect this pins: an item filed under <c>backlog</c> opened onto "Nothing
    /// matches these filters" when its task wore the bare <c>#plan-a</c>, because the list
    /// asked only for <c>+plan-a</c> inside the bar's repository while the roadmap counted
    /// by its own wider rule.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Open_in_tasks_from_a_roadmap_item_shows_its_plan_in_the_task_list()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-workspace-surface-tests", Guid.NewGuid().ToString("n"));
        var navigation = new ShellNavigationStore(Path.Combine(root, "shell-navigation.json"));
        using var harness = CreateHarness(
            shellNavigation: navigation,
            seed: "# Wire the importer +plan-a\n`task` `!ready` `repo:backlog`\n",
            moreSeeds:
            [
                "# Map the fields\n`task` `!ready` `#plan-a` `repo:backlog`\n",
                "# Sketch the flow +plan-a\n`task` `!ready`\n",
                "# Plan the cutover\n`task` `!ready` `repo:backlog`\n",
                "# Tidy the docs\n`task` `!ready` `#other` `repo:backlog`\n",
            ]);

        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();
        component.WaitForAssertion(() => Assert.Equal(5, state.Rows.Count(row => row.IsPersisted)));
        var linked = state.Rows.Single(row => row.PreviewTitle.StartsWith("Plan the cutover", StringComparison.Ordinal)).Id;
        Assert.NotNull(linked);

        // Linked to the untagged task, and filed under backlog alone — the bar the reader
        // opens names that one repository.
        var planning = harness.Context.Services.GetRequiredService<IRoadmapPlanning>();
        var added = await planning.AddItemAsync(
            "Plan A",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: ["backlog"],
            taskId: linked,
            tag: "plan-a",
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(added.IsSuccess);

        OpenTheRoadmap(component);

        component.WaitForElement(".roadmap-bar__body").Click();
        component.WaitForElement("[data-testid='roadmap-editor-open-tasks']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Equal("true", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
            Assert.Equal("+plan-a", state.SelectedPlan);
            Assert.Equal(
                new[] { "Map the fields", "Plan the cutover", "Sketch the flow", "Wire the importer" },
                state.FilteredRows.Select(row => row.PreviewTitle.Split(" +")[0]).Order(StringComparer.Ordinal));
            Assert.DoesNotContain(state.FilteredRows, row => row.PreviewTitle.StartsWith("Tidy the docs", StringComparison.Ordinal));
            Assert.Empty(state.SelectedRepositoryAliases);
            Assert.Empty(navigation.RepositoryScope);
        });
    }

    /// <summary>AC3: the same plan opened from the action at the end of its bar, with no
    /// editor in between — across every repository, as from the editor, though the item
    /// is filed under <c>backlog</c> alone.</summary>
    [Fact]
    public async Task Open_in_tasks_from_a_roadmap_bar_shows_its_plan_in_the_task_list()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-workspace-surface-tests", Guid.NewGuid().ToString("n"));
        var navigation = new ShellNavigationStore(Path.Combine(root, "shell-navigation.json"));
        using var harness = CreateHarness(
            shellNavigation: navigation,
            seed: "# Wire the importer +plan-a\n`task` `!ready` `repo:backlog`\n");
        var planning = harness.Context.Services.GetRequiredService<IRoadmapPlanning>();
        var added = await planning.AddItemAsync(
            "Plan A",
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 9),
            repositoryAliases: ["backlog"],
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(added.IsSuccess);

        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();
        OpenTheRoadmap(component);

        component.WaitForElement("[data-roadmap-action='open']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Equal("+plan-a", state.SelectedPlan);
            // Every repository, not the bar's: the roadmap gathers a plan's work
            // whatever repository it is filed under, and the list shows all of it.
            Assert.Empty(state.SelectedRepositoryAliases);
            Assert.Empty(navigation.RepositoryScope);
        });
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

    /// <summary>Gives the Devbook the screen through its header option.</summary>
    private static void OpenTheDevbook(IRenderedComponent<Home> component)
    {
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='devbook-pane-option']")));
        component.Find("[data-testid='devbook-pane-option']").Click();
        component.WaitForAssertion(() =>
            Assert.NotEmpty(component.FindAll("[data-testid='devbook-surface'] [data-testid='devbook-stack']")));
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
    /// The Devbook is a registered surface (local ADR 0041 §1): the shell draws it
    /// through its catalog entry, and a task followed out of a Brain note reaches the
    /// task list through the window's navigator (§3) rather than a callback the shell
    /// handed the pane — leaving the Devbook for the list, that task selected.
    /// </summary>
    [Fact]
    public void A_task_followed_out_of_the_devbook_goes_through_the_navigator_to_the_task_list()
    {
        using var harness = CreateHarness(seed: PlanEntryText);
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();
        component.WaitForState(() => state.Rows.FirstOrDefault()?.Id is not null);
        var row = state.Rows.First(candidate => candidate.PreviewTitle.Contains("Read delivery run files", StringComparison.Ordinal));
        var requests = new List<SurfaceRequest>();
        harness.Context.Services.GetRequiredService<ISurfaceNavigator>().ShowRequested += requests.Add;

        OpenTheDevbook(component);

        var pane = component.FindComponent<DevbookPane>().Instance;
        var titleFor = Assert.IsType<Func<string, string?>>(pane.TaskTitleFor);
        Assert.Equal(row.PreviewTitle, titleFor(row.Id!.Value.ToString()));
        Assert.Null(titleFor(Guid.NewGuid().ToString()));

        component.InvokeAsync(() => pane.OnOpenTask.InvokeAsync(row.Id!.Value.ToString()));

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
            var selected = Assert.Single(component.FindAll(".task-item--selected"));
            Assert.Contains("Read delivery run files", selected.TextContent);
        });
        var request = Assert.Single(requests);
        Assert.Equal("tasks", request.SurfaceId);
        Assert.Equal(row.Id!.Value.ToString(), request.Value("entry"));
    }

    /// <summary>
    /// The main window answers a jump to a view (local ADR 0041 §3, §6): from the
    /// Roadmap, a request for Tasks with a plan shows the Tasks view, from the catalog,
    /// with that plan chip pressed; and an entry the backlog does not hold still shows
    /// Tasks, with a toast saying so.
    /// </summary>
    [Fact]
    public void A_jump_to_a_view_shows_it_from_the_catalog_with_its_query()
    {
        using var harness = CreateHarness(seed: "# Wire the importer +plan-a\n`task` `!ready` `repo:backlog`\n");
        var component = Render(harness);
        var state = harness.Context.Services.GetRequiredService<TasksDesktopState>();
        var navigator = harness.Context.Services.GetRequiredService<ISurfaceNavigator>();
        component.WaitForState(() => state.Rows.Count > 0);
        OpenTheRoadmap(component);

        component.InvokeAsync(() => navigator.Show("tasks", new Dictionary<string, string> { ["plan"] = "plan-a", ["unregistered"] = "dropped" }));

        component.WaitForAssertion(() =>
        {
            Assert.Empty(component.FindAll("[data-testid='roadmap-band']"));
            Assert.Equal("true", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
            Assert.Equal("+plan-a", state.SelectedPlan);
        });

        var toasts = harness.Context.Services.GetRequiredService<ToastChannel>();
        component.InvokeAsync(() => navigator.Show("board", new Dictionary<string, string>()));
        component.WaitForAssertion(() => Assert.Equal("true", component.Find("[data-testid='board-view-option']").GetAttribute("aria-pressed")));

        component.InvokeAsync(() => navigator.Show("tasks", new Dictionary<string, string> { ["entry"] = Guid.NewGuid().ToString() }));

        component.WaitForAssertion(() =>
        {
            Assert.Equal("true", component.Find("[data-testid='tasks-view-option']").GetAttribute("aria-pressed"));
            Assert.Contains(toasts.Visible, toast => toast.TestId == TasksSurface.EntryMissingTestId);
        });
    }

    /// <summary>
    /// A Devbook the reader pressed open is the catalog's: switching its flag off
    /// takes it out of the catalog, the header loses its option, and the surface
    /// leaves the screen for the workspace.
    /// </summary>
    [Fact]
    public void The_devbook_option_and_surface_follow_its_catalog_entry()
    {
        using var harness = CreateHarness();
        var component = Render(harness);
        var catalog = harness.Context.Services.GetRequiredService<ISurfaceCatalog>();
        var features = (AppFeatureSettingsStore)harness.Context.Services.GetRequiredService<IAppFeatureSettings>();

        Assert.True(catalog.TryGet("devbook", out var devbook));
        Assert.Equal(ShellSurfaceGroup.Takeover, devbook.Group);

        OpenTheDevbook(component);
        Assert.Equal("true", component.Find("[data-testid='devbook-pane-option']").GetAttribute("aria-pressed"));

        _ = features.SetEnabled(DevbookFeatures.RepositoryDevbook, false);

        component.WaitForAssertion(() =>
        {
            Assert.False(catalog.TryGet("devbook", out _));
            Assert.Empty(component.FindAll("[data-testid='devbook-pane-option']"));
            Assert.Empty(component.FindAll("[data-testid='devbook-surface']"));
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
        Action<IServiceCollection>? configureServices = null,
        string[]? moreSeeds = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-workspace-surface-tests", Guid.NewGuid().ToString("n"));
        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));

        string[] seeds = [.. seed is null ? [] : new[] { seed }, .. moreSeeds ?? []];
        for (var order = 0; order < seeds.Length; order++)
        {
            // Written through the real use case, so the entry carries the plan and
            // the item id exactly as an import leaves them.
            var saved = TasksTestHost.EntriesFor(store).SaveFromTextAsync(null, seeds[order], order).GetAwaiter().GetResult();

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
        context.Services.AddShellSurfaces();
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
            new DashboardInsightPorts(
                sp.GetRequiredService<IProductivityInsights>(),
                sp.GetRequiredService<ITaskInsights>(),
                sp.GetRequiredService<ISessionInsights>(),
                sp.GetRequiredService<IHoursWorkedInsights>(),
                sp.GetRequiredService<IDriftInsights>(),
                sp.GetRequiredService<ICostInsights>(),
                sp.GetRequiredService<IAttentionInsights>()),
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

    /// <summary>A source that never finishes composing until its ask is cancelled,
    /// and hands over that ask's token once it has started.</summary>
    private sealed class HangingSource(string areaKey, string areaTitle) : IAiContentSource
    {
        public TaskCompletionSource<CancellationToken> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string AreaKey => areaKey;

        public string AreaTitle => areaTitle;

        public async Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
        }
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
