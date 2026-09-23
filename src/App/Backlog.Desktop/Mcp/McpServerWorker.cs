using System.Net;
using System.Net.Sockets;

using Backlog.Desktop.UI.Extensions;
using Backlog.Desktop.UI.Mcp;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.Mcp;
using Backlog.SharedKernel;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Desktop.Mcp;

/// <summary>
/// The loopback listener local ADR 0012 §1 decided: a Streamable HTTP MCP
/// endpoint the desktop application serves from inside its own process, on
/// <c>127.0.0.1</c> and nothing else.
/// <para>
/// A plain object whose constructor starts the listener rather than an
/// <c>IHostedService</c>, and shaped on <c>TaskSyncWorker</c> deliberately —
/// §2 names it as the pattern. The reasoning is that worker's own: "There is no
/// hosted service anywhere in this repository because there is nowhere for one
/// to start: a <c>BackgroundService</c> would be started by the generic host
/// behind the browser harness and never by the MAUI desktop head." Something
/// both heads can resolve after <c>Build()</c> starts in both. That is also why
/// nothing here is asynchronous at the edges — it is not started, it is
/// constructed.
/// </para>
/// <para>
/// <b>The web harness does not use this class</b>, and that is the arrangement
/// rather than an omission. The harness already has a Kestrel pipeline, so it
/// maps the same endpoint onto it with <c>MapMcp()</c>; what both hosts share is
/// <see cref="BacklogMcpServerRegistration.AddBacklogMcpServer"/>, which is the
/// tools and the feature gates. The two things this class adds — a listener of
/// its own, and the <c>Origin</c> and bearer-token checks in front of it — are
/// exactly the two the harness must not have: Aspire owns the harness's port, it
/// binds <c>localhost:0</c>, and the harness is not the installed app.
/// </para>
/// <para>
/// <b>Two containers.</b> The MAUI head's services are not the listener's: a
/// <c>WebApplication</c> builds a container of its own for Kestrel, routing and
/// the MCP session. §2 forbids that container from composing a second
/// <c>ITaskItems</c> or opening a second SQLite connection — "a second
/// composition would be a second <c>Changed</c> graph the panes do not listen to
/// and a second connection the sync loops do not know" — so it composes none of
/// them and forwards instead. See <see cref="BridgeToDesktopContainer"/>.
/// </para>
/// </summary>
public sealed class McpServerWorker : IDisposable
{
    /// <summary>Where the endpoint is served from, under the listener's address.
    /// Shared with the web harness through
    /// <see cref="BacklogMcpServerRegistration.EndpointPath"/>, because the two
    /// hosts answering on different paths would make the harness prove nothing
    /// about the app.</summary>
    public const string EndpointPath = BacklogMcpServerRegistration.EndpointPath;

    /// <summary>
    /// How long a listener is given to stop before it is abandoned.
    /// <para>
    /// The generic host's own default is thirty seconds, and it is spent in full
    /// whenever a session is holding the Streamable-HTTP stream open — which is
    /// the ordinary state of a connected client rather than an unusual one. Thirty
    /// seconds is a reasonable wait for a web application draining real requests;
    /// for a switch on a settings screen it is the app appearing to have hung.
    /// </para>
    /// </summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    /// <summary>How long <see cref="Dispose"/> waits for a transition the chain
    /// has not reached yet. Longer than <see cref="ShutdownTimeout"/> because a
    /// queued step may be a release <em>and</em> a bind, and short enough that a
    /// closing window is never held on it.</summary>
    private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Guards <see cref="_listener"/>, <see cref="_boundPort"/> and
    /// <see cref="_disposed"/> together, because the three are what decide
    /// whether a listener may still be created: the settings store raising
    /// <c>McpChanged</c> on one thread while the window closes on another is
    /// otherwise a port bound after disposal, which nothing would ever release.
    /// </summary>
    private readonly Lock _gate = new();

    private readonly IServiceProvider _services;
    private readonly IAppFeatureSettings _features;
    private readonly WorkspaceSettingsStore _settings;
    private readonly ILogger _log;

    /// <summary>Cancelled on disposal and handed to the start, so a bind still
    /// in progress when the window closes is asked to stop rather than left
    /// holding a socket in a process that is going away.</summary>
    private readonly CancellationTokenSource _lifetime = new();

    private WebApplication? _listener;

    /// <summary>
    /// Every start and every release this worker has queued, in order, as one
    /// chain.
    /// <para>
    /// The chain is what makes a flip of the feature switch safe to do twice in a
    /// second. Releasing a listener means disposing its host, which blocks until
    /// Kestrel has closed the socket; starting one means binding that same port.
    /// Run concurrently, the second flip's start meets the first flip's release
    /// still holding 5757 and reports a collision with itself — and this server
    /// does not retry on another port. Chained, the release is always finished
    /// before the next bind begins, and <see cref="_listener"/> — assigned under
    /// <see cref="_gate"/> before anything is queued — is never two listeners or
    /// one that has already been let go.
    /// </para>
    /// <para>
    /// Continued on <see cref="TaskScheduler.Default"/> on purpose: that is what
    /// takes the release off the thread that raised the event, which during a
    /// flip on the Settings screen is the one drawing it.
    /// </para>
    /// </summary>
    private Task _pending = Task.CompletedTask;

    /// <summary>Which port <see cref="_listener"/> was built for. Kept because
    /// the running listener cannot be asked: a port change has to rebind, and a
    /// <c>McpChanged</c> raised for the token cannot be allowed to.</summary>
    private int _boundPort;

    private bool _disposed;

    public McpServerWorker(
        IServiceProvider services,
        IAppFeatureSettings features,
        WorkspaceSettingsStore settings,
        ILogger<McpServerWorker>? log = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(settings);

        _services = services;
        _features = features;
        _settings = settings;
        _log = log ?? NullLogger<McpServerWorker>.Instance;

        // Both gates can move while the app is running, and each of them moving
        // is somebody watching to see whether it worked: the feature switch on
        // the Settings screen, and the port beside it.
        _features.Changed += OnGateChanged;
        _settings.McpChanged += OnGateChanged;

        ApplyGates();
    }

    /// <summary>Raised when the listener starts, fails to start, or stops, so a
    /// screen showing <see cref="LastError"/> or <see cref="Address"/> can
    /// redraw. It arrives on a thread pool thread whenever a start or a stop
    /// produced it: a renderer subscribing to it has to marshal.</summary>
    public event Action? Changed;

    /// <summary>True while the listener holds its port.</summary>
    public bool IsListening { get; private set; }

    /// <summary>
    /// Why the listener is not running, or null when it is or was never asked
    /// to be.
    /// <para>
    /// The overwhelmingly likely value is a port already in use, and local ADR
    /// 0012 is explicit about what happens then: "a collision at start is
    /// reported on the row, not retried on another port, because every
    /// registration names the port". A listener that moved to 5758 by itself
    /// would be a server no <c>.mcp.json</c> in the world points at, reporting
    /// success.
    /// </para>
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>Where the endpoint is, for a settings screen to show beside the
    /// token, or null while nothing is listening. Always loopback, and always
    /// the configured port.</summary>
    public Uri? Address { get; private set; }

    /// <summary>The one gate. Unlike the sync loops there is no second
    /// condition: a token is generated on demand and a port always has a value,
    /// so the feature switch is the whole of the question.</summary>
    private bool ShouldRun => _features.IsEnabled(AppFeatures.McpServer);

    /// <summary>What a person is told when the port could not be taken. The
    /// exception's own message names a socket error code and an address, which
    /// is written for whoever reads the log rather than for a settings screen;
    /// the detail goes to the log and the sentence goes here, the way
    /// <c>TaskSyncWorker.UnexpectedFailure</c> does it.</summary>
    private static string PortUnavailable(int port) =>
        $"Port {port} is already in use, so the MCP server could not start. "
        + "Close whatever is using it, or choose another port — and change it in every registration too.";

    /// <summary>Stops the listener, releases the port, and lets go of both
    /// stores. Unsubscribing matters more than stopping the listener does, for
    /// the reason <c>TaskSyncWorker.Dispose</c> gives: a worker that stayed on a
    /// singleton store's event would be kept alive by it and would go on
    /// building listeners over a provider that has been disposed underneath
    /// it.</summary>
    public void Dispose()
    {
        WebApplication? listener;
        Task pending;

        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            listener = _listener;
            pending = _pending;
            _listener = null;
            IsListening = false;
            Address = null;
        }

        _features.Changed -= OnGateChanged;
        _settings.McpChanged -= OnGateChanged;

        _lifetime.Cancel();

        // Synchronously, and on purpose. Disposing the host disposes Kestrel,
        // which closes its listening socket before it returns - so by the time
        // this method ends the port is free. Handing it to the thread pool
        // instead would let a relaunch seconds later meet its own previous
        // process still holding 5757, and this server does not retry on another
        // port; it would simply report a collision with itself.
        //
        // That reasoning is shutdown's alone. A flip of the feature switch goes
        // the other way - see ApplyGates - because there the thread being blocked
        // is drawing the screen the person just flipped, and the app carries on
        // afterwards.
        if (listener is not null) Release(listener);

        // And whatever the chain still owes, bounded. A listener queued by a flip
        // the window closed on top of is one this method never saw, and it holds
        // the port until its release runs. The wait is short and swallowed: a
        // chain that has not finished by now is not worth keeping the window open
        // for, and ShutdownTimeout below already caps each step of it.
        try
        {
            pending.Wait(DisposeDrainTimeout);
        }
        catch (Exception ex) when (ex is AggregateException or OperationCanceledException)
        {
            // Every failure in the chain was already logged where it happened.
        }

        _lifetime.Dispose();
    }

    private void OnGateChanged() => ApplyGates();

    /// <summary>
    /// Stops a listener and gives its port back.
    /// <para>
    /// Through <see cref="IDisposable"/> because <see cref="WebApplication"/>
    /// implements <see cref="IHost"/> explicitly and has no <c>Dispose</c> of
    /// its own to call. Synchronous on purpose: disposing the host disposes
    /// Kestrel, and Kestrel closes its listening socket before it returns — so
    /// when this method ends, the port is free. The asynchronous form would let
    /// a relaunch seconds later meet the previous process still holding 5757,
    /// and this server does not retry on another port; it would report a
    /// collision with itself and stop.
    /// </para>
    /// </summary>
    private static void Release(WebApplication listener) => ((IDisposable)listener).Dispose();

    /// <summary>
    /// Brings the listener into line with the gate and the configured port:
    /// started when the feature is on, gone when it is off, and rebound when the
    /// port moved under it.
    /// <para>
    /// An already-running listener on the port it was asked for is left alone
    /// rather than recycled — the same reasoning <c>TaskSyncWorker.ApplyGates</c>
    /// gives for not rescheduling its timer. That matters more here than there:
    /// <c>McpChanged</c> also fires the first time a token is generated, and
    /// tearing the port down for that would drop whatever session was mid-call.
    /// The token is read per request, so a listener never needs rebuilding for
    /// one.
    /// </para>
    /// <para>
    /// <b>Nothing blocks here, and that is the point.</b> This runs on the thread
    /// that raised the event, which for <c>IAppFeatureSettings.Changed</c> is the
    /// Settings screen's own. Releasing a listener in place would block that
    /// thread inside <c>Host.Dispose()</c> — <c>StopAsync().GetAwaiter().GetResult()</c>,
    /// up to <see cref="HostOptions.ShutdownTimeout"/>, and with a session holding
    /// the Streamable-HTTP stream open it really does take that long — while
    /// <see cref="_gate"/> was held, so a <c>McpChanged</c> from another thread
    /// queued behind it. So the decision is made under the lock and the work is
    /// handed to <see cref="_pending"/>, which runs it off this thread and in
    /// order.
    /// </para>
    /// </summary>
    private void ApplyGates()
    {
        lock (_gate)
        {
            if (_disposed) return;

            var port = _settings.McpServerPort;
            WebApplication? releasing = null;
            WebApplication? starting = null;

            if (_listener is not null && (!ShouldRun || port != _boundPort))
            {
                releasing = _listener;
                _listener = null;
                IsListening = false;
                Address = null;
                LastError = null;
            }

            if (ShouldRun && _listener is null)
            {
                _boundPort = port;
                _listener = starting = CreateListener(port);
            }

            // Both under the gate, and queued as one step: a release and the
            // start that replaces it must not overlap, or the new listener binds
            // a port the old one has not given back yet.
            if (releasing is not null || starting is not null)
            {
                _pending = Continue(_pending, releasing, starting, _boundPort);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Queues one transition behind whatever the chain is already doing, on a
    /// thread pool thread.
    /// <para>
    /// <c>Unwrap</c> because the continuation is itself asynchronous: without it
    /// the chain would carry the task that <em>starts</em> the transition rather
    /// than the one that finishes it, and the next flip would run on top of this
    /// one. <see cref="CancellationToken.None"/> for the same reason
    /// <see cref="Dispose"/> waits: a queued release still has to run when the
    /// window is closing, because it is what gives the port back.
    /// </para>
    /// </summary>
    private Task Continue(Task previous, WebApplication? releasing, WebApplication? starting, int port) =>
        previous
            .ContinueWith(
                _ => TransitionAsync(releasing, starting, port),
                CancellationToken.None,
                TaskContinuationOptions.DenyChildAttach,
                TaskScheduler.Default)
            .Unwrap();

    /// <summary>Let the old listener go, then bind the new one. Never throws:
    /// every failure inside it is caught and reported through
    /// <see cref="LastError"/> and the log, so the chain is never faulted and the
    /// next flip is never queued behind a broken task.</summary>
    private async Task TransitionAsync(WebApplication? releasing, WebApplication? starting, int port)
    {
        if (releasing is not null)
        {
            try
            {
                Release(releasing);
            }
            catch (Exception ex)
            {
                // A host that would not stop is not a reason to refuse to start
                // the next one; the bind below will say so if the port is still
                // held.
                _log.LogError(ex, "The MCP server's previous listener could not be stopped cleanly.");
            }

            // The port is free and the screen says "not listening" already - but
            // only now is that true, and a person who flipped the switch off is
            // watching the row say so.
            Changed?.Invoke();
        }

        if (starting is not null) await StartAsync(starting, port).ConfigureAwait(false);
    }

    /// <summary>
    /// The listener, built but not yet bound.
    /// <para>
    /// A <c>WebApplication</c> rather than a bare Kestrel host because
    /// <c>MapMcp</c> is an endpoint and needs routing under it, and because the
    /// slim builder is the arrangement the ASP.NET Core team maintains for a
    /// host embedded in something else.
    /// </para>
    /// </summary>
    private WebApplication CreateListener(int port)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(McpServerWorker).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });

        // The MAUI head already has logging arranged - a file in an installed
        // build, the debugger in a Debug one - and this listener is not a second
        // application with a second log. Its own providers are cleared and what
        // matters is written through _log, which is the head's.
        builder.Logging.ClearProviders();

        // Shutdown is a flip of a switch here, not a deployment draining traffic.
        // See ShutdownTimeout: the host's own thirty seconds is spent in full
        // whenever a session holds the stream open.
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = ShutdownTimeout);

        builder.WebHost.ConfigureKestrel(options =>
        {
            // Loopback and not Any: §1 binds this to the loopback interface, and
            // the binding is half of why a bearer token is enough. A listener on
            // Any would be reachable from the network the laptop is on, where
            // neither the token's strength nor the Origin check was chosen to be
            // the only defence.
            options.Listen(IPAddress.Loopback, port);

            // And both spellings of loopback, because a registration says
            // "localhost" and Windows resolves that to ::1 first. Bound to 127.0.0.1
            // alone, a hand-written http://localhost:5757/mcp is refused while the
            // app is running and listening - the failure that looks exactly like
            // the app not being open.
            //
            // Guarded rather than tried and caught: Listen only records an
            // endpoint, so a machine with IPv6 switched off would fail at the
            // bind, taking the working IPv4 listener down with it. A bind that
            // fails with IPv6 supported is a port collision, and §1 has that
            // reported rather than worked around - quietly falling back to IPv4
            // would be a server half the registrations cannot reach, reporting
            // success.
            if (Socket.OSSupportsIPv6) options.Listen(IPAddress.IPv6Loopback, port);
        });

        builder.Services.AddBacklogMcpServer().WithHttpTransport();

        BridgeToDesktopContainer(builder.Services);

        var app = builder.Build();

        // Before MapMcp and therefore before any tool runs, which is the
        // requirement rather than a preference: a refusal that happened after
        // dispatch would already have read somebody's backlog.
        app.Use(GuardAsync);

        app.MapMcp(EndpointPath);

        return app;
    }

    /// <summary>
    /// Binds the port, and reports it either way.
    /// <para>
    /// Never awaited by the thread that asked for it: the constructor and a flag
    /// flip both run on the thread drawing the window, and what happened arrives
    /// through <see cref="Changed"/> the way a sync cycle's result does. It is
    /// awaited inside <see cref="TransitionAsync"/> only so the next transition
    /// queues behind this bind rather than beside it. Everything is caught, so
    /// the chain is never left faulted.
    /// </para>
    /// </summary>
    private async Task StartAsync(WebApplication listener, int port)
    {
        try
        {
            // Before the bind, so the token exists the moment the server does.
            // Leaving it to the first request would be a chicken-and-egg: the
            // token has to be in a registration before a client can make one,
            // and there would be nothing for the Settings screen to show until
            // somebody had already connected without it.
            _ = _settings.EnsureMcpServerToken();

            await listener.StartAsync(_lifetime.Token).ConfigureAwait(false);

            lock (_gate)
            {
                // Somebody switched the feature off, or moved the port, while
                // this was binding: that listener has already been disposed and
                // this one is not ours to report.
                if (!ReferenceEquals(_listener, listener)) return;

                IsListening = true;
                LastError = null;
                Address = new Uri($"http://{IPAddress.Loopback}:{port}{EndpointPath}");
            }

            _log.LogInformation("The MCP server is listening on http://127.0.0.1:{Port}{Path}.", port, EndpointPath);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed mid-bind. Nothing failed, and there is nobody
            // left to tell about it.
            Release(listener);
            return;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_listener, listener))
                {
                    _listener = null;
                    IsListening = false;
                    Address = null;
                    LastError = PortUnavailable(port);
                }
            }

            Release(listener);

            _log.LogError(ex, "The MCP server could not bind 127.0.0.1:{Port}. It will not try another port.", port);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// The two checks in front of every request, in the order a refusal is
    /// cheapest. Both reject before <c>MapMcp</c> is reached, so neither can let
    /// a tool run first.
    /// <para>
    /// The predicates themselves are <see cref="McpLoopbackGuard"/>'s, in
    /// <c>Backlog.Desktop.UI</c>, because no test project can reference a MAUI
    /// head and this is the security surface of the feature. What is left here
    /// is the header plumbing and the status codes.
    /// </para>
    /// <para>
    /// A header sent more than once is refused outright, which is a decision
    /// this method makes rather than the predicate. Two <c>Origin</c>s is not
    /// something a browser produces, so it is a confused proxy or an attempt to
    /// have one of the two read — and honouring either would be choosing which
    /// caller to believe.
    /// </para>
    /// </summary>
    private async Task GuardAsync(HttpContext context, RequestDelegate next)
    {
        var origin = context.Request.Headers.Origin;

        if (origin.Count > 1 || !McpLoopbackGuard.IsLoopbackOrigin(origin.Count == 0 ? null : origin[0]))
        {
            // Forbidden rather than Unauthorized: this is not a credential the
            // caller could supply and retry with, and answering "authenticate"
            // would invite it to try.
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            _log.LogWarning("An MCP request was refused: its Origin is not loopback.");
            return;
        }

        var authorization = context.Request.Headers.Authorization;

        if (authorization.Count > 1
            || !McpLoopbackGuard.IsAuthorized(
                authorization.Count == 0 ? null : authorization[0],
                _settings.EnsureMcpServerToken()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            _log.LogWarning("An MCP request was refused: no valid bearer token.");
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes the desktop application's own instances answerable from inside the
    /// listener's container, without composing any of them twice.
    /// <para>
    /// One scope of the MAUI container is opened per listener request and
    /// registered as a scoped service, so the container disposes it with the
    /// request; every port is then resolved out of <em>that</em> scope. It is
    /// the arrangement <c>TaskSyncWorker.ResolveSession</c> uses per cycle, for
    /// the same reason: <c>ITaskItems</c> and <c>IRoadmapPlanning</c> are
    /// registered <c>AddScoped</c>, and a singleton holding one would pin it.
    /// </para>
    /// <para>
    /// Which ports, read off the tool classes' own constructors rather than
    /// listed here. A list would be a second statement of what the tools need,
    /// in a project that does not own them — and the group table is already
    /// where a group is added or split, so a tool that grows a dependency is
    /// carried across without this file being edited. Anything the desktop
    /// container does not provide is deliberately left un-forwarded, so that the
    /// listener's own container still answers for <c>ILogger&lt;T&gt;</c>,
    /// <c>IOptions&lt;T&gt;</c> and the rest of the framework's own services.
    /// </para>
    /// </summary>
    private void BridgeToDesktopContainer(IServiceCollection listenerServices)
    {
        var probe = _services.GetService<IServiceProviderIsService>();

        listenerServices.AddScoped(_ => _services.CreateScope());

        foreach (var port in ForwardedPorts(probe))
        {
            listenerServices.Add(new ServiceDescriptor(
                port,
                request => request.GetRequiredService<IServiceScope>().ServiceProvider.GetRequiredService(port),
                ServiceLifetime.Scoped));
        }
    }

    /// <summary>
    /// Every service the tool classes are constructed from, plus the feature
    /// switches the two request filters read, narrowed to the ones the desktop
    /// container actually has.
    /// <para>
    /// <see cref="IAppFeatureSettings"/> is named rather than discovered because
    /// nothing constructs it: <see cref="BacklogMcpServerRegistration"/>'s
    /// filters resolve it from the request, so it appears in no tool's
    /// constructor and would be missed by the reflection below.
    /// </para>
    /// <para>
    /// The probe is <see cref="IServiceProviderIsService"/>, which every
    /// <c>Microsoft.Extensions.DependencyInjection</c> provider offers. A host
    /// that somehow has none forwards everything, which is the behaviour that
    /// fails loudly rather than the one that silently serves a tool with the
    /// wrong instance.
    /// </para>
    /// </summary>
    private static IEnumerable<Type> ToolPorts() =>
        BacklogMcpTools.Groups
            .Select(group => group.ToolType)
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Append(typeof(IAppFeatureSettings))
            .Distinct();

    private IEnumerable<Type> ForwardedPorts(IServiceProviderIsService? probe)
    {
        var ports = ToolPorts().ToList();
        var forwarded = ports.Where(port => probe?.IsService(port) ?? true).ToList();

        // Named at error rather than left to fail per request. A port the head
        // never composed is a tool that throws every time a session calls it,
        // and the exception it throws names a type rather than the arrangement
        // that is wrong. tests/Backlog.HostComposition.UnitTests is the gate
        // that stops this reaching a build; this is what says so on a machine
        // where it already did.
        var missing = ports.Except(forwarded).ToList();

        if (missing.Count > 0)
        {
            _log.LogError(
                "The desktop container composes no {Missing}, so the MCP tools that need them will fail when called.",
                string.Join(", ", missing.Select(port => port.Name)));
        }

        return forwarded;
    }
}
