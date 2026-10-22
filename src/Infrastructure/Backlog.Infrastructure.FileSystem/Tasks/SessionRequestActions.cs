using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.Tasks;

/// <summary>
/// The Start session and Start plan session actions' ports
/// (<see cref="IEntrySessionRequests"/>, <see cref="IPlanSessionRequests"/>), answered
/// out of the Sessions context's request queue (local ADR 0037).
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
/// </summary>
public sealed class SessionRequestActions(
    ISessionRequestStore? queue,
    IAppFeatureSettings? features = null,
    TimeProvider? clock = null) : IEntrySessionRequests, IPlanSessionRequests
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>The ports of a host that composed no queue: never offered, nothing
    /// open, and a press refused.</summary>
    public static SessionRequestActions NotOffered { get; } = new(null);

    /// <summary>The <c>session-requests</c> switch, when there is a queue to ask. Null
    /// features reads as on, for a test that composes the adapter alone.</summary>
    public bool IsOffered => queue is not null && (features?.IsEnabled(SessionFeatures.SessionRequests) ?? true);

    async Task<IReadOnlyDictionary<Guid, EntrySessionRequest>> IEntrySessionRequests.OpenAsync(CancellationToken cancellationToken)
    {
        var answer = new Dictionary<Guid, EntrySessionRequest>();

        foreach (var request in await OpenAsync(SessionRequestKind.Entry, cancellationToken).ConfigureAwait(false))
        {
            if (request.EntryId is { } entryId && ToEntry(request) is { } open) answer.TryAdd(entryId, open);
        }

        return answer;
    }

    async Task<IReadOnlyDictionary<string, PlanSessionRequest>> IPlanSessionRequests.OpenAsync(CancellationToken cancellationToken)
    {
        var answer = new Dictionary<string, PlanSessionRequest>(StringComparer.OrdinalIgnoreCase);

        foreach (var request in await OpenAsync(SessionRequestKind.Plan, cancellationToken).ConfigureAwait(false))
        {
            if (request.PlanTag is { Length: > 0 } tag && ToPlan(request) is { } open) answer.TryAdd(tag, open);
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

        // The store answers the open request when there is one, so what comes back is
        // always open; Requested stands in for the impossible rest.
        return ToEntry(held) ?? new EntrySessionRequest(held.Id, EntrySessionRequestState.Requested, held.Repository);
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

        return ToPlan(held) ?? new PlanSessionRequest(held.Id, PlanSessionRequestState.Requested, held.Repository);
    }

    public async Task<bool> CancelAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var outcome = await Queue.CancelAsync(requestId, cancellationToken).ConfigureAwait(false);

        return outcome.Succeeded;
    }

    private async Task<IReadOnlyList<SessionRequest>> OpenAsync(SessionRequestKind kind, CancellationToken cancellationToken)
    {
        if (queue is null) return [];

        // The three open states asked for one by one rather than the whole history,
        // which only grows: nothing deletes a Linked or Cancelled request.
        List<SessionRequest> held = [];
        try
        {
            foreach (var state in (SessionRequestState[])[SessionRequestState.Requested, SessionRequestState.Claimed, SessionRequestState.Started])
            {
                held.AddRange(await queue.ListAsync(state, cancellationToken).ConfigureAwait(false));
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
        return [.. held.Where(request => request.Kind == kind && request.IsOpenAt(now)).OrderBy(request => request.CreatedAt)];
    }

    private ISessionRequestStore Queue =>
        queue ?? throw new InvalidOperationException("No session request queue is composed in this host.");

    private EntrySessionRequest? ToEntry(SessionRequest request) =>
        request.StateAt(_clock.GetUtcNow()) switch
        {
            SessionRequestState.Requested => new(request.Id, EntrySessionRequestState.Requested, request.Repository),
            SessionRequestState.Claimed => new(request.Id, EntrySessionRequestState.Claimed, request.Repository),
            SessionRequestState.Started => new(request.Id, EntrySessionRequestState.Started, request.Repository),
            _ => null
        };

    private PlanSessionRequest? ToPlan(SessionRequest request) =>
        request.StateAt(_clock.GetUtcNow()) switch
        {
            SessionRequestState.Requested => new(request.Id, PlanSessionRequestState.Requested, request.Repository),
            SessionRequestState.Claimed => new(request.Id, PlanSessionRequestState.Claimed, request.Repository),
            SessionRequestState.Started => new(request.Id, PlanSessionRequestState.Started, request.Repository),
            _ => null
        };
}
