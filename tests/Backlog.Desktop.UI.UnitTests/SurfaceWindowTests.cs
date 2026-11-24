using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.Sessions;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Services;
using Backlog.Modules.Inbox.UI;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Results;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A popped-out window (local ADR 0043 §3, §4, §6): the slim header over one
/// surface from the catalog, "not available" for an id the catalog does not offer,
/// a jump that switches what the window shows, and Show in main window.
/// <para>
/// Rendered over two surfaces of the test's own rather than the Devbook, so what is
/// asserted is the window and not whatever a real pane needs to draw.
/// </para>
/// </summary>
public sealed class SurfaceWindowTests
{
    private const string WindowKey = "window-1";

    [Fact]
    public async Task The_header_shows_the_surface_title_the_scope_chips_and_show_in_main_window_and_no_ask_ai()
    {
        using var host = await CreateHostAsync();
        var window = host.Render("alpha", "?note=remembered&scope=docs&unregistered=dropped");

        Assert.Equal("Alpha", window.Find("[data-testid='surface-window-title']").TextContent.Trim());
        Assert.Equal(
            ["backlog", "docs"],
            window.FindAll("[data-testid='surface-window-scope-option']").Select(chip => chip.TextContent.Trim()));
        Assert.Single(window.FindAll("[data-testid='surface-window-brain-scope-option']"));
        Assert.Equal("Show in main window", window.Find("[data-testid='surface-window-show-in-main']").TextContent.Trim());

        // No view switch and no Ask AI: those are the main window's.
        Assert.Empty(window.FindAll("[data-testid='ai-toggle-button']"));
        Assert.Empty(window.FindAll("[data-testid='view-switch']"));

        // The surface, handed only the keys it registered — and the window's scope
        // follows the one the query named.
        var surface = window.Find("[data-testid='alpha-surface'] [data-testid='alpha-body']");
        Assert.Equal("note=remembered;scope=docs", surface.GetAttribute("data-query"));
        Assert.Equal(["docs"], host.Pane.State.SelectedRepositoryAliases);
        Assert.Equal("Alpha", host.Windows.Titles[WindowKey]);
    }

    /// <summary>A window served as a web page — a harness tab, a browser pane — has no
    /// title bar for <c>SetTitle</c>, so the page title names the surface, and follows
    /// a jump to another.</summary>
    [Fact]
    public async Task The_page_title_names_the_surface_and_follows_a_jump()
    {
        using var host = await CreateHostAsync();
        var window = host.Render("alpha");

        Assert.Equal("Alpha - Backlog", PageTitleOf(host, window));

        window.Find("[data-testid='alpha-jump']").Click();

        window.WaitForAssertion(() => Assert.Equal("Beta - Backlog", PageTitleOf(host, window)));
    }

    private static string PageTitleOf(Host host, IRenderedComponent<SurfaceWindow> window)
    {
        var content = window.FindComponent<Microsoft.AspNetCore.Components.Web.PageTitle>().Instance.ChildContent;
        Assert.NotNull(content);
        return host.Pane.Context.Render(content).Markup.Trim();
    }

    [Theory]
    [InlineData("no-such-surface")]
    [InlineData("proposals")]
    public async Task An_id_the_catalog_does_not_offer_shows_not_available_and_opens_no_surface(string surfaceId)
    {
        using var host = await CreateHostAsync();
        var window = host.Render(surfaceId);

        Assert.Equal("This surface is not available.", window.Find("[data-testid='surface-window-unavailable']").TextContent.Trim());
        Assert.Empty(window.FindAll("[data-testid='alpha-body']"));
        Assert.Empty(window.FindAll("[data-testid='beta-body']"));
        Assert.Empty(window.FindAll("[data-testid='surface-window-show-in-main']"));
    }

    [Fact]
    public async Task A_jump_inside_the_window_switches_the_surface_it_shows_and_its_title()
    {
        using var host = await CreateHostAsync();
        var window = host.Render("alpha");

        window.Find("[data-testid='alpha-jump']").Click();

        window.WaitForAssertion(() =>
        {
            Assert.Empty(window.FindAll("[data-testid='alpha-body']"));
            Assert.Equal("item=7", window.Find("[data-testid='beta-surface'] [data-testid='beta-body']").GetAttribute("data-query"));
            Assert.Equal("Beta", window.Find("[data-testid='surface-window-title']").TextContent.Trim());
        });
        Assert.Equal("Beta", host.Windows.Titles[WindowKey]);

        // The main window was not asked: the jump stayed in this window.
        Assert.Empty(host.MainWindowRequests);
    }

    [Fact]
    public async Task A_jump_to_a_surface_the_catalog_cannot_show_in_a_window_goes_to_the_main_window()
    {
        using var host = await CreateHostAsync();
        var window = host.Render("beta");

        window.Find("[data-testid='beta-jump']").Click();

        window.WaitForAssertion(() => Assert.Equal(["tasks"], host.MainWindowRequests.Select(request => request.SurfaceId)));
        Assert.NotEmpty(window.FindAll("[data-testid='beta-body']"));
        Assert.Equal(1, host.Windows.Activations);
    }

    [Fact]
    public async Task Show_in_main_window_shows_the_surface_there_and_closes_this_window()
    {
        using var host = await CreateHostAsync();
        var window = host.Render("alpha", "?note=kept");

        window.Find("[data-testid='surface-window-show-in-main']").Click();

        window.WaitForAssertion(() => Assert.Equal([WindowKey], host.Windows.Closed));
        var request = Assert.Single(host.MainWindowRequests);
        Assert.Equal("alpha", request.SurfaceId);
        Assert.Equal("kept", request.Value("note"));
        Assert.Equal(1, host.Windows.Activations);
        // Shown in the main window, not popped out into another one.
        Assert.Empty(host.Windows.Opened);
    }

    /// <summary>
    /// Escape in a popped-out window closes nothing (local ADR 0043 §4): the window
    /// marks no surface for the document-level listener, registers none with it — that
    /// listener is one per WebView, and only the main window's shell owns it — and a
    /// press inside the surface neither closes the window nor asks the main window for
    /// anything, so no other window is touched either.
    /// </summary>
    [Fact]
    public async Task Escape_in_a_popped_out_window_closes_nothing_here_or_in_another_window()
    {
        using var host = await CreateHostAsync();
        var window = host.Render("alpha");

        Assert.Empty(window.FindAll("[data-escape-closes]"));
        // The surface's landmark carries no key handler at all.
        Assert.Throws<MissingEventHandlerException>(() =>
            window.Find("[data-testid='alpha-surface']").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" }));

        Assert.NotEmpty(window.FindAll("[data-testid='alpha-body']"));
        Assert.Empty(host.Windows.Closed);
        Assert.Empty(host.MainWindowRequests);
        Assert.DoesNotContain(
            host.Pane.Context.JSInterop.Invocations,
            invocation => invocation.Identifier.StartsWith("backlogTakeoverEscape.", StringComparison.Ordinal));
    }

    [Fact]
    public void The_route_carries_the_surface_and_its_query_there_and_back()
    {
        var path = SurfaceWindowRoute.PathFor("devbook", new Dictionary<string, string>
        {
            ["chapter"] = ".devbook/arc42/adr/0040 a.md#status",
            ["scope"] = "JSdotNet/Backlog",
        });

        Assert.StartsWith("/window/devbook?", path, StringComparison.Ordinal);
        var query = SurfaceWindowRoute.QueryOf("https://0.0.0.1" + path);
        Assert.Equal(".devbook/arc42/adr/0040 a.md#status", query["chapter"]);
        Assert.Equal("JSdotNet/Backlog", query["scope"]);
        Assert.Empty(SurfaceWindowRoute.QueryOf("https://0.0.0.1/window/devbook"));
    }

    [Fact]
    public async Task The_main_window_attached_last_answers_and_a_detached_one_is_not_asked()
    {
        var surfaces = new MainWindowSurfaces();
        var asked = new List<string>();
        using var first = surfaces.Attach(_ => { asked.Add("first"); return Task.FromResult(true); });
        var second = surfaces.Attach(_ => { asked.Add("second"); return Task.FromResult(true); });

        Assert.True(await surfaces.ShowAsync(new SurfaceRequest("alpha", new Dictionary<string, string>())));
        second.Dispose();
        Assert.True(await surfaces.ShowAsync(new SurfaceRequest("alpha", new Dictionary<string, string>())));
        first.Dispose();
        Assert.False(await surfaces.ShowAsync(new SurfaceRequest("alpha", new Dictionary<string, string>())));

        Assert.Equal(["second", "first"], asked);
    }

    /// <summary>
    /// A task written outside this window — the MCP server, or the main window's
    /// list, through the process-wide task signal (local ADR 0043 §5) — reloads the
    /// window's own list, and what the window shows follows.
    /// </summary>
    [Fact]
    public async Task A_task_written_outside_the_window_reloads_its_list_and_the_window_shows_it()
    {
        var signal = new TaskChangeSignal();
        using var host = await CreateHostAsync(signal);
        var elsewhere = host.Pane.EntriesElsewhere(signal);
        var saved = Assert.IsType<Ok<SavedTaskDto>>((await elsewhere.SaveFromTextAsync(null, "# Named in the main window\n`task` `!ready`\n", 0, cancellationToken: TestContext.Current.CancellationToken)).Value).Value;
        await host.Pane.State.ReloadFromStoreAsync();

        var window = host.Render("rows");
        Assert.Equal(["Named in the main window"], RowTitles(window));

        // Off the renderer's thread, as a request to the MCP endpoint is.
        var renamed = await Task.Run(() => elsewhere.SaveFromTextAsync(
            saved.Entry.Id, "# Renamed in the main window\n`task` `!ready`\n", 0));
        Assert.IsNotType<Error>(renamed.Value);

        window.WaitForAssertion(
            () => Assert.Equal(["Renamed in the main window"], RowTitles(window)),
            TimeSpan.FromSeconds(10));
    }

    /// <summary>A popped-out window's Inbox hears an MCP capture as the main
    /// window's does: its own Inbox state reloads, on its own dispatcher.</summary>
    [Fact]
    public async Task An_inbox_capture_made_outside_the_window_reloads_its_inbox()
    {
        using var temp = new TemporaryDirectory("backlog-surface-window-tests");
        using var host = await CreateHostAsync();
        var signal = new InboxChangeSignal();
        host.Pane.Context.Services.AddSingleton<IInboxChangeSignal>(signal);
        host.Pane.Context.Services.AddSingleton(new Backlog.Infrastructure.GitHub.GitHubSettingsStore(
            temp.Combine("github.json")));
        var fake = InboxTestHost.AddInboxState(host.Pane.Context.Services);

        var window = host.Render("rows");
        var inbox = host.Pane.Context.Services.GetRequiredService<InboxDesktopState>();
        await window.InvokeAsync(inbox.ReloadAsync);
        Assert.Empty(inbox.Items);

        await Task.Run(() =>
        {
            fake.Seed("Remind me to call the plumber");
            signal.Raise();
        }, TestContext.Current.CancellationToken);

        window.WaitForAssertion(
            () => Assert.Equal("Remind me to call the plumber", Assert.Single(inbox.Items).Title),
            TimeSpan.FromSeconds(10));
    }

    /// <summary>A window that closed stops listening: a write after it reloads
    /// nothing into the list it held.</summary>
    [Fact]
    public async Task A_closed_window_no_longer_reloads_for_a_write_made_outside_it()
    {
        var signal = new TaskChangeSignal();
        using var host = await CreateHostAsync(signal);
        var elsewhere = host.Pane.EntriesElsewhere(signal);
        var saved = Assert.IsType<Ok<SavedTaskDto>>((await elsewhere.SaveFromTextAsync(null, "# Named before the window closed\n`task` `!ready`\n", 0, cancellationToken: TestContext.Current.CancellationToken)).Value).Value;
        await host.Pane.State.ReloadFromStoreAsync();

        host.Render("rows");
        await host.Pane.Context.DisposeComponentsAsync();

        var changed = 0;
        host.Pane.State.Changed += () => Interlocked.Increment(ref changed);

        var renamed = await Task.Run(() => elsewhere.SaveFromTextAsync(
            saved.Entry.Id, "# Renamed after the window closed\n`task` `!ready`\n", 0));
        Assert.IsNotType<Error>(renamed.Value);

        // Long enough for a reload the dispatcher had queued to land; none should.
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.Equal(0, Volatile.Read(ref changed));
        Assert.Equal("Named before the window closed", Assert.Single(host.Pane.State.Rows).PreviewTitle);
    }

    /// <summary>A popped-out window's Sessions list hears a run file written by the
    /// delivery surface's MCP tools, through the one process-wide run signal: the run
    /// appears in place, and the window reads no sessions again for it.</summary>
    [Fact]
    public async Task A_run_written_outside_the_window_shows_in_its_sessions_list()
    {
        using var host = await CreateHostAsync();
        var signal = new DeliveryRunChangeSignal();
        var (sessions, runs) = AddSessions(host, signal, [PoppedRun("run-1")]);

        var window = host.Render("sessions");
        window.WaitForAssertion(
            () => Assert.Equal(["run-1"], RunIds(window)),
            TimeSpan.FromSeconds(10));

        runs.Runs = [PoppedRun("run-2", minutesAgo: 1), PoppedRun("run-1")];
        await Task.Run(signal.Raise, TestContext.Current.CancellationToken);

        window.WaitForAssertion(
            () => Assert.Equal(["run-2", "run-1"], RunIds(window)),
            TimeSpan.FromSeconds(10));
        Assert.Equal(1, sessions.Reads);
    }

    /// <summary>A Sessions window that closed stops listening: a run written after it
    /// reads nothing.</summary>
    [Fact]
    public async Task A_closed_sessions_window_no_longer_reads_for_a_run_written_outside_it()
    {
        using var host = await CreateHostAsync();
        var signal = new DeliveryRunChangeSignal();
        var (sessions, runs) = AddSessions(host, signal, [PoppedRun("run-1")]);

        var window = host.Render("sessions");
        window.WaitForAssertion(
            () => Assert.Equal(["run-1"], RunIds(window)),
            TimeSpan.FromSeconds(10));
        await host.Pane.Context.DisposeComponentsAsync();

        await Task.Run(signal.Raise, TestContext.Current.CancellationToken);

        // Long enough for a read the dispatcher had queued to start; none should.
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        Assert.Equal(1, runs.Reads);
        Assert.Equal(1, sessions.Reads);
    }

    private const string PoppedFolder = @"D:\Repos\Backlog\.claude\worktrees\keen-bose-667825";

    private static (StubSessionSource Sessions, PoppedRunSource Runs) AddSessions(Host host, IDeliveryRunChangeSignal signal, IReadOnlyList<DeliveryRun> runs)
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = new StubSessionSource(
        [
            new AgentSession(
                Id: "5905cf2d",
                Kind: AgentSessionKind.Claude,
                EnvironmentId: "tower",
                Environment: "DEV-TOWER",
                Title: "keen-bose-667825",
                WorkingFolder: PoppedFolder,
                Repository: null,
                Branch: "claude/desktop-session-area",
                StartedAt: now.AddHours(-2),
                LastActivityAt: now.AddMinutes(-1),
                State: AgentSessionState.Running,
                TurnCount: 12,
                Origin: AgentSessionOrigin.Local)
        ]);
        var source = new PoppedRunSource(runs);

        var services = host.Pane.Context.Services;
        services.AddSingleton(new ShellSurface("sessions", "Sessions", ShellSurfaceGroup.Takeover, 40, typeof(SessionsSurface)));
        services.AddSingleton<IAgentSessionSource>(sessions);
        services.AddSingleton<IDeliveryRunSource>(source);
        services.AddSingleton(signal);

        return (sessions, source);
    }

    private static DeliveryRun PoppedRun(string id, int minutesAgo = 10)
    {
        var now = DateTimeOffset.UtcNow;

        return new DeliveryRun(
            Id: id,
            Dashboard: "orch-dashboard",
            Worktree: DeliveryRunWorktrees.KeyOf(PoppedFolder)!,
            WorktreeName: "keen-bose-667825",
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            SkillId: "orch-feature",
            Title: "A run",
            Status: "in_progress",
            ChangeKind: "new-functionality",
            References: [],
            StartedAt: now.AddMinutes(-minutesAgo - 5),
            UpdatedAt: now.AddMinutes(-minutesAgo),
            Stages: [],
            TokenUsage: null,
            Context: null,
            InsightsByCategory: [],
            InsightsByServer: []);
    }

    private static string?[] RunIds(IRenderedComponent<SurfaceWindow> window) =>
        [.. window.FindAll("[data-testid='sessions-run-card'] [data-testid='sessions-run']").Select(line => line.GetAttribute("data-run-id"))];

    private sealed class PoppedRunSource(IReadOnlyList<DeliveryRun> runs) : IDeliveryRunSource
    {
        private int _reads;
        private volatile IReadOnlyList<DeliveryRun> _runs = runs;

        public int Reads => Volatile.Read(ref _reads);

        public IReadOnlyList<DeliveryRun> Runs
        {
            get => _runs;
            set => _runs = value;
        }

        public Task<DeliveryRunCatalog> GetRunsAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads);

            return Task.FromResult(new DeliveryRunCatalog(_runs, []));
        }
    }

    /// <summary>
    /// Keep on top and Remember this window live in the head's native title bar
    /// (local ADR 0043 §4; <see cref="SurfaceWindowCaption"/> decides them), so the
    /// slim header draws neither, even where the head can do both. What is left reads
    /// title, scope chips, Show in main window.
    /// </summary>
    [Fact]
    public async Task The_header_draws_no_caption_toggle_and_reads_title_scope_chips_then_show_in_main_window()
    {
        using var host = await CreateHostAsync(canRemember: true, canKeepOnTop: true);
        var window = host.Render("alpha");

        var header = window.Find("[data-testid='surface-window-header']");
        Assert.Empty(header.QuerySelectorAll("[data-testid='surface-window-on-top']"));
        Assert.Empty(header.QuerySelectorAll("[data-testid='surface-window-remember']"));
        Assert.Empty(header.QuerySelectorAll("svg.pin-icon, svg.bookmark-icon"));

        Assert.Equal(
            ["surface-window-title", "surface-window-scope", "surface-window-actions"],
            header.Children.Select(Role));
        Assert.Equal(
            ["surface-window-show-in-main"],
            header.QuerySelector(".surface-window__actions")!.Children.Select(child => child.GetAttribute("data-testid")));

        // Drawing nothing, the header changes nothing either.
        Assert.Empty(host.Windows.OnTop);
        Assert.Empty(host.Windows.Remembered);

        static string Role(AngleSharp.Dom.IElement child) =>
            child.GetAttribute("data-testid")
            ?? (child.ClassList.Contains("surface-window__scope") ? "surface-window-scope"
                : child.ClassList.Contains("surface-window__actions") ? "surface-window-actions"
                : child.LocalName);
    }

    /// <summary>
    /// The surface is handed no close of its own (local ADR 0043 §4): the OS close
    /// button closes the window, so a ✕ the surface drew would only repeat it. A
    /// surface that offers a close while it has a delegate therefore draws none here.
    /// </summary>
    [Fact]
    public async Task The_surface_is_handed_no_close_callback_and_draws_no_close_of_its_own()
    {
        using var host = await CreateHostAsync();
        var window = host.Render("alpha");

        Assert.NotEmpty(window.FindAll("[data-testid='alpha-body']"));
        Assert.Empty(window.FindAll("[data-testid='alpha-close']"));

        var parameters = window.FindComponent<DynamicComponent>().Instance.Parameters;
        Assert.NotNull(parameters);
        var close = Assert.IsType<EventCallback>(parameters[ShellSurface.CloseParameter]);
        Assert.False(close.HasDelegate);
    }

    /// <summary>A jump or a chip changes what the window shows, and the window says so,
    /// so a remembered window reopens on what it showed last.</summary>
    [Fact]
    public async Task A_jump_and_a_chip_press_report_what_the_window_shows_now()
    {
        using var host = await CreateHostAsync(canRemember: true);
        var window = host.Render("alpha", "?note=kept");

        Assert.Equal(("alpha", "note=kept"), host.Windows.LastShowing);

        window.FindAll("[data-testid='surface-window-scope-option']")[1].Click();
        Assert.Equal(("alpha", "scope=docs"), host.Windows.LastShowing);

        window.Find("[data-testid='alpha-jump']").Click();
        window.WaitForAssertion(() => Assert.Equal(("beta", "item=7"), host.Windows.LastShowing));
    }

    /// <summary>Show in main window keeps its name when a narrow window hides its words:
    /// the name is on the button, not only in the text the stylesheet may hide.</summary>
    [Fact]
    public async Task Show_in_main_window_is_named_on_the_button_itself()
    {
        using var host = await CreateHostAsync();
        var window = host.Render("alpha");

        Assert.Equal("Show in main window", window.Find("[data-testid='surface-window-show-in-main']").GetAttribute("aria-label"));
    }

    private static string[] RowTitles(IRenderedComponent<SurfaceWindow> window) =>
        [.. window.FindAll("[data-testid='rows-surface'] [data-testid='rows-row']").Select(row => row.TextContent.Trim())];

    private static async Task<Host> CreateHostAsync(TaskChangeSignal? taskWrites = null, bool canRemember = false, bool canKeepOnTop = false)
    {
        var pane = await TasksPaneHost.CreateAsync(
            roadmapTags: null,
            ["backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs"],
            taskWrites: taskWrites);
        var windows = new RecordingShellWindows { CanRemember = canRemember, CanKeepOnTop = canKeepOnTop };
        var main = new MainWindowSurfaces();
        var requests = new List<SurfaceRequest>();
        main.Attach(request =>
        {
            lock (requests) requests.Add(request);
            return Task.FromResult(true);
        });

        var services = pane.Context.Services;
        services.AddSingleton(new ShellSurface("alpha", "Alpha", ShellSurfaceGroup.Takeover, 10, typeof(AlphaSurface), QueryKeys: ["note", "scope"]));
        services.AddSingleton(new ShellSurface("beta", "Beta", ShellSurfaceGroup.Takeover, 20, typeof(BetaSurface), QueryKeys: ["item"]));
        services.AddSingleton(new ShellSurface("rows", "Rows", ShellSurfaceGroup.Takeover, 30, typeof(RowsSurface)));
        services.AddSingleton<ISurfaceCatalog, SurfaceCatalog>();
        services.AddScoped<ISurfaceNavigator, SurfaceNavigator>();
        services.AddSingleton<IShellWindows>(windows);
        services.AddSingleton(main);

        return new Host(pane, windows, requests);
    }

    private sealed class Host(TasksPaneHost pane, RecordingShellWindows windows, List<SurfaceRequest> requests) : IDisposable
    {
        public TasksPaneHost Pane { get; } = pane;

        public RecordingShellWindows Windows { get; } = windows;

        public IReadOnlyList<SurfaceRequest> MainWindowRequests
        {
            get
            {
                lock (requests) return [.. requests];
            }
        }

        public IRenderedComponent<SurfaceWindow> Render(string surfaceId, string query = "", string? windowKey = WindowKey)
        {
            Pane.Context.Services.GetRequiredService<NavigationManager>().NavigateTo($"window/{surfaceId}{query}");
            return Pane.Context.Render<SurfaceWindow>(parameters =>
            {
                parameters.Add(window => window.SurfaceId, surfaceId);
                if (windowKey is not null) parameters.AddCascadingValue(SurfaceWindowRoute.WindowKeyCascade, windowKey);
            });
        }

        public void Dispose() => Pane.Dispose();
    }

    /// <summary>A surface that draws the query it was handed, jumps to
    /// <see cref="JumpTo"/> on its button, and offers a close of its own only while it
    /// was handed a close to call, as the module panes do.</summary>
    private abstract class TestSurface : ComponentBase
    {
        [Inject]
        private ISurfaceNavigator Navigator { get; set; } = default!;

        [Parameter]
        public IReadOnlyDictionary<string, string>? Query { get; set; }

        [Parameter]
        public EventCallback OnClose { get; set; }

        protected abstract string Id { get; }

        protected abstract (string SurfaceId, Dictionary<string, string> Query) JumpTo { get; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "data-testid", $"{Id}-body");
            builder.AddAttribute(2, "data-query", string.Join(';', (Query ?? new Dictionary<string, string>())
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}")));
            builder.OpenElement(3, "button");
            builder.AddAttribute(4, "data-testid", $"{Id}-jump");
            builder.AddAttribute(5, "onclick", EventCallback.Factory.Create(this, () => Navigator.Show(JumpTo.SurfaceId, JumpTo.Query)));
            builder.AddContent(6, "Jump");
            builder.CloseElement();
            if (OnClose.HasDelegate)
            {
                builder.OpenElement(7, "button");
                builder.AddAttribute(8, "data-testid", $"{Id}-close");
                builder.AddAttribute(9, "onclick", OnClose);
                builder.AddContent(10, "✕");
                builder.CloseElement();
            }

            builder.CloseElement();
        }
    }

    private sealed class AlphaSurface : TestSurface
    {
        protected override string Id => "alpha";

        protected override (string SurfaceId, Dictionary<string, string> Query) JumpTo =>
            ("beta", new Dictionary<string, string>(StringComparer.Ordinal) { ["item"] = "7" });
    }

    private sealed class BetaSurface : TestSurface
    {
        protected override string Id => "beta";

        // Not in this test's catalog, so this window cannot show it itself.
        protected override (string SurfaceId, Dictionary<string, string> Query) JumpTo =>
            ("tasks", new Dictionary<string, string>(StringComparer.Ordinal) { ["entry"] = "42" });
    }

    /// <summary>A surface that draws the titles of the window's list, as Tasks,
    /// Board, Calendar and Roadmap draw from it. It listens to nothing itself: what
    /// it shows changes only when the window renders it again.</summary>
    private sealed class RowsSurface : ComponentBase
    {
        [Inject]
        private TasksDesktopState State { get; set; } = default!;

        [Parameter]
        public IReadOnlyDictionary<string, string>? Query { get; set; }

        [Parameter]
        public EventCallback OnClose { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "ul");
            foreach (var row in State.Rows)
            {
                builder.OpenElement(1, "li");
                builder.AddAttribute(2, "data-testid", "rows-row");
                builder.AddContent(3, row.PreviewTitle);
                builder.CloseElement();
            }

            builder.CloseElement();
        }
    }
}
