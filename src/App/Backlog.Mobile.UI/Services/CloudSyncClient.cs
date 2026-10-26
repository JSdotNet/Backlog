using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// Thin client over the cloud sync API. Mobile is sync-dependent by design: it
/// captures and reviews, but never owns canonical data.
/// <para>
/// A capture does not go through here directly any more — it goes into the
/// outbox, and the outbox's capture kind calls <see cref="PostCaptureAsync"/>.
/// </para>
/// </summary>
public sealed class CloudSyncClient(HttpClient http)
{
    private const string InboxRoute = "/api/sync/inbox";
    private const string TasksRoute = "/api/sync/tasks";

    /// <summary>The endpoint's own default page, said here so the loop over it
    /// can be read.</summary>
    private const int PullPageSize = 100;

    /// <summary>The inbox, or <see cref="SyncUnavailableException"/> naming why
    /// not — the status and, when the service sent one, the problem's code, so
    /// the screen can tell a replica that is still warming up from a network
    /// that is not there.</summary>
    public async Task<IReadOnlyList<InboxItem>> GetInboxAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync(InboxRoute, ct);

        if (!response.IsSuccessStatusCode)
        {
            var problem = await SyncProblem.ReadAsync(response, ct);
            throw new SyncUnavailableException(response.StatusCode, problem.Code, problem.Detail);
        }

        return await response.Content.ReadFromJsonAsync<List<InboxItem>>(ct) ?? [];
    }

    /// <summary>One attempt at one capture: the status, and the problem's detail
    /// when it failed. Throws only when there was no answer at all.</summary>
    public async Task<(HttpStatusCode Status, string? Detail)> PostCaptureAsync(CaptureRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(InboxRoute, request, ct);

        if (response.IsSuccessStatusCode) return (response.StatusCode, null);

        var problem = await SyncProblem.ReadAsync(response, ct);
        return (response.StatusCode, problem.Detail);
    }

    /// <summary>
    /// One attempt at one attachment's bytes, under its own id, with the digest
    /// the service checks them against. 201 is a new upload and 200 the same
    /// bytes already there — both mean the capture may now name it. Throws only
    /// when there was no answer at all.
    /// </summary>
    public async Task<(HttpStatusCode Status, string? Detail)> PutAttachmentAsync(
        AttachmentMetadata attachment,
        Stream content,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        using var body = new StreamContent(content);
        body.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(attachment.ContentType);
        body.Headers.ContentLength = attachment.SizeBytes;

        using var request = new HttpRequestMessage(HttpMethod.Put, SyncRoutes.AttachmentFor(attachment.Id)) { Content = body };
        request.Headers.Add(SyncRoutes.AttachmentSha256Header, attachment.Sha256);

        using var response = await http.SendAsync(request, ct);

        if (response.IsSuccessStatusCode) return (response.StatusCode, null);

        var problem = await SyncProblem.ReadAsync(response, ct);
        return (response.StatusCode, problem.Detail);
    }

    public async Task AcknowledgeAsync(Guid id, CancellationToken ct = default)
        => (await http.PostAsync($"{InboxRoute}/{id}/ack", null, ct)).EnsureSuccessStatusCode();

    /// <summary>One page of the owner's task feed from <paramref name="since"/>,
    /// or from the beginning when it is null — or <see cref="SyncUnavailableException"/>
    /// carrying the problem's code, which is how a caller tells a cursor the
    /// service will no longer take from a network that is not there.</summary>
    public async Task<PullTasksResponse> PullTasksAsync(string? since, CancellationToken ct = default)
    {
        // Escaped, not concatenated: the cursor is base64 of a signed payload, and
        // a raw '+' would arrive as a space and fail its own signature.
        var route = string.IsNullOrWhiteSpace(since)
            ? $"{TasksRoute}?maxItems={PullPageSize}"
            : $"{TasksRoute}?maxItems={PullPageSize}&since={Uri.EscapeDataString(since)}";

        using var response = await http.GetAsync(route, ct);

        if (!response.IsSuccessStatusCode)
        {
            var problem = await SyncProblem.ReadAsync(response, ct);
            throw new SyncUnavailableException(response.StatusCode, problem.Code, problem.Detail);
        }

        return await response.Content.ReadFromJsonAsync<PullTasksResponse>(ct)
            ?? new PullTasksResponse([], since ?? string.Empty, HasMore: false);
    }

    /// <summary>One page of the owner's notification feed from <paramref name="since"/>,
    /// or from the beginning when it is null (local ADR 0038) — the same paging and the
    /// same <see cref="SyncUnavailableException"/> as <see cref="PullTasksAsync"/>.</summary>
    public async Task<PullNotificationsResponse> PullNotificationsAsync(string? since, CancellationToken ct = default)
    {
        var route = SyncRoutes.Absolute(SyncRoutes.Notifications);
        route = string.IsNullOrWhiteSpace(since)
            ? $"{route}?maxItems={PullPageSize}"
            : $"{route}?maxItems={PullPageSize}&since={Uri.EscapeDataString(since)}";

        using var response = await http.GetAsync(route, ct);

        if (!response.IsSuccessStatusCode)
        {
            var problem = await SyncProblem.ReadAsync(response, ct);
            throw new SyncUnavailableException(response.StatusCode, problem.Code, problem.Detail);
        }

        return await response.Content.ReadFromJsonAsync<PullNotificationsResponse>(ct)
            ?? new PullNotificationsResponse([], since ?? string.Empty, HasMore: false);
    }

    /// <summary>One attempt at one reply to a question: the status, and the problem's
    /// detail when it failed. Throws only when there was no answer at all. Posting the
    /// same reply id twice stores it once, so a retry is safe.</summary>
    public async Task<(HttpStatusCode Status, string? Detail)> PostNotificationReplyAsync(NotificationReplyPost reply, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reply);

        using var response = await http.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.NotificationReplies), reply, ct);

        if (response.IsSuccessStatusCode) return (response.StatusCode, null);

        var problem = await SyncProblem.ReadAsync(response, ct);
        return (response.StatusCode, problem.Detail);
    }

    /// <summary>Hands the service this install's FCM token, so it keeps this device's Notification
    /// Hubs installation (local ADR 0038 §4). <see cref="RegisterPushInstallationResponse.Registered"/>
    /// is false from a service with no hub, which is not worth asking again; any other refusal is
    /// <see cref="SyncUnavailableException"/> with the problem's code.</summary>
    public async Task<RegisterPushInstallationResponse> RegisterPushAsync(string fcmToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fcmToken);

        using var response = await http.PutAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.NotificationPush),
            new RegisterPushInstallationRequest(fcmToken),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var problem = await SyncProblem.ReadAsync(response, ct);
            throw new SyncUnavailableException(response.StatusCode, problem.Code, problem.Detail);
        }

        return await response.Content.ReadFromJsonAsync<RegisterPushInstallationResponse>(ct)
            ?? new RegisterPushInstallationResponse(Registered: false);
    }

    /// <summary>One attempt at one task: the status, and the problem's detail
    /// when it failed. Throws only when there was no answer at all.</summary>
    public async Task<(HttpStatusCode Status, string? Detail)> PushTaskAsync(TaskChange change, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(TasksRoute, new PushTasksRequest([change]), ct);

        if (response.IsSuccessStatusCode) return (response.StatusCode, null);

        var problem = await SyncProblem.ReadAsync(response, ct);
        return (response.StatusCode, problem.Detail);
    }
}

/// <summary>The service answered, and not with the inbox.</summary>
public sealed class SyncUnavailableException(HttpStatusCode status, string? code, string? detail)
    : Exception(detail ?? $"Cloud sync answered {(int)status}.")
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>The problem body's <c>code</c> — <c>sync.replica_unavailable</c>
    /// while the Cosmos emulator warms up — or null when there was none.</summary>
    public string? Code { get; } = code;
}

/// <summary>The two fields of a sync problem body a device acts on.</summary>
internal readonly record struct SyncProblem(string? Code, string? Detail)
{
    public static async Task<SyncProblem> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = document.RootElement;

            return new SyncProblem(
                root.TryGetProperty("code", out var code) ? code.GetString() : null,
                root.TryGetProperty("detail", out var detail) ? detail.GetString() : null);
        }
        catch (JsonException)
        {
            // A proxy's HTML error page, or nothing at all: the status is all there is.
            return default;
        }
    }
}
