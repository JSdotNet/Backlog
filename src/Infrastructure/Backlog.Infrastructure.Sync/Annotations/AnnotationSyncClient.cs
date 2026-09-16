using System.Globalization;
using System.Net.Http.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync.Annotations;

/// <summary>
/// The two directions of annotation replication, as this device calls them:
/// push a batch of changes, pull the owner's annotation feed from a cursor.
/// Transport and nothing else, on <see cref="TaskSyncClient"/>'s terms — no
/// state, no repository, no conflict decided here, and the same failure
/// convention: an expected 4xx comes back as a failed result carrying the
/// ProblemDetails <c>code</c>.
/// </summary>
public sealed class AnnotationSyncClient
{
    /// <summary>What a pull asks for when the caller does not say — the same
    /// number the endpoint would have defaulted to.</summary>
    public const int DefaultMaxItems = 100;

    private readonly HttpClient _http;

    public AnnotationSyncClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
    }

    /// <summary>Writes a batch of changes into this owner's replica. Safe to
    /// retry with no idempotency key: a whole-document upsert lands the same
    /// documents in the same state however many times it is sent.</summary>
    public async Task<Result<PushAnnotationsResponse>> PushAsync(
        IReadOnlyList<AnnotationChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return await SyncHttp.SendAsync<PushAnnotationsResponse>(
            () => _http.PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.Annotations),
                new PushAnnotationsRequest(changes),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one page of this owner's annotation feed.</summary>
    /// <param name="since">The cursor the previous page handed back, or null to
    /// start from the beginning.</param>
    /// <param name="maxItems">How many changes to ask for; the service clamps it.</param>
    public async Task<Result<PullAnnotationsResponse>> PullAsync(
        string? since,
        int maxItems = DefaultMaxItems,
        CancellationToken cancellationToken = default) =>
        await SyncHttp.SendAsync<PullAnnotationsResponse>(
            () => _http.GetAsync(PullRoute(since, maxItems), cancellationToken),
            cancellationToken).ConfigureAwait(false);

    /// <summary>The pull URL with its query, the cursor escaped for the reason
    /// <see cref="TaskSyncClient"/> gives: it is base64 of a signed payload.</summary>
    private static string PullRoute(string? since, int maxItems)
    {
        var route = $"{SyncRoutes.Absolute(SyncRoutes.Annotations)}?maxItems={maxItems.ToString(CultureInfo.InvariantCulture)}";

        return string.IsNullOrWhiteSpace(since)
            ? route
            : $"{route}&since={Uri.EscapeDataString(since)}";
    }
}
