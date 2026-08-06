using System.Net.Http.Json;

namespace Backlog.Mobile.Services;

/// <summary>Capture awaiting triage, as returned by the cloud sync layer.</summary>
public sealed record InboxItem(Guid Id, string Title, string Source, DateTimeOffset CapturedAt);

/// <summary>
/// Thin client over the cloud sync API. Mobile is sync-dependent by design: it
/// captures and reviews, but never owns canonical data.
/// </summary>
public sealed class CloudSyncClient(HttpClient http)
{
    public async Task<IReadOnlyList<InboxItem>> GetInboxAsync(CancellationToken ct = default)
        => await http.GetFromJsonAsync<List<InboxItem>>("/api/sync/inbox", ct) ?? [];

    public async Task CaptureAsync(string title, CancellationToken ct = default)
        => (await http.PostAsJsonAsync("/api/sync/inbox", new { Title = title, Source = "mobile" }, ct))
            .EnsureSuccessStatusCode();

    public async Task AcknowledgeAsync(Guid id, CancellationToken ct = default)
        => (await http.PostAsync($"/api/sync/inbox/{id}/ack", null, ct)).EnsureSuccessStatusCode();
}
