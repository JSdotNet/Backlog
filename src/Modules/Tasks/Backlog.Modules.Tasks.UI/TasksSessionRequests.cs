using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Data;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The Tasks pane's view of the session requests its entries have open (local ADR
/// 0038): what each entry is waiting for, the press that queues one and the Cancel
/// that withdraws it.
/// <para>
/// The requests change where this pane cannot hear it — a dispatcher claims one and
/// reports its session through the MCP listener, and the session links itself — so
/// while the action is offered the open requests are read again every
/// <see cref="PollInterval"/>. It is one small read of a local table, and
/// <see cref="Changed"/> is raised only when the answer differs, so a quiet queue
/// redraws nothing. The library's <see cref="PolledSnapshot{TKey, TValue}"/> keeps the
/// answer and its guard.
/// </para>
/// <para>
/// Held by the pane rather than by <c>TasksDesktopState</c>: a request is the Sessions
/// context's record, not part of an entry, and nothing about the list's rows,
/// filters or writes depends on it.
/// </para>
/// </summary>
public sealed class TasksSessionRequests : IDisposable
{
    /// <summary>How often the open requests are read again while the action is offered.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IEntrySessionRequests? _port;
    private readonly PolledSnapshot<Guid, EntrySessionRequest> _open;
    private readonly HashSet<Guid> _busy = [];

    /// <param name="port">The queue's port, or null when the host composed none.</param>
    /// <param name="clock">What the poll waits on; the system clock when null.</param>
    public TasksSessionRequests(IEntrySessionRequests? port, TimeProvider? clock = null)
    {
        _port = port;
        _open = new PolledSnapshot<Guid, EntrySessionRequest>(
            () => _port!.OpenAsync(),
            _port is null ? null : RefreshAsync,
            PollInterval,
            clock: clock);
    }

    /// <summary>Raised when what an entry is waiting for changed, or a press started
    /// or ended. On whichever thread noticed: the pane marshals.</summary>
    public event Action? Changed;

    /// <summary>Whether the action is offered: a queue is composed and the
    /// <c>session-requests</c> switch is on.</summary>
    public bool IsOffered => _port?.IsOffered ?? false;

    /// <summary>The last press that could not be queued or cancelled, in words, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>The request <paramref name="entryId"/> is waiting on, or null — none
    /// open, or the action is not offered.</summary>
    public EntrySessionRequest? OpenFor(Guid? entryId) =>
        IsOffered && entryId is { } id && _open.TryGet(id, out var request) ? request : null;

    /// <summary>Whether a press for <paramref name="entryId"/> is still on its way.</summary>
    public bool IsBusy(Guid? entryId) => entryId is { } id && _busy.Contains(id);

    /// <summary>Reads the open requests again, and redraws when they changed.</summary>
    public async Task RefreshAsync()
    {
        if (_port is null || _open.IsDisposed || !_port.IsOffered) return;

        if (await _open.RefreshAsync()) Changed?.Invoke();
    }

    /// <summary>Queues a request for <paramref name="entryId"/> in
    /// <paramref name="target"/>'s repository. Answers whether one is now open.</summary>
    public async Task<bool> RequestAsync(Guid entryId, SessionStartTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (_port is null || !target.HasCloneFolder || !_busy.Add(entryId)) return false;

        _open.Supersede();
        Error = null;
        Changed?.Invoke();

        try
        {
            var request = await _port.RequestAsync(entryId, target.Repository, target.CloneFolder!);
            _open.Put(entryId, request);
            return true;
        }
        catch (Exception)
        {
            Error = $"Couldn't ask for a session in {target.Repository}.";
            return false;
        }
        finally
        {
            _busy.Remove(entryId);
            Changed?.Invoke();
        }
    }

    /// <summary>Cancels the request <paramref name="entryId"/> is waiting on, while it
    /// is still Requested. Answers whether it was cancelled.</summary>
    public async Task<bool> CancelAsync(Guid entryId)
    {
        if (_port is null || OpenFor(entryId) is not { State: EntrySessionRequestState.Requested } request || !_busy.Add(entryId)) return false;

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
            _busy.Remove(entryId);

            // Read back rather than assumed: a refused cancel means a dispatcher moved
            // it on, and the chip should say where to.
            await _open.ReadBackAsync();

            if (!_open.IsDisposed) Changed?.Invoke();
        }
    }

    public void Dispose() => _open.Dispose();
}
