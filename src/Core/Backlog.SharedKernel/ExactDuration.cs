namespace Backlog.SharedKernel;

/// <summary>
/// A duration as an exact decimal, counted from its ticks rather than converted from
/// <see cref="TimeSpan.TotalHours"/>.
/// <para>
/// A tick count divides exactly in decimal: seven hours and eighteen seconds is 7.005
/// hours. The double <see cref="TimeSpan.TotalHours"/> holds the nearest binary fraction
/// instead, and since .NET 11 a <c>(decimal)</c> cast keeps every digit of it —
/// 7.00499999… — where earlier runtimes kept fifteen and gave 7.005. A figure rounded to
/// the hundredth then lands a cent below its midpoint. Rounding from these values
/// rounds the duration itself, the same on every runtime.
/// </para>
/// </summary>
public static class ExactDuration
{
    /// <summary>The duration in hours, exactly.</summary>
    public static decimal Hours(TimeSpan span) => (decimal)span.Ticks / TimeSpan.TicksPerHour;

    /// <summary>The duration in seconds, exactly.</summary>
    public static decimal Seconds(TimeSpan span) => (decimal)span.Ticks / TimeSpan.TicksPerSecond;

    /// <summary>What share of <paramref name="whole"/> <paramref name="part"/> is, exactly;
    /// zero for an empty whole.</summary>
    public static decimal Share(TimeSpan part, TimeSpan whole) =>
        whole.Ticks == 0 ? 0m : (decimal)part.Ticks / whole.Ticks;
}
