using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Tests;

/// <summary>A session request queue held in memory, applying the
/// <see cref="SessionRequests"/> transitions against <paramref name="clock"/> the way
/// the SQLite store applies them in one write. <see cref="Add"/> puts a request in as
/// it is, so a test can hold one in any state.</summary>
internal sealed class InMemorySessionRequestStore(TimeProvider? clock = null) : ISessionRequestStore
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, SessionRequest> _held = [];

    /// <summary>How many times <see cref="NextAsync"/> was asked.</summary>
    public int NextCalls { get; private set; }

    public SessionRequest Add(SessionRequest request)
    {
        lock (_gate) _held[request.Id] = request;

        return request;
    }

    public Task<SessionRequest> RequestAsync(SessionRequest candidate, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var open = SessionRequests.OpenDuplicateOf(candidate, _held.Values, _clock.GetUtcNow());

            return Task.FromResult(open ?? Add(candidate));
        }
    }

    public Task<SessionRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate) return Task.FromResult(_held.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<SessionRequest>> ListAsync(SessionRequestState? state = null, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<SessionRequest>>(
            [
                .. _held.Values
                    .Where(request => state is null || request.StateAt(now) == state)
                    .OrderBy(request => request.CreatedAt)
                    .ThenBy(request => request.Id)
            ]);
        }
    }

    public Task<SessionRequest?> NextAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            NextCalls++;

            return Task.FromResult(SessionRequests.Waiting(_held.Values, _clock.GetUtcNow()).FirstOrDefault());
        }
    }

    public Task<SessionRequestOutcome> ClaimAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default) =>
        Apply(id, request => SessionRequests.Claim(request, dispatcher, _clock.GetUtcNow()));

    public Task<SessionRequestOutcome> ReleaseAsync(Guid id, string dispatcher, CancellationToken cancellationToken = default) =>
        Apply(id, request => SessionRequests.Release(request, dispatcher, _clock.GetUtcNow()));

    public Task<SessionRequestOutcome> ReportStartedAsync(Guid id, string dispatcher, string sessionId, CancellationToken cancellationToken = default) =>
        Apply(id, request => SessionRequests.Report(request, dispatcher, sessionId, _clock.GetUtcNow()));

    public Task<SessionRequestOutcome> CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        Apply(id, request => SessionRequests.Cancel(request, _clock.GetUtcNow()));

    public Task<IReadOnlyList<SessionRequest>> LinkAsync(
        Guid entryId,
        IReadOnlyCollection<string> planTags,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();

        lock (_gate)
        {
            var linked = _held.Values
                .Select(request => SessionRequests.Link(request, entryId, planTags, sessionId, now))
                .OfType<SessionRequest>()
                .ToList();

            foreach (var request in linked) _held[request.Id] = request;

            return Task.FromResult<IReadOnlyList<SessionRequest>>(linked);
        }
    }

    private Task<SessionRequestOutcome> Apply(Guid id, Func<SessionRequest, SessionRequestOutcome> transition)
    {
        lock (_gate)
        {
            if (!_held.TryGetValue(id, out var request))
            {
                return Task.FromResult(SessionRequestOutcome.Refused(null, $"No session request has the id '{id}'."));
            }

            var outcome = transition(request);

            if (outcome.Succeeded && outcome.Request is { } moved) _held[id] = moved;

            return Task.FromResult(outcome);
        }
    }
}
