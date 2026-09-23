using Backlog.Desktop.UI.Extensions;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Desktop.Mcp;

/// <summary>
/// This machine's own MCP server, answered for the tools pane: the feature
/// switch, the configured port, why nothing is listening when nothing is, and
/// the token a registration has to carry.
/// <para>
/// Three sources, because the facts genuinely live in three places. The switch
/// is <see cref="AppFeatures.McpServer"/>, the port and the token are the
/// workspace settings, and why the socket was not taken is the only thing the
/// worker knows. Nothing here decides any of them; it reads them and says when
/// one moved.
/// </para>
/// </summary>
public sealed class DesktopMcpEndpointSource : IMcpEndpointSource, IDisposable
{
    private readonly IAppFeatureSettings _features;
    private readonly WorkspaceSettingsStore _settings;

    /// <summary>
    /// The worker, asked for rather than held.
    /// <para>
    /// A <see cref="Func{TResult}"/> and not the worker itself, and that is a
    /// correctness requirement rather than a style: <c>McpServerWorker</c>'s
    /// constructor calls <c>ApplyGates()</c>, which is what binds the port. The
    /// head resolves it deliberately after <c>Build()</c> — one line, with a
    /// comment saying why — and resolving <see cref="IDevToolService"/> must
    /// never be a second way to reach that. Somebody opening the Tools pane on a
    /// machine that has the feature switched off would otherwise be starting the
    /// server by looking at a table.
    /// </para>
    /// <para>
    /// So it is reached only where the answer genuinely requires it: the two
    /// properties that are about a socket, and the subscription. All three
    /// happen when the pane opens, long after the head has already composed the
    /// worker — so in the app this is a singleton lookup, not a construction.
    /// </para>
    /// </summary>
    private readonly Func<McpServerWorker> _worker;

    /// <summary>
    /// Set while this source is inside its own <see cref="EnsureToken"/>.
    /// <para>
    /// <c>EnsureMcpServerToken</c> raises <c>McpChanged</c> when it mints, and
    /// the worker turns that into a <c>Changed</c> of its own — so an apply that
    /// mints a token would tell the pane its listing had gone stale, from inside
    /// the apply that is still running. The guard in the pane's handler is what
    /// makes that survivable; this is what makes it not happen.
    /// </para>
    /// <para>
    /// Thread-static because it is exact: both raises are synchronous, on the
    /// thread that called in here, so suppressing per thread suppresses this
    /// mint and nothing else. An instance field would swallow a genuine port
    /// change another thread made at the same moment.
    /// </para>
    /// </summary>
    [ThreadStatic]
    private static bool _minting;

    private bool _listening;
    private bool _disposed;

    public DesktopMcpEndpointSource(
        IAppFeatureSettings features,
        WorkspaceSettingsStore settings,
        Func<McpServerWorker> worker)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(worker);

        _features = features;
        _settings = settings;
        _worker = worker;

        _settings.McpChanged += OnChanged;
    }

    /// <inheritdoc />
    public bool Enabled => _features.IsEnabled(AppFeatures.McpServer);

    /// <inheritdoc />
    public int Port => _settings.McpServerPort;

    /// <summary>Why nothing is listening, in the worker's own words — which for
    /// the overwhelmingly likely case is the port-collision sentence that ends by
    /// telling the reader to change it in every registration too. That is
    /// precisely what the row this feeds makes actionable.
    /// <para>
    /// Null without asking the worker when the feature is off, and that is not
    /// only an optimisation: it is the one state in which nothing should be able
    /// to construct the worker by reading a row. A switched-off server has its
    /// own sentence, which the describer writes from <see cref="Enabled"/>
    /// without a socket in the question at all.
    /// </para></summary>
    public string? Unavailable => Enabled ? _worker().LastError : null;

    /// <inheritdoc />
    public string EnsureToken()
    {
        // Set before the call and cleared after it in a finally, so a save that
        // threw cannot leave this thread deaf to every later change.
        var restore = _minting;
        _minting = true;

        try
        {
            return _settings.EnsureMcpServerToken();
        }
        finally
        {
            _minting = restore;
        }
    }

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

    private void OnChanged()
    {
        if (_minting) return;

        _changed?.Invoke();
    }
}
