using Backlog.Desktop.UI.Extensions;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Desktop.Mcp;

/// <summary>
/// This machine's own MCP server, answered for the tools pane and the Settings
/// screen's MCP server page: the configured port, whether and where the listener
/// holds it, why nothing is listening when nothing is, and the escape hatch's
/// token where a debug setting switched it on.
/// <para>
/// Two sources, because the facts genuinely live in two places. The port and
/// the escape hatch are the workspace settings, and why the socket was not taken is
/// the only thing the worker knows. Nothing here decides any of them; it reads
/// them and says when one moved.
/// </para>
/// </summary>
public sealed class DesktopMcpEndpointSource : IMcpEndpointSource, IDisposable
{
    private readonly WorkspaceSettingsStore _settings;

    /// <summary>
    /// The worker, asked for rather than held.
    /// <para>
    /// A <see cref="Func{TResult}"/> and not the worker itself, and that is a
    /// correctness requirement rather than a style: <c>McpServerWorker</c>'s
    /// constructor calls <c>ApplyGates()</c>, which is what binds the port. The
    /// head resolves it deliberately after <c>Build()</c> — one line, with a
    /// comment saying why — and resolving <see cref="IDevToolService"/> must
    /// never be a second way to reach that, or composing the tool service
    /// before <c>Build()</c> had finished would be what bound the port.
    /// </para>
    /// <para>
    /// So it is reached only where the answer genuinely requires it: the
    /// properties that are about a socket, <see cref="Retry"/>, and the
    /// subscription. All of them happen when a pane or the Settings page opens,
    /// long after the head has already composed the worker — so in the app this
    /// is a singleton lookup, not a construction.
    /// </para>
    /// </summary>
    private readonly Func<McpServerWorker> _worker;

    private bool _listening;
    private bool _disposed;

    public DesktopMcpEndpointSource(
        WorkspaceSettingsStore settings,
        Func<McpServerWorker> worker)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(worker);

        _settings = settings;
        _worker = worker;

        _settings.McpChanged += OnChanged;
    }

    /// <inheritdoc />
    public int Port => _settings.McpServerPort;

    /// <summary>Why nothing is listening, in the worker's own words — which for
    /// the overwhelmingly likely case is the port-collision sentence that ends by
    /// telling the reader to change it in every registration too. That is
    /// precisely what the row this feeds makes actionable.</summary>
    public string? Unavailable => _worker().LastError;

    /// <inheritdoc />
    public bool IsListening => _worker().IsListening;

    /// <inheritdoc />
    /// <remarks>The worker's own <c>Address</c>, which it builds with
    /// <c>BacklogMcpServerRegistration.EndpointUri</c> as it binds — the helper a
    /// registration is written with — so it is never restated here.</remarks>
    public Uri? Endpoint => _worker().Address;

    /// <inheritdoc />
    /// <remarks>Through the store and not the worker: the port is a setting, and
    /// the worker already follows the store's <c>McpChanged</c> to rebind. Going
    /// to the worker directly would be a second path to the same rebind.</remarks>
    public string? SetPort(int port) => _settings.SetMcpServerPort(port);

    /// <inheritdoc />
    public void Retry() => _worker().Retry();

    /// <inheritdoc />
    /// <remarks>The store's, which reads it off this process's environment and only while the
    /// debug setting is on; nothing is minted or saved.</remarks>
    public string? EnsureToken() => _settings.EnvironmentMcpToken;

    /// <inheritdoc />
    public string? CurrentToken => _settings.EnvironmentMcpToken;

    /// <inheritdoc />
    /// <remarks>
    /// Written out rather than left to the compiler because the <c>add</c> has
    /// something of its own to do — see <see cref="Listen"/>. The combine and
    /// the remove are then this file's job to get right, and they are done the
    /// way the compiler would have done them: a compare-and-swap loop, not
    /// <c>_changed += value</c>. The raise arrives off a thread pool thread and
    /// the pane subscribes off the renderer's, so two threads reaching a
    /// read-modify-write on the same field is the ordinary case here rather than
    /// a contrived one, and losing a subscription that way is a pane that
    /// silently stops re-listing.
    /// </remarks>
    public event Action? Changed
    {
        add
        {
            // The worker's own event, taken on the first subscriber rather than
            // in the constructor. See _worker: this is the point at which asking
            // for it is safe, because nothing subscribes to a pane that is not
            // open.
            Listen();

            Action? seen;
            Action? combined;
            do
            {
                seen = _changed;
                combined = (Action?)Delegate.Combine(seen, value);
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _changed, combined, seen), seen));
        }

        remove
        {
            Action? seen;
            Action? remaining;
            do
            {
                seen = _changed;
                remaining = (Action?)Delegate.Remove(seen, value);
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _changed, remaining, seen), seen));
        }
    }

    private Action? _changed;

    /// <summary>Lets go of both events. A source that stayed on a singleton
    /// store's event would be kept alive by it, which is
    /// <c>McpServerWorker.Dispose</c>'s own reasoning for the same line.</summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _settings.McpChanged -= OnChanged;

        if (_listening)
        {
            _worker().Changed -= OnChanged;
            _listening = false;
        }
    }

    private void Listen()
    {
        if (_listening || _disposed) return;

        _listening = true;
        _worker().Changed += OnChanged;
    }

    private void OnChanged() => _changed?.Invoke();
}
