using System.Net.Http.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync.KnowledgeEvents;

/// <summary>
/// The two directions of knowledge event replication, as this device calls them: push a batch
/// of changes, pull the owner's knowledge event feed from a cursor. Transport and nothing else,
/// on <see cref="DevbookSubjects.DevbookSubjectSyncClient"/>'s terms: an expected 4xx comes back
/// as a failed result carrying the ProblemDetails <c>code</c>, and a bare 404 as
/// <c>sync.http_404</c>, which the session reads as a service without the route.
/// </summary>
public sealed class KnowledgeEventSyncClient
{
    /// <summary>What a pull asks for when the caller does not say — the number the endpoint
    /// would have defaulted to.</summary>
    public const int DefaultMaxItems = 100;

    private readonly HttpClient _http;

    public KnowledgeEventSyncClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
    }

    /// <summary>Writes a batch of changes into this owner's replica. Safe to send twice: the
    /// replica takes a version once and refuses its echo.</summary>
    public async Task<Result<PushKnowledgeEventsResponse>> PushAsync(
        IReadOnlyList<KnowledgeEventChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return await SyncHttp.SendAsync<PushKnowledgeEventsResponse>(
            () => _http.PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.KnowledgeEvents),
                new PushKnowledgeEventsRequest(changes),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one page of this owner's knowledge event feed.</summary>
    /// <param name="since">The cursor the previous page handed back, or null to start from the
    /// beginning.</param>
    /// <param name="maxItems">How many changes to ask for; the service clamps it.</param>
    public async Task<Result<PullKnowledgeEventsResponse>> PullAsync(
        string? since,
        int maxItems = DefaultMaxItems,
        CancellationToken cancellationToken = default) =>
        await SyncHttp.SendAsync<PullKnowledgeEventsResponse>(
            () => _http.GetAsync(ReplicaRoutes.PullRoute(SyncRoutes.KnowledgeEvents, since, maxItems), cancellationToken),
            cancellationToken).ConfigureAwait(false);
}
