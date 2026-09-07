using System.Globalization;
using System.Net.Http.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The two directions of task replication, as this device calls them: push a
/// batch of changes, pull the owner's change feed from a cursor.
/// <para>
/// Transport and nothing else. It holds no state, reads no repository and
/// decides no conflict — what to send, how far it got and which of two copies
/// wins live in <see cref="TaskSyncSession"/>, <see cref="ITaskSyncStateStore"/>
/// and <see cref="TaskReplicaMerge"/>. Kept apart because those three are the
/// parts worth asserting without a socket, and folding them in here would put
/// every one of their rules behind an HTTP fixture.
/// </para>
/// <para>
/// Neither call names an owner or a device. Both come from the token this
/// client's handler attaches, so there is no field a caller could set to write
/// into somebody else's data (.arc42/adr/0005 §Identity).
/// </para>
/// <para>
/// The failure convention is <see cref="DevicePairingClient"/>'s, and shared with
/// it: an expected 4xx — a cursor the store no longer resumes from, a cursor
/// belonging to somebody else — comes back as a failed
/// <see cref="Result{TValue}"/> carrying the ProblemDetails <c>code</c>, and a
/// failure that never reached the service comes back as
/// <see cref="DevicePairingClient.UnreachableCode"/>. A pull that has to start
/// over is an ordinary outcome of a device that has been off for a month, and
/// the session branches on the code to decide that.
/// </para>
/// </summary>
public sealed class TaskSyncClient
{
    /// <summary>What a pull asks for when the caller does not say — the same
    /// number the endpoint would have defaulted to. Written here as well so a
    /// reader of the loop can see the page size it is looping over.</summary>
    public const int DefaultMaxItems = 100;

    private readonly HttpClient _http;

    public TaskSyncClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
    }

    /// <summary>
    /// Writes a batch of changes into this owner's replica.
    /// <para>
    /// Safe to retry, and safe under the standard resilience handler's retries,
    /// with no idempotency key: the replica upserts whole documents under
    /// last-write-wins, so sending the same batch twice lands the same documents
    /// in the same state. A key would buy nothing and would have to be stored
    /// somewhere, which is a second piece of per-device state for a property the
    /// write already has by construction.
    /// </para>
    /// </summary>
    public async Task<Result<PushTasksResponse>> PushAsync(
        IReadOnlyList<TaskChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return await SyncHttp.SendAsync<PushTasksResponse>(
            () => _http.PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.Tasks),
                new PushTasksRequest(changes),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one page of this owner's change feed.
    /// </summary>
    /// <param name="since">The cursor the previous page handed back, or null to
    /// start from the beginning — which is what a freshly paired device sends,
    /// and what a device sends again after the service says its cursor has
    /// expired.</param>
    /// <param name="maxItems">How many changes to ask for. The service clamps
    /// this to its own maximum, so a larger number is a request rather than a
    /// promise.</param>
    public async Task<Result<PullTasksResponse>> PullAsync(
        string? since,
        int maxItems = DefaultMaxItems,
        CancellationToken cancellationToken = default) =>
        await SyncHttp.SendAsync<PullTasksResponse>(
            () => _http.GetAsync(PullRoute(since, maxItems), cancellationToken),
            cancellationToken).ConfigureAwait(false);

    /// <summary>The pull URL with its query. The cursor is escaped rather than
    /// concatenated: it is base64 of a signed payload, so <c>+</c> and <c>=</c>
    /// are ordinary characters in it and a raw <c>+</c> would arrive as a space
    /// and fail its own signature check.</summary>
    private static string PullRoute(string? since, int maxItems)
    {
        var route = $"{SyncRoutes.Absolute(SyncRoutes.Tasks)}?maxItems={maxItems.ToString(CultureInfo.InvariantCulture)}";

        return string.IsNullOrWhiteSpace(since)
            ? route
            : $"{route}&since={Uri.EscapeDataString(since)}";
    }
}
