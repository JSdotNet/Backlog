using System.Net.Http.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync.DevbookSubjects;

/// <summary>
/// The two directions of devbook subject replication, as this device calls
/// them: push a batch of changes, pull the owner's subject feed from a cursor.
/// Transport and nothing else, on <see cref="Annotations.AnnotationSyncClient"/>'s
/// terms, with the same failure convention: an expected 4xx comes back as a
/// failed result carrying the ProblemDetails <c>code</c>.
/// </summary>
public sealed class DevbookSubjectSyncClient
{
    /// <summary>What a pull asks for when the caller does not say — the same
    /// number the endpoint would have defaulted to.</summary>
    public const int DefaultMaxItems = 100;

    private readonly HttpClient _http;

    public DevbookSubjectSyncClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
    }

    /// <summary>Writes a batch of changes into this owner's replica. Safe to send
    /// twice: the replica takes a version once and refuses its echo.</summary>
    public async Task<Result<PushDevbookSubjectsResponse>> PushAsync(
        IReadOnlyList<DevbookSubjectChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return await SyncHttp.SendAsync<PushDevbookSubjectsResponse>(
            () => _http.PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.DevbookSubjects),
                new PushDevbookSubjectsRequest(changes),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one page of this owner's subject feed.</summary>
    /// <param name="since">The cursor the previous page handed back, or null to
    /// start from the beginning.</param>
    /// <param name="maxItems">How many changes to ask for; the service clamps it.</param>
    public async Task<Result<PullDevbookSubjectsResponse>> PullAsync(
        string? since,
        int maxItems = DefaultMaxItems,
        CancellationToken cancellationToken = default) =>
        await SyncHttp.SendAsync<PullDevbookSubjectsResponse>(
            () => _http.GetAsync(ReplicaRoutes.PullRoute(SyncRoutes.DevbookSubjects, since, maxItems), cancellationToken),
            cancellationToken).ConfigureAwait(false);
}
