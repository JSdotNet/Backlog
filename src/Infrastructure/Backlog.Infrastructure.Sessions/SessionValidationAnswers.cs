using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The <see cref="INotificationAnswerHandler"/> for the <c>sessions</c> source: the answer to
/// "Approve the validation of &lt;run&gt;?" is recorded on the run as its Personal Validation
/// decision (<see cref="ValidationQuestions"/>), and an Approve + follow-up first files the
/// follow-up entry after the gated entry through Tasks' <see cref="ITaskFollowUps"/>. The flow
/// reads the decision back from the run, and the pull request is opened at the desktop.
/// <para>
/// Idempotent, as the contract asks: the run and its worktree are read from the answer's
/// source key, and the run file records which question it was answered through, so a
/// replayed answer changes nothing and files nothing. A run that no longer exists has
/// nothing to record on.
/// </para>
/// <para>
/// The follow-up is filed with the run's gate let go — decide under it, release it, file,
/// then record under it again, re-checked — so the run file is never held while another
/// store is awaited. Filing is idempotent on the notification id, so a store that throws
/// leaves the answer to be delivered again. A follow-up Tasks refuses — the gated entry is
/// gone — still records the approval with the person's words, and no follow-up.
/// </para>
/// </summary>
public sealed class SessionValidationAnswers(
    IDeliverySurfaceLifecycle lifecycle,
    ILogger<SessionValidationAnswers>? logger = null,
    ITaskFollowUps? followUps = null)
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

        var step = await local.DecideValidationAnswerAsync(named.Worktree, named.RunId, answer, cancellationToken).ConfigureAwait(false);

        if (step.Kind is not ValidationAnswerKind.FileFollowUp)
        {
            _logger.LogDebug(
                step.Kind is ValidationAnswerKind.Record ? "Recorded the validation answer on run {Run}." : "The validation answer about run {Run} changed nothing.",
                named.RunId);
            return;
        }

        var followUp = await FileAsync(step.Gated!, answer, named.RunId, cancellationToken).ConfigureAwait(false);
        var recorded = await local.RecordValidationAnswerAsync(named.Worktree, named.RunId, answer, followUp, cancellationToken).ConfigureAwait(false);

        if (recorded is not ValidationRecord.Recorded && followUp is { } filed)
        {
            // The narrow window the gate was let go for: the desktop decided while Tasks
            // filed. Its decision stands; the entry stays, for the person to keep or drop.
            _logger.LogWarning(
                "Run {Run} was decided at the desktop while follow-up {FollowUp} was filed; the desktop's decision stands and the follow-up is kept.",
                named.RunId,
                filed);
        }
    }

    /// <summary>The follow-up's id, or null where none could be filed for good — no Tasks
    /// composed, or Tasks refusing it. A store that throws is left to throw.</summary>
    private async Task<Guid?> FileAsync(GatedEntry gated, NotificationAnswer answer, string runId, CancellationToken cancellationToken)
    {
        if (followUps is null)
        {
            _logger.LogWarning("No Tasks follow-ups are composed; run {Run} records the approval without a follow-up.", runId);
            return null;
        }

        var filed = await followUps
            .FileAfterAsync(new TaskFollowUpRequest(gated, answer.Text!.Trim(), answer.NotificationId), cancellationToken)
            .ConfigureAwait(false);

        if (filed.IsSuccess) return filed.Value;

        _logger.LogWarning(
            "The follow-up asked for on run {Run} was not filed ({Error}); the approval is recorded with its words and no follow-up.",
            runId,
            filed.Error.Code);
        return null;
    }
}
