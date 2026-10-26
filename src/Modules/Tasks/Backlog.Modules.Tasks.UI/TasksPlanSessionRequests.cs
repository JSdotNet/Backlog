using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The filter bar's view of plan session requests (local ADR 0037 §2): which plan is
/// waiting for a session, the plan chip's queue icon press and Cancel, and the
/// repositories each plan is filed under, which the icon's offer is made from.
/// <para>
/// The requests move where the pane cannot hear it, so while the action is offered they
/// are read again every <see cref="PollInterval"/>, and <see cref="Changed"/> is raised
/// only when the answer differs — <see cref="TasksSessionRequests"/>' terms. The plan's
/// repositories change only when the roadmap is edited, so they are read once and again
/// on the port's <see cref="IPlanTagSessionRequests.PlanChanged"/>, not on the poll.
/// </para>
/// </summary>
public sealed class TasksPlanSessionRequests : IDisposable
{
    /// <summary>How often the open requests are read again while the action is offered.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IPlanTagSessionRequests? _port;
    private readonly ITimer? _poll;
    private IReadOnlyDictionary<string, EntrySessionRequest> _open =
        new Dictionary<string, EntrySessionRequest>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, PlanFiling> _repositories =
        new Dictionary<string, PlanFiling>(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

    /// <summary>Whether the repositories read are current: until they are, every
    /// refresh reads them. False before the first read, after a read that failed, and
    /// after a plan change heard while the action was not offered — so a switch turned
    /// on later reads the plan as it is now, not as it was when the switch went off.</summary>
    private volatile bool _repositoriesRead;

    /// <summary>Moved on by every press, before it and again once its answer is in, so a
    /// poll that set out before either cannot land after it and put back the answer the
    /// press replaced.</summary>
    private int _version;

    /// <summary>Moved on by every repositories read, so of two that overlap — back-to-back
    /// roadmap saves — the one that set out last is the one kept.</summary>
    private int _repositoriesVersion;

    /// <param name="port">The port, or null when the host composed none.</param>
    /// <param name="clock">What the poll waits on; the system clock when null.</param>
    public TasksPlanSessionRequests(IPlanTagSessionRequests? port, TimeProvider? clock = null)
    {
        _port = port;

        if (_port is not null)
        {
            _port.PlanChanged += OnPlanChanged;
            _poll = (clock ?? TimeProvider.System).CreateTimer(_ => _ = RefreshAsync(), null, PollInterval, PollInterval);
        }
    }

    /// <summary>Raised when a plan's wait or repositories changed, or a press started or
    /// ended. On whichever thread noticed: the pane marshals.</summary>
    public event Action? Changed;

    /// <summary>Whether the action is offered: a queue is composed and the
    /// <c>session-requests</c> switch is on.</summary>
    public bool IsOffered => _port?.IsOffered ?? false;

    /// <summary>Whether a press is still on its way.</summary>
    public bool Busy { get; private set; }

    /// <summary>The last press that could not be queued or cancelled, in words, or null.</summary>
    public string? Error { get; private set; }

    /// <summary>The request the plan tagged <paramref name="tag"/> is waiting on, or
    /// null — none open, or the action is not offered.</summary>
    public EntrySessionRequest? OpenFor(string? tag)
    {
        var key = Bare(tag);

        return IsOffered && key.Length > 0 && _open.TryGetValue(key, out var request) ? request : null;
    }

    /// <summary>Where the plan tagged <paramref name="tag"/> is filed — its roadmap item's
    /// own spelling of the tag and the repository aliases it names — or null when no
    /// roadmap item carries the tag (or none has been read yet).</summary>
    public PlanFiling? RepositoriesOf(string? tag)
    {
        var key = Bare(tag);

        return key.Length > 0 && _repositories.TryGetValue(key, out var filing) ? filing : null;
    }

    /// <summary>Reads the open requests again — and the plans' repositories, when
    /// <paramref name="withRepositories"/> or never yet read — and redraws when either changed.</summary>
    public async Task RefreshAsync(bool withRepositories = false)
    {
        if (_port is null || _disposed) return;

        if (!_port.IsOffered)
        {
            // Nothing is read while the switch is off, so a plan change heard now would
            // be lost: mark the repositories stale and the switch turned on reads them.
            if (withRepositories) _repositoriesRead = false;
            return;
        }

        var version = Volatile.Read(ref _version);
        var changed = false;

        if (withRepositories || !_repositoriesRead)
        {
            var repositoriesVersion = Interlocked.Increment(ref _repositoriesVersion);
            try
            {
                var repositories = await _port.RepositoriesAsync();
                if (!_disposed && repositoriesVersion == Volatile.Read(ref _repositoriesVersion))
                {
                    _repositoriesRead = true;
                    if (!SameRepositories(repositories, _repositories))
                    {
                        _repositories = repositories.ToDictionary(
                            pair => pair.Key,
                            pair => new PlanFiling(pair.Key, pair.Value),
                            StringComparer.OrdinalIgnoreCase);
                        changed = true;
                    }
                }
            }
            catch (Exception)
            {
                // The next refresh reads them again, unless a later read already has.
                if (repositoriesVersion == Volatile.Read(ref _repositoriesVersion)) _repositoriesRead = false;
            }
        }

        try
        {
            var read = await _port.OpenAsync();

            // Not while a press is on its way, nor after one landed since this read set
            // out: either would put back the answer the press replaced.
            if (!_disposed && !Busy && version == Volatile.Read(ref _version) && !SameOpen(read, _open))
            {
                _open = new Dictionary<string, EntrySessionRequest>(read, StringComparer.OrdinalIgnoreCase);
                changed = true;
            }
        }
        catch (Exception)
        {
            // The poll will try again.
        }

        if (changed && !_disposed) Changed?.Invoke();
    }

    /// <summary>Queues a plan request on the terms of <paramref name="offer"/>. Answers
    /// whether one is now open; refuses while a press is on its way.</summary>
    public async Task<bool> RequestAsync(PlanQueueOffer offer)
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
            _open = new Dictionary<string, EntrySessionRequest>(_open, StringComparer.OrdinalIgnoreCase) { [offer.Tag] = request };
            Interlocked.Increment(ref _version);
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
    /// on, while it is still Requested. Answers whether it was cancelled.</summary>
    public async Task<bool> CancelAsync(string? tag)
    {
        if (_port is null || Busy || OpenFor(tag) is not { State: EntrySessionRequestState.Requested } request) return false;

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

            // Read back rather than assumed: a refused cancel means a dispatcher moved
            // it on, and the chip should say where to.
            try
            {
                if (!_disposed) _open = new Dictionary<string, EntrySessionRequest>(await _port.OpenAsync(), StringComparer.OrdinalIgnoreCase);
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
        if (_port is not null) _port.PlanChanged -= OnPlanChanged;
    }

    private void OnPlanChanged() => _ = RefreshAsync(withRepositories: true);

    private static string Bare(string? tag) => (tag ?? string.Empty).Trim().TrimStart('+', '#').Trim();

    private static bool SameOpen(IReadOnlyDictionary<string, EntrySessionRequest> a, IReadOnlyDictionary<string, EntrySessionRequest> b) =>
        a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && other == pair.Value);

    private static bool SameRepositories(IReadOnlyDictionary<string, IReadOnlyList<string>> a, IReadOnlyDictionary<string, PlanFiling> b) =>
        a.Count == b.Count && a.All(pair =>
            b.TryGetValue(pair.Key, out var other)
            && string.Equals(other.Tag, pair.Key, StringComparison.Ordinal)
            && other.Repositories.SequenceEqual(pair.Value, StringComparer.OrdinalIgnoreCase));
}

/// <summary>Where a plan is filed: its roadmap item's own spelling of the tag, without the
/// sigil — what a request names, so the filter bar stores the tag the roadmap dialog
/// would — and the repository aliases the item names, in its order.</summary>
public sealed record PlanFiling(string Tag, IReadOnlyList<string> Repositories);
