using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.Sync;

/// <summary>How a development harness's attempt to pair itself ended.</summary>
public enum LocalPairingOutcome
{
    /// <summary>The harness already held a credential the service still knows —
    /// or, on the phone, one the manual-pairing marker keeps unchecked — so
    /// nothing was registered or paired.</summary>
    AlreadyPaired,

    /// <summary>The phone was made "never paired" on purpose, so it was left
    /// for a person (or the end-to-end suite) to pair by hand.</summary>
    ManualPairingRequested,

    /// <summary>The desktop registered and stored its credential.</summary>
    Registered,

    /// <summary>The phone paired under the desktop's owner and stored its
    /// credential.</summary>
    Paired,

    /// <summary>The service refused in a way asking again would not change, or
    /// a call ended in a way that does not say whether it landed. Logged;
    /// pairing by hand is still possible.</summary>
    Stopped,

    /// <summary>The host stopped before the attempt finished.</summary>
    Cancelled
}

/// <summary>
/// The desktop and mobile web harnesses pairing themselves when the AppHost
/// asks them to, through the same four device endpoints a person's clicks reach.
/// <para>
/// The desktop registers as the first device. Only a pairing code crosses
/// between the two harnesses, never a credential
/// (<c>.devbook/domain/sync/features.md#pairing-a-device</c>): the phone asks the
/// desktop for a code through an <see cref="IPairingCodeHandoff"/>, the desktop
/// mints one with its own credential and hands it over, and the phone redeems
/// it, saving the answer into its own <see cref="IDeviceCredentialStore"/> — so
/// its pairing gate opens on <see cref="IDeviceCredentialStore.Changed"/> without
/// a restart. Before that save the phone forgets what its views cached, so the
/// first pull shows synced data alone.
/// </para>
/// <para>
/// <strong>What is sent again, and what is not</strong> (guideline ADR 0015).
/// Registering and redeeming each create a device, and neither carries an
/// idempotency key, so each is sent again only when it provably did not land:
/// the connection was never made (<see cref="HttpRequestError.ConnectionError"/>,
/// <see cref="HttpRequestError.NameResolutionError"/>), or the replica turned it
/// away as too busy (<see cref="SyncErrorCodes.ReplicaBusy"/>, a 429 Cosmos never
/// applies). A timeout, a dropped response, a 5xx with no code, or
/// <see cref="SyncErrorCodes.ReplicaUnavailable"/> — which the device registry
/// also gives a write that timed out after landing — ends the attempt instead:
/// the service may have applied it, and a second send would leave an orphaned
/// owner or a second phone. The readiness probe below waits out a replica that
/// is not there yet before the one register is sent. The token
/// exchange is a read and a pairing code nobody redeems lapses in ten minutes,
/// so those two are retried on any transient failure; a refused redeem's code is
/// replaced by asking the desktop for another.
/// </para>
/// <para>
/// So that the desktop's one register is not spent on a service that is still
/// starting — a proxy in front of a process not yet listening fails in the
/// ambiguous way — it first waits for the device routes to answer, by trading a
/// device id nobody holds for a token. <see cref="SyncErrorCodes.DeviceCredentialInvalid"/>
/// is the answer from a service whose device registry is reachable, and that is
/// what registering needs.
/// </para>
/// <para>
/// <strong>A credential the service forgot.</strong> The Cosmos emulator keeps
/// no volume, so its device registry can come back empty while both credential
/// files stay. A harness that holds a credential therefore first trades it for
/// a token, once per start. Only a definite
/// <see cref="SyncErrorCodes.DeviceCredentialInvalid"/> discards it — through the
/// harness's own store, so its screens hear it — and the harness pairs again as
/// on a fresh run. A service that cannot say yet keeps the credential. The
/// desktop mints a code only after its own credential has been checked or
/// registered, so the phone is never handed one under a forgotten owner. The
/// manual-pairing marker leaves the phone's credential unchecked.
/// </para>
/// <para>
/// A development-time readiness poll, not a retry policy: it waits for the
/// Cosmos emulator's cold start, which takes minutes, until the host stops or
/// <see cref="GiveUpAfter"/> has passed since the first attempt — then it logs
/// one warning naming the manual recipe (Settings → Devices) and stops. The
/// waits grow from <see cref="FirstRetryDelay"/> to
/// <see cref="MaxRetryDelay"/> with jitter. The <see cref="HttpClient"/> each
/// call goes out on is <see cref="HttpClientName"/>, which carries no resilience
/// pipeline of its own, so this loop is the only one sending anything again.
/// </para>
/// <para>
/// Never on a startup path: a harness runs this in the background and keeps
/// serving while it waits. Nothing here throws, and no log line carries a
/// credential, a token or a pairing code — only error codes and device ids.
/// </para>
/// </summary>
public sealed class LocalDevelopmentPairing
{
    /// <summary>The client every call here goes out on: the sync service's
    /// address, no authentication handler, and no resilience pipeline. See
    /// <c>SyncClientRegistration.AddLocalDevelopmentPairing</c>.</summary>
    public const string HttpClientName = "sync-local-pairing";

    /// <summary>The switch the AppHost sets on both harnesses
    /// (<c>Harness__AutoPair</c>). Unset is off, so only a run the AppHost
    /// started pairs itself.</summary>
    public const string ConfigurationKey = "Harness:AutoPair";

    /// <summary>The variable the AppHost sets on both harnesses to the folder they
    /// hand a pairing code through (<see cref="DpapiPairingCodeHandoff"/>).</summary>
    public const string HandoffPathVariable = "BACKLOG_PAIRING_HANDOFF_PATH";

    /// <summary>What the desktop harness calls itself in the Devices list, so
    /// the pairing a run created is recognisable.</summary>
    public const string DesktopDeviceName = "Desktop harness";

    /// <summary>What the phone harness calls itself in the Devices list.</summary>
    public const string PhoneDeviceName = "Phone harness";

    /// <summary>The file beside the phone's credential whose presence means
    /// "leave this phone unpaired": the end-to-end reset writes it, and a person
    /// can create or delete it.</summary>
    public const string ManualPairingMarkerFileName = "manual-pairing";

    internal static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>How far either side of its base a wait may land, as a fraction
    /// of it, so two harnesses started together do not knock in step.</summary>
    internal const double Jitter = 0.2;

    /// <summary>How many codes the phone has refused before it decides the
    /// refusals are not about the code.</summary>
    internal const int MaxRefusedCodes = 3;

    /// <summary>How often the desktop looks for the phone's request for a code:
    /// a file check, cheap enough to make for the life of the host.</summary>
    internal static readonly TimeSpan HandoffPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>The credential sent with the readiness probe. Not a secret: it
    /// goes with a device id nobody holds, and is refused.</summary>
    private const string ReadinessProbeCredential = "local-development-readiness-probe";

    /// <summary>How long the poll keeps trying, from its first attempt, before
    /// it gives up and leaves the pairing to a person. A Cosmos emulator cold
    /// start takes minutes; a quarter of an hour is a service that needs
    /// looking at (scope criterion 4, guideline ADR 0015: no unbounded
    /// retries).</summary>
    internal static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(15);

    private readonly ILogger _log;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _wait;
    private readonly Func<double> _random;
    private readonly TimeSpan _giveUpAfter;

    public LocalDevelopmentPairing(ILogger<LocalDevelopmentPairing> log, TimeProvider time)
        : this(log, time, WaitOn(time), Random.Shared.NextDouble, GiveUpAfter)
    {
    }

    /// <summary>With the wait between attempts, its jitter and the deadline
    /// supplied, so a test can see the backoff and the give-up without sleeping
    /// through them: its wait moves <paramref name="time"/> on instead.</summary>
    internal LocalDevelopmentPairing(
        ILogger<LocalDevelopmentPairing> log,
        TimeProvider time,
        Func<TimeSpan, CancellationToken, Task> wait,
        Func<double> random,
        TimeSpan giveUpAfter)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(wait);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(giveUpAfter, TimeSpan.Zero);

        _log = log;
        _time = time;
        _wait = wait;
        _random = random;
        _giveUpAfter = giveUpAfter;
    }

    /// <summary>
    /// Registers the desktop harness as the first device unless
    /// <paramref name="credentials"/> already holds a credential the service
    /// still knows — one it refuses as unknown is discarded first — waiting for a
    /// service that is not ready until it is, <paramref name="cancellationToken"/>
    /// fires, or <see cref="GiveUpAfter"/> passes.
    /// </summary>
    /// <param name="sync">The <see cref="HttpClientName"/> client.</param>
    /// <param name="credentials">The desktop's own store.</param>
    public async Task<LocalPairingOutcome> RegisterDesktopAsync(
        HttpClient sync,
        IDeviceCredentialStore credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(credentials);

        // One deadline for the whole start: the check of a held credential and
        // the registering that may follow it share it.
        var started = _time.GetTimestamp();

        if (credentials.Current is { } held
            && await KeepOrDiscardAsync(sync, credentials, held, "desktop harness", started, cancellationToken).ConfigureAwait(false) is { } kept)
        {
            return kept;
        }

        return await RunAsync(
            "registering the desktop harness",
            token => RegisterOnceAsync(sync, credentials, token),
            started,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pairs the phone harness under the desktop harness's owner unless
    /// <paramref name="credentials"/> already holds a credential the service
    /// still knows — one it refuses as unknown is discarded first — or
    /// <paramref name="manualPairingRequested"/> says to leave the phone as it is.
    /// </summary>
    /// <param name="sync">The <see cref="HttpClientName"/> client: the phone's
    /// own credential check and the anonymous redeem.</param>
    /// <param name="credentials">The phone's own store.</param>
    /// <param name="handoff">Where the phone asks the desktop for a code and
    /// takes the one it hands over. Only a code crosses, never a credential.</param>
    /// <param name="manualPairingRequested">Whether the manual-pairing marker
    /// (<see cref="ManualPairingMarkerFileName"/>) is present; asked once. It
    /// keeps a held credential unchecked, and an empty store empty.</param>
    /// <param name="forgetViews">Forgets what the phone's views cached from
    /// before — a stand-up seed, an earlier owner's goals and tasks — called once
    /// a fresh pairing has been redeemed and before its credential is kept, so the
    /// first pull starts them afresh. Never the outbox of unsent work.</param>
    public async Task<LocalPairingOutcome> PairPhoneAsync(
        HttpClient sync,
        IDeviceCredentialStore credentials,
        IPairingCodeHandoff handoff,
        Func<bool> manualPairingRequested,
        Func<CancellationToken, Task> forgetViews,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(manualPairingRequested);
        ArgumentNullException.ThrowIfNull(forgetViews);

        // One deadline for the whole start, as on the desktop.
        var started = _time.GetTimestamp();

        if (credentials.Current is { } held)
        {
            // The marker means a person, or the end-to-end suite, owns this
            // phone's pairing: what it holds is left as it is, unchecked.
            if (manualPairingRequested()) return LocalPairingOutcome.AlreadyPaired;

            if (await KeepOrDiscardAsync(sync, credentials, held, "phone harness", started, cancellationToken).ConfigureAwait(false) is { } kept)
            {
                return kept;
            }
        }
        else if (manualPairingRequested())
        {
            _log.LogInformation("Development auto-pair: the phone harness is marked for pairing by hand; leaving it unpaired.");
            return LocalPairingOutcome.ManualPairingRequested;
        }

        var attempt = new PhoneAttempt();
        return await RunAsync(
            "pairing the phone harness",
            token => PairOnceAsync(sync, credentials, handoff, forgetViews, attempt, token),
            started,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether a held credential still names a device the service knows, asked
    /// once per start with one token exchange — a read — and waited out while the
    /// service cannot say. Kept on anything but a definite
    /// <see cref="SyncErrorCodes.DeviceCredentialInvalid"/>; on that, discarded
    /// through the harness's own store so the harness pairs again as on a fresh
    /// run. The Cosmos emulator keeps no volume, so its device registry can come
    /// back empty while both credential files stay behind (scope criterion 9).
    /// </summary>
    /// <returns>The outcome to end on, or <c>null</c> when the credential was
    /// discarded and the harness should pair afresh.</returns>
    private async Task<LocalPairingOutcome?> KeepOrDiscardAsync(
        HttpClient sync,
        IDeviceCredentialStore credentials,
        DeviceCredential held,
        string who,
        long started,
        CancellationToken cancellationToken)
    {
        var checking = $"checking the {who}'s stored credential";
        var refused = false;

        var outcome = await RunAsync(
            checking,
            async token =>
            {
                var answer = await ExchangeAsync(sync, held.DeviceId, held.Credential, token).ConfigureAwait(false);
                if (answer.IsSuccess) return Pass.Done(LocalPairingOutcome.AlreadyPaired);

                if (answer.Error.Code == SyncErrorCodes.DeviceCredentialInvalid)
                {
                    refused = true;
                    return Pass.Done(LocalPairingOutcome.AlreadyPaired);
                }

                return IsTransient(answer.Error) ? Pass.Wait(checking, answer.Error) : Stop(checking, answer.Error);
            },
            started,
            cancellationToken).ConfigureAwait(false);

        if (!refused) return outcome;

        // Replaced by hand while it was being checked: that pairing stands.
        if (credentials.Current != held) return LocalPairingOutcome.AlreadyPaired;

        _log.LogWarning(
            "Development auto-pair: the sync service no longer knows the {Who} (device {DeviceId}) — its device registry was most likely reset. Discarding the stored credential and pairing again.",
            who,
            held.DeviceId);
        credentials.Clear();
        return null;
    }

    /// <summary>
    /// The readiness poll both sides share: one pass, and if it asks to wait,
    /// a jittered wait that grows to <see cref="MaxRetryDelay"/>, until a pass
    /// finishes, the host stops, or <see cref="GiveUpAfter"/> has passed since
    /// <paramref name="started"/> — the start's first attempt, so a credential
    /// check and the pairing after it share one deadline. A background task
    /// nobody awaits, so nothing leaves it as an exception.
    /// </summary>
    private async Task<LocalPairingOutcome> RunAsync(
        string what,
        Func<CancellationToken, Task<Pass>> pass,
        long started,
        CancellationToken cancellationToken)
    {
        try
        {
            var delay = FirstRetryDelay;

            while (true)
            {
                var result = await pass(cancellationToken).ConfigureAwait(false);
                if (result.Outcome is { } outcome) return outcome;

                if (_time.GetElapsedTime(started) >= _giveUpAfter)
                {
                    _log.LogWarning(
                        "Development auto-pair: {What} gave up after {Minutes:0} minutes; the last answer was {ErrorCode}. Pair by hand in Settings → Devices: Register this device on the desktop harness, then Pair with a code on the phone harness.",
                        what,
                        _giveUpAfter.TotalMinutes,
                        result.Error?.Code ?? "no pairing code yet");
                    return LocalPairingOutcome.Stopped;
                }

                var wait = Jittered(delay);
                if (result.Error is { } error)
                {
                    _log.LogInformation(
                        "Development auto-pair: {Step} answered {ErrorCode}; trying again in {DelaySeconds:0.0} s.",
                        result.Step,
                        error.Code,
                        wait.TotalSeconds);
                }
                else
                {
                    _log.LogDebug(
                        "Development auto-pair: {Step}; checking again in {DelaySeconds:0.0} s.",
                        result.Step,
                        wait.TotalSeconds);
                }

                await _wait(wait, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                delay = Next(delay);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return LocalPairingOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Development auto-pair: {What} failed; pair by hand from Settings.", what);
            return LocalPairingOutcome.Stopped;
        }
    }

    private async Task<Pass> RegisterOnceAsync(
        HttpClient sync,
        IDeviceCredentialStore credentials,
        CancellationToken cancellationToken)
    {
        // Paired by hand while this was waiting: that registration stands.
        if (credentials.Current is not null) return Pass.Done(LocalPairingOutcome.AlreadyPaired);

        const string probing = "waiting for the sync service's device routes";
        var probe = await ExchangeAsync(sync, Guid.NewGuid(), ReadinessProbeCredential, cancellationToken).ConfigureAwait(false);
        if (probe.IsFailure && probe.Error.Code != SyncErrorCodes.DeviceCredentialInvalid)
        {
            return IsTransient(probe.Error) ? Pass.Wait(probing, probe.Error) : Stop(probing, probe.Error);
        }

        const string registering = "registering the desktop harness";
        var registered = await SendOnceAsync<DeviceRegistrationResponse>(
            () => sync.PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.RegisterDevice),
                new RegisterDeviceRequest(DesktopDeviceName),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (registered.Result.IsFailure)
        {
            return registered.MaySendAgain ? Pass.Wait(registering, registered.Result.Error) : Stop(registering, registered.Result.Error);
        }

        var credential = new DeviceCredential(
            registered.Result.Value.OwnerId,
            registered.Result.Value.DeviceId,
            DesktopDeviceName,
            registered.Result.Value.Credential);

        if (!await TrySaveAsync(credentials, credential).ConfigureAwait(false)) return Pass.Done(LocalPairingOutcome.Stopped);

        _log.LogInformation("Development auto-pair: the desktop harness registered as device {DeviceId}.", credential.DeviceId);
        return Pass.Done(LocalPairingOutcome.Registered);
    }

    /// <summary>
    /// One pass on the phone: ask the desktop for a code once, take the code it
    /// handed over, and redeem it as the phone. A code refused for being spent,
    /// lapsed or unknown is replaced by asking again, up to
    /// <see cref="MaxRefusedCodes"/>; a code whose redeem provably did not land
    /// is sent again as it is, since it was never spent.
    /// </summary>
    private async Task<Pass> PairOnceAsync(
        HttpClient sync,
        IDeviceCredentialStore credentials,
        IPairingCodeHandoff handoff,
        Func<CancellationToken, Task> forgetViews,
        PhoneAttempt attempt,
        CancellationToken cancellationToken)
    {
        // Paired by hand while this was waiting: that pairing stands.
        if (credentials.Current is not null) return Pass.Done(LocalPairingOutcome.AlreadyPaired);

        if (!attempt.Requested)
        {
            handoff.RequestCode();
            attempt.Requested = true;
        }

        if (attempt.Code is null)
        {
            // Asked before taking: a code handed over between the two is taken,
            // not mistaken for one that was dropped.
            var handedOver = handoff.CodeHandedOver;
            attempt.Code = handoff.TakeCode();

            if (attempt.Code is null && handedOver)
            {
                // Handed over but unreadable, and dropped; the desktop withdrew
                // the request it answered, so nothing stands until this asks again.
                handoff.RequestCode();
            }
        }

        if (attempt.Code is not { } code) return Pass.Wait("waiting for the desktop harness to hand over a pairing code", error: null);

        // Paired by hand while the code was on its way: redeeming now would save
        // over that pairing and orphan the device it made.
        if (credentials.Current is not null) return Pass.Done(LocalPairingOutcome.AlreadyPaired);

        const string redeeming = "redeeming the pairing code the desktop harness handed over";
        var redeemed = await SendOnceAsync<DeviceRegistrationResponse>(
            () => sync.PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
                new RedeemPairingCodeRequest(code, PhoneDeviceName),
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (redeemed.Result.IsSuccess)
        {
            attempt.Code = null;

            var credential = new DeviceCredential(
                redeemed.Result.Value.OwnerId,
                redeemed.Result.Value.DeviceId,
                PhoneDeviceName,
                redeemed.Result.Value.Credential);

            await ForgetViewsAsync(forgetViews).ConfigureAwait(false);

            if (!await TrySaveAsync(credentials, credential).ConfigureAwait(false)) return Pass.Done(LocalPairingOutcome.Stopped);

            _log.LogInformation(
                "Development auto-pair: the phone harness paired as device {DeviceId} under the desktop harness's owner.",
                credential.DeviceId);
            return Pass.Done(LocalPairingOutcome.Paired);
        }

        if (redeemed.Result.Error.Code is SyncErrorCodes.PairingCodeExpired
            or SyncErrorCodes.PairingCodeUsed
            or SyncErrorCodes.PairingCodeNotFound)
        {
            // The refusal was about that code, not about the service: the desktop
            // is asked for a fresh one.
            attempt.Code = null;
            if (++attempt.Refused >= MaxRefusedCodes) return Stop(redeeming, redeemed.Result.Error);

            handoff.RequestCode();
            return Pass.Wait(redeeming, redeemed.Result.Error);
        }

        if (redeemed.MaySendAgain) return Pass.Wait(redeeming, redeemed.Result.Error);

        attempt.Code = null;
        return Stop(redeeming, redeemed.Result.Error);
    }

    /// <summary>
    /// Forgets the phone's cached views before a fresh pairing's credential is
    /// kept. Not cancellable, for the save's reason: the service has issued the
    /// credential and will not issue it again. A view that cannot be cleared is
    /// logged and the pairing kept: a stale view is a lesser loss than a device
    /// the service holds and the phone does not.
    /// </summary>
    private async Task ForgetViewsAsync(Func<CancellationToken, Task> forgetViews)
    {
        try
        {
            await forgetViews(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Development auto-pair: the phone harness's cached views could not be cleared before pairing; it may show what it held before.");
        }
    }

    /// <summary>
    /// The desktop's half of the handoff, for as long as the host runs: whenever
    /// the phone asks for a code and this desktop holds a credential, trade that
    /// credential for a token and mint a code on the bearer route with it, each
    /// sent once per attempt on the <see cref="HttpClientName"/> client, and hand
    /// the code over. Only the code leaves this process. Call it once the
    /// desktop's own credential has been checked or registered, so the phone is
    /// never handed a code under an owner the service has forgotten.
    /// <para>
    /// Only the token route refusing this desktop's credential
    /// (<see cref="SyncErrorCodes.DeviceCredentialInvalid"/>), or another refusal
    /// that asking again would not change, ends the loop. A transient answer on
    /// either route, a bare 401 on the codes route — a token the service stopped
    /// taking, say after a restart, which says nothing about the credential — or
    /// a hand-over the file system refused keeps the request and asks again; the
    /// next attempt trades for a fresh token. Each request is asked for until
    /// <see cref="GiveUpAfter"/> has passed since its first attempt (guideline
    /// ADR 0015: no unbounded retries); then one warning, the request withdrawn,
    /// and the loop serves the next request a phone makes.
    /// </para>
    /// </summary>
    /// <param name="sync">The <see cref="HttpClientName"/> client.</param>
    /// <returns><see cref="LocalPairingOutcome.Cancelled"/> when the host stops;
    /// <see cref="LocalPairingOutcome.Stopped"/> when the service refuses this
    /// desktop a code in a way asking again would not change.</returns>
    public async Task<LocalPairingOutcome> ServePairingCodesAsync(
        HttpClient sync,
        IDeviceCredentialStore credentials,
        IPairingCodeHandoff handoff,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(handoff);

        const string minting = "minting a pairing code for the phone harness";

        try
        {
            var backoff = FirstRetryDelay;
            long? requestSeen = null;

            while (true)
            {
                var wait = HandoffPollInterval;

                if (!handoff.CodeRequested)
                {
                    requestSeen = null;
                    backoff = FirstRetryDelay;
                }
                else if (credentials.Current is { } held)
                {
                    var seen = requestSeen ??= _time.GetTimestamp();

                    var minted = await MintOnceAsync(sync, held, cancellationToken).ConfigureAwait(false);
                    Error? failed = null;

                    if (minted.IsFailure)
                    {
                        if (!IsTransient(minted.Error) && minted.Error.Code != UnauthorizedCode)
                        {
                            Stop(minting, minted.Error);
                            return LocalPairingOutcome.Stopped;
                        }

                        failed = minted.Error;
                        wait = backoff;
                        backoff = Next(backoff);
                    }
                    else if (!TryHandOver(handoff, minted.Value))
                    {
                        // The request stands; the next poll mints and tries again.
                        failed = HandOverRefused;
                    }

                    if (failed is null)
                    {
                        requestSeen = null;
                        backoff = FirstRetryDelay;
                        _log.LogInformation("Development auto-pair: the desktop harness handed a pairing code to the phone harness.");
                    }
                    else if (_time.GetElapsedTime(seen) < _giveUpAfter)
                    {
                        _log.LogInformation(
                            "Development auto-pair: {Step} answered {ErrorCode}; trying again.",
                            minting,
                            failed.Value.Code);
                    }
                    else
                    {
                        _log.LogWarning(
                            "Development auto-pair: {What} gave up on the phone harness's request after {Minutes:0} minutes; the last answer was {ErrorCode}. A later request is served afresh; or pair by hand in Settings → Devices.",
                            minting,
                            _giveUpAfter.TotalMinutes,
                            failed.Value.Code);
                        handoff.WithdrawRequest();
                        requestSeen = null;
                        backoff = FirstRetryDelay;
                        wait = HandoffPollInterval;
                    }
                }

                await _wait(Jittered(wait), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return LocalPairingOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Development auto-pair: {What} failed; pair by hand from Settings.", minting);
            return LocalPairingOutcome.Stopped;
        }
    }

    /// <summary>What <see cref="SyncHttp"/> files a 401 with no problem body as:
    /// the bearer scheme's challenge, for a token refused or missing.</summary>
    private const string UnauthorizedCode = "sync.http_401";

    /// <summary>What a hand-over the file system refused is logged as. It
    /// carries no code.</summary>
    private static readonly Error HandOverRefused = Error.Unexpected(
        "local_pairing.handoff_unavailable",
        "The pairing-code handoff file could not be written.");

    /// <summary>
    /// One attempt at a code for the phone: this desktop's own credential traded
    /// for a token on the anonymous token route, then that token sent once on the
    /// bearer codes route. Both are safe to send again — the exchange is a read,
    /// and a code nobody redeems lapses in ten minutes — and the caller decides
    /// whether to. No <see cref="SyncAuthenticationHandler"/> is involved: the
    /// token is fetched here and goes out on this one request alone.
    /// </summary>
    private static async Task<Result<string>> MintOnceAsync(
        HttpClient sync,
        DeviceCredential held,
        CancellationToken cancellationToken)
    {
        var token = await ExchangeAsync(sync, held.DeviceId, held.Credential, cancellationToken).ConfigureAwait(false);
        if (token.IsFailure) return Result.Failure<string>(token.Error);

        var issued = await SyncHttp.SendAsync<PairingCodeResponse>(
            async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, SyncRoutes.Absolute(SyncRoutes.PairingCodes));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value.AccessToken);
                return await sync.SendAsync(request, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return issued.IsSuccess ? Result.Success(issued.Value.Code) : Result.Failure<string>(issued.Error);
    }

    /// <summary>Hands a code over, or says the file system refused: a scanner
    /// holding the staged file, or the phone still reading the last code.</summary>
    private static bool TryHandOver(IPairingCodeHandoff handoff, string code)
    {
        try
        {
            handoff.HandOver(code);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>What one phone attempt carries from pass to pass.</summary>
    private sealed class PhoneAttempt
    {
        /// <summary>Whether the desktop has been asked for a code yet.</summary>
        public bool Requested { get; set; }

        /// <summary>A code taken and not yet spent: kept for a redeem that
        /// provably did not land.</summary>
        public string? Code { get; set; }

        /// <summary>How many codes the service refused.</summary>
        public int Refused { get; set; }
    }

    /// <summary>
    /// Sends a call that creates something, once, and says whether it is safe
    /// to send again: only when the connection was never made or the replica
    /// turned it away as too busy. The answer itself is read the way
    /// every client in this project reads one.
    /// </summary>
    private static async Task<(Result<T> Result, bool MaySendAgain)> SendOnceAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        var neverSent = false;

        var result = await SyncHttp.SendAsync<T>(
            async () =>
            {
                try
                {
                    return await send().ConfigureAwait(false);
                }
                catch (HttpRequestException ex) when (ex.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError)
                {
                    neverSent = true;
                    throw;
                }
            },
            cancellationToken).ConfigureAwait(false);

        // Not ReplicaUnavailable: the device registry files every Cosmos fault
        // but a 429 under it, the 408 of a write that landed late included.
        var maySendAgain = result.IsFailure
            && (neverSent || result.Error.Code is SyncErrorCodes.ReplicaBusy);

        return (result, maySendAgain);
    }

    /// <summary>A credential for a token, on the anonymous token route — the
    /// exchange <see cref="SyncTokenProvider"/> makes, made here to check this
    /// harness's own held credential or for the readiness probe.</summary>
    private static Task<Result<DeviceTokenResponse>> ExchangeAsync(
        HttpClient sync,
        Guid deviceId,
        string credential,
        CancellationToken cancellationToken) =>
        SyncHttp.SendAsync<DeviceTokenResponse>(
            () => sync.PostAsJsonAsync(
                SyncRoutes.Absolute(SyncRoutes.DeviceToken),
                new DeviceTokenRequest(deviceId, credential),
                cancellationToken),
            cancellationToken);

    /// <summary>Saves a credential the service has already issued. Not
    /// cancellable, for <see cref="DevicePairingClient"/>'s reason: the service
    /// will not issue it again. A store that cannot keep it ends the attempt
    /// rather than registering once more.</summary>
    private async Task<bool> TrySaveAsync(IDeviceCredentialStore credentials, DeviceCredential credential)
    {
        try
        {
            await credentials.SaveAsync(credential, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            _log.LogWarning(
                "Development auto-pair: device {DeviceId} was created but its credential could not be saved at {StorePath}; pair by hand from Settings.",
                credential.DeviceId,
                credentials.StorePath);
            return false;
        }
    }

    private Pass Stop(string step, Error error)
    {
        _log.LogWarning(
            "Development auto-pair: {Step} answered {ErrorCode} ({ErrorMessage}); not sending it again. Pair by hand from Settings.",
            step,
            error.Code,
            error.Message);
        return Pass.Done(LocalPairingOutcome.Stopped);
    }

    private static Func<TimeSpan, CancellationToken, Task> WaitOn(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return (delay, cancellationToken) => Task.Delay(delay, time, cancellationToken);
    }

    private TimeSpan Jittered(TimeSpan delay) =>
        delay * (1 - Jitter + (2 * Jitter * _random()));

    private static TimeSpan Next(TimeSpan delay)
    {
        var doubled = delay + delay;
        return doubled > MaxRetryDelay ? MaxRetryDelay : doubled;
    }

    /// <summary>Whether coming back later could get a different answer, for a
    /// call that is safe to send again. A code the service did not put on the
    /// answer (<c>sync.http_5xx</c>) is a proxy or a host that is still
    /// starting.</summary>
    private static bool IsTransient(Error error) =>
        error.Code is DevicePairingClient.UnreachableCode
            or SyncErrorCodes.ReplicaUnavailable
            or SyncErrorCodes.ReplicaBusy
        || error.Code.StartsWith("sync.http_5", StringComparison.Ordinal);

    /// <summary>How one pass ended: finished with an outcome, or asking to wait
    /// — with the error that said so, or none when it is the desktop's
    /// credential that is not there yet.</summary>
    private sealed record Pass(LocalPairingOutcome? Outcome, string Step, Error? Error)
    {
        public static Pass Done(LocalPairingOutcome outcome) => new(outcome, string.Empty, null);

        public static Pass Wait(string step, Error? error) => new(null, step, error);
    }
}
