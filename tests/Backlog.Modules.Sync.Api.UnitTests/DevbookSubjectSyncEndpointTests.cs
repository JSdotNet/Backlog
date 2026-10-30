using System.Net;
using System.Net.Http.Json;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Endpoints;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Devbook subject state replication over HTTP: a state one desktop sets,
/// another desktop of the same owner takes out — the cases
/// <see cref="AnnotationSyncEndpointTests"/> pins, over the subject container,
/// with the real cursor codec.
/// </summary>
public class DevbookSubjectSyncEndpointTests : IDisposable
{
    private const string Repository = "JSdotNet/Backlog";
    private const string Record = ".devbook/arc42/tdr/0003-paging.md";

    private readonly SyncServiceFactory _service = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Two_paired_devices_share_a_subjects_state()
    {
        var (desktop, laptop) = await PairedDevices();
        var id = Guid.CreateVersion7();

        var pushed = await desktop.PushSubjects([Debt(id, DateTimeOffset.UtcNow, "planned")]);

        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        Assert.Equal(1, (await pushed.Content.ReadFromJsonAsync<PushDevbookSubjectsResponse>(Cancellation))!.Accepted);

        var pulled = await laptop.PullSubjects();

        var arrived = Assert.Single(pulled.Subjects);
        Assert.Equal(id, arrived.Change.Id);
        Assert.Equal("debt", arrived.Change.Subject.Kind);
        Assert.Equal(Record, arrived.Change.Subject.Key);
        Assert.Equal("planned", arrived.Change.Subject.State);
        Assert.Equal(Repository, arrived.Change.Subject.RepositoryAlias);
        Assert.False(pulled.HasMore);
        Assert.False(string.IsNullOrWhiteSpace(pulled.Since));
    }

    [Fact]
    public async Task A_proposals_approval_crosses_whole()
    {
        var (desktop, laptop) = await PairedDevices();
        var at = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        var approval = new DevbookSubjectDecisionPayload("DEV-TOWER", at, "sha256:0123abcd");

        await desktop.PushSubjects(
        [
            new DevbookSubjectChange(Guid.CreateVersion7(), at, new DevbookSubjectPayload(
                Repository, "proposal", "add-subject-store", null, "approved", "DEV-TOWER", at, approval)),
        ]);

        var arrived = Assert.Single((await laptop.PullSubjects()).Subjects);
        Assert.Equal(approval, arrived.Change.Subject.Approval);
        Assert.Null(arrived.Change.Subject.Acceptance);
    }

    [Fact]
    public async Task A_separate_owner_pulls_nothing()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        await mine.PushSubjects([Debt(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "planned")]);

        Assert.Empty((await theirs.PullSubjects()).Subjects);
        Assert.Single((await mine.PullSubjects()).Subjects);
    }

    [Fact]
    public async Task A_cursor_minted_for_another_owner_is_refused()
    {
        var mine = await _service.CreateClient().RegisteredDevice("Mine");
        var theirs = await _service.CreateClient().RegisteredDevice("Theirs");

        await mine.PushSubjects([Debt(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "planned")]);
        var myCursor = (await mine.PullSubjects()).Since;

        var replayed = await theirs.GetAsync(
            $"{SyncRoutes.Absolute(SyncRoutes.DevbookSubjects)}?since={Uri.EscapeDataString(myCursor)}", Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, replayed.StatusCode);
        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, (await replayed.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
    }

    /// <summary>The laptop pulled <c>identified</c>, the desktop moved the record
    /// on to <c>planned</c>, and the laptop's echo of what it pulled arrives
    /// last. The replica answers the honest count and keeps <c>planned</c>.</summary>
    [Fact]
    public async Task A_stale_state_pushed_after_a_later_one_is_refused_and_the_later_one_travels()
    {
        var (desktop, laptop) = await PairedDevices();
        var id = Guid.CreateVersion7();
        var identified = new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
        var planned = identified.AddDays(1);

        await desktop.PushSubjects([Debt(id, identified, "identified")]);
        var laptopCursor = (await laptop.PullSubjects()).Since;

        await desktop.PushSubjects([Debt(id, planned, "planned")]);

        var echoed = await laptop.PushSubjects([Debt(id, identified, "identified")]);
        Assert.Equal(0, (await echoed.Content.ReadFromJsonAsync<PushDevbookSubjectsResponse>(Cancellation))!.Accepted);

        var arrived = Assert.Single((await laptop.PullSubjects(laptopCursor)).Subjects);
        Assert.Equal("planned", arrived.Change.Subject.State);
    }

    [Fact]
    public async Task A_push_of_more_changes_than_the_cap_is_refused_whole()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushSubjects(
            [.. Enumerable.Range(0, SyncRequestLimits.MaximumPushDevbookSubjects + 1)
                .Select(_ => Debt(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "planned"))]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(SyncErrorCodes.DevbookSubjectBatchTooLarge, (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))?.Code);
        Assert.Empty((await device.PullSubjects()).Subjects);
    }

    [Fact]
    public async Task A_state_outside_its_kinds_row_is_refused_with_the_whole_batch()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");

        var response = await device.PushSubjects(
        [
            Debt(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "planned"),
            Debt(Guid.CreateVersion7(), DateTimeOffset.UtcNow, "decided"),
        ]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation);
        Assert.Equal(SyncErrorCodes.DevbookSubjectInvalid, problem?.Code);
        Assert.Empty((await device.PullSubjects()).Subjects);
    }

    [Fact]
    public async Task An_unpaired_caller_is_refused()
    {
        var response = await _service.CreateClient().GetAsync(SyncRoutes.Absolute(SyncRoutes.DevbookSubjects), Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    internal static DevbookSubjectChange Debt(Guid id, DateTimeOffset at, string state) =>
        new(id, at, new DevbookSubjectPayload(Repository, "debt", Record, null, state, "DEV-TOWER", at));

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

internal static class DevbookSubjectSyncClientExtensions
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static Task<HttpResponseMessage> PushSubjects(this HttpClient client, IReadOnlyList<DevbookSubjectChange> changes) =>
        client.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.DevbookSubjects), new PushDevbookSubjectsRequest(changes), Cancellation);

    internal static async Task<PullDevbookSubjectsResponse> PullSubjects(this HttpClient client, string? since = null)
    {
        var route = SyncRoutes.Absolute(SyncRoutes.DevbookSubjects);
        var response = await client.GetAsync(
            since is null ? route : $"{route}?since={Uri.EscapeDataString(since)}", Cancellation);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PullDevbookSubjectsResponse>(Cancellation))!;
    }
}
