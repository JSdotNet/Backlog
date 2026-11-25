using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Windows Hello before this device mints a pairing code, revokes another device, or refuses
/// a device that just paired (local ADR 0058 §6): each is hard to undo — a code lets a new
/// device in, a revocation or a refusal shuts one out. <see cref="DevicePairingClient"/> asks it
/// before any of these calls leaves the machine.
/// <para>
/// <b>Composed by the desktop head alone.</b> A host that registers none asks nothing: the
/// web harness keeps no step-up (§9), and the phone has no Windows Hello. So the client takes
/// this type rather than <see cref="IUserConsent"/> itself, which the harness registers to
/// refuse an agent's Allow.
/// </para>
/// </summary>
/// <param name="consent">The prompt: Windows Hello on the desktop head.</param>
/// <param name="time">The clock a prompt's window runs on (<see cref="ConfirmWithinAsync"/>); the
/// system clock when none is given.</param>
public sealed class DeviceStepUp(IUserConsent consent, TimeProvider? time = null)
{
    /// <summary>Where an act the person cancelled at the Windows Hello prompt is filed.</summary>
    public const string DeclinedCode = "sync.step_up_declined";

    /// <summary>Where an act nothing here could confirm is filed: no Windows Hello set up.</summary>
    public const string UnavailableCode = "sync.step_up_unavailable";

    /// <summary>Where an act whose prompt nobody answered within its window is filed.</summary>
    public const string ExpiredCode = "sync.step_up_expired";

    private readonly IUserConsent _consent = consent ?? throw new ArgumentNullException(nameof(consent));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Asks the person to confirm <paramref name="prompt"/>, and answers success when
    /// they did, or a failure whose message says, after <paramref name="refused"/>, why not. For
    /// the person's own acts at the desk, so a confirmation the host remembers may answer it.</summary>
    /// <param name="prompt">What the Windows Hello prompt says.</param>
    /// <param name="refused">What did not happen, for the failure's message — "No pairing code
    /// was made".</param>
    /// <param name="cancellationToken">Withdraws the prompt and throws
    /// <see cref="OperationCanceledException"/>: the app closing is not the person saying no,
    /// so it is never filed as a decline.</param>
    public async Task<Result> ConfirmAsync(string prompt, string refused, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(refused);

        var outcome = await _consent.VerifyAsync(prompt, cancellationToken).ConfigureAwait(false);

        // Windows Hello answers a withdrawn prompt as cancelled rather than throwing.
        cancellationToken.ThrowIfCancellationRequested();

        return Answer(outcome, refused);
    }

    /// <summary>
    /// <see cref="ConfirmAsync"/>, with the prompt withdrawn once <paramref name="window"/> has
    /// passed unanswered: a failure filed under <see cref="ExpiredCode"/>. For a prompt raised
    /// by something other than the person at this desktop — a Refuse answered on the phone —
    /// which nobody may be here to answer, and which every answer queued behind it waits on.
    /// A confirmation that lands as the window closes still counts. Always shown
    /// (<see cref="IUserConsent.VerifyEveryTimeAsync"/>): a confirmation the person gave a moment
    /// ago for their own act does not answer one raised from elsewhere.
    /// </summary>
    /// <param name="prompt">What the Windows Hello prompt says.</param>
    /// <param name="refused">What did not happen, for the failure's message.</param>
    /// <param name="window">How long the prompt stands.</param>
    /// <param name="cancellationToken">Withdraws the prompt and throws
    /// <see cref="OperationCanceledException"/>, as for <see cref="ConfirmAsync"/>.</param>
    public async Task<Result> ConfirmWithinAsync(
        string prompt,
        string refused,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(refused);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        using var expiry = new CancellationTokenSource(window, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);

        UserConsentOutcome outcome;
        try
        {
            outcome = await _consent.VerifyEveryTimeAsync(prompt, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (expiry.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A consent that throws on cancellation rather than answering Declined.
            outcome = UserConsentOutcome.Declined;
        }

        // Shutdown, not an answer: the caller replays the act rather than filing it as declined.
        cancellationToken.ThrowIfCancellationRequested();

        if (outcome != UserConsentOutcome.Verified
            && expiry.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(Error.Validation(
                ExpiredCode,
                $"{refused}: Windows Hello was not confirmed within {Describe(window)}."));
        }

        return Answer(outcome, refused);
    }

    private static Result Answer(UserConsentOutcome outcome, string refused) => outcome switch
    {
        UserConsentOutcome.Verified => Result.Success(),
        UserConsentOutcome.NotAvailable => Result.Failure(Error.Validation(
            UnavailableCode,
            $"{refused}: it needs Windows Hello, and Windows Hello is not available on this PC.")),
        _ => Result.Failure(Error.Validation(
            DeclinedCode,
            $"{refused}: Windows Hello was cancelled.")),
    };

    /// <summary>"2 minutes", "1 minute", or seconds for a window under a minute.</summary>
    private static string Describe(TimeSpan window)
    {
        if (window < TimeSpan.FromMinutes(1))
        {
            var seconds = (int)Math.Ceiling(window.TotalSeconds);
            return seconds == 1 ? "1 second" : $"{seconds} seconds";
        }

        var minutes = (int)Math.Round(window.TotalMinutes);
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }
}
