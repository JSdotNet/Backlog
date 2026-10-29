using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

using Backlog.Desktop.Mcp;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.FileSystem;
using Backlog.SharedKernel;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// The pop-out pages the desktop app's MCP listener serves (local ADR 0040 §7):
/// <c>/window/{surfaceId}</c> rendered from the MAUI head's own container, through
/// the real <see cref="McpServerWorker"/>, compiled here from the head's source.
/// <para>
/// The container is the head's as <see cref="MauiDesktopCompositionTests"/> restates
/// it, with two probe surfaces registered beside the Devbook, and the snapshot of its
/// registrations <c>MauiProgram</c> hands the listener. The pages' files are read
/// through the desktop harness's static web assets manifest, which lists the same
/// files the head copies into its output, because a test has no head output to serve.
/// </para>
/// </summary>
public sealed class SurfaceWindowPagesTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _appData = Path.Combine(
        Path.GetTempPath(), "backlog-surface-window-pages", Guid.NewGuid().ToString("N"));

    private ServiceProvider? _provider;

    /// <summary>The page renders <see cref="SurfaceWindow"/> for the surface the
    /// address names, with what the surface's component drew — from the desktop
    /// container, under the window's slim header — and the scripts that start its
    /// circuit. No bearer token: a browser loading a page has none.</summary>
    [Fact]
    public async Task The_window_route_renders_the_surface_from_the_desktop_container()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head's container and its DPAPI credential store are Windows-only.");
            return;
        }

        using var listener = await StartAsync(servesWindowPages: true);
        using var client = listener.Client();

        using var response = await client.GetAsync("/window/probe?keep=yes&drop=no", TestContext.Current.CancellationToken);
        var page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"surface-window\"", page, StringComparison.Ordinal);
        Assert.Contains("data-surface-id=\"probe\"", page, StringComparison.Ordinal);
        Assert.Contains(">Probe</h1>", page, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"probe-query\">keep=yes</", page, StringComparison.Ordinal);
        Assert.Contains("_framework/blazor.web.js", page, StringComparison.Ordinal);
        Assert.Contains("_content/Backlog.UI.Components/components.js", page, StringComparison.Ordinal);
        Assert.Contains("app.js", page, StringComparison.Ordinal);
    }

    /// <summary>An id the catalog does not offer — the reserved <c>proposals</c> among
    /// them — is the window's "not available", as in a native window.</summary>
    [Theory]
    [InlineData("nothing-registered")]
    [InlineData(ShellSurface.ProposalsId)]
    public async Task An_unknown_surface_renders_not_available(string surfaceId)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head's container and its DPAPI credential store are Windows-only.");
            return;
        }

        using var listener = await StartAsync(servesWindowPages: true);
        using var client = listener.Client();

        var page = await client.GetStringAsync("/window/" + surfaceId, TestContext.Current.CancellationToken);

        Assert.Contains("surface-window-unavailable", page, StringComparison.Ordinal);
    }

    /// <summary>The files the page loads are served beside it, a module script as
    /// JavaScript — the type MAUI's WebView table lacks and ASP.NET Core's has.</summary>
    [Theory]
    [InlineData("/app.js", "text/javascript")]
    [InlineData("/app.css", "text/css")]
    [InlineData("/_content/Backlog.UI.Components/components.js", "text/javascript")]
    [InlineData("/_content/Backlog.UI.Components/components.css", "text/css")]
    [InlineData("/_framework/blazor.web.js", "text/javascript")]
    [InlineData("/_content/Backlog.UI.Components/lib/mermaid/mermaid.esm.min.mjs", "text/javascript")]
    public async Task The_files_the_page_loads_are_served(string path, string mediaType)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head's container and its DPAPI credential store are Windows-only.");
            return;
        }

        using var listener = await StartAsync(servesWindowPages: true);
        using var client = listener.Client();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>Serving pages waives the bearer token for them alone: <c>/mcp</c> still
    /// refuses a request without one, and a page is still refused to a foreign
    /// <c>Origin</c>.</summary>
    [Fact]
    public async Task The_mcp_endpoint_keeps_its_checks_and_a_page_keeps_the_origin_check()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head's container and its DPAPI credential store are Windows-only.");
            return;
        }

        using var listener = await StartAsync(servesWindowPages: true);
        using var client = listener.Client();

        using (var noToken = await client.PostAsync(McpServerWorker.EndpointPath, new StringContent("{}"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        }

        using (var foreign = new HttpRequestMessage(HttpMethod.Post, McpServerWorker.EndpointPath) { Content = new StringContent("{}") })
        {
            foreign.Headers.Add("Origin", "https://example.com");
            foreign.Headers.Authorization = new AuthenticationHeaderValue("Bearer", listener.Settings.EnsureMcpServerToken());
            using var refused = await client.SendAsync(foreign, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using (var page = new HttpRequestMessage(HttpMethod.Get, "/window/probe"))
        {
            page.Headers.Add("Origin", "https://example.com");
            using var refused = await client.SendAsync(page, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        // A same-origin GET carries no Origin, so a site rebound to 127.0.0.1 is
        // refused on the name it still sends as its Host.
        using (var rebound = new HttpRequestMessage(HttpMethod.Get, "/window/probe"))
        {
            rebound.Headers.Host = "attacker.example";
            using var refused = await client.SendAsync(rebound, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        }

        using (var named = new HttpRequestMessage(HttpMethod.Get, "/window/probe"))
        {
            named.Headers.Host = $"localhost:{listener.Settings.McpServerPort}";
            using var served = await client.SendAsync(named, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        }
    }

    /// <summary>
    /// A page renders over the desktop app's own singletons and never owns them: the
    /// request that rendered one ending, and the listener itself going, leave a
    /// disposable singleton of the head's undisposed — the main window goes on
    /// using it.
    /// </summary>
    [Fact]
    public async Task A_page_never_disposes_the_desktop_apps_singletons()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head's container and its DPAPI credential store are Windows-only.");
            return;
        }

        var listener = await StartAsync(servesWindowPages: true);
        var shared = _provider!.GetRequiredService<ProbeSingleton>();

        using (var client = listener.Client())
        {
            var page = await client.GetStringAsync("/window/probe", TestContext.Current.CancellationToken);
            Assert.Contains($"data-testid=\"probe-singleton\">{shared.Id}</", page, StringComparison.Ordinal);
        }

        Assert.False(shared.Disposed, "The page's request disposed a singleton of the desktop app.");

        listener.Dispose();

        Assert.False(shared.Disposed, "The listener disposed a singleton of the desktop app.");
    }

    /// <summary>The routes assembly also holds the main window and the Settings screen;
    /// the listener serves popped-out surfaces and nothing else. Neither path is a
    /// page's, so it is held to the bearer token first, and with one it is still not
    /// found.</summary>
    [Theory]
    [InlineData("/")]
    [InlineData("/settings")]
    public async Task No_other_page_of_the_shell_is_served(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head's container and its DPAPI credential store are Windows-only.");
            return;
        }

        using var listener = await StartAsync(servesWindowPages: true);
        using var client = listener.Client();

        using (var anonymous = await client.GetAsync(path, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", listener.Settings.EnsureMcpServerToken());
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A listener that does not serve the pages — a Release one, until the
    /// link token guards them — answers the route as it answers any other path: the
    /// bearer token first.</summary>
    [Fact]
    public async Task Without_the_pages_the_route_is_refused_like_any_other_path()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head's container and its DPAPI credential store are Windows-only.");
            return;
        }

        using var listener = await StartAsync(servesWindowPages: false);
        using var client = listener.Client();

        using var response = await client.GetAsync("/window/probe", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", listener.Settings.EnsureMcpServerToken());
        using var authorized = await client.GetAsync("/window/probe", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, authorized.StatusCode);
    }

    /// <summary>
    /// A surface interaction over the circuit, in a real browser: a press re-renders
    /// the surface, a jump through the window's <see cref="ISurfaceNavigator"/> switches
    /// the page to the surface asked for and retitles its tab, and two pages are two
    /// windows — each its own scope of the desktop container.
    /// <para>
    /// Skipped where no Playwright Chromium is installed, which is CI; run
    /// <c>pwsh tests/Backlog.EndToEndTests/bin/Debug/net10.0/playwright.ps1 install chromium</c>
    /// to have it run.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_surface_interaction_runs_over_the_pages_circuit()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The MAUI head's container and its DPAPI credential store are Windows-only.");
            return;
        }

        using var listener = await StartAsync(servesWindowPages: true);
        using var playwright = await Playwright.CreateAsync();

        IBrowser browser;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException ex)
        {
            Assert.Skip("No Playwright Chromium is installed here: " + ex.Message.Split('\n')[0]);
            return;
        }

        await using (browser)
        {
            var first = await browser.NewPageAsync();
            var second = await browser.NewPageAsync();
            await first.GotoAsync(listener.Address + "window/probe");
            await second.GotoAsync(listener.Address + "window/probe");

            // Interactive once the circuit answers a press: the prerendered button
            // does nothing until the circuit is connected.
            await WaitInteractiveAsync(first);
            await WaitInteractiveAsync(second);

            // Two pages, two windows: each circuit is its own scope of the desktop
            // container, so each has a navigator of its own.
            Assert.NotEqual(
                await first.GetByTestId("probe-window").TextContentAsync(),
                await second.GetByTestId("probe-window").TextContentAsync());

            // A jump in the first page switches that page, and only that page.
            await first.GetByTestId("probe-jump").ClickAsync();
            await Assertions.Expect(first.GetByTestId("surface-window-title")).ToHaveTextAsync("Probe target");
            await Assertions.Expect(first.GetByTestId("probe-target")).ToBeVisibleAsync();
            await Assertions.Expect(first).ToHaveTitleAsync("Probe target - Backlog");
            await Assertions.Expect(second.GetByTestId("surface-window-title")).ToHaveTextAsync("Probe");
        }
    }

    public void Dispose()
    {
        _provider?.Dispose();

        try
        {
            Directory.Delete(_appData, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Presses until the page's circuit answers: a press before it is
    /// connected is lost, so the first one that counts is the proof.</summary>
    private static async Task WaitInteractiveAsync(IPage page)
    {
        var presses = page.GetByTestId("probe-presses");
        var deadline = DateTime.UtcNow + Patience;
        while (await presses.TextContentAsync() == "0")
        {
            Assert.True(DateTime.UtcNow < deadline, "The page's circuit never answered a press.");
            await page.GetByTestId("probe-press").ClickAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task<Listener> StartAsync(bool servesWindowPages)
    {
        var services = MauiDesktopCompositionTests.MauiShapedServices(_appData);
        services.AddSingleton(new ShellSurface("probe", "Probe", ShellSurfaceGroup.Takeover, 900, typeof(ProbeSurface), QueryKeys: ["keep"]));
        services.AddSingleton(new ShellSurface("probe-target", "Probe target", ShellSurfaceGroup.Takeover, 901, typeof(ProbeTargetSurface)));
        services.AddSingleton<ProbeSingleton>();
        services.AddSingleton(new DesktopServiceRegistrations([.. services]));
        _provider = services.BuildServiceProvider();

        var settings = _provider.GetRequiredService<WorkspaceSettingsStore>();
        Assert.Null(settings.SetMcpServerPort(FreePort()));

        var worker = new McpServerWorker(
            _provider,
            settings,
            log: null,
            servesWindowPages,
            Path.Combine(AppContext.BaseDirectory, "Backlog.Desktop.WebHarness.staticwebassets.runtime.json"));

        var deadline = DateTime.UtcNow + Patience;
        while (!worker.IsListening)
        {
            Assert.Null(worker.LastError);
            Assert.True(DateTime.UtcNow < deadline, "The listener never came up.");
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        return new Listener(worker, settings);
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed class Listener(McpServerWorker worker, WorkspaceSettingsStore settings) : IDisposable
    {
        public WorkspaceSettingsStore Settings { get; } = settings;

        public string Address { get; } = $"http://127.0.0.1:{settings.McpServerPort}/";

        public HttpClient Client() => new() { BaseAddress = new Uri(Address) };

        public void Dispose() => worker.Dispose();
    }
}

/// <summary>A surface of the test's own: the query it was handed, a press it counts,
/// which window's navigator it was given, and a jump to <see cref="ProbeTargetSurface"/>.</summary>
public sealed class ProbeSurface : ComponentBase
{
    [Parameter]
    public IReadOnlyDictionary<string, string>? Query { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    [Inject]
    private ISurfaceNavigator Navigator { get; set; } = default!;

    [Inject]
    private ProbeSingleton Shared { get; set; } = default!;

    private int _presses;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "data-testid", "probe-query");
        builder.AddContent(2, string.Join('&', (Query ?? new Dictionary<string, string>()).Select(pair => pair.Key + "=" + pair.Value)));
        builder.CloseElement();

        builder.OpenElement(3, "p");
        builder.AddAttribute(4, "data-testid", "probe-presses");
        builder.AddContent(5, _presses);
        builder.CloseElement();

        builder.OpenElement(6, "p");
        builder.AddAttribute(7, "data-testid", "probe-window");
        builder.AddContent(8, RuntimeHelpers.GetHashCode(Navigator));
        builder.CloseElement();

        builder.OpenElement(9, "button");
        builder.AddAttribute(10, "data-testid", "probe-press");
        builder.AddAttribute(11, "onclick", EventCallback.Factory.Create(this, () => _presses++));
        builder.AddContent(12, "Press");
        builder.CloseElement();

        builder.OpenElement(13, "button");
        builder.AddAttribute(14, "data-testid", "probe-jump");
        builder.AddAttribute(15, "onclick", EventCallback.Factory.Create(this, () => Navigator.Show("probe-target")));
        builder.AddContent(16, "Jump");
        builder.CloseElement();

        builder.OpenElement(17, "p");
        builder.AddAttribute(18, "data-testid", "probe-singleton");
        builder.AddContent(19, Shared.Id);
        builder.CloseElement();
    }
}

/// <summary>A disposable singleton of the desktop container's, which a page uses
/// and must never dispose.</summary>
public sealed class ProbeSingleton : IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

/// <summary>Where <see cref="ProbeSurface"/> jumps to.</summary>
public sealed class ProbeTargetSurface : ComponentBase
{
    [Parameter]
    public IReadOnlyDictionary<string, string>? Query { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "data-testid", "probe-target");
        builder.AddContent(2, "Target");
        builder.CloseElement();
    }
}
