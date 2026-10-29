using Backlog.Desktop.UI.Shell;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backlog.Desktop.Mcp;

/// <summary>
/// Every service the desktop head composed, as it registered them, so the MCP
/// listener can forward what a page served from it asks for (local ADR 0040 §7).
/// <para>
/// A snapshot the head takes just before it builds its container, because a built
/// <see cref="IServiceProvider"/> cannot be asked what it holds — only whether it
/// holds one type, which is enough for the tools' named ports and not for a Razor
/// component tree that injects whatever its panes need.
/// </para>
/// </summary>
public sealed class DesktopServiceRegistrations(IReadOnlyList<ServiceDescriptor> descriptors)
{
    public IReadOnlyList<ServiceDescriptor> Descriptors { get; } = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
}

/// <summary>
/// The pop-out pages the desktop app serves on its loopback MCP listener (local ADR
/// 0040 §7): <c>/window/{surfaceId}</c> as an interactive server page rendering
/// <see cref="SurfaceWindow"/>, for Claude Desktop's browser pane, and the web files
/// that page loads.
/// <para>
/// Beside <see cref="McpServerWorker"/> rather than inside it, because what a page
/// needs is a second concern on that listener: Razor components and their circuit,
/// the static files, and a container that answers for everything a pane injects.
/// It takes no MAUI type, so the test project that compiles the worker from source
/// compiles this beside it.
/// </para>
/// <para>
/// <b>No guard yet, so Debug only.</b> The link token that is to guard these pages is
/// the next step of the plan; until it lands, <see cref="ServedByDefault"/> is true
/// in a Debug build alone, and a Release listener maps none of this.
/// </para>
/// </summary>
internal static class SurfaceWindowPages
{
    /// <summary>Whether a listener serves the pages unless told otherwise: in a Debug
    /// build only, until the link token guards them.</summary>
    public static bool ServedByDefault =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// The paths a page and its files are served on. A request for one of them is
    /// a browser loading a page, which carries no bearer token, so the listener
    /// checks its <c>Origin</c> alone; every other path keeps both checks.
    /// <para>
    /// Named rather than inferred as "anything but the MCP routes", so a route added
    /// beside <c>/mcp</c> later keeps its token check without anyone remembering
    /// this list.
    /// </para>
    /// </summary>
    private static readonly string[] PagePrefixes = ["/window", "/_blazor", "/_framework", "/_content"];

    /// <summary>The shell's own web files, served from the web root's top level.</summary>
    private static readonly string[] PageFiles = ["/app.css", "/app.js"];

    /// <summary>
    /// Namespaces whose registrations a page never takes from the desktop container.
    /// The listener has its own web stack — routing, the renderer, its
    /// <c>NavigationManager</c> and <c>IJSRuntime</c> — and the head's MAUI and
    /// BlazorWebView plumbing belongs to its windows, not to a page; the framework's
    /// logging, options, configuration and hosting are the listener's own as well.
    /// <para>
    /// So a pane that injected <c>IOptions&lt;T&gt;</c>, <c>IConfiguration</c> or
    /// another framework service would get the listener's, not the head's. None does:
    /// a pane takes the app's own ports and stores, and the head configures those.
    /// A pane that needs the head's configuration reaches it through one of them.
    /// </para>
    /// </summary>
    private static readonly string[] HostOnlyNamespaces =
    [
        "Microsoft.AspNetCore",
        "Microsoft.Extensions",
        "Microsoft.JSInterop",
        "Microsoft.Maui",
    ];

    /// <summary>The head's own plumbing, which a page has no business resolving: the
    /// listener itself, whose disposal would take the page down with it, and the
    /// snapshot it forwards from.</summary>
    private static readonly Type[] HostOnlyTypes = [typeof(McpServerWorker), typeof(DesktopServiceRegistrations)];

    /// <summary>The names a request for a page may arrive under: loopback, as the
    /// listener is bound.</summary>
    private static readonly string[] LoopbackHosts = ["127.0.0.1", "localhost", "[::1]"];

    /// <summary>Whether <paramref name="path"/> is a page or one of its files.</summary>
    public static bool IsPagePath(PathString path) =>
        PagePrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
        || PageFiles.Any(file => path.Equals(file, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a page request was addressed to a loopback name. A browser sends no
    /// <c>Origin</c> on a same-origin GET, so the <c>Origin</c> check alone would let
    /// a site whose name was rebound to 127.0.0.1 read a rendered surface; such a
    /// request still carries that site's name as its <c>Host</c>, and is refused on
    /// it. The bearer token closes this for every other path; the link token is to
    /// close it for pages.
    /// </summary>
    public static bool IsLoopbackHost(HostString host) =>
        host.HasValue && LoopbackHosts.Contains(host.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The listener's own half of a page: the Razor components and their circuit,
    /// and <see cref="IShellWindows"/> as a browser has it — a pop-out opens a new
    /// tab, as in the web harness. Registered before the bridge forwards anything,
    /// so the desktop container's own windows are never the ones a page reaches.
    /// </summary>
    public static void AddTo(IServiceCollection listener)
    {
        listener.AddRazorComponents().AddInteractiveServerComponents();
        listener.AddScoped<IShellWindows, BrowserTabShellWindows>();
    }

    /// <summary>
    /// Forwards every service the desktop head composed that the listener does not
    /// answer for itself, so a page renders the head's panes over the head's own
    /// instances. A Blazor circuit is one scope of the listener's container, and the
    /// listener opens one desktop scope per scope of its own, so a page has the view
    /// state of a window of its own, and the data, the stores and their change
    /// signals every native window shares.
    /// <list type="bullet">
    /// <item><b>A singleton is handed over as the instance</b>, resolved from the
    /// desktop root here, at build. The listener's container disposes whatever a
    /// factory gives it when the scope that asked ends, so a singleton forwarded
    /// through one would be disposed with the first page that closed, and a
    /// disposed store would go on serving the main window, stale. An instance
    /// registered as one is never the container's to dispose. One that cannot be
    /// resolved is logged and left out, and a pane that asks for it fails
    /// loudly.</item>
    /// <item><b>A scoped or transient service comes out of the page's desktop
    /// scope</b>, whose disposal is what ends it.</item>
    /// <item><b>A type registered several times</b> is forwarded as many times, each to
    /// its own registration, so a pane that injects all of them — the settings
    /// sections, the shell surfaces — still gets all of them.</item>
    /// </list>
    /// An open generic or a keyed registration is left alone: neither is something
    /// a pane injects, and forwarding either would need a registration this
    /// container cannot express.
    /// </summary>
    public static void Forward(
        IServiceCollection listener,
        IEnumerable<ServiceDescriptor> host,
        IServiceProvider desktop,
        ILogger log)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(log);

        var own = listener.Select(descriptor => descriptor.ServiceType).ToHashSet();

        foreach (var registrations in host.Where(IsForwardable).GroupBy(descriptor => descriptor.ServiceType))
        {
            var type = registrations.Key;
            if (own.Contains(type)) continue;

            var lifetimes = registrations.Select(descriptor => descriptor.Lifetime).ToList();

            if (lifetimes.All(lifetime => lifetime == ServiceLifetime.Singleton))
            {
                ForwardSingletons(listener, type, lifetimes.Count, desktop, log);
                continue;
            }

            for (var index = 0; index < lifetimes.Count; index++)
            {
                var position = index;
                var only = lifetimes.Count == 1;
                var lifetime = lifetimes[index] == ServiceLifetime.Transient ? ServiceLifetime.Transient : ServiceLifetime.Scoped;

                listener.Add(new ServiceDescriptor(
                    type,
                    request =>
                    {
                        var scope = request.GetRequiredService<IServiceScope>().ServiceProvider;
                        return only
                            ? scope.GetRequiredService(type)
                            : scope.GetServices(type).ElementAt(position)!;
                    },
                    lifetime));
            }
        }
    }

    private static void ForwardSingletons(IServiceCollection listener, Type type, int count, IServiceProvider desktop, ILogger log)
    {
        object[] instances;
        try
        {
            instances = count == 1
                ? [desktop.GetRequiredService(type)]
                : [.. desktop.GetServices(type).OfType<object>()];
        }
#pragma warning disable CA1031 // Any failure leaves the one service out; the page reports it if a pane asks.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            log.LogWarning(ex, "The pop-out pages cannot use the desktop app's {Service}; a pane that needs it will not render.", type.Name);
            return;
        }

        foreach (var instance in instances) listener.Add(new ServiceDescriptor(type, instance));
    }

    /// <summary>
    /// The page route and its files, on the listener's pipeline after its guard.
    /// The routes assembly also holds the main window's <c>Home</c> and the Settings
    /// screen; a request that routes to any page but <see cref="SurfaceWindow"/>
    /// answers 404, so this listener serves popped-out surfaces and nothing else.
    /// </summary>
    public static void Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Revalidated on every request, as the web harness does it and for its
        // reason: a browser left to invent a freshness window serves old CSS
        // under new markup.
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache, must-revalidate",
        });

        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>() is { } page
                && page.Type != typeof(SurfaceWindow))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        app.UseAntiforgery();

        app.MapRazorComponents<SurfaceWindowDocument>()
            .AddInteractiveServerRenderMode();
    }

    private static bool IsForwardable(ServiceDescriptor descriptor)
    {
        var type = descriptor.ServiceType;

        if (descriptor.IsKeyedService || type.ContainsGenericParameters || HostOnlyTypes.Contains(type)) return false;

        // A hosted service forwarded here would be one the listener's host starts
        // a second time.
        if (type == typeof(IHostedService) || type == typeof(IServiceProvider) || type == typeof(IServiceScopeFactory)) return false;

        var name = type.Namespace ?? string.Empty;
        return !HostOnlyNamespaces.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
    }
}
