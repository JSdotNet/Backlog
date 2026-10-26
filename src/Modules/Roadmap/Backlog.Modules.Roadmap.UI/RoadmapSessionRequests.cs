using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// The roadmap's view of the plan session requests it has open: which plan is waiting
/// for a session, the queue icon's press and Cancel (local ADR 0037). The
/// requests move where the band cannot hear it, so while the action is offered they
/// are read again every <see cref="PollInterval"/>, and <see cref="Changed"/> is raised
/// only when the answer differs.
/// </summary>
public sealed class RoadmapSessionRequests : IDisposable
{
    /// <summary>How often the open requests are read again while the action is offered.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IPlanSessionRequests? _port;
    private readonly ITimer? _poll;
    private IReadOnlyDictionary<string, PlanSessionRequest> _open =
        new Dictionary<string, PlanSessionRequest>(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

    /// <summary>Moved on by every press, so a poll that set out before it cannot land
    /// after it and put back the answer the press replaced.</summary>
    private int _version;

    /// <param name="port">The queue's port, or null when the host composed none.</param>
    /// <param name="clock">What the poll waits on; the system clock when null.</param>
    public RoadmapSessionRequests(IPlanSessionRequests? port, TimeProvider? clock = null)
    {
        _port = port;

        if (_port is not null)
        {
            _poll = (clock ?? TimeProvider.System).CreateTimer(_ => _ = RefreshAsync(), null, PollInterval, PollInterval);
        }
    }

    /// <summary>Raised when a plan's wait changed, or a press started or ended.</summary>
    public event Action? Changed;

    /// <summary>Whether the action is offered: a queue is composed and the
    /// <c>session-requests</c> switch is on.</summary>
    public bool IsOffered => _port?.IsOffered ?? false;

    /// <summary>Whether a press is still on its way.</summary>
    public bool Busy { get; private set; }

    /// <summary>The last press that could not be queued or cancelled, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>The request the plan tagged <paramref name="tag"/> is waiting on, or null.</summary>
    public PlanSessionRequest? OpenFor(string? tag)
    {
        var key = (tag ?? string.Empty).Trim().TrimStart('+', '#').Trim();

        return IsOffered && key.Length > 0 && _open.TryGetValue(key, out var request) ? request : null;
    }

    /// <summary>Reads the open requests again, and redraws when they changed.</summary>
    public async Task RefreshAsync()
    {
        if (_port is null || _disposed || !_port.IsOffered) return;

        var version = Volatile.Read(ref _version);

        IReadOnlyDictionary<string, PlanSessionRequest> read;
        try
        {
            read = await _port.OpenAsync();
        }
        catch (Exception)
        {
            return;
        }

        if (_disposed || version != Volatile.Read(ref _version) || Same(read, _open)) return;

        _open = new Dictionary<string, PlanSessionRequest>(read, StringComparer.OrdinalIgnoreCase);
        Changed?.Invoke();
    }

    /// <summary>Queues a plan request on the terms of <paramref name="offer"/>.</summary>
    public async Task<bool> RequestAsync(PlanSessionOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);

        if (_port is null || !offer.IsEnabled || Busy) return false;

        Busy = true;
        Interlocked.Increment(ref _version);
        Error = null;
        Changed?.Invoke();

        try
        {
            var request = await _port.RequestAsync(offer.Tag, offer.Repository!, offer.CloneFolder!);
            _open = new Dictionary<string, PlanSessionRequest>(_open, StringComparer.OrdinalIgnoreCase) { [offer.Tag] = request };
            return true;
        }
        catch (Exception)
        {
            Error = $"Couldn't ask for a session in {offer.Repository}.";
            return false;
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Cancels the request the plan tagged <paramref name="tag"/> is waiting
    /// on, while it is still Requested.</summary>
    public async Task<bool> CancelAsync(string tag)
    {
        if (_port is null || Busy || OpenFor(tag) is not { State: PlanSessionRequestState.Requested } request) return false;

        Busy = true;
        Interlocked.Increment(ref _version);
        Error = null;
        Changed?.Invoke();

        try
        {
            var cancelled = await _port.CancelAsync(request.Id);
            if (!cancelled) Error = "The session has already started, so the request can no longer be cancelled.";
            return cancelled;
        }
        catch (Exception)
        {
            Error = "Couldn't cancel the session request.";
            return false;
        }
        finally
        {
            Busy = false;

            try
            {
                if (!_disposed) _open = new Dictionary<string, PlanSessionRequest>(await _port.OpenAsync(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // The poll will try again.
            }

            if (!_disposed) Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _poll?.Dispose();
    }

    private static bool Same(IReadOnlyDictionary<string, PlanSessionRequest> a, IReadOnlyDictionary<string, PlanSessionRequest> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && other == pair.Value);
}
