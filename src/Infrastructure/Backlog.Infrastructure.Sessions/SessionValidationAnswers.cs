using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The <see cref="INotificationAnswerHandler"/> for the <c>sessions</c> source: the answer to
/// "Approve the validation of &lt;run&gt;?" is recorded on the run as its Personal Validation
/// decision (<see cref="ValidationQuestions"/>). Nothing else happens: the flow reads the
/// decision back from the run, and the pull request is opened at the desktop.
/// <para>
/// Idempotent, as the contract asks: the run and its worktree are read from the answer's
/// source key, and the run file records which question it was answered through, so a
/// replayed answer changes nothing. A run that no longer exists has nothing to record on.
/// </para>
/// </summary>
public sealed class SessionValidationAnswers(IDeliverySurfaceLifecycle lifecycle, ILogger<SessionValidationAnswers>? logger = null)
    : INotificationAnswerHandler
{
    private readonly IDeliverySurfaceLifecycle _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public string Source => SessionNotificationSubjects.Context;

    public async Task HandleAnswerAsync(NotificationAnswer answer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (SessionNotificationSubjects.ParseValidationSourceKey(answer.Source.Key) is not { } named)
        {
            _logger.LogDebug("An answer with source key {Key} names no run; nothing to do.", answer.Source.Key);
            return;
        }

        if (_lifecycle is not LocalDeliverySurfaceLifecycle local)
        {
            _logger.LogDebug("No local run files are composed; the answer about run {Run} is not recorded.", named.RunId);
            return;
        }

        var recorded = await local.RecordValidationAnswerAsync(named.Worktree, named.RunId, answer, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            recorded ? "Recorded the validation answer on run {Run}." : "The validation answer about run {Run} changed nothing.",
            named.RunId);
    }
}
