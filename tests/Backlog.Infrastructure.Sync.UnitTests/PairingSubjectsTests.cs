using System.Net;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The Sync context closing the notifications about a pairing when its code is redeemed or
/// expires, and answering the startup probe about it
/// (<c>.devbook/domain/notifications/domain.md#subject</c>).
/// </summary>
public sealed class PairingSubjectsTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Nine);
    private readonly RecordingNotifications _notifications = new RecordingNotifications();

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
    }

    [Fact]
    public async Task A_code_that_expires_closes_the_notifications_about_its_pairing()
    {
        using var pairings = Pairings();
        var id = pairings.Issued(new PairingCodeResponse("K7MN9PQR", Nine.AddMinutes(10)));

        _clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Empty(_notifications.Closed);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await pairings.LastEnd;

        var closed = Assert.Single(_notifications.Closed);
        Assert.Equal(new NotificationSubject("sync", "pairing", id.ToString("D")), closed.Subject);
        Assert.Equal("Pairing finished", closed.Reason);
        Assert.False(pairings.IsOpen(id));
        Assert.Null(pairings.Current);
    }

    [Fact]
    public async Task A_redeemed_code_closes_them_once_and_its_expiry_closes_nothing_more()
    {
        using var pairings = Pairings();
        var id = pairings.Issued(new PairingCodeResponse("K7MN9PQR", Nine.AddMinutes(10)));

        await pairings.RedeemedAsync(id);
        await pairings.RedeemedAsync(id);
        _clock.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal(id.ToString("D"), Assert.Single(_notifications.Closed).Subject.Id);
    }

    [Fact]
    public async Task The_probe_calls_a_pairing_live_only_while_this_process_holds_it_open()
    {
        using var pairings = Pairings();
        var open = pairings.Issued(new PairingCodeResponse("AAAA1111", Nine.AddMinutes(10)));
        var redeemed = pairings.Issued(new PairingCodeResponse("BBBB2222", Nine.AddMinutes(10)));
        await pairings.RedeemedAsync(redeemed);
        var probe = new PairingSubjectProbe(pairings);

        Assert.Equal("sync", probe.Context);
        Assert.True(await probe.IsLiveAsync(Subject(open), TestContext.Current.CancellationToken));
        Assert.False(await probe.IsLiveAsync(Subject(redeemed), TestContext.Current.CancellationToken));
        // Issued by an earlier run of the app: the code went with it.
        Assert.False(await probe.IsLiveAsync(Subject(Guid.NewGuid()), TestContext.Current.CancellationToken));
        Assert.True(await probe.IsLiveAsync(new NotificationSubject("sync", "device", "x"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_pairing_ends_quietly_when_closing_fails_or_no_notifications_are_composed()
    {
        _notifications.Fails = new InvalidOperationException("locked");
        using var failing = Pairings();
        using var absent = new PairingSubjects(() => null, _clock);
        var one = failing.Issued(new PairingCodeResponse("AAAA1111", Nine.AddMinutes(10)));
        var two = absent.Issued(new PairingCodeResponse("BBBB2222", Nine.AddMinutes(10)));

        await failing.RedeemedAsync(one);
        await absent.RedeemedAsync(two);

        Assert.False(failing.IsOpen(one));
        Assert.False(absent.IsOpen(two));
    }

    private PairingSubjects Pairings() => new(() => _notifications, _clock);

    private static NotificationSubject Subject(Guid id) => new("sync", "pairing", id.ToString("D"));
}
