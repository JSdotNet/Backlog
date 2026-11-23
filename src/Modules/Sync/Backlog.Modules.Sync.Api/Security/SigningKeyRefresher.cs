using Backlog.Modules.Sync.Api.Options;
using Microsoft.Extensions.Options;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// Loads the signing keys before the host serves its first request, and
/// re-reads them every <see cref="SyncTokenOptions.KeyRefreshMinutes"/> so a
/// rotation reaches a running instance without a restart.
/// <para>
/// The first load is part of the start, so a vault the identity cannot read
/// stops the service rather than leaving it up and unable to sign. A later
/// load that fails is logged and the keys already held stay in use.
/// </para>
/// </summary>
internal sealed class SigningKeyRefresher(
    SigningKeyRing ring,
    IOptions<SyncTokenOptions> options,
    TimeProvider clock,
    ILogger<SigningKeyRefresher> logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await ring.RefreshAsync(cancellationToken).ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.KeyRefreshMinutes), clock);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await ring.RefreshAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(
                        "Refreshing the device-token signing key failed ({Reason}); the keys already loaded stay in use.",
                        exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
