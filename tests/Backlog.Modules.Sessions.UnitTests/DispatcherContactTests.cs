using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// Dispatcher contact (<c>.devbook/domain/sessions/domain.md#session-request-queue</c>,
/// local ADR 0038 §4): when a dispatcher last reached this application on its wait route,
/// in memory since the application started, and whether it has gone silent — nothing
/// heard for more than two minutes, counted from the start when nothing was ever heard.
/// </summary>
public sealed class DispatcherContactTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Start);

    [Fact]
    public void Nothing_heard_since_the_application_started_reads_as_no_contact()
    {
        var contact = new DispatcherContact(_clock);

        Assert.Null(contact.LastHeardAt);
        Assert.Equal(Start, contact.StartedAt);
    }

    [Fact]
    public void The_latest_call_is_the_one_kept()
    {
        var contact = new DispatcherContact(_clock);

        _clock.Advance(TimeSpan.FromSeconds(10));
        contact.Heard();
        _clock.Advance(TimeSpan.FromSeconds(30));
        contact.Heard();

        Assert.Equal(Start.AddSeconds(40), contact.LastHeardAt);
    }

    [Fact]
    public void A_call_held_open_counts_as_contact_now_and_its_arrival_once_it_lets_go()
    {
        var contact = new DispatcherContact(_clock);
        _clock.Advance(TimeSpan.FromSeconds(5));

        var held = contact.Hold();
        _clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(Start.AddSeconds(5).AddMinutes(5), contact.LastHeardAt);

        held.Dispose();
        held.Dispose();

        Assert.Equal(Start.AddSeconds(5), contact.LastHeardAt);
    }

    [Fact]
    public void A_second_hold_keeps_contact_current_after_the_first_lets_go()
    {
        var contact = new DispatcherContact(_clock);

        var first = contact.Hold();
        using var second = contact.Hold();
        first.Dispose();
        _clock.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal(Start.AddMinutes(3), contact.LastHeardAt);
    }

    [Fact]
    public void The_silence_threshold_is_two_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), DispatcherContact.Threshold);
    }

    [Fact]
    public void A_dispatcher_heard_two_minutes_ago_is_not_yet_silent_and_one_heard_longer_ago_is()
    {
        var contact = new DispatcherContact(_clock);
        contact.Heard();

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(contact.IsSilent);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(contact.IsSilent);
    }

    [Fact]
    public void Nothing_heard_is_not_silent_until_two_minutes_after_the_application_started()
    {
        var contact = new DispatcherContact(_clock);

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.False(contact.IsSilent);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(contact.IsSilent);
    }

    [Fact]
    public void A_call_held_open_is_never_silent()
    {
        var contact = new DispatcherContact(_clock);
        using var held = contact.Hold();

        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.False(contact.IsSilent);
    }

    [Fact]
    public async Task Calls_recorded_at_once_from_many_threads_all_land()
    {
        var contact = new DispatcherContact(_clock);

        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() =>
        {
            using var held = contact.Hold();
            contact.Heard();
        }, TestContext.Current.CancellationToken)));

        _clock.Advance(TimeSpan.FromMinutes(1));

        // Every hold let go: contact reads as the last call, not as now.
        Assert.Equal(Start, contact.LastHeardAt);
    }
}
