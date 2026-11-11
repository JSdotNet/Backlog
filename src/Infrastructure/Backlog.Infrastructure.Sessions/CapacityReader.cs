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
internal sealed class CapacityReader(
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
            _logger.LogWarning(exception, "The session requests could not be read; working sessions read unavailable.");
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
            _logger.LogWarning(exception, "The sessions could not be read; working sessions read unavailable.");
            return null;
        }
    }

    /// <summary>The week's usage over every stored request inside the horizon, as the
    /// Sessions pane reads it: records that fail only lose the refusals.</summary>
    private async Task<(WeeklyUsage? Weekly, IReadOnlyList<ClaudeApiRequest>? Requests)> ReadWeeklyAsync(CancellationToken cancellationToken)
    {
        if (apiRequests is null) return (null, null);

        var now = _clock.GetUtcNow();
        IReadOnlyList<ClaudeApiRequest> week;

        try
        {
            // Only the limit horizon and the week before its oldest refusal can matter.
            var from = now - LimitHitHorizon - UsageWeek.Length;
            week = [.. (await apiRequests.ListAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Where(request => request.Timestamp >= from)];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "The request telemetry could not be read; plan usage reads unavailable.");
            return (null, null);
        }

        IReadOnlyList<AgentLimitHit> hits = [];

        if (records is not null)
        {
            try
            {
                var since = now - LimitHitHorizon;
                var all = await Task.Run(records.All, cancellationToken).ConfigureAwait(false);
                hits = [.. all.SelectMany(record => record.Activity?.LimitHits ?? []).Where(hit => hit.At >= since)];
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "The session records could not be read; no limit refusal sizes plan usage.");
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
            _logger.LogWarning(exception, "The machine load could not be read; CPU and memory read unavailable.");
            return null;
        }
    }
}
