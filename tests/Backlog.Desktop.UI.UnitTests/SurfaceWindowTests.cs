using Backlog.Desktop.UI.Shell;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A popped-out window (local ADR 0040 §3, §4, §6): the slim header over one
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
    }

    /// <summary>
    /// Escape in a popped-out window closes nothing (local ADR 0040 §4): the window
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

    private static async Task<Host> CreateHostAsync()
    {
        var pane = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        var windows = new RecordingShellWindows();
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

        public IRenderedComponent<SurfaceWindow> Render(string surfaceId, string query = "")
        {
            Pane.Context.Services.GetRequiredService<NavigationManager>().NavigateTo($"window/{surfaceId}{query}");
            return Pane.Context.Render<SurfaceWindow>(parameters => parameters
                .Add(window => window.SurfaceId, surfaceId)
                .AddCascadingValue(SurfaceWindowRoute.WindowKeyCascade, WindowKey));
        }

        public void Dispose() => Pane.Dispose();
    }

    private sealed class RecordingShellWindows : IShellWindows
    {
        public Dictionary<string, string> Titles { get; } = new(StringComparer.Ordinal);

        public List<string> Closed { get; } = [];

        public int Activations { get; private set; }

        public void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null) =>
            throw new NotSupportedException("No test here pops a surface out.");

        public void Close(string windowKey) => Closed.Add(windowKey);

        public void SetTitle(string windowKey, string title) => Titles[windowKey] = title;

        public void ActivateMainWindow() => Activations++;
    }

    /// <summary>A surface that draws the query it was handed and jumps to
    /// <see cref="JumpTo"/> on its button.</summary>
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
}
