using System.Net;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;
using Backlog.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The Sync context's pairings as notification subjects
/// (<c>.devbook/domain/notifications/domain.md#subject</c>): asking on the issuing desktop
/// about the device that redeemed its code, ending the pairing when that is answered or the
/// code expires unredeemed, and answering the startup probe about it.
/// </summary>
public sealed class PairingSubjectsTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid ThisDesktop = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Phone = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly FakeTimeProvider _clock = new(Nine);
    private readonly RecordingNotifications _notifications = new();
    private readonly FakeRoster _roster = new();

    [Fact]
    public async Task Issuing_a_code_opens_a_pairing_named_by_an_id_and_never_by_the_code()
    {
        using var pairings = Pairings();
        using var handler = new StubHttpMessageHandler((_, _) =>
            StubHttpMessageHandler.Json(HttpStatusCode.Created, """{"code":"K7MN9PQR","expiresAt":"2026-10-22T09:10:00+00:00"}"""));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };
        var client = new DevicePairingClient(http, new InMemoryDeviceCredentialStore(), pairings);

        await client.IssuePairingCodeAsync(TestContext.Current.CancellationToken);

        var id = Assert.NotNull(pairings.Current);
        Assert.True(pairings.IsOpen(id));
        Assert.Empty(_notifications.Closed);
        Assert.Empty(_notifications.Published);
    }

    [Fact]
    public async Task A_code_that_expires_unredeemed_closes_the_notifications_about_its_pairing()
    {
        using var pairings = Pairings();
        var id = pairings.Issued(new PairingCodeResponse("K7MN9PQR", Nine.AddMinutes(10)));

        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Empty(_notifications.Closed);

        // Watched a little past its expiry, for a redemption in its last seconds.
        _clock.Advance(PairingSubjects.ExpiryGrace);
        await pairings.LastEnd;

        var closed = Assert.Single(_notifications.Closed);
        Assert.Equal(new NotificationSubject("sync", "pairing", id.ToString("D")), closed.Subject);
        Assert.Equal("Pairing finished", closed.Reason);
        Assert.False(pairings.IsOpen(id));
        Assert.Null(pairings.Current);
        Assert.Empty(_notifications.Published);
    }

    [Fact]
    public async Task A_device_this_desktops_code_paired_is_asked_about_here_only_as_allow_or_refuse()
    {
        using var pairings = Pairings();
        var id = pairings.Issued(new PairingCodeResponse("K7MN9PQR", Nine.AddMinutes(10)));
        _roster.Devices.Add(new DeviceSummary(ThisDesktop, "Study desktop", Nine.AddDays(-3), PairedBy: null));
        _roster.Devices.Add(new DeviceSummary(Phone, "Pixel 8", Nine.AddMinutes(1), PairedBy: ThisDesktop));

        _clock.Advance(PairingSubjects.PollInterval);
        await pairings.LastPoll;
        await pairings.LastAsk;

        var (_, draft) = Assert.Single(_notifications.Published);
        Assert.Equal(NotificationKind.Question, draft.Kind);
        Assert.Equal("Pair Pixel 8?", draft.Title);
        Assert.Equal(new NotificationSubject("sync", "pairing", id.ToString("D")), draft.Subject);
        Assert.Equal(new NotificationSource("sync", SyncNotificationSubjects.SourceKey(id, Phone)), draft.Source);
        var answer = Assert.IsType<AnswerSpec>(draft.Answer);
        Assert.Equal(AnswerOn.Owner, answer.AnswerOn);
        Assert.Equal(["allow", "refuse"], answer.Choices.Select(choice => choice.Id));
        Assert.Equal(["Allow", "Refuse"], answer.Choices.Select(choice => choice.Label));
        Assert.Null(answer.Open);
    }

    [Fact]
    public async Task A_code_redeemed_in_its_last_seconds_is_still_asked_about()
    {
        using var pairings = Pairings();
        var id = pairings.Issued(new PairingCodeResponse("K7MN9PQR", Nine.AddMinutes(10)));
        _roster.Devices.Add(new DeviceSummary(Phone, "Pixel 8", Nine.AddMinutes(10).AddSeconds(-1), PairedBy: ThisDesktop));
        _roster.Hidden = true;

        // Every read before the expiry misses the device; it shows up only afterwards.
        _clock.Advance(TimeSpan.FromMinutes(10));
        await pairings.LastPoll;
        _roster.Hidden = false;
        _clock.Advance(PairingSubjects.ExpiryGrace);
        await pairings.LastEnd;
        await pairings.LastAsk;

        Assert.Equal("Pair Pixel 8?", Assert.Single(_notifications.Published).Draft.Title);
        Assert.True(pairings.IsOpen(id));
        Assert.Empty(_notifications.Closed);
    }

    [Fact]
    public async Task A_redeemed_pairing_outlives_its_code_until_the_question_is_settled()
    {
        using var pairings = Pairings();
        var id = pairings.Issued(new PairingCodeResponse("K7MN9PQR", Nine.AddMinutes(10)));

        await pairings.RedeemedAsync(id, Phone, "Pixel 8");
        await pairings.RedeemedAsync(id, Phone, "Pixel 8");
        _clock.Advance(TimeSpan.FromMinutes(30));

        Assert.True(pairings.IsOpen(id));
        Assert.Single(_notifications.Published);
        Assert.Empty(_notifications.Closed);

        await pairings.SettledAsync(id);
        await pairings.SettledAsync(id);

        Assert.False(pairings.IsOpen(id));
        Assert.Equal(id.ToString("D"), Assert.Single(_notifications.Closed).Subject.Id);
    }

    [Fact]
    public async Task Devices_another_device_paired_or_paired_before_the_code_are_not_asked_about()
    {
        using var pairings = Pairings();
        pairings.Issued(new PairingCodeResponse("K7MN9PQR", Nine.AddMinutes(10)));
        _roster.Devices.Add(new DeviceSummary(Guid.NewGuid(), "Laptop's phone", Nine.AddMinutes(1), PairedBy: Guid.NewGuid()));
        _roster.Devices.Add(new DeviceSummary(Guid.NewGuid(), "Old phone", Nine.AddDays(-1), PairedBy: ThisDesktop));

        _clock.Advance(PairingSubjects.PollInterval);
        await pairings.LastPoll;

        Assert.Empty(_notifications.Published);
        Assert.Equal(1, _roster.Reads);
    }

    [Fact]
    public async Task The_device_list_is_read_only_while_a_code_waits_to_be_redeemed()
    {
        using var pairings = Pairings();
        var id = pairings.Issued(new PairingCodeResponse("K7MN9PQR", Nine.AddMinutes(10)));
        await pairings.RedeemedAsync(id, Phone, "Pixel 8");

        _clock.Advance(PairingSubjects.PollInterval * 4);
        await pairings.LastPoll;

        Assert.Equal(0, _roster.Reads);
    }

    [Fact]
    public async Task The_probe_calls_a_pairing_ended_only_when_this_process_saw_it_end()
    {
        using var pairings = Pairings();
        var open = pairings.Issued(new PairingCodeResponse("AAAA1111", Nine.AddMinutes(10)));
        var settled = pairings.Issued(new PairingCodeResponse("BBBB2222", Nine.AddMinutes(10)));
        await pairings.RedeemedAsync(settled, Phone, "Pixel 8");
        await pairings.SettledAsync(settled);
        var probe = new PairingSubjectProbe(pairings);

        Assert.Equal("sync", probe.Context);
        Assert.True(await probe.IsLiveAsync(Subject(open), TestContext.Current.CancellationToken));
        Assert.False(await probe.IsLiveAsync(Subject(settled), TestContext.Current.CancellationToken));
        // Asked about by an earlier run of the app: its device is paired, so the question
        // stays until the person answers it.
        Assert.True(await probe.IsLiveAsync(Subject(Guid.NewGuid()), TestContext.Current.CancellationToken));
        Assert.True(await probe.IsLiveAsync(new NotificationSubject("sync", "device", "x"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_pairing_ends_quietly_when_closing_fails_or_no_notifications_are_composed()
    {
        using var failing = Pairings();
        using var absent = new PairingSubjects(() => null, _clock);
        var one = failing.Issued(new PairingCodeResponse("AAAA1111", Nine.AddMinutes(10)));
        var two = absent.Issued(new PairingCodeResponse("BBBB2222", Nine.AddMinutes(10)));
        _notifications.Fails = new InvalidOperationException("locked");

        await failing.RedeemedAsync(one, Phone, "Pixel 8");
        await absent.RedeemedAsync(two, Phone, "Pixel 8");
        await failing.SettledAsync(one);
        await absent.SettledAsync(two);

        Assert.False(failing.IsOpen(one));
        Assert.False(absent.IsOpen(two));
    }

    [Fact]
    public async Task Refuse_revokes_the_device_and_ends_the_pairing()
    {
        using var pairings = Pairings();
        var id = pairings.Issued(new PairingCodeResponse("K7MN9PQR", Nine.AddMinutes(10)));
        await pairings.RedeemedAsync(id, Phone, "Pixel 8");
        var answers = new PairingAnswers(pairings, () => _roster);

        await answers.HandleAnswerAsync(Answer(id, Phone, "refuse"), TestContext.Current.CancellationToken);

        Assert.Equal("sync", answers.Source);
        Assert.Equal([Phone], _roster.Revoked);
        Assert.False(pairings.IsOpen(id));
        Assert.Single(_notifications.Closed);
    }

    [Fact]
    public async Task Allow_keeps_the_device_and_ends_the_pairing()
    {
        using var pairings = Pairings();
        var id = pairings.Issued(new PairingCodeResponse("K7MN9PQR", Nine.AddMinutes(10)));
        await pairings.RedeemedAsync(id, Phone, "Pixel 8");
        var answers = new PairingAnswers(pairings, () => _roster);

        await answers.HandleAnswerAsync(Answer(id, Phone, "allow"), TestContext.Current.CancellationToken);

        Assert.Empty(_roster.Revoked);
        Assert.False(pairings.IsOpen(id));
    }

    [Fact]
    public async Task A_refusal_replayed_after_a_restart_revokes_from_the_source_key_and_a_device_already_gone_counts()
    {
        // A fresh process: nothing in memory remembers the pairing.
        using var pairings = Pairings();
        var answers = new PairingAnswers(pairings, () => _roster);
        var pairing = Guid.NewGuid();
        _roster.RevokeResult = Result.Failure(Error.NotFound(SyncErrorCodes.DeviceNotFound, "gone"));

        await answers.HandleAnswerAsync(Answer(pairing, Phone, "refuse"), TestContext.Current.CancellationToken);
        await answers.HandleAnswerAsync(Answer(pairing, Phone, "refuse"), TestContext.Current.CancellationToken);

        Assert.Equal([Phone, Phone], _roster.Revoked);
    }

    [Fact]
    public async Task A_revocation_the_service_did_not_make_throws_so_the_answer_is_replayed()
    {
        using var pairings = Pairings();
        var id = pairings.Issued(new PairingCodeResponse("K7MN9PQR", Nine.AddMinutes(10)));
        await pairings.RedeemedAsync(id, Phone, "Pixel 8");
        var answers = new PairingAnswers(pairings, () => _roster);
        _roster.RevokeResult = Result.Failure(Error.Unexpected(DevicePairingClient.UnreachableCode, "offline"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            answers.HandleAnswerAsync(Answer(id, Phone, "refuse"), TestContext.Current.CancellationToken));

        Assert.True(pairings.IsOpen(id));
    }

    [Fact]
    public async Task The_pairing_client_lists_and_revokes_devices_on_the_sync_service()
    {
        var calls = new List<string>();
        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            calls.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return request.Method == HttpMethod.Get
                ? StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"callerDeviceId":"{{ThisDesktop}}","devices":[{"deviceId":"{{Phone}}","name":"Pixel 8","registeredAt":"2026-10-22T09:01:00+00:00","pairedBy":"{{ThisDesktop}}"}]}""")
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };
        var client = new DevicePairingClient(http, new InMemoryDeviceCredentialStore());

        var listed = await client.ListDevicesAsync(TestContext.Current.CancellationToken);
        var revoked = await client.RevokeDeviceAsync(Phone, TestContext.Current.CancellationToken);

        Assert.True(listed.IsSuccess);
        Assert.Equal(ThisDesktop, listed.Value.CallerDeviceId);
        Assert.Equal(ThisDesktop, Assert.Single(listed.Value.Devices).PairedBy);
        Assert.True(revoked.IsSuccess);
        Assert.Equal(["GET /api/sync/devices", $"DELETE /api/sync/devices/{Phone:D}"], calls);
    }

    private PairingSubjects Pairings() => new(() => _notifications, _clock, roster: () => _roster);

    private static NotificationSubject Subject(Guid id) => new("sync", "pairing", id.ToString("D"));

    private static NotificationAnswer Answer(Guid pairing, Guid device, string choice) =>
        new(Guid.NewGuid(), new NotificationSource("sync", SyncNotificationSubjects.SourceKey(pairing, device)), Guid.NewGuid(), ThisDesktop, [choice], null, Nine);

    private sealed class FakeRoster : IDeviceRoster
    {
        public List<DeviceSummary> Devices { get; } = [];

        public List<Guid> Revoked { get; } = [];

        public int Reads { get; private set; }

        /// <summary>Answers an empty list while set, as the service does before it has
        /// stored a redemption.</summary>
        public bool Hidden { get; set; }

        public Result RevokeResult { get; set; } = Result.Success();

        public Task<Result<DeviceListResponse>> ListDevicesAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(Result.Success(new DeviceListResponse(ThisDesktop, Hidden ? [] : [.. Devices])));
        }

        public Task<Result> RevokeDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            Revoked.Add(deviceId);
            return Task.FromResult(RevokeResult);
        }
    }
}
