using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Windows Hello before this device mints a pairing code or revokes another device (local ADR
/// 0058 §6): both are hard to undo — a code lets a new device in, a revocation shuts one out.
/// <see cref="DevicePairingClient"/> asks it before either call leaves the machine.
/// <para>
/// <b>Composed by the desktop head alone.</b> A host that registers none asks nothing: the
/// web harness keeps no step-up (§9), and the phone has no Windows Hello. So the client takes
/// this type rather than <see cref="IUserConsent"/> itself, which the harness registers to
/// refuse an agent's Allow.
/// </para>
/// </summary>
public sealed class DeviceStepUp(IUserConsent consent)
{
    /// <summary>Where an act the person cancelled at the Windows Hello prompt is filed.</summary>
    public const string DeclinedCode = "sync.step_up_declined";

    /// <summary>Where an act nothing here could confirm is filed: no Windows Hello set up.</summary>
    public const string UnavailableCode = "sync.step_up_unavailable";

    private readonly IUserConsent _consent = consent ?? throw new ArgumentNullException(nameof(consent));

    /// <summary>Asks the person to confirm <paramref name="prompt"/>, and answers success when
    /// they did, or a failure whose message says, after <paramref name="refused"/>, why not.</summary>
    /// <param name="prompt">What the Windows Hello prompt says.</param>
    /// <param name="refused">What did not happen, for the failure's message — "No pairing code
    /// was made".</param>
    /// <param name="cancellationToken">Cancels the prompt, which counts as cancelled.</param>
    public async Task<Result> ConfirmAsync(string prompt, string refused, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(refused);

        var outcome = await _consent.VerifyAsync(prompt, cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            UserConsentOutcome.Verified => Result.Success(),
            UserConsentOutcome.NotAvailable => Result.Failure(Error.Validation(
                UnavailableCode,
                $"{refused}: it needs Windows Hello, and Windows Hello is not available on this PC.")),
            _ => Result.Failure(Error.Validation(
                DeclinedCode,
                $"{refused}: Windows Hello was cancelled.")),
        };
    }
}
