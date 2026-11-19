namespace Backlog.Infrastructure.Sessions;

/// <summary>One cumulative reading of the system's CPU times, in 100-nanosecond units, as
/// <c>GetSystemTimes</c> gives them: kernel time includes idle time.</summary>
public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

/// <summary>
/// The CPU busy share a loop's capacity reading takes (local ADR 0051 §4): the share over the
/// last <see cref="Span"/> of the cumulative CPU times a platform samples — between the newest
/// sample and the newest one at least that much older — whenever the read comes. While less
/// than <see cref="Span"/> has been sampled, the share is over all that is held. It keeps only
/// the samples that window can still use, so its size stays a handful whatever the sampling
/// runs for.
/// </summary>
/// <param name="clock">When each sample is taken; the system clock when null.</param>
public sealed class CpuWindow(TimeProvider? clock = null)
{
    /// <summary>How far back the share reaches: the 30 seconds <c>dispatch-sessions</c>
    /// averages its CPU limit over.</summary>
    public static readonly TimeSpan Span = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly LinkedList<(DateTimeOffset At, CpuTimes Times)> _samples = new();
    private double? _last;

    /// <summary>Records <paramref name="times"/> as the newest sample, taken now, and lets
    /// go of every sample older than the newest one at least <see cref="Span"/> back.</summary>
    public void Add(CpuTimes times)
    {
        var at = _clock.GetUtcNow();

        lock (_gate)
        {
            _samples.AddLast((at, times));

            while (_samples.First!.Next is { } next && next.Value.At <= at - Span) _samples.RemoveFirst();
        }
    }

    /// <summary>The busy share, 0–100, over the last <see cref="Span"/>, or over all that is
    /// held while less has been sampled; with no time between the two samples it compares,
    /// the last share read; null with fewer than two samples.</summary>
    public double? BusyPercent()
    {
        lock (_gate)
        {
            if (_samples.Count < 2) return _last;

            var (_, then) = _samples.First!.Value;
            var (_, now) = _samples.Last!.Value;

            var total = (now.Kernel - then.Kernel) + (now.User - then.User);
            var idle = now.Idle - then.Idle;

            if (total == 0) return _last;

            _last = Math.Clamp((total - idle) * 100d / total, 0, 100);
            return _last;
        }
    }
}
