using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Desktop.WebHarness;

/// <summary>Where the two harnesses hand a pairing code over: the folder the
/// AppHost passes to both as <c>BACKLOG_PAIRING_HANDOFF_PATH</c>, one value with
/// two readers so they cannot drift.</summary>
internal sealed record DesktopAutoRegistrationOptions(string? HandoffFolder);

/// <summary>
/// The desktop harness registering itself as the first device of this run's
/// <c>sync</c> service, when the AppHost turns <c>Harness:AutoPair</c> on, and then
/// minting a pairing code for the phone harness whenever it asks — so a reviewer
/// opens the phone on synced data without clicking through Settings → Devices.
/// <para>
/// Only the code leaves this process, through the handoff folder, in the
/// OS-protected envelope a stored credential gets; this harness's credential never
/// does. It mints only once its own credential has been checked or registered, so
/// the phone is never handed a code under an owner the service has forgotten.
/// </para>
/// <para>
/// It also turns this harness's own <c>sync</c> switch on. The switch lives in this
/// worktree's <c>feature.settings.json</c>, and without it the task sync loop never
/// pushes, so the phone would be paired with nothing to show.
/// </para>
/// <para>
/// A hosted service rather than the resolve-after-<c>Build()</c> the workers use:
/// those are shared with the MAUI head, which has no generic host, and this is the
/// harness's alone. It starts after the host does and is never awaited by it;
/// <see cref="LocalDevelopmentPairing"/> does the waiting, and throws nothing.
/// </para>
/// </summary>
internal sealed class DesktopAutoRegistration(
    LocalDevelopmentPairing pairing,
    IHttpClientFactory clients,
    IDeviceCredentialStore credentials,
    IAppFeatureSettings features,
    DesktopAutoRegistrationOptions options,
    ILogger<DesktopAutoRegistration> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the start path: StartAsync returns at the first await.
        await Task.Yield();

        if (!features.IsEnabled(SyncFeatures.Sync) && features.SetEnabled(SyncFeatures.Sync, true) is { } refused)
        {
            log.LogWarning("Development auto-pair: the desktop harness could not turn sync on ({Reason}); turn it on in Settings.", refused);
        }

        using var sync = clients.CreateClient(LocalDevelopmentPairing.HttpClientName);
        var registered = await pairing.RegisterDesktopAsync(sync, credentials, stoppingToken);

        if (registered is not (LocalPairingOutcome.Registered or LocalPairingOutcome.AlreadyPaired)) return;

        if (!OperatingSystem.IsWindows())
        {
            log.LogInformation("Development auto-pair: the desktop harness cannot hand the phone harness a pairing code here — the handoff is a DPAPI envelope, on Windows only. Pair by hand from Settings.");
            return;
        }

        if (string.IsNullOrWhiteSpace(options.HandoffFolder))
        {
            log.LogWarning("Development auto-pair: the AppHost passed no BACKLOG_PAIRING_HANDOFF_PATH, so the desktop harness has nowhere to hand the phone harness a pairing code. Pair by hand from Settings.");
            return;
        }

        await pairing.ServePairingCodesAsync(sync, credentials, new DpapiPairingCodeHandoff(options.HandoffFolder), stoppingToken);
    }
}
