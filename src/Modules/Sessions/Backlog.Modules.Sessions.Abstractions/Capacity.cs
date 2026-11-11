namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>One of the four limits a <see cref="CapacityReading"/> is held against, in the
/// order <see cref="CapacityReading.HeldBy"/> names them (local ADR 0051 §4).</summary>
public enum CapacityLimit
{
    /// <summary>The loop's dispatched sessions working.</summary>
    Sessions,

    /// <summary>Plan usage, as a share of the weekly limit.</summary>
    Usage,

    /// <summary>This machine's CPU.</summary>
    Cpu,

    /// <summary>This machine's free memory.</summary>
    Memory
}

/// <summary>
/// The limits a loop starts no more sessions at (local ADR 0051 §4). Each can be
/// overridden with <c>with</c>.
/// </summary>
/// <param name="Sessions">Dispatched sessions working at once.</param>
/// <param name="UsagePercent">Plan usage, in percent of the weekly limit.</param>
/// <param name="CpuPercent">CPU busy, in percent.</param>
/// <param name="MinFreeGb">Free memory the machine keeps, in GB; held below it.</param>
public sealed record CapacityLimits(int Sessions, double UsagePercent, double CpuPercent, double MinFreeGb)
{
    /// <summary>Three sessions, 80 % usage, 80 % CPU and 4 GB free.</summary>
    public static CapacityLimits Default { get; } = new(3, 80, 80, 4);
}

/// <summary>This machine's CPU and free memory as one read found them. A figure the
/// machine could not give is null.</summary>
/// <param name="CpuPercent">CPU busy since the previous read, 0–100.</param>
/// <param name="FreeMemoryGb">Physical memory available, in GB (bytes / 1024³).</param>
public sealed record MachineLoad(double? CpuPercent, double? FreeMemoryGb)
{
    /// <summary>Neither figure read.</summary>
    public static MachineLoad Unavailable { get; } = new(null, null);
}

/// <summary>
/// PORT — this machine's CPU and free memory. Only the desktop head registers one; a host
/// without one reads both figures unavailable.
/// </summary>
public interface IMachineLoad
{
    /// <summary>The load now. Never throws but for cancellation: a figure the machine
    /// cannot give is null.</summary>
    Task<MachineLoad> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// What a loop has room for (local ADR 0051 §4,
/// <c>.devbook/domain/sessions/domain.md#capacity-reading</c>): four figures, each beside
/// its limit. A null figure is unavailable and holds nothing back.
/// </summary>
/// <param name="WorkingSessions">The loop's dispatched sessions Running or Stalled.</param>
/// <param name="PlanUsagePercent">The week's estimated share of the weekly limit, in percent.</param>
/// <param name="CpuPercent">CPU busy, in percent.</param>
/// <param name="FreeMemoryGb">Free memory, in GB.</param>
/// <param name="Limits">The limits the figures are held against.</param>
public sealed record CapacityReading(
    int? WorkingSessions,
    double? PlanUsagePercent,
    double? CpuPercent,
    double? FreeMemoryGb,
    CapacityLimits Limits)
{
    /// <summary>Whether no limit is reached.</summary>
    public bool HasRoom => HeldBy().Count == 0;

    /// <summary>Every limit reached, in the order sessions, usage, CPU, memory — on the
    /// edges <c>capacity.ps1</c> used: at or over a limit, or below the free memory kept.</summary>
    public IReadOnlyList<CapacityLimit> HeldBy()
    {
        var held = new List<CapacityLimit>(4);

        if (WorkingSessions >= Limits.Sessions) held.Add(CapacityLimit.Sessions);
        if (PlanUsagePercent >= Limits.UsagePercent) held.Add(CapacityLimit.Usage);
        if (CpuPercent >= Limits.CpuPercent) held.Add(CapacityLimit.Cpu);
        if (FreeMemoryGb < Limits.MinFreeGb) held.Add(CapacityLimit.Memory);

        return held;
    }
}

/// <summary>
/// Works a <see cref="CapacityReading"/> out of what the stores answered, as a pure
/// function. Each input a host could not read is null, and leaves its figure unavailable.
/// </summary>
public static class CapacityReadings
{
    /// <summary>The reading over the given inputs.</summary>
    /// <param name="requests">Every session request; null when unreadable.</param>
    /// <param name="sessions">The sessions with Waiting already read in, as
    /// <see cref="IAgentSessionSource"/> answers them; null when unreadable.</param>
    /// <param name="weekly">The week's usage; null when unreadable.</param>
    /// <param name="apiRequests">The requests <paramref name="weekly"/> was worked out over.</param>
    /// <param name="load">This machine's load; null when the host reads none.</param>
    /// <param name="limits">The limits.</param>
    /// <param name="dispatcher">When given, only the requests this dispatcher claimed count.</param>
    public static CapacityReading Read(
        IReadOnlyList<SessionRequest>? requests,
        IReadOnlyList<AgentSession>? sessions,
        WeeklyUsage? weekly,
        IReadOnlyList<ClaudeApiRequest>? apiRequests,
        MachineLoad? load,
        CapacityLimits limits,
        string? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(limits);

        return new(
            Working(requests, sessions, dispatcher),
            PlanUsage(weekly, apiRequests),
            load?.CpuPercent,
            load?.FreeMemoryGb,
            limits);
    }

    /// <summary>The distinct sessions of Started or Linked requests that read Running or
    /// Stalled. Waiting is read in upstream, so it is not counted here.</summary>
    private static int? Working(IReadOnlyList<SessionRequest>? requests, IReadOnlyList<AgentSession>? sessions, string? dispatcher)
    {
        if (requests is null || sessions is null) return null;

        var dispatched = requests
            .Where(request => request.State is SessionRequestState.Started or SessionRequestState.Linked)
            .Where(request => dispatcher is null || string.Equals(request.ClaimedBy, dispatcher, StringComparison.Ordinal))
            .Select(request => request.SessionId)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return sessions
            .Where(session => session.State is AgentSessionState.Running or AgentSessionState.Stalled)
            .Select(session => session.DesktopSessionId)
            .OfType<string>()
            .Where(dispatched.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    /// <summary>The higher of the all-models and Fable shares of their limits, in percent.</summary>
    private static double? PlanUsage(WeeklyUsage? weekly, IReadOnlyList<ClaudeApiRequest>? apiRequests)
    {
        if (weekly is null || apiRequests is null) return null;

        if (weekly.ShareOf(apiRequests) is not { } share) return null;

        return (share.OfLimit, share.FableOfLimit) switch
        {
            ({ } all, { } fable) => Math.Max(all, fable) * 100,
            ({ } all, null) => all * 100,
            (null, { } fable) => fable * 100,
            _ => null
        };
    }
}

/// <summary>
/// PORT — measures a <see cref="CapacityReading"/> from this app's own stores and this
/// machine's load.
/// </summary>
public interface ICapacityReader
{
    /// <summary>The reading now, against <paramref name="limits"/>. Never throws but for
    /// cancellation: a figure whose store is missing or fails reads unavailable.</summary>
    /// <param name="limits">The limits.</param>
    /// <param name="dispatcher">When given, only the sessions that dispatcher started count.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<CapacityReading> ReadAsync(CapacityLimits limits, string? dispatcher = null, CancellationToken cancellationToken = default);
}
