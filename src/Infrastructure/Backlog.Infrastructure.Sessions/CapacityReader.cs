using Backlog.Modules.Sessions.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// Measures a <see cref="CapacityReading"/> from this app's stores (local ADR 0051 §4): the
/// session requests, the sessions with Waiting read in, the week's usage read the way the
/// Sessions pane reads it, and this machine's load.
/// <para>
/// Every store but the session source is optional. A store that is missing or fails leaves
/// only its own figure unavailable, so the reading never throws but for cancellation.
/// </para>
/// </summary>
internal sealed partial class CapacityReader(
    IAgentSessionSource sessions,
    ISessionRequestStore? requests,
    IClaudeApiRequestStore? apiRequests,
    IAgentSessionRecordStore? records,
    IMachineLoad? machineLoad,
    TimeProvider? clock = null,
    ILogger? logger = null) : ICapacityReader
{
    /// <summary>How far back a limit refusal can still size a weekly limit; the Sessions
    /// pane's horizon.</summary>
    private static readonly TimeSpan LimitHitHorizon = TimeSpan.FromDays(28);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public async Task<CapacityReading> ReadAsync(CapacityLimits limits, string? dispatcher = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        cancellationToken.ThrowIfCancellationRequested();

        // First, because a first CPU read samples over a second while the stores answer.
        var load = ReadLoadAsync(cancellationToken);

        var held = await ReadRequestsAsync(cancellationToken).ConfigureAwait(false);
        var live = await ReadSessionsAsync(cancellationToken).ConfigureAwait(false);
        var (weekly, week) = await ReadWeeklyAsync(cancellationToken).ConfigureAwait(false);

        return CapacityReadings.Read(held, live, weekly, week, await load.ConfigureAwait(false), limits, dispatcher);
    }

    private async Task<IReadOnlyList<SessionRequest>?> ReadRequestsAsync(CancellationToken cancellationToken)
    {
        if (requests is null) return null;

        try
        {
            return await requests.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SessionRequestsUnreadable(_logger, exception);
            return null;
        }
    }

    private async Task<IReadOnlyList<AgentSession>?> ReadSessionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await sessions.GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken).ConfigureAwait(false);
            return catalog.Sessions;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SessionsUnreadable(_logger, exception);
            return null;
        }
    }

    /// <summary>The week's usage over the stored requests inside the horizon, as the
    /// Sessions pane reads it: records that fail only lose the refusals. Both reads are
    /// bounded by date in the store, so a check-in never reads the whole history.</summary>
    private async Task<(WeeklyUsage? Weekly, IReadOnlyList<ClaudeApiRequest>? Requests)> ReadWeeklyAsync(CancellationToken cancellationToken)
    {
        if (apiRequests is null) return (null, null);

        var now = _clock.GetUtcNow();
        IReadOnlyList<ClaudeApiRequest> week;

        try
        {
            // Only the limit horizon and the week before its oldest refusal can matter.
            week = await apiRequests.ListSinceAsync(now - LimitHitHorizon - UsageWeek.Length, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RequestTelemetryUnreadable(_logger, exception);
            return (null, null);
        }

        IReadOnlyList<AgentLimitHit> hits = [];

        if (records is not null)
        {
            try
            {
                var since = now - LimitHitHorizon;
                // A refusal is folded into its record no earlier than it happened, so a record
                // last amended before the horizon holds none inside it.
                var amended = await Task.Run(() => records.AmendedSince(since), cancellationToken).ConfigureAwait(false);
                hits = [.. amended.SelectMany(record => record.Activity?.LimitHits ?? []).Where(hit => hit.At >= since)];
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SessionRecordsUnreadable(_logger, exception);
            }
        }

        return (WeeklyUsage.Of(week, hits, now), week);
    }

    private async Task<MachineLoad?> ReadLoadAsync(CancellationToken cancellationToken)
    {
        if (machineLoad is null) return null;

        try
        {
            return await machineLoad.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            MachineLoadUnreadable(_logger, exception);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The session requests could not be read; working sessions read unavailable.")]
    private static partial void SessionRequestsUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The sessions could not be read; working sessions read unavailable.")]
    private static partial void SessionsUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The request telemetry could not be read; plan usage reads unavailable.")]
    private static partial void RequestTelemetryUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The session records could not be read; no limit refusal sizes plan usage.")]
    private static partial void SessionRecordsUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The machine load could not be read; CPU and memory read unavailable.")]
    private static partial void MachineLoadUnreadable(ILogger logger, Exception exception);
}
