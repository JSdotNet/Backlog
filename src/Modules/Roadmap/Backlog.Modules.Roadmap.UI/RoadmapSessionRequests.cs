using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.UI.Components.Data;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// The roadmap's view of the plan session requests it has open: which plan is waiting
/// for a session, the queue icon's press and Cancel (local ADR 0038). The
/// requests move where the band cannot hear it, so while the action is offered they
/// are read again every <see cref="PollInterval"/>, and <see cref="Changed"/> is raised
/// only when the answer differs — the library's <see cref="PolledSnapshot{TKey, TValue}"/>
/// keeps the answer and its guard.
/// </summary>
public sealed class RoadmapSessionRequests : IDisposable
{
    /// <summary>How often the open requests are read again while the action is offered.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IPlanSessionRequests? _port;
    private readonly PolledSnapshot<string, PlanSessionRequest> _open;

    /// <param name="port">The queue's port, or null when the host composed none.</param>
    /// <param name="clock">What the poll waits on; the system clock when null.</param>
    public RoadmapSessionRequests(IPlanSessionRequests? port, TimeProvider? clock = null)
    {
        _port = port;
        _open = new PolledSnapshot<string, PlanSessionRequest>(
            () => _port!.OpenAsync(),
            _port is null ? null : RefreshAsync,
            PollInterval,
            StringComparer.OrdinalIgnoreCase,
            clock);
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

        return IsOffered && key.Length > 0 && _open.TryGet(key, out var request) ? request : null;
    }

    /// <summary>Reads the open requests again, and redraws when they changed.</summary>
    public async Task RefreshAsync()
    {
        if (_port is null || _open.IsDisposed || !_port.IsOffered) return;

        if (await _open.RefreshAsync()) Changed?.Invoke();
    }

    /// <summary>Queues a plan request on the terms of <paramref name="offer"/>.</summary>
    public async Task<bool> RequestAsync(PlanSessionOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);

        if (_port is null || !offer.IsEnabled || Busy) return false;

        Busy = true;
        _open.Supersede();
        Error = null;
        Changed?.Invoke();

        try
        {
            var request = await _port.RequestAsync(offer.Tag, offer.Repository!, offer.CloneFolder!);
            _open.Put(offer.Tag, request);
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
        _open.Supersede();
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

            // Read back rather than assumed: a refused cancel means a dispatcher moved
            // it on, and the chip should say where to.
            await _open.ReadBackAsync();

            if (!_open.IsDisposed) Changed?.Invoke();
        }
    }

    public void Dispose() => _open.Dispose();
}
