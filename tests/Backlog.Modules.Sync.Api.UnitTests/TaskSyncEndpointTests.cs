using System.Net;
using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Endpoints;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Ports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Task replication over HTTP: what one device puts in, another device of the
/// same owner takes out — and what happens to everybody else.
/// <para>
/// These run against the in-memory replica, which is not a compromise for the
/// security cases: the cursor is minted and verified by the real
/// <c>HmacSyncCursorCodec</c> either way, because the codec sits in the handler
/// above whichever adapter is registered. What the in-memory adapter cannot
/// show is Cosmos's own change-feed ordering, which no unit test could.
/// </para>
/// </summary>
public class TaskSyncEndpointTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Two_paired_devices_share_a_task()
    {
        var (desktop, phone) = await PairedDevices();

        var id = Guid.CreateVersion7();
        var pushed = await desktop.PushTask(id, "Call the dentist");

        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        Assert.Equal(1, (await pushed.Content.ReadFromJsonAsync<PushTasksResponse>(Cancellation))!.Accepted);

        var pulled = await phone.PullTasks();

        var arrived = Assert.Single(pulled.Tasks);
        Assert.Equal(id, arrived.Change.Id);
        Assert.Equal("Call the dentist", arrived.Change.Task.Title);
        Assert.False(pulled.HasMore);
        Assert.False(string.IsNullOrWhiteSpace(pulled.Since));
    }

    /// <summary>The device id comes back with the change so a client can drop
    /// its own echo instead of re-applying what it just pushed. It is the token's
    /// device, never anything the body said.</summary>
    [Fact]
    public async Task A_pulled_change_names_the_device_that_wrote_it()
    {
        var (desktop, phone) = await PairedDevices();

        await desktop.PushTask(Guid.CreateVersion7(), "Call the dentist");

        var status = await desktop.GetFromJsonAsync<DeviceStatusResponse>(
            SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation);

        var arrived = Assert.Single((await phone.PullTasks()).Tasks);
        Assert.Equal(status!.DeviceId, arrived.DeviceId);
    }

    [Fact]
    public async Task A_separate_owner_pulls_nothing()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        await mine.PushTask(Guid.CreateVersion7(), "My secret");

        var theirPage = await theirs.PullTasks();

        // From the beginning, not from a cursor: this is the pull a device makes
        // the moment it is registered, and it must see nothing of anybody else.
        Assert.Empty(theirPage.Tasks);
        Assert.Single((await mine.PullTasks()).Tasks);
    }

    /// <summary>
    /// The case .arc42/adr/0005 §Consequences asks for by name. A Cosmos
    /// continuation embeds the feed range it was minted for, so replaying one
    /// belonging to somebody else reads their partition and the store has no
    /// opinion about it — the service reaches Cosmos under one identity that can
    /// see everything. The signed cursor is the check that stops it, and it is
    /// 403 rather than 404 because a correctly-signed cursor for another
    /// person's feed was obtained rather than guessed.
    /// </summary>
    [Fact]
    public async Task A_cursor_minted_for_another_owner_is_refused()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        await mine.PushTask(Guid.CreateVersion7(), "My secret");
        var myCursor = (await mine.PullTasks()).Since;

        var replayed = await theirs.GetAsync(PullRoute(myCursor), Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, replayed.StatusCode);

        var problem = await replayed.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.SyncCursorNotYours, problem?.Type);
        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, problem?.Code);

        // And the refusal is not the only thing keeping them apart: pulling
        // properly, from the beginning, still shows them nothing.
        Assert.Empty((await theirs.PullTasks()).Tasks);
    }

    [Fact]
    public async Task A_tampered_cursor_is_a_bad_request()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        await device.PushTask(Guid.CreateVersion7(), "Call the dentist");

        var response = await device.GetAsync(PullRoute(Tamper((await device.PullTasks()).Since)), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.SyncCursorMalformed, problem?.Type);
        Assert.Equal(SyncErrorCodes.SyncCursorMalformed, problem?.Code);
    }

    /// <summary>A cursor resumes rather than repeating: the second pull sees
    /// only what arrived after the first.</summary>
    [Fact]
    public async Task A_cursor_resumes_where_the_last_page_stopped()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        await device.PushTask(Guid.CreateVersion7(), "First");
        var first = await device.PullTasks();
        Assert.Single(first.Tasks);

        // Nothing new yet, and the cursor still comes back so the client does not
        // rescan from where it was.
        var quiet = await device.PullTasks(first.Since);
        Assert.Empty(quiet.Tasks);
        Assert.False(quiet.HasMore);

        var id = Guid.CreateVersion7();
        await device.PushTask(id, "Second");

        Assert.Equal(id, Assert.Single((await device.PullTasks(quiet.Since)).Tasks).Change.Id);
    }

    [Fact]
    public async Task Acknowledging_another_owners_capture_is_not_found()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        var captured = await mine.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox), new CaptureRequest("My secret", "phone"), Cancellation);

        var item = (await captured.Content.ReadFromJsonAsync<InboxItem>(Cancellation))!;

        var acknowledged = await theirs.PostAsync(
            SyncRoutes.AcknowledgeInboxItemFor(item.Id), content: null, Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, acknowledged.StatusCode);

        var problem = await acknowledged.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.InboxItemNotFound, problem?.Type);

        Assert.Single((await mine.GetFromJsonAsync<List<InboxItem>>(
            SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation))!);
    }

    /// <summary>
    /// Acknowledging takes a capture out of the inbox and leaves the task alive.
    /// It deliberately does not write a tombstone: the replica is whole-document
    /// last-write-wins across every device, so deleting here would delete the
    /// task everywhere — including the version the desktop had already pulled
    /// and turned into real work.
    /// <para>
    /// This test exists to fail if somebody "fixes" the acknowledgement into a
    /// delete.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Acknowledging_a_capture_keeps_the_task()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var captured = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox), new CaptureRequest("Call the dentist", "phone"), Cancellation);

        var item = (await captured.Content.ReadFromJsonAsync<InboxItem>(Cancellation))!;

        var acknowledged = await device.PostAsync(
            SyncRoutes.AcknowledgeInboxItemFor(item.Id), content: null, Cancellation);

        Assert.Equal(HttpStatusCode.NoContent, acknowledged.StatusCode);

        Assert.Empty((await device.GetFromJsonAsync<List<InboxItem>>(
            SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation))!);

        var surviving = (await device.PullTasks()).Tasks.Single(record => record.Change.Id == item.Id);

        Assert.Null(surviving.Change.DeletedAt);
        Assert.Null(surviving.Change.Task.SourceInboxId);
        Assert.Equal("Call the dentist", surviving.Change.Task.Title);
    }

    /// <summary>A capture is an ordinary task document, so the desktop receives
    /// it through the same feed as everything else rather than through a second
    /// path of its own.</summary>
    [Fact]
    public async Task A_capture_arrives_as_a_task_on_the_other_device()
    {
        var (desktop, phone) = await PairedDevices();

        await phone.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox), new CaptureRequest("Call the dentist", "phone"), Cancellation);

        var arrived = Assert.Single((await desktop.PullTasks()).Tasks);

        Assert.Equal("Call the dentist", arrived.Change.Task.Title);
        Assert.Equal("phone", arrived.Change.Task.SourceInboxId);
        Assert.Equal("task", arrived.Change.Task.Type);
        Assert.Equal("draft", arrived.Change.Task.Status);
        Assert.Equal("medium", arrived.Change.Task.Priority);
    }

    /// <summary>The page size is the service's decision, not the caller's: a
    /// request for a hundred thousand is a request for a timeout.</summary>
    [Fact]
    public async Task A_page_is_capped_however_much_is_asked_for()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        for (var i = 0; i < 3; i++)
        {
            await device.PushTask(Guid.CreateVersion7(), $"Task {i}");
        }

        var response = await device.GetAsync($"{SyncRoutes.Absolute(SyncRoutes.Tasks)}?maxItems=2", Cancellation);
        var page = (await response.Content.ReadFromJsonAsync<PullTasksResponse>(Cancellation))!;

        Assert.Equal(2, page.Tasks.Count);
        Assert.True(page.HasMore);

        Assert.Single((await device.PullTasks(page.Since)).Tasks);
    }

    /// <summary>
    /// The replica not being there yet is a 503 with its own code, not a 500 and
    /// not a hang. Nothing in the app model waits on Cosmos — the emulator is a
    /// 2.5 GB image with a cold start in minutes — so this is the ordinary state
    /// of the service for the first part of every local run.
    /// </summary>
    [Fact]
    public async Task A_replica_that_is_not_there_yet_is_service_unavailable()
    {
        using var starting = Replaced(new UnavailableTaskReplica());
        var device = await starting.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.GetAsync(SyncRoutes.Absolute(SyncRoutes.Tasks), Cancellation);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.ReplicaUnavailable, problem?.Type);
        Assert.Equal(SyncErrorCodes.ReplicaUnavailable, problem?.Code);
    }

    /// <summary>
    /// A cursor the store will not resume from. It is signed and it is the
    /// caller's own, so the codec has nothing to say about it — only the store
    /// knows how far back its feed still reaches — and the answer is a 400 the
    /// client recovers from by dropping the cursor.
    /// </summary>
    [Fact]
    public async Task A_cursor_the_store_will_not_resume_from_is_a_bad_request()
    {
        using var forgetful = Replaced(new ExpiredCursorTaskReplica());
        var device = await forgetful.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.GetAsync(SyncRoutes.Absolute(SyncRoutes.Tasks), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.SyncCursorExpired, problem?.Type);
        Assert.Equal(SyncErrorCodes.SyncCursorExpired, problem?.Code);
    }

    // --- What a caller may send ---------------------------------------------

    /// <summary>
    /// A push is capped like a pull is. Registration is anonymous and mints a
    /// fresh owner with no gate, so an uncapped push is an unbounded write into
    /// durable, per-request-billed storage from anybody who can reach the
    /// service; the replica issues one round trip per element, so the count is
    /// the thing that has to be bounded rather than the body alone.
    /// <para>
    /// The client batches at 200, so the cap is not a limit any device of ours
    /// meets.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_push_of_more_tasks_than_the_cap_is_refused()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Tasks), Batch(SyncRequestLimits.MaximumPushTasks + 1), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.PushBatchTooLarge, problem?.Type);
        Assert.Equal(SyncErrorCodes.PushBatchTooLarge, problem?.Code);

        // Refused whole. Half a batch stored would leave the device believing
        // the other half was too.
        Assert.Empty((await device.PullTasks()).Tasks);
    }

    /// <summary>A batch at the cap still goes through, so the cap is a bound on
    /// abuse rather than on use.</summary>
    [Fact]
    public async Task A_push_at_the_cap_is_accepted()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Tasks), Batch(SyncRequestLimits.MaximumPushTasks), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The count cap bounds what reaches the store; the body limit bounds what
    /// reaches the parser. Asserted as metadata because the server is what
    /// enforces it and the test host has no request-body-size feature to enforce
    /// it with — what can be pinned here is that the route carries the limit and
    /// that the pull route, which has no body, does not.
    /// </summary>
    [Fact]
    public void The_push_route_carries_a_body_size_limit()
    {
        var endpoints = _service.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        var push = Assert.Single(
            endpoints,
            endpoint => endpoint is RouteEndpoint route
                && route.RoutePattern.RawText?.EndsWith(SyncRoutes.Tasks, StringComparison.Ordinal) == true
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) == true);

        var limit = push.Metadata.GetMetadata<IRequestSizeLimitMetadata>();

        Assert.NotNull(limit);
        Assert.Equal(SyncRequestLimits.PushBodyBytes, limit.MaxRequestBodySize);
    }

    /// <summary>
    /// A capture with nothing in it, and a capture with far too much. Neither is
    /// something a device of ours sends, and both used to reach the store: the
    /// first as an empty task nobody can act on, the second as a document Cosmos
    /// answers for with an error the person cannot do anything about.
    /// </summary>
    [Theory]
    [InlineData("", "phone")]
    [InlineData("   ", "phone")]
    [InlineData("Call the dentist", "")]
    public async Task A_capture_that_is_not_within_bounds_is_refused(string title, string source)
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox), new CaptureRequest(title, source), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.Equal(SyncErrorCodes.CaptureInvalid, problem?.Code);
    }

    [Fact]
    public async Task A_capture_longer_than_the_service_stores_is_refused()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Inbox),
            new CaptureRequest(new string('x', SyncRequestLimits.MaximumCaptureTitle + 1), "phone"),
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            SyncErrorCodes.CaptureInvalid,
            (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);

        Assert.Empty((await device.GetFromJsonAsync<List<InboxItem>>(
            SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation))!);
    }

    /// <summary>A batch of the least a task can be, which is all the count cap
    /// is about.</summary>
    private static PushTasksRequest Batch(int count) =>
        new([.. Enumerable.Range(0, count).Select(index =>
            new TaskChange(Guid.CreateVersion7(), DateTimeOffset.UtcNow, DeletedAt: null, TaskSyncClientExtensions.Payload($"Task {index}")))]);

    /// <summary>
    /// A document over the store's own size ceiling. It has to come back coded
    /// and as the caller's to fix: as an unclassified 500 it would fail the batch
    /// carrying it on every run for ever, and the device's push watermark would
    /// never advance past it again — so nothing after that task would ever be
    /// sent either.
    /// </summary>
    [Fact]
    public async Task A_task_the_store_will_not_take_is_refused_with_its_own_code()
    {
        using var refusing = Replaced(new RefusingTaskReplica(SyncErrorCodes.TaskTooLarge, "That task is too large."));
        var device = await refusing.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushTask(Guid.CreateVersion7(), "War and Peace");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.TaskTooLarge, problem?.Type);
        Assert.Equal(SyncErrorCodes.TaskTooLarge, problem?.Code);
    }

    /// <summary>Throttling that outlived the SDK's own retries: 429, and a code
    /// that says the store is up and this caller is going too fast — which is a
    /// different sentence from the 503 that means it is not up yet.</summary>
    [Fact]
    public async Task A_throttled_store_is_refused_with_its_own_code()
    {
        using var busy = Replaced(new RefusingTaskReplica(SyncErrorCodes.ReplicaBusy, "The task replica is busy."));
        var device = await busy.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushTask(Guid.CreateVersion7(), "Call the dentist");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(
            SyncErrorCodes.ReplicaBusy,
            (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    /// <summary>The service with one adapter swapped out. <c>RemoveAll</c> then
    /// add, through <c>ConfigureTestServices</c>, which runs after the host has
    /// made its own registrations — so this wins wherever the real one was
    /// registered.</summary>
    private static SyncServiceFactory Replaced(ITaskReplica replica) => new()
    {
        TestServices = services =>
        {
            services.RemoveAll<ITaskReplica>();
            services.AddSingleton(replica);
        },
    };

    private static string PullRoute(string since) =>
        $"{SyncRoutes.Absolute(SyncRoutes.Tasks)}?since={Uri.EscapeDataString(since)}";

    /// <summary>One byte of the signature flipped. Base64url has no character
    /// that is not also in the alphabet, so shifting one keeps the cursor
    /// decodable and makes it unverifiable, which is the case worth
    /// covering.</summary>
    private static string Tamper(string cursor)
    {
        var parts = cursor.Split('.');
        var signature = parts[2].ToCharArray();
        signature[0] = signature[0] == 'A' ? 'B' : 'A';

        return $"{parts[0]}.{parts[1]}.{new string(signature)}";
    }

    private async Task<(HttpClient Desktop, HttpClient Phone)> PairedDevices()
    {
        var desktop = _service.CreateClient();
        var registration = await desktop.RegisterDevice("Study desktop");
        desktop.Bearing(await desktop.DeviceToken(registration));

        var code = await desktop.MintPairingCode();

        var phone = _service.CreateClient();
        var paired = await phone.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Phone"),
            Cancellation);

        phone.Bearing(await phone.DeviceToken(
            (await paired.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!));

        return (desktop, phone);
    }
}

/// <summary>Pushing and pulling in one line, so a test reads as the exchange it
/// is about rather than as two serialisation calls.</summary>
internal static class TaskSyncClientExtensions
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static Task<HttpResponseMessage> PushTask(this HttpClient client, Guid id, string title) =>
        client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Tasks),
            new PushTasksRequest([new TaskChange(id, DateTimeOffset.UtcNow, DeletedAt: null, Payload(title))]),
            Cancellation);

    internal static async Task<PullTasksResponse> PullTasks(this HttpClient client, string? since = null)
    {
        var route = SyncRoutes.Absolute(SyncRoutes.Tasks);
        var response = await client.GetAsync(
            since is null ? route : $"{route}?since={Uri.EscapeDataString(since)}", Cancellation);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PullTasksResponse>(Cancellation))!;
    }

    /// <summary>The least a task can be. Everything the service never reads is
    /// left empty on purpose: a test that filled it in would suggest the service
    /// cared.</summary>
    internal static TaskPayload Payload(string title) => new(
        title,
        ContentMd: string.Empty,
        Type: "task",
        Status: "draft",
        Priority: "medium",
        Order: 0,
        Area: null,
        DateTimeOffset.UtcNow,
        SourceInboxId: null,
        RecurrenceSourceId: null,
        DueOn: null,
        RemindAt: null,
        Recurrence: null,
        InMyDayOn: null,
        View: null,
        Effort: null,
        ImportPlanId: null,
        ImportItemId: null,
        AttachmentPath: null,
        Tags: [],
        RepoIds: [],
        DependsOn: [],
        SubItems: [],
        UsageEvents: [],
        ProjectionRefs: []);
}

/// <summary>A replica that is not up yet. Every call answers the same way,
/// because a store that cannot be reached cannot answer any of them.</summary>
internal sealed class UnavailableTaskReplica : ITaskReplica
{
    public Task<int> Upsert(OwnerScope scope, IReadOnlyList<TaskChange> changes, CancellationToken cancellationToken = default) =>
        throw Fault();

    public Task<TaskReplicaPage> ReadChanges(OwnerId owner, TaskReplicaCursor? cursor, int maxItems, CancellationToken cancellationToken = default) =>
        throw Fault();

    public Task<IReadOnlyList<TaskChangeRecord>> ListCaptures(OwnerId owner, CancellationToken cancellationToken = default) =>
        throw Fault();

    public Task<TaskChangeRecord?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default) =>
        throw Fault();

    private static SyncReplicaException Fault() => new(
        SyncErrorCodes.ReplicaUnavailable, "The task replica is not available yet. Try again shortly.");
}

/// <summary>A replica whose feed no longer reaches back far enough. Only the
/// pull can fail this way, so only the pull does.</summary>
internal sealed class ExpiredCursorTaskReplica : ITaskReplica
{
    public Task<int> Upsert(OwnerScope scope, IReadOnlyList<TaskChange> changes, CancellationToken cancellationToken = default) =>
        Task.FromResult(changes.Count);

    public Task<TaskReplicaPage> ReadChanges(OwnerId owner, TaskReplicaCursor? cursor, int maxItems, CancellationToken cancellationToken = default) =>
        throw new SyncReplicaException(
            SyncErrorCodes.SyncCursorExpired, "That cursor is too old to resume from. Pull again without one.");

    public Task<IReadOnlyList<TaskChangeRecord>> ListCaptures(OwnerId owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaskChangeRecord>>([]);

    public Task<TaskChangeRecord?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult<TaskChangeRecord?>(null);
}

/// <summary>A replica that refuses every write with a given code — the two
/// Cosmos answers a push can get that are neither "not up yet" nor success.</summary>
internal sealed class RefusingTaskReplica(string code, string message) : ITaskReplica
{
    public Task<int> Upsert(OwnerScope scope, IReadOnlyList<TaskChange> changes, CancellationToken cancellationToken = default) =>
        throw new SyncReplicaException(code, message);

    public Task<TaskReplicaPage> ReadChanges(OwnerId owner, TaskReplicaCursor? cursor, int maxItems, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TaskReplicaPage([], new TaskReplicaCursor(owner, string.Empty), HasMore: false));

    public Task<IReadOnlyList<TaskChangeRecord>> ListCaptures(OwnerId owner, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TaskChangeRecord>>([]);

    public Task<TaskChangeRecord?> Find(OwnerId owner, Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult<TaskChangeRecord?>(null);
}
