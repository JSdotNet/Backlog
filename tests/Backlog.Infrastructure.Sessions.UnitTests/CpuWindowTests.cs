using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sessions.UnitTests;

/// <summary>
/// The CPU figure of a capacity reading (local ADR 0051 §4; <c>dispatch-sessions</c>' "cpu
/// average % over 30 s"): the busy share over the last 30 seconds of samples, however long
/// ago the previous read was, and over what is held while less than that has been sampled.
/// </summary>
public sealed class CpuWindowTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    /// <summary>100-nanosecond ticks in a second of one core's time.</summary>
    private const ulong Second = 10_000_000;

    private readonly FakeTimeProvider _clock = new(Start);
    private ulong _idle;
    private ulong _busy;

    /// <summary>Runs the machine for <paramref name="seconds"/> at <paramref name="busyShare"/>,
    /// sampling every five seconds as the desktop's sampler does.</summary>
    private void Run(CpuWindow window, int seconds, double busyShare)
    {
        for (var elapsed = 0; elapsed < seconds; elapsed += 5)
        {
            _clock.Advance(TimeSpan.FromSeconds(5));
            _busy += (ulong)(5 * Second * busyShare);
            _idle += (ulong)(5 * Second * (1 - busyShare));
            window.Add(Sample());
        }
    }

    // Kernel time includes idle time; the busy time is all counted as user time here.
    private CpuTimes Sample() => new(_idle, _idle, _busy);

    [Fact]
    public void The_share_is_the_last_30_seconds_however_long_ago_the_previous_read_was()
    {
        var window = new CpuWindow(_clock);
        window.Add(Sample());
        Run(window, 5, busyShare: 0);
        window.BusyPercent();

        Run(window, 20 * 60, busyShare: 0.02);
        Run(window, 30, busyShare: 1);

        Assert.Equal(100, window.BusyPercent()!.Value, 6);
    }

    [Fact]
    public void Two_reads_close_together_both_answer_the_last_30_seconds()
    {
        var window = new CpuWindow(_clock);
        window.Add(Sample());
        Run(window, 60, busyShare: 0.5);
        Run(window, 30, busyShare: 1);

        Assert.Equal(100, window.BusyPercent()!.Value, 6);

        Run(window, 5, busyShare: 1);

        Assert.Equal(100, window.BusyPercent()!.Value, 6);
    }

    /// <summary>The first read samples over a second: with less than 30 seconds held, the
    /// share is over all of it.</summary>
    [Fact]
    public void The_first_read_samples_over_a_second()
    {
        var window = new CpuWindow(_clock);
        window.Add(new CpuTimes(Idle: 0, Kernel: 0, User: 0));
        _clock.Advance(TimeSpan.FromSeconds(1));
        window.Add(new CpuTimes(Idle: Second / 4, Kernel: Second / 4, User: 3 * Second / 4));

        Assert.Equal(75, window.BusyPercent()!.Value, 6);
    }

    [Fact]
    public void Nothing_sampled_reads_unavailable()
    {
        Assert.Null(new CpuWindow(_clock).BusyPercent());
    }
}
