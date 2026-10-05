using System.Diagnostics;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Ports;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.Cosmos;

/// <summary>
/// Whether the account has answered yet, asked once for every adapter and
/// waited on for no longer than <see cref="CosmosOptions.ReadinessTimeoutSeconds"/>.
/// <para>
/// The SDK's first operation reads the account (<c>GET /</c>) before it does
/// anything else, with HTTP timeouts of 5, 10 and 20 seconds, and that read
/// neither honours <see cref="CosmosClientOptions.RequestTimeout"/> nor the
/// caller's cancellation token. Against an account that accepts the connection
/// and never answers — a local run with Docker Desktop down, where the AppHost's
/// proxy still owns the emulator's port — every request therefore waited a
/// minute or more for a 503 its client had long stopped waiting for.
/// </para>
/// <para>
/// So the account read is made here, as one shared probe, and a request waits
/// on it for the budget and no longer. Past the budget the request is the coded
/// 503; the probe carries on, because it is the same initialization the SDK
/// would otherwise have done inside the next request, and the request after it
/// finds it done. A probe that failed is started again by the next caller —
/// a failure is never cached, for the reason <see cref="CosmosContainerHandle"/>
/// gives for its own lazy client.
/// </para>
/// <para>
/// Once the account has answered the gate stays open for the life of the
/// process. An account lost after that is the SDK's to report, through the
/// request timeout the client is configured with; this answers "has the store
/// come up yet", which is the question of the first minutes of a run.
/// </para>
/// </summary>
internal sealed partial class CosmosAccountGate(
    IServiceProvider services,
    IOptions<CosmosOptions> options,
    ILogger<CosmosAccountGate> log)
{
    private readonly Lock _sync = new();
    private Task<bool>? _probe;
    private volatile bool _reached;

    /// <summary>Returns once the account has answered, or throws the coded
    /// unavailable failure — in <paramref name="unavailableMessage"/>, the
    /// adapter's own words — once the budget has passed without it.</summary>
    public async ValueTask WaitUntilReachable(string unavailableMessage, CancellationToken cancellationToken)
    {
        if (_reached)
        {
            return;
        }

        bool reached;

        try
        {
            reached = await Probe()
                .WaitAsync(TimeSpan.FromSeconds(options.Value.ReadinessTimeoutSeconds), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException budget)
        {
            throw new SyncReplicaException(SyncErrorCodes.ReplicaUnavailable, unavailableMessage, budget);
        }

        if (!reached)
        {
            throw new SyncReplicaException(SyncErrorCodes.ReplicaUnavailable, unavailableMessage);
        }
    }

    private Task<bool> Probe()
    {
        lock (_sync)
        {
            // A finished probe that did not reach the account is spent; one
            // still running is shared.
            if (_probe is null || _probe is { IsCompleted: true, Result: false })
            {
                // The probe outlives the request that started it and serves
                // every request after it, so it must not report itself as a
                // child of that one — and the client it builds starts a
                // background refresh that would inherit the same parent. See
                // CosmosContainerHandle.
                var ambient = Activity.Current;
                Activity.Current = null;

                try
                {
                    _probe = ReadAccount();
                }
                finally
                {
                    Activity.Current = ambient;
                }
            }

            return _probe;
        }
    }

    /// <summary>Answers rather than throws: a probe that failed after every
    /// waiting request gave up on it has nobody left to observe its exception,
    /// so the failure is logged here and the result says the rest.</summary>
    private async Task<bool> ReadAccount()
    {
        try
        {
            await services.GetRequiredService<CosmosClient>().ReadAccountAsync().ConfigureAwait(false);
            _reached = true;
            return true;
        }
        catch (Exception failure)
        {
            // Logged here because the request that started the probe has
            // usually been answered by now, and its 503 only knows the budget
            // ran out — not why.
            AccountUnreachable(log, failure);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Cosmos account did not answer; the next request will ask again.")]
    private static partial void AccountUnreachable(ILogger logger, Exception failure);
}
