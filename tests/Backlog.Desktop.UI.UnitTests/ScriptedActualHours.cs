namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The actual hours a roadmap's begun heads show (local ADR 0019, §6), answered from
/// what a test scripts rather than from agent sessions. By default it answers that the
/// host cannot state them — <see langword="null"/> — so a band drawn by a test that is
/// not about actual hours reads exactly as it did before there were any.
/// </summary>
internal sealed class ScriptedActualHours : IRoadmapActualHours
{
    private int _reads;

    /// <summary>What every read answers, before it is cut to the asked range.</summary>
    public IReadOnlyDictionary<DateOnly, TimeSpan>? Answer { get; set; }

    /// <summary>Whether a read fails, as a Sessions store that cannot be read does.</summary>
    public bool Fails { get; set; }

    /// <summary>How many times the band asked.</summary>
    public int Reads => Volatile.Read(ref _reads);

    /// <summary>The range of the last read, from and through.</summary>
    public (DateOnly From, DateOnly Through)? LastRange { get; private set; }

    public Task<IReadOnlyDictionary<DateOnly, TimeSpan>?> ReadAsync(
        DateOnly from,
        DateOnly through,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _reads);
        LastRange = (from, through);

        if (Fails) throw new IOException("The session activity could not be read.");
        if (Answer is not { } answer) return Task.FromResult<IReadOnlyDictionary<DateOnly, TimeSpan>?>(null);

        return Task.FromResult<IReadOnlyDictionary<DateOnly, TimeSpan>?>(
            answer.Where(day => day.Key >= from && day.Key <= through).ToDictionary());
    }
}
