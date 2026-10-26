using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;
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
/// device, and a device already gone counts as revoked. A revocation that fails for any
/// other reason throws, so the answer stays undelivered and is replayed on the next start.
/// </para>
/// </summary>
public sealed class PairingAnswers : INotificationAnswerHandler
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
            _logger.LogDebug("An answer with source key {Key} names no pairing; nothing to do.", answer.Source.Key);
            return;
        }

        var (pairingId, deviceId) = named;

        if (answer.ChoiceIds.Contains(SyncNotificationSubjects.RefuseChoice, StringComparer.Ordinal))
        {
            var roster = _roster() ?? throw new InvalidOperationException("No sync client is composed to revoke the refused device with.");
            var revoked = await roster.RevokeDeviceAsync(deviceId, cancellationToken).ConfigureAwait(false);

            if (revoked.IsFailure && revoked.Error.Code != SyncErrorCodes.DeviceNotFound)
            {
                throw new InvalidOperationException($"The refused device {deviceId:D} was not revoked: {revoked.Error.Message}");
            }

            _logger.LogInformation("Refused the device that redeemed pairing {Pairing}; it is revoked.", pairingId);
        }

        await _pairings.SettledAsync(pairingId).ConfigureAwait(false);
    }
}
