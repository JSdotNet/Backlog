using System.Runtime.Versioning;

using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Progress;
using Backlog.Mobile.UI.Tasks;

namespace Backlog.Mobile.WebHarness;

/// <summary>Where the two harnesses hand a pairing code over: the folder the
/// AppHost passes to both as <c>BACKLOG_PAIRING_HANDOFF_PATH</c>, one value with
/// two readers so they cannot drift.</summary>
internal sealed record PhoneAutoPairingOptions(string? HandoffFolder);

/// <summary>
/// The phone harness pairing itself under the desktop harness's owner when the
/// AppHost turns <c>Harness:AutoPair</c> on, so a reviewer opens the phone on
/// synced data rather than on its pairing screen.
/// <para>
/// Only a pairing code crosses between the two harnesses: the phone asks the
/// desktop for one through the handoff folder, the desktop mints it with its own
/// credential and hands it over in the OS-protected envelope, and the phone
/// redeems it. The phone never opens the desktop's credential. What the pairing
/// saves goes through this harness's own store, so its pairing gate opens on
/// <see cref="IDeviceCredentialStore.Changed"/> without a restart — and just before
/// that, a fresh pairing forgets what the Progress and task views cached from
/// before (a stand-up seed, an earlier owner), so the first pull shows synced data
/// alone. The outbox of unsent captures and edits is never touched.
/// </para>
/// <para>
/// Left alone by the manual-pairing marker,
/// <see cref="LocalDevelopmentPairing.ManualPairingMarkerFileName"/>, beside the
/// phone's credential: the end-to-end reset writes it so a test can pair by hand,
/// and a person can create or delete it. Off Windows the handoff's envelope does
/// not exist, so it says so and stops.
/// </para>
/// </summary>
internal sealed class PhoneAutoPairing(
    LocalDevelopmentPairing pairing,
    IHttpClientFactory clients,
    IDeviceCredentialStore credentials,
    PhoneAutoPairingOptions options,
    IServiceProvider services,
    ILogger<PhoneAutoPairing> log) : BackgroundService
{
    /// <summary>How the attempt ended; <c>null</c> until it has, or when it never
    /// started.</summary>
    public LocalPairingOutcome? Outcome { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Off the start path: StartAsync returns at the first await.
        await Task.Yield();

        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(credentials.StorePath))
        {
            log.LogInformation("Development auto-pair: unavailable here — the pairing-code handoff is a DPAPI envelope, on Windows only. Pair by hand from Settings.");
            return;
        }

        if (string.IsNullOrWhiteSpace(options.HandoffFolder))
        {
            log.LogWarning("Development auto-pair: the AppHost passed no BACKLOG_PAIRING_HANDOFF_PATH, so the phone harness cannot ask the desktop harness for a pairing code. Pair by hand from Settings.");
            return;
        }

        Outcome = await PairAsync(options.HandoffFolder, stoppingToken);
    }

    [SupportedOSPlatform("windows")]
    private async Task<LocalPairingOutcome> PairAsync(string handoffFolder, CancellationToken stoppingToken)
    {
        var marker = Path.Combine(Path.GetDirectoryName(credentials.StorePath)!, LocalDevelopmentPairing.ManualPairingMarkerFileName);

        using var sync = clients.CreateClient(LocalDevelopmentPairing.HttpClientName);
        return await pairing.PairPhoneAsync(
            sync,
            credentials,
            new DpapiPairingCodeHandoff(handoffFolder),
            () => File.Exists(marker),
            ForgetViewsAsync,
            stoppingToken);
    }

    /// <summary>The Progress and task views, as a phone paired afresh starts them:
    /// empty, with their cursors at the beginning. The same singletons the screens
    /// read, so an open page redraws on their <c>Changed</c>.</summary>
    private async Task ForgetViewsAsync(CancellationToken cancellationToken)
    {
        await services.GetRequiredService<ProgressViewProjection>().ForgetAsync(cancellationToken);
        await services.GetRequiredService<TaskViewProjection>().ForgetAsync(cancellationToken);
    }
}
