using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Endpoints;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Knowledge event replication over HTTP (local ADR 0050 §2): an event one desktop saves,
/// another device of the same owner takes out — the cases
/// <see cref="DevbookSubjectSyncEndpointTests"/> pins, over the event container, with the
/// real cursor codec and the event carried as a document the service never reads.
/// </summary>
public class KnowledgeEventSyncEndpointTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Two_paired_devices_share_an_event()
    {
        var (desktop, phone) = await PairedDevices();
        var id = Guid.CreateVersion7();

        var pushed = await desktop.PushEvents([Event(id, DateTimeOffset.UtcNow, "Planned")]);

        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        Assert.Equal(1, (await pushed.Content.ReadFromJsonAsync<PushKnowledgeEventsResponse>(Cancellation))!.Accepted);

        var pulled = await phone.PullEvents();

        var arrived = Assert.Single(pulled.Events);
        Assert.Equal(id, arrived.Change.Id);
        Assert.Equal(KnowledgeEventKinds.KnowledgeEvent, arrived.Change.Kind);
        Assert.Equal("NDC Oslo 2026", arrived.Change.Document.GetProperty("title").GetString());
        Assert.Equal("Planned", arrived.Change.Document.GetProperty("state").GetString());
        Assert.False(pulled.HasMore);
        Assert.False(string.IsNullOrWhiteSpace(pulled.Since));
    }

    /// <summary>Sync reads none of an event's fields, so whatever the Events context puts in
    /// the document — a shape this service has never seen — comes back as it was written.</summary>
    [Fact]
    public async Task The_document_crosses_whole_and_unread()
    {
        var (desktop, phone) = await PairedDevices();
        const string document = """{"title":"NDC Oslo 2026","keyDates":[{"date":"2026-06-01","label":"CFP closes"}],"noteLinks":[{"kind":"Talk","target":"0193e0a4-0000-7000-8000-000000000001","title":"Keynote"}],"futureField":{"nested":true}}""";

        await desktop.PushEvents([new KnowledgeEventChange(Guid.CreateVersion7(), KnowledgeEventKinds.KnowledgeEvent, DateTimeOffset.UtcNow, Json(document))]);

        var arrived = Assert.Single((await phone.PullEvents()).Events);
        Assert.True(JsonElement.DeepEquals(Json(document), arrived.Change.Document));
    }

    [Fact]
    public async Task A_series_travels_beside_its_events()
    {
        var (desktop, phone) = await PairedDevices();

        await desktop.PushEvents(
        [
            new KnowledgeEventChange(Guid.CreateVersion7(), KnowledgeEventKinds.EventSeries, DateTimeOffset.UtcNow, Json("""{"name":"NDC Oslo"}""")),
            Event(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "Planned"),
        ]);

        var kinds = (await phone.PullEvents()).Events.Select(record => record.Change.Kind).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal([KnowledgeEventKinds.EventSeries, KnowledgeEventKinds.KnowledgeEvent], kinds);
    }

    [Fact]
    public async Task A_separate_owner_pulls_nothing()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        await mine.PushEvents([Event(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "Planned")]);

        Assert.Empty((await theirs.PullEvents()).Events);
        Assert.Single((await mine.PullEvents()).Events);
    }

    [Fact]
    public async Task A_cursor_minted_for_another_owner_is_refused()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        await mine.PushEvents([Event(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "Planned")]);
        var myCursor = (await mine.PullEvents()).Since;

        var replayed = await theirs.GetAsync(
            $"{SyncRoutes.Absolute(SyncRoutes.KnowledgeEvents)}?since={Uri.EscapeDataString(myCursor)}", Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, replayed.StatusCode);
        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, (await replayed.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    /// <summary>The laptop pulled the event while it was Planned, the desktop marked it
    /// Attended, and the laptop's echo of what it pulled arrives last. The replica answers
    /// the honest count and keeps Attended.</summary>
    [Fact]
    public async Task A_stale_copy_pushed_after_a_later_one_is_refused_and_the_later_one_travels()
    {
        var (desktop, laptop) = await PairedDevices();
        var id = Guid.CreateVersion7();
        var planned = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        var attended = planned.AddDays(7);

        await desktop.PushEvents([Event(id, planned, "Planned")]);
        var laptopCursor = (await laptop.PullEvents()).Since;

        await desktop.PushEvents([Event(id, attended, "Attended")]);

        var echoed = await laptop.PushEvents([Event(id, planned, "Planned")]);
        Assert.Equal(0, (await echoed.Content.ReadFromJsonAsync<PushKnowledgeEventsResponse>(Cancellation))!.Accepted);

        var arrived = Assert.Single((await laptop.PullEvents(laptopCursor)).Events);
        Assert.Equal("Attended", arrived.Change.Document.GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_push_of_more_changes_than_the_cap_is_refused_whole()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushEvents(
            [.. Enumerable.Range(0, SyncRequestLimits.MaximumPushKnowledgeEvents + 1)
                .Select(_ => Event(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "Planned"))]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.KnowledgeEventBatchTooLarge, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Empty((await device.PullEvents()).Events);
    }

    [Fact]
    public async Task A_kind_the_feed_does_not_carry_is_refused_with_the_whole_batch()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushEvents(
        [
            Event(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "Planned"),
            new KnowledgeEventChange(Guid.CreateVersion7(), "note", DateTimeOffset.UtcNow, Json("{}")),
        ]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.KnowledgeEventInvalid, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Empty((await device.PullEvents()).Events);
    }

    [Fact]
    public async Task A_document_that_is_not_an_object_is_refused()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushEvents(
            [new KnowledgeEventChange(Guid.CreateVersion7(), KnowledgeEventKinds.KnowledgeEvent, DateTimeOffset.UtcNow, Json("[1,2]"))]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.KnowledgeEventInvalid, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Fact]
    public async Task A_pull_pages_the_feed_until_it_is_drained()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        await device.PushEvents([.. Enumerable.Range(0, 3).Select(_ => Event(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "Planned"))]);

        var first = await device.PullEvents(maxItems: 2);
        var second = await device.PullEvents(first.Since, maxItems: 2);

        Assert.Equal(2, first.Events.Count);
        Assert.True(first.HasMore);
        Assert.Single(second.Events);
        Assert.False(second.HasMore);
    }

    [Fact]
    public async Task An_unpaired_caller_is_refused()
    {
        var response = await _service.CreateClient().GetAsync(SyncRoutes.Absolute(SyncRoutes.KnowledgeEvents), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    internal static KnowledgeEventChange Event(Guid id, DateTimeOffset at, string state) =>
        new(id, KnowledgeEventKinds.KnowledgeEvent, at, Json($$"""{"title":"NDC Oslo 2026","state":"{{state}}"}"""));

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private async Task<(HttpClient Desktop, HttpClient Other)> PairedDevices()
    {
        var desktop = _service.CreateClient();
        var registration = await desktop.RegisterDevice("Study desktop");
        desktop.Bearing(await desktop.DeviceToken(registration));

        var code = await desktop.MintPairingCode();

        var other = _service.CreateClient();
        var paired = await other.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Phone"),
            Cancellation);

        other.Bearing(await other.DeviceToken(
            (await paired.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!));

        return (desktop, other);
    }
}

internal static class KnowledgeEventSyncClientExtensions
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static Task<HttpResponseMessage> PushEvents(this HttpClient client, IReadOnlyList<KnowledgeEventChange> changes) =>
        client.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.KnowledgeEvents), new PushKnowledgeEventsRequest(changes), Cancellation);

    internal static async Task<PullKnowledgeEventsResponse> PullEvents(this HttpClient client, string? since = null, int? maxItems = null)
    {
        var query = new List<string>();
        if (since is not null) query.Add($"since={Uri.EscapeDataString(since)}");
        if (maxItems is { } max) query.Add($"maxItems={max}");

        var route = SyncRoutes.Absolute(SyncRoutes.KnowledgeEvents);
        var response = await client.GetAsync(query.Count == 0 ? route : $"{route}?{string.Join('&', query)}", Cancellation);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PullKnowledgeEventsResponse>(Cancellation))!;
    }
}
