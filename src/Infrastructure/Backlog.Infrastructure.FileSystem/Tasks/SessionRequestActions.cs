using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.Tasks;

/// <summary>
/// The ports of the queue icons that queue an entry session and queue a plan session
/// (<see cref="IEntrySessionRequests"/>, <see cref="IPlanSessionRequests"/>), answered
/// out of the Sessions context's request queue (local ADR 0038).
/// <para>
/// A cross-context join, here for the reason <see cref="LinkedSessionStates"/> is: the
/// backlog's pane and the roadmap may not reference Sessions' published surface, so an
/// adapter that may see all three answers their ports. It is a mapping and nothing
/// else — every state is read through <see cref="SessionRequest.StateAt"/>, so a lapsed
/// claim reads as Requested, and the duplicate rule is the store's.
/// </para>
/// <para>
/// One class for both ports because they are the same queue asked about its two kinds
/// of request; each port sees only its own kind.
/// </para>
/// <para>
/// Dispatcher contact is read from <paramref name="contact"/>, the record the wait route
/// keeps; without one the port has nothing to say and warns of nothing.
/// </para>
/// </summary>
public sealed class SessionRequestActions(
    ISessionRequestStore? queue,
    IAppFeatureSettings? features = null,
    TimeProvider? clock = null,
    DispatcherContact? contact = null) : IEntrySessionRequests, IPlanSessionRequests, ISessionQueueOrder
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>The ports of a host that composed no queue: never offered, nothing
    /// open, and a press refused.</summary>
    public static SessionRequestActions NotOffered { get; } = new(null);

    /// <summary>The <c>session-requests</c> switch, when there is a queue to ask. Null
    /// features reads as on, for a test that composes the adapter alone.</summary>
    public bool IsOffered => queue is not null && (features?.IsEnabled(SessionFeatures.SessionRequests) ?? true);

    public DispatcherContactReading DispatcherContact =>
        queue is not null && contact is not null
            ? new DispatcherContactReading(contact.LastHeardAt, contact.IsSilent)
            : DispatcherContactReading.Unknown;

    async Task<IReadOnlyDictionary<Guid, EntrySessionRequest>> IEntrySessionRequests.OpenAsync(CancellationToken cancellationToken)
    {
        var answer = new Dictionary<Guid, EntrySessionRequest>();

        foreach (var (request, position) in await OpenAsync(SessionRequestKind.Entry, cancellationToken).ConfigureAwait(false))
        {
            if (request.EntryId is { } entryId && ToEntry(request, position) is { } open) answer.TryAdd(entryId, open);
        }

        return answer;
    }

    async Task<IReadOnlyDictionary<string, PlanSessionRequest>> IPlanSessionRequests.OpenAsync(CancellationToken cancellationToken)
    {
        var answer = new Dictionary<string, PlanSessionRequest>(StringComparer.OrdinalIgnoreCase);

        foreach (var (request, position) in await OpenAsync(SessionRequestKind.Plan, cancellationToken).ConfigureAwait(false))
        {
            if (request.PlanTag is { Length: > 0 } tag && ToPlan(request, position) is { } open) answer.TryAdd(tag, open);
        }

        return answer;
    }

    public async Task<EntrySessionRequest> RequestAsync(
        Guid entryId,
        string repository,
        string cloneFolder,
        CancellationToken cancellationToken = default)
    {
        var held = await Queue.RequestAsync(
            SessionRequests.ForEntry(entryId, repository, cloneFolder, _clock.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);

        var position = await PositionOfAsync(held, cancellationToken).ConfigureAwait(false);

        // The store answers the open request when there is one, so what comes back is
        // always open; Requested stands in for the impossible rest.
        return ToEntry(held, position) ?? new EntrySessionRequest(held.Id, EntrySessionRequestState.Requested, held.Repository, held.CreatedAt, position);
    }

    async Task<PlanSessionRequest> IPlanSessionRequests.RequestAsync(
        string planTag,
        string repository,
        string cloneFolder,
        CancellationToken cancellationToken)
    {
        var held = await Queue.RequestAsync(
            SessionRequests.ForPlan(planTag, repository, cloneFolder, _clock.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);

        var position = await PositionOfAsync(held, cancellationToken).ConfigureAwait(false);

        return ToPlan(held, position) ?? new PlanSessionRequest(held.Id, PlanSessionRequestState.Requested, held.Repository, held.CreatedAt, position);
    }

    public async Task<bool> IsPausedAsync(CancellationToken cancellationToken = default)
    {
        if (queue is null) return false;

        try
        {
            return await queue.IsPausedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // An unreadable queue is not a pause, as it is not a state: it reads as running.
            return false;
        }
    }

    public Task SetPausedAsync(bool paused, CancellationToken cancellationToken = default) =>
        Queue.SetPausedAsync(paused, cancellationToken);

    public async Task<bool> CancelAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var outcome = await Queue.CancelAsync(requestId, cancellationToken).ConfigureAwait(false);

        return outcome.Succeeded;
    }

    /// <summary>The store's move, in one write; a host with no queue moves nothing.</summary>
    public async Task<bool> MoveAsync(Guid requestId, int toIndex, CancellationToken cancellationToken = default)
    {
        if (queue is null) return false;

        var outcome = await queue.MoveAsync(requestId, toIndex, cancellationToken).ConfigureAwait(false);

        return outcome.Succeeded;
    }

    /// <summary>Where <paramref name="request"/> stands among the Requested requests, or
    /// null when it is not one.</summary>
    private async Task<int?> PositionOfAsync(SessionRequest request, CancellationToken cancellationToken)
    {
        if (request.StateAt(_clock.GetUtcNow()) != SessionRequestState.Requested) return null;

        var waiting = await Queue.ListAsync(SessionRequestState.Requested, cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < waiting.Count; index++)
        {
            if (waiting[index].Id == request.Id) return index;
        }

        return null;
    }

    private async Task<IReadOnlyList<(SessionRequest Request, int? Position)>> OpenAsync(SessionRequestKind kind, CancellationToken cancellationToken)
    {
        if (queue is null) return [];

        // The three open states asked for one by one rather than the whole history,
        // which only grows: nothing deletes a Linked or Cancelled request. The Requested
        // ones come back in queue order across both kinds, which is what a position is.
        List<SessionRequest> held = [];
        var positions = new Dictionary<Guid, int>();
        try
        {
            foreach (var state in (SessionRequestState[])[SessionRequestState.Requested, SessionRequestState.Claimed, SessionRequestState.Started])
            {
                var listed = await queue.ListAsync(state, cancellationToken).ConfigureAwait(false);

                if (state == SessionRequestState.Requested)
                {
                    for (var index = 0; index < listed.Count; index++) positions.TryAdd(listed[index].Id, index);
                }

                held.AddRange(listed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // An unreadable queue is not a state: nothing reads as waiting.
            return [];
        }

        var now = _clock.GetUtcNow();

        // Oldest first, so the duplicate rule's survivor is the one a key keeps.
        return
        [
            .. held
                .Where(request => request.Kind == kind && request.IsOpenAt(now))
                .OrderBy(request => request.CreatedAt)
                .Select(request => (request, positions.TryGetValue(request.Id, out var at) ? at : (int?)null))
        ];
    }

    private ISessionRequestStore Queue =>
        queue ?? throw new InvalidOperationException("No session request queue is composed in this host.");

    private EntrySessionRequest? ToEntry(SessionRequest request, int? position) =>
        request.StateAt(_clock.GetUtcNow()) switch
        {
            SessionRequestState.Requested => new(request.Id, EntrySessionRequestState.Requested, request.Repository, request.CreatedAt, position),
            SessionRequestState.Claimed => new(request.Id, EntrySessionRequestState.Claimed, request.Repository, request.CreatedAt),
            SessionRequestState.Started => new(request.Id, EntrySessionRequestState.Started, request.Repository, request.CreatedAt),
            _ => null
        };

    private PlanSessionRequest? ToPlan(SessionRequest request, int? position) =>
        request.StateAt(_clock.GetUtcNow()) switch
        {
            SessionRequestState.Requested => new(request.Id, PlanSessionRequestState.Requested, request.Repository, request.CreatedAt, position),
            SessionRequestState.Claimed => new(request.Id, PlanSessionRequestState.Claimed, request.Repository, request.CreatedAt),
            SessionRequestState.Started => new(request.Id, PlanSessionRequestState.Started, request.Repository, request.CreatedAt),
            _ => null
        };
}
