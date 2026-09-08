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
/// Session replication over HTTP: what one machine appends, another machine of
/// the same owner takes out — and what happens to everybody else.
/// <para>
/// These run against the in-memory replica, which is not a compromise for the
/// security cases: the cursor is minted and verified by the real
/// <c>HmacSyncCursorCodec</c> either way, because the codec sits in the handler
/// above whichever adapter is registered. What the in-memory adapter cannot show
/// is Cosmos's own change-feed ordering, which no unit test could.
/// </para>
/// </summary>
public class SessionSyncEndpointTests : IDisposable
{
    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Two_paired_devices_share_a_session_record()
    {
        var (desktop, laptop) = await PairedDevices();

        var pushed = await desktop.PushSession(Session("s-1"));

        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        Assert.Equal(1, (await pushed.Content.ReadFromJsonAsync<PushSessionsResponse>(Cancellation))!.Accepted);

        var pulled = await laptop.PullSessions();

        var arrived = Assert.Single(pulled.Sessions);
        Assert.Equal("s-1", arrived.Record.SessionId);
        Assert.False(pulled.HasMore);
        Assert.False(string.IsNullOrWhiteSpace(pulled.Since));
    }

    /// <summary>
    /// The request has nowhere to put a machine id, so the one that comes back is
    /// the token's and can be nothing else. That is .arc42/adr/0005 §Session
    /// records' "a caller may only write records stamped with its own machine id"
    /// held by construction rather than by a check — there is no field to lie in.
    /// </summary>
    [Fact]
    public async Task A_pushed_record_is_stamped_with_the_machine_from_the_token()
    {
        var (desktop, laptop) = await PairedDevices();

        await desktop.PushSession(Session("s-1"));

        var status = await desktop.GetFromJsonAsync<DeviceStatusResponse>(
            SyncRoutes.Absolute(SyncRoutes.DeviceStatus), Cancellation);

        var arrived = Assert.Single((await laptop.PullSessions()).Sessions);
        Assert.Equal(status!.DeviceId, arrived.MachineId);
    }

    /// <summary>
    /// The single-writer rule, from the other side. Two machines reporting the
    /// same session id are two records, because the document is keyed on the
    /// machine first — so no machine can overwrite another's history even by
    /// sending the same id, and the two show up separately rather than one
    /// silently replacing the other.
    /// </summary>
    [Fact]
    public async Task Two_machines_reporting_one_session_id_do_not_overwrite_each_other()
    {
        var (desktop, laptop) = await PairedDevices();

        await desktop.PushSession(Session("shared-id") with { MachineName = "JS-DESKTOP" });
        await laptop.PushSession(Session("shared-id") with { MachineName = "JS-LAPTOP" });

        var page = await desktop.PullSessions();

        Assert.Equal(2, page.Sessions.Count);
        Assert.Equal(2, page.Sessions.Select(entry => entry.MachineId).Distinct().Count());
        Assert.Contains(page.Sessions, entry => entry.Record.MachineName == "JS-DESKTOP");
        Assert.Contains(page.Sessions, entry => entry.Record.MachineName == "JS-LAPTOP");
    }

    /// <summary>
    /// Append-only in the sense .arc42/adr/0005 means: a session that is still
    /// running reports later evidence under the same identity, and that replaces
    /// the machine's own record rather than accumulating a second one. There is
    /// no tombstone and no updated_at to reconcile.
    /// </summary>
    [Fact]
    public async Task Re_pushing_a_session_replaces_that_machines_own_record()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        await device.PushSession(Session("s-1") with { TurnCount = 3 });
        await device.PushSession(Session("s-1") with { TurnCount = 9 });

        var arrived = Assert.Single((await device.PullSessions()).Sessions);
        Assert.Equal(9, arrived.Record.TurnCount);
    }

    /// <summary>Exactly the ten whitelisted fields survive the round trip — nine
    /// on the wire and the machine id the service stamps — and the three that can
    /// honestly be unknown come back as null rather than as blanks.</summary>
    [Fact]
    public async Task The_whole_whitelist_survives_the_round_trip()
    {
        var (desktop, laptop) = await PairedDevices();

        var sent = Session("s-1");
        await desktop.PushSession(sent);
        await desktop.PushSession(Session("s-2") with
        {
            RepositoryAlias = null,
            Branch = null,
            StartedAt = null,
        });

        var page = await laptop.PullSessions();

        var full = page.Sessions.Single(entry => entry.Record.SessionId == "s-1").Record;
        Assert.Equal(sent, full);

        var sparse = page.Sessions.Single(entry => entry.Record.SessionId == "s-2").Record;
        Assert.Null(sparse.RepositoryAlias);
        Assert.Null(sparse.Branch);
        Assert.Null(sparse.StartedAt);
    }

    [Fact]
    public async Task A_separate_owner_pulls_nothing()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        await mine.PushSession(Session("s-1"));

        // From the beginning, not from a cursor: this is the pull a device makes
        // the moment it is registered, and it must see nothing of anybody else.
        Assert.Empty((await theirs.PullSessions()).Sessions);
        Assert.Single((await mine.PullSessions()).Sessions);
    }

    /// <summary>
    /// The case .arc42/adr/0005 §Consequences asks for by name, on the second
    /// container. A Cosmos continuation embeds the feed range it was minted for,
    /// so replaying one belonging to somebody else reads their partition and the
    /// store has no opinion about it — the service reaches Cosmos under one
    /// identity that can see everything. The signed cursor is the check that stops
    /// it, and it is 403 rather than 404 because a correctly-signed cursor for
    /// another person's feed was obtained rather than guessed.
    /// </summary>
    [Fact]
    public async Task A_cursor_minted_for_another_owner_is_refused()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        await mine.PushSession(Session("s-1"));
        var myCursor = (await mine.PullSessions()).Since;

        var replayed = await theirs.GetAsync(PullRoute(myCursor), Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, replayed.StatusCode);

        var problem = await replayed.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.SyncCursorNotYours, problem?.Type);
        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, problem?.Code);

        // And the refusal is not the only thing keeping them apart: pulling
        // properly, from the beginning, still shows them nothing.
        Assert.Empty((await theirs.PullSessions()).Sessions);
    }

    /// <summary>
    /// The refusal happens before the replica is touched, which is what
    /// §Consequences means by testing the negative case: a replica that threw on
    /// a foreign cursor would prove the check ran late, and a replica that
    /// answered would prove nothing at all.
    /// </summary>
    [Fact]
    public async Task A_foreign_cursor_never_reaches_the_replica()
    {
        var counting = new CountingSessionReplica();
        using var service = Replaced(counting);

        var mine = await service.CreateClient().RegisteredDevice("Mine");
        var theirs = await service.CreateClient().RegisteredDevice("Theirs");

        var myCursor = (await mine.PullSessions()).Since;
        var reads = counting.Reads;

        var replayed = await theirs.GetAsync(PullRoute(myCursor), Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, replayed.StatusCode);
        Assert.Equal(reads, counting.Reads);
    }

    /// <summary>A malformed cursor is a different event from a valid one
    /// belonging to somebody else, and only one of the two is worth waking up
    /// for. They stay distinct all the way out to the status code.</summary>
    [Fact]
    public async Task A_tampered_cursor_is_a_bad_request()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        await device.PushSession(Session("s-1"));

        var response = await device.GetAsync(PullRoute(Tamper((await device.PullSessions()).Since)), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.SyncCursorMalformed, problem?.Type);
        Assert.Equal(SyncErrorCodes.SyncCursorMalformed, problem?.Code);
    }

    /// <summary>A cursor resumes rather than repeating, and the feed advances
    /// over a quiet poll — which for the session feed is the ordinary case, not
    /// the edge one, because a fleet nobody is working on writes nothing for
    /// hours.</summary>
    [Fact]
    public async Task A_cursor_resumes_where_the_last_page_stopped()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        await device.PushSession(Session("s-1"));
        var first = await device.PullSessions();
        Assert.Single(first.Sessions);

        var quiet = await device.PullSessions(first.Since);
        Assert.Empty(quiet.Sessions);
        Assert.False(quiet.HasMore);

        await device.PushSession(Session("s-2"));

        Assert.Equal("s-2", Assert.Single((await device.PullSessions(quiet.Since)).Sessions).Record.SessionId);
    }

    /// <summary>The page size is the service's decision, not the caller's: a
    /// request for a hundred thousand is a request for a timeout.</summary>
    [Fact]
    public async Task A_page_is_capped_however_much_is_asked_for()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        for (var i = 0; i < 3; i++)
        {
            await device.PushSession(Session($"s-{i}"));
        }

        var response = await device.GetAsync($"{SyncRoutes.Absolute(SyncRoutes.Sessions)}?maxItems=2", Cancellation);
        var page = (await response.Content.ReadFromJsonAsync<PullSessionsResponse>(Cancellation))!;

        Assert.Equal(2, page.Sessions.Count);
        Assert.True(page.HasMore);

        Assert.Single((await device.PullSessions(page.Since)).Sessions);
    }

    // --- What a caller may send ---------------------------------------------

    /// <summary>
    /// A push is capped like a pull is. Registration is anonymous and mints a
    /// fresh owner with no gate, so an uncapped push is an unbounded write into
    /// durable, per-request-billed storage from anybody who can reach the service;
    /// the replica issues one round trip per element, so the count is the thing
    /// that has to be bounded rather than the body alone.
    /// </summary>
    [Fact]
    public async Task A_push_of_more_records_than_the_cap_is_refused()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Sessions),
            Batch(SyncRequestLimits.MaximumPushSessions + 1),
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.EndsWith(SyncErrorCodes.SessionBatchTooLarge, problem?.Type);
        Assert.Equal(SyncErrorCodes.SessionBatchTooLarge, problem?.Code);

        // Refused whole. Half a batch stored would leave the machine believing
        // the other half was too, and its watermark would move past it.
        Assert.Empty((await device.PullSessions()).Sessions);
    }

    [Fact]
    public async Task A_push_at_the_cap_is_accepted()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Sessions),
            Batch(SyncRequestLimits.MaximumPushSessions),
            Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// A record with no identity, and a record with a field far larger than
    /// anything a machine reports. Both are refused at the edge naming the field
    /// rather than reaching the store, which is where the second one would become
    /// an error the person can do nothing about.
    /// </summary>
    [Theory]
    [InlineData("", "claude")]
    [InlineData("   ", "claude")]
    [InlineData("s-1", "")]
    [InlineData("s-1", "  ")]
    public async Task A_record_with_no_identity_is_refused(string sessionId, string agentKind)
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushSession(Session(sessionId) with { AgentKind = agentKind });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            SyncErrorCodes.SessionInvalid,
            (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    /// <summary>Every free-text field is bounded, so a caller cannot post a
    /// two-megabyte branch name into durable storage. Named by field rather than
    /// handed a built record, so a failure says which bound stopped working.</summary>
    [Theory]
    [InlineData("session id")]
    [InlineData("agent kind")]
    [InlineData("machine name")]
    [InlineData("repository alias")]
    [InlineData("branch")]
    public async Task A_record_longer_than_the_service_stores_is_refused(string field)
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushSession(Oversized(field));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            SyncErrorCodes.SessionInvalid,
            (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);

        Assert.Empty((await device.PullSessions()).Sessions);
    }

    /// <summary>One bad record refuses the batch it arrived in rather than being
    /// dropped from it. A push that quietly stored the good half and answered 200
    /// would move the machine's watermark past the other half, and those records
    /// would never be offered again.</summary>
    [Fact]
    public async Task One_bad_record_refuses_the_whole_batch()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Sessions),
            new PushSessionsRequest([Session("s-1"), Session(string.Empty), Session("s-3")]),
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await device.PullSessions()).Sessions);
    }

    /// <summary>
    /// The count cap bounds what reaches the store; the body limit bounds what
    /// reaches the parser. Asserted as metadata because the server is what
    /// enforces it and the test host has no request-body-size feature to enforce
    /// it with — what can be pinned here is that the route carries the limit, and
    /// that it is the session limit rather than the task one.
    /// </summary>
    [Fact]
    public void The_push_route_carries_a_body_size_limit()
    {
        var endpoints = _service.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        var push = Assert.Single(
            endpoints,
            endpoint => endpoint is RouteEndpoint route
                && route.RoutePattern.RawText?.EndsWith(SyncRoutes.Sessions, StringComparison.Ordinal) == true
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) == true);

        var limit = push.Metadata.GetMetadata<IRequestSizeLimitMetadata>();

        Assert.NotNull(limit);
        Assert.Equal(SyncRequestLimits.SessionPushBodyBytes, limit.MaxRequestBodySize);
    }

    // --- What the store can answer ------------------------------------------

    /// <summary>The replica not being there yet is a 503 with its own code, not a
    /// 500 and not a hang. Nothing in the app model waits on Cosmos, so this is
    /// the ordinary state of the service for the first part of every local
    /// run.</summary>
    [Fact]
    public async Task A_replica_that_is_not_there_yet_is_service_unavailable()
    {
        using var starting = Replaced(new UnavailableSessionReplica());
        var device = await starting.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.GetAsync(SyncRoutes.Absolute(SyncRoutes.Sessions), Cancellation);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            SyncErrorCodes.ReplicaUnavailable,
            (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    /// <summary>A cursor the store will not resume from. It is signed and it is
    /// the caller's own, so the codec has nothing to say about it — only the
    /// store knows how far back its feed still reaches — and the answer is a 400
    /// the client recovers from by dropping the cursor.</summary>
    [Fact]
    public async Task A_cursor_the_store_will_not_resume_from_is_a_bad_request()
    {
        using var forgetful = Replaced(new RefusingSessionReplica(
            SyncErrorCodes.SyncCursorExpired, "That cursor is too old to resume from.", onRead: true));

        var device = await forgetful.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.GetAsync(SyncRoutes.Absolute(SyncRoutes.Sessions), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            SyncErrorCodes.SyncCursorExpired,
            (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    /// <summary>A record the store will not take comes back coded and as the
    /// caller's to fix. It should be unreachable — every field is bounded at the
    /// edge well below Cosmos's ceiling — but as an unclassified 500 it would fail
    /// the batch carrying it on every run for ever.</summary>
    [Fact]
    public async Task A_record_the_store_will_not_take_is_refused_with_its_own_code()
    {
        using var refusing = Replaced(new RefusingSessionReplica(
            SyncErrorCodes.SessionTooLarge, "That session record is too large."));

        var device = await refusing.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushSession(Session("s-1"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(
            SyncErrorCodes.SessionTooLarge,
            (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    /// <summary>Throttling that outlived the SDK's own retries: 429, and a code
    /// that says the store is up and this caller is going too fast — which is a
    /// different sentence from the 503 that means it is not up yet.</summary>
    [Fact]
    public async Task A_throttled_store_is_refused_with_its_own_code()
    {
        using var busy = Replaced(new RefusingSessionReplica(
            SyncErrorCodes.ReplicaBusy, "The session replica is busy."));

        var device = await busy.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushSession(Session("s-1"));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(
            SyncErrorCodes.ReplicaBusy,
            (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    /// <summary>The service with the session replica swapped out. <c>RemoveAll</c>
    /// then add, through <c>ConfigureTestServices</c>, which runs after the host
    /// has made its own registrations.</summary>
    private static SyncServiceFactory Replaced(ISessionReplica replica) => new()
    {
        TestServices = services =>
        {
            services.RemoveAll<ISessionReplica>();
            services.AddSingleton(replica);
        },
    };

    /// <summary>A record with exactly one field one character past its
    /// bound.</summary>
    private static SessionRecord Oversized(string field) => field switch
    {
        "session id" => Session(Long(SyncRequestLimits.MaximumSessionId)),
        "agent kind" => Session("s-1") with { AgentKind = Long(SyncRequestLimits.MaximumAgentKind) },
        "machine name" => Session("s-1") with { MachineName = Long(SyncRequestLimits.MaximumMachineName) },
        "repository alias" => Session("s-1") with { RepositoryAlias = Long(SyncRequestLimits.MaximumRepositoryAlias) },
        "branch" => Session("s-1") with { Branch = Long(SyncRequestLimits.MaximumBranch) },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "No bound by that name."),
    };

    private static string Long(int limit) => new('x', limit + 1);

    private static PushSessionsRequest Batch(int count) =>
        new([.. Enumerable.Range(0, count).Select(index => Session($"s-{index}"))]);

    private static SessionRecord Session(string sessionId) => new(
        sessionId,
        AgentKind: "claude",
        MachineName: "JS-DESKTOP",
        RepositoryAlias: "backlog",
        Branch: "main",
        StartedAt: new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero),
        LastActivityAt: new DateTimeOffset(2026, 9, 8, 10, 30, 0, TimeSpan.Zero),
        TurnCount: 42,
        DurationSeconds: 5_400);

    private static string PullRoute(string since) =>
        $"{SyncRoutes.Absolute(SyncRoutes.Sessions)}?since={Uri.EscapeDataString(since)}";

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

/// <summary>Pushing and pulling in one line, so a test reads as the exchange it
/// is about rather than as two serialisation calls.</summary>
internal static class SessionSyncClientExtensions
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static Task<HttpResponseMessage> PushSession(this HttpClient client, SessionRecord record) =>
        client.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.Sessions), new PushSessionsRequest([record]), Cancellation);

    internal static async Task<PullSessionsResponse> PullSessions(this HttpClient client, string? since = null)
    {
        var route = SyncRoutes.Absolute(SyncRoutes.Sessions);
        var response = await client.GetAsync(
            since is null ? route : $"{route}?since={Uri.EscapeDataString(since)}", Cancellation);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PullSessionsResponse>(Cancellation))!;
    }
}

/// <summary>A replica that is not up yet. Both calls answer the same way, because
/// a store that cannot be reached cannot answer either of them.</summary>
internal sealed class UnavailableSessionReplica : ISessionReplica
{
    public Task<int> Append(OwnerScope scope, IReadOnlyList<SessionRecord> records, CancellationToken cancellationToken = default) =>
        throw Fault();

    public Task<SessionReplicaPage> ReadChanges(OwnerId owner, SessionReplicaCursor? cursor, int maxItems, CancellationToken cancellationToken = default) =>
        throw Fault();

    private static SyncReplicaException Fault() => new(
        SyncErrorCodes.ReplicaUnavailable, "The session replica is not available yet. Try again shortly.");
}

/// <summary>A replica that refuses one side with a given code — the answers a
/// push or a pull can get that are neither "not up yet" nor success.</summary>
internal sealed class RefusingSessionReplica(string code, string message, bool onRead = false) : ISessionReplica
{
    public Task<int> Append(OwnerScope scope, IReadOnlyList<SessionRecord> records, CancellationToken cancellationToken = default) =>
        onRead ? Task.FromResult(records.Count) : throw new SyncReplicaException(code, message);

    public Task<SessionReplicaPage> ReadChanges(OwnerId owner, SessionReplicaCursor? cursor, int maxItems, CancellationToken cancellationToken = default) =>
        onRead
            ? throw new SyncReplicaException(code, message)
            : Task.FromResult(new SessionReplicaPage([], new SessionReplicaCursor(owner, string.Empty), HasMore: false));
}

/// <summary>A replica that counts what reached it. The cross-owner case is about
/// the cursor being refused <em>before</em> the store is touched, and nothing
/// else in the response distinguishes "refused early" from "refused late".</summary>
internal sealed class CountingSessionReplica : ISessionReplica
{
    private int _reads;

    internal int Reads => Volatile.Read(ref _reads);

    public Task<int> Append(OwnerScope scope, IReadOnlyList<SessionRecord> records, CancellationToken cancellationToken = default) =>
        Task.FromResult(records.Count);

    public Task<SessionReplicaPage> ReadChanges(OwnerId owner, SessionReplicaCursor? cursor, int maxItems, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);

        return Task.FromResult(new SessionReplicaPage(
            [], new SessionReplicaCursor(owner, "0"), HasMore: false));
    }
}
