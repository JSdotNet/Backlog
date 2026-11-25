using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The <see cref="INotificationAnswerHandler"/> for the <c>sync</c> source: the answer to
/// "Pair &lt;device name&gt;?" (<see cref="PairingQuestion"/>). Refuse revokes the device
/// that redeemed the code; Allow keeps it. Either way the pairing then ends
/// (<see cref="PairingSubjects.SettledAsync"/>).
/// <para>
/// Idempotent, as the contract asks: the pairing and the device are read from the answer's
/// source key, not from memory, so an answer replayed after a restart revokes the same
/// device, and a device already gone counts as revoked.
/// </para>
/// <para>
/// Refusing asks for Windows Hello where the host composed it, as revoking does
/// (<see cref="IDeviceRoster.RefuseDeviceAsync"/>, local ADR 0058 §6), and that prompt expires
/// unanswered, so an unattended desktop does not hold every other answer back for long. A
/// refusal the person cancelled, could not confirm, or left to expire is consumed, not
/// replayed — a replay would ask again on every start: the device stays paired, the pairing
/// ends, and an alert (<see cref="PairingSubjects.NotRefusedAsync"/>) asks the person to refuse
/// it again from Settings → Devices. A revocation that fails for any other reason throws, so
/// the answer stays undelivered and is replayed on the next start.
/// </para>
/// </summary>
public sealed partial class PairingAnswers : INotificationAnswerHandler
{
    private readonly PairingSubjects _pairings;
    private readonly Func<IDeviceRoster?> _roster;
    private readonly ILogger _logger;

    /// <summary>What a host composes: the roster is the sync client, resolved per answer
    /// because it is a transient typed client.</summary>
    public PairingAnswers(PairingSubjects pairings, IServiceProvider services, ILogger<PairingAnswers>? logger = null)
        : this(pairings, () => services.GetService<DevicePairingClient>(), logger)
    {
        ArgumentNullException.ThrowIfNull(services);
    }

    internal PairingAnswers(PairingSubjects pairings, Func<IDeviceRoster?> roster, ILogger? logger = null)
    {
        _pairings = pairings ?? throw new ArgumentNullException(nameof(pairings));
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        _logger = logger ?? NullLogger.Instance;
    }

    public string Source => SyncNotificationSubjects.Context;

    public async Task HandleAnswerAsync(NotificationAnswer answer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (SyncNotificationSubjects.ParseSourceKey(answer.Source.Key) is not { } named)
        {
            LogAnswerNamesNoPairing(_logger, answer.Source.Key);
            return;
        }

        var (pairingId, deviceId) = named;

        if (answer.ChoiceIds.Contains(SyncNotificationSubjects.RefuseChoice, StringComparer.Ordinal))
        {
            var roster = _roster() ?? throw new InvalidOperationException("No sync client is composed to revoke the refused device with.");
            var revoked = await roster.RefuseDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);

            if (revoked is Error unconfirmed && IsUnconfirmed(unconfirmed.Code))
            {
                LogRefusalNotConfirmed(_logger, pairingId, unconfirmed.Code);

                await _pairings.NotRefusedAsync(pairingId, deviceId, unconfirmed.Code, unconfirmed.Message).ConfigureAwait(false);
            }
            else if (revoked is Error error && error.Code != SyncErrorCodes.DeviceNotFound)
            {
                throw new InvalidOperationException($"The refused device {deviceId:D} was not revoked: {error.Message}");
            }
            else
            {
                LogDeviceRefused(_logger, pairingId);
            }
        }

        await _pairings.SettledAsync(pairingId).ConfigureAwait(false);
    }

    /// <summary>Whether a refusal failed because the person did not confirm it — the step-up's
    /// codes — rather than because the service did not take it.</summary>
    private static bool IsUnconfirmed(string code) =>
        code is DeviceStepUp.DeclinedCode or DeviceStepUp.UnavailableCode or DeviceStepUp.ExpiredCode;

    [LoggerMessage(Level = LogLevel.Debug, Message = "An answer with source key {Key} names no pairing; nothing to do.")]
    private static partial void LogAnswerNamesNoPairing(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Information, Message = "The refusal of the device that redeemed pairing {Pairing} was not confirmed ({Code}); it stays paired.")]
    private static partial void LogRefusalNotConfirmed(ILogger logger, Guid pairing, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Refused the device that redeemed pairing {Pairing}; it is revoked.")]
    private static partial void LogDeviceRefused(ILogger logger, Guid pairing);
}
