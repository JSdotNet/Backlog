using System.Globalization;
using System.Net.Http.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// The two directions of session replication, as this device calls them: push a
/// batch of this machine's records, pull the owner's session feed from a cursor.
/// <para>
/// The same shape as <see cref="TaskSyncClient"/>, deliberately and to the
/// letter — same typed <see cref="HttpClient"/>, same escape of the cursor, same
/// mapping of an answer onto a <see cref="Result{TValue}"/> through
/// <see cref="SyncHttp"/>. Two transports over one service that answered
/// failures differently would leave a screen branching on a code one half of the
/// exchange never produces, and the half that drifted would be the one nobody
/// was reading at the time.
/// </para>
/// <para>
/// Transport and nothing else. What may leave the machine is
/// <see cref="SessionRecordMapping"/>, how far this device got is
/// <see cref="ISessionSyncStateStore"/>, and what arrives is kept by
/// <see cref="IReplicatedSessionStore"/>. Kept apart because the sanitization
/// boundary is the part worth asserting without a socket, and folding it in here
/// would put .arc42/adr/0005 §Session records' whitelist behind an HTTP fixture.
/// </para>
/// <para>
/// Neither call names a machine. The service stamps the machine id from this
/// device's own token (.arc42/adr/0005 §Identity), so there is no field a caller
/// could set that would write a record attributed to another box — which is what
/// makes "a caller may only write records stamped with its own machine id" hold
/// by construction rather than by a check somebody could delete.
/// </para>
/// </summary>
public sealed class SessionSyncClient
{
    /// <summary>What a pull asks for when the caller does not say — the same
    /// number the endpoint would have defaulted to. Written here as well so a
    /// reader of the loop can see the page size it is looping over, and
    /// deliberately not read as the end of the feed: the contract says a page
    /// size is a hint to the store, not a promise.</summary>
    public const int DefaultMaxItems = 100;

    private readonly HttpClient _http;

    public SessionSyncClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
    }

    /// <summary>
    /// Appends a batch of this machine's session records to the owner's replica.
    /// <para>
    /// Safe to retry, and safe under the standard resilience handler's retries,
    /// with no idempotency key — for a different reason than the task push has
    /// none. A task push is idempotent because the replica upserts whole
    /// documents under last-write-wins; a session push is idempotent because the
    /// document key is derived from the machine id, the agent and the session id,
    /// so the same record sent twice lands on the same document rather than
    /// beside it. Session records are single-writer and have no lost-edit failure
    /// mode at all (.arc42/adr/0005 §Session records), so there is nothing a
    /// second send could discard.
    /// </para>
    /// </summary>
    public async Task<Result<PushSessionsResponse>> PushAsync(
        IReadOnlyList<SessionRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        return await SyncHttp.SendAsync<PushSessionsResponse>(
            () => _http.PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.Sessions),
                new PushSessionsRequest(records),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one page of the owner's session feed.
    /// </summary>
    /// <param name="since">The cursor the previous page handed back, or null to
    /// start from the beginning — which is what a freshly paired device sends,
    /// and what a device sends again after the service says its cursor has
    /// expired.</param>
    /// <param name="maxItems">How many records to ask for. The service clamps
    /// this to its own maximum, so a larger number is a request rather than a
    /// promise.</param>
    public async Task<Result<PullSessionsResponse>> PullAsync(
        string? since,
        int maxItems = DefaultMaxItems,
        CancellationToken cancellationToken = default) =>
        await SyncHttp.SendAsync<PullSessionsResponse>(
            () => _http.GetAsync(PullRoute(since, maxItems), cancellationToken),
            cancellationToken).ConfigureAwait(false);

    /// <summary>The pull URL with its query. The cursor is escaped rather than
    /// concatenated, for the reason <c>TaskSyncClient.PullRoute</c> gives: it is
    /// base64 of a signed payload, so <c>+</c> and <c>=</c> are ordinary
    /// characters in it and a raw <c>+</c> would arrive as a space and fail a
    /// signature the service minted itself.</summary>
    private static string PullRoute(string? since, int maxItems)
    {
        var route = $"{SyncRoutes.Absolute(SyncRoutes.Sessions)}?maxItems={maxItems.ToString(CultureInfo.InvariantCulture)}";

        return string.IsNullOrWhiteSpace(since)
            ? route
            : $"{route}&since={Uri.EscapeDataString(since)}";
    }
}
