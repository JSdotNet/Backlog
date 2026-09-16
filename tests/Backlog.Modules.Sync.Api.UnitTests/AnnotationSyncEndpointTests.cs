using System.Net;
using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Endpoints;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Annotation replication over HTTP: a remark one desktop puts in, another
/// desktop of the same owner takes out — and what happens to everybody else.
/// The cases <see cref="TaskSyncEndpointTests"/> pins for the first container,
/// over the third; the cursor is minted and verified by the real codec either
/// way.
/// </summary>
public class AnnotationSyncEndpointTests : IDisposable
{
    private const string Repository = "JSdotNet/Backlog";
    private const string Chapter = ".domain/devbook/features.md";

    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Two_paired_devices_share_a_remark()
    {
        var (desktop, laptop) = await PairedDevices();

        var id = Guid.CreateVersion7();
        var pushed = await desktop.PushAnnotation(id, "Say which team owns this.");

        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        Assert.Equal(1, (await pushed.Content.ReadFromJsonAsync<PushAnnotationsResponse>(Cancellation))!.Accepted);

        var pulled = await laptop.PullAnnotations();

        var arrived = Assert.Single(pulled.Annotations);
        Assert.Equal(id, arrived.Change.Id);
        Assert.Equal("Say which team owns this.", arrived.Change.Annotation.Body);
        Assert.Equal(Repository, arrived.Change.Annotation.RepositoryAlias);
        Assert.Equal(Chapter, arrived.Change.Annotation.ChapterPath);
        Assert.Equal(2, arrived.Change.Annotation.BlockIndex);
        Assert.False(pulled.HasMore);
        Assert.False(string.IsNullOrWhiteSpace(pulled.Since));
    }

    [Fact]
    public async Task A_pulled_change_names_the_device_that_wrote_it()
    {
        var (desktop, laptop) = await PairedDevices();

        await desktop.PushAnnotation(Guid.CreateVersion7(), "Mine.");

        var status = await desktop.GetFromJsonAsync<DeviceStatusResponse>(
            SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation);

        var arrived = Assert.Single((await laptop.PullAnnotations()).Annotations);
        Assert.Equal(status!.DeviceId, arrived.DeviceId);
    }

    [Fact]
    public async Task A_separate_owner_pulls_nothing()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        await mine.PushAnnotation(Guid.CreateVersion7(), "My private note.");

        Assert.Empty((await theirs.PullAnnotations()).Annotations);
        Assert.Single((await mine.PullAnnotations()).Annotations);
    }

    [Fact]
    public async Task A_cursor_minted_for_another_owner_is_refused()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        await mine.PushAnnotation(Guid.CreateVersion7(), "My private note.");
        var myCursor = (await mine.PullAnnotations()).Since;

        var replayed = await theirs.GetAsync(
            $"{SyncRoutes.Absolute(SyncRoutes.Annotations)}?since={Uri.EscapeDataString(myCursor)}", Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, replayed.StatusCode);
        var problem = await replayed.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, problem?.Code);
    }

    [Fact]
    public async Task A_tombstone_travels_like_any_other_change()
    {
        var (desktop, laptop) = await PairedDevices();
        var id = Guid.CreateVersion7();

        await desktop.PushAnnotation(id, "Written.");
        var first = await laptop.PullAnnotations();
        Assert.Null(Assert.Single(first.Annotations).Change.DeletedAt);

        var now = DateTimeOffset.UtcNow;
        await desktop.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Annotations),
            new PushAnnotationsRequest([new AnnotationChange(id, now, now, Payload("Written."))]),
            Cancellation);

        var tombstone = Assert.Single((await laptop.PullAnnotations(first.Since)).Annotations);
        Assert.Equal(id, tombstone.Change.Id);
        Assert.Equal(now, tombstone.Change.DeletedAt);
    }

    [Fact]
    public async Task A_push_of_more_annotations_than_the_cap_is_refused_whole()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Annotations), Batch(SyncRequestLimits.MaximumPushAnnotations + 1), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.Equal(SyncErrorCodes.AnnotationBatchTooLarge, problem?.Code);
        Assert.Empty((await device.PullAnnotations()).Annotations);
    }

    [Fact]
    public async Task A_remark_that_names_no_chapter_is_refused_with_the_whole_batch()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Annotations),
            new PushAnnotationsRequest(
            [
                new AnnotationChange(Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, Payload("Fine.")),
                new AnnotationChange(Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, Payload("Lost.") with { ChapterPath = string.Empty }),
            ]),
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.Equal(SyncErrorCodes.AnnotationInvalid, problem?.Code);
        Assert.Empty((await device.PullAnnotations()).Annotations);
    }

    [Fact]
    public async Task A_body_past_the_bound_is_refused()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushAnnotation(Guid.CreateVersion7(), new string('x', SyncRequestLimits.MaximumAnnotationBody + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.AnnotationInvalid, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    [Fact]
    public async Task An_unpaired_caller_is_refused()
    {
        var anonymous = _service.CreateClient();

        var response = await anonymous.GetAsync(SyncRoutes.Absolute(SyncRoutes.Annotations), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static PushAnnotationsRequest Batch(int count) =>
        new([.. Enumerable.Range(0, count).Select(index =>
            new AnnotationChange(Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, Payload($"Remark {index}")))]);

    internal static AnnotationPayload Payload(string body) =>
        new(Repository, Chapter, 2, body, "DEV-TOWER", DateTimeOffset.UtcNow, Resolved: false);

    private async Task<(HttpClient Desktop, HttpClient Laptop)> PairedDevices()
    {
        var desktop = _service.CreateClient();
        var registration = await desktop.RegisterDevice("Study desktop");
        desktop.Bearing(await desktop.DeviceToken(registration));

        var code = await desktop.MintPairingCode();

        var laptop = _service.CreateClient();
        var paired = await laptop.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Laptop"),
            Cancellation);

        laptop.Bearing(await laptop.DeviceToken(
            (await paired.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!));

        return (desktop, laptop);
    }
}

internal static class AnnotationSyncClientExtensions
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static Task<HttpResponseMessage> PushAnnotation(this HttpClient client, Guid id, string body) =>
        client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Annotations),
            new PushAnnotationsRequest([new AnnotationChange(id, DateTimeOffset.UtcNow, DeletedAt: null, AnnotationSyncEndpointTests.Payload(body))]),
            Cancellation);

    internal static async Task<PullAnnotationsResponse> PullAnnotations(this HttpClient client, string? since = null)
    {
        var route = SyncRoutes.Absolute(SyncRoutes.Annotations);
        var response = await client.GetAsync(
            since is null ? route : $"{route}?since={Uri.EscapeDataString(since)}", Cancellation);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PullAnnotationsResponse>(Cancellation))!;
    }
}
