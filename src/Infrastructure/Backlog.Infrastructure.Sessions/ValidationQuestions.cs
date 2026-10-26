using System.Globalization;
using System.Text.Json.Nodes;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The question Sessions asks when a delivery run reaches Personal Validation: "Approve the
/// validation of &lt;run&gt;?", Approve or Reject, or an answer in the person's own words
/// (<c>.devbook/domain/notifications/context.md</c>). It is about the run, so the run's end
/// closes it, and its answer is recorded on the run file as the gate decision — the pull
/// request is still opened at the desktop.
/// <para>
/// The run file remembers the question under <c>validationQuestion</c>: the notification's
/// id, when it was asked, and whether it was answered or closed. The gate asks when it moves
/// into progress from another status, or when it is reported in progress again while the
/// run's approval is back to pending after a rejection — and only once the earlier question
/// is settled. A re-report that only adds review links, or a resumed session, asks nothing.
/// </para>
/// <para>
/// A decision the desktop recorded after the question was asked wins over a late answer:
/// the answer is recorded only while the run's approval is not newer than the question.
/// </para>
/// </summary>
internal static class ValidationQuestions
{
    private const string Key = "validationQuestion";
    private const int MaxTitleLength = 80;

    /// <summary>
    /// What a stage update means for the question: the gate going in progress asks it, and
    /// the gate moving on while it is unanswered closes it, because the person decided at
    /// the desktop. Writes what it did into <paramref name="run"/>, which the caller saves.
    /// Never throws: a question that could not be asked or closed is logged, and the stage
    /// update stands.
    /// </summary>
    internal static async Task OnStageAsync(
        JsonObject run,
        string stageName,
        string? previousStatus,
        string status,
        string worktree,
        string runId,
        Func<INotifications?>? notifications,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (notifications is null || !DeliveryRunStageResolution.IsGate(stageName)) return;

        try
        {
            if (notifications() is not { } port) return;

            var asked = run[Key] as JsonObject;
            var settled = asked is null || Flag(asked, "answered") || Flag(asked, "closed");

            if (string.Equals(status, DeliveryStageStatuses.InProgress, StringComparison.Ordinal))
            {
                var entering = !string.Equals(previousStatus, DeliveryStageStatuses.InProgress, StringComparison.Ordinal);
                var revised = asked is not null && Flag(asked, "answered") && string.Equals(Decided(run), "pending", StringComparison.Ordinal);

                if (!settled || !(entering || revised)) return;

                var published = await port.PublishAsync(Draft(run, runId, worktree), cancellationToken).ConfigureAwait(false);

                if (published.IsFailure)
                {
                    logger.LogDebug("The validation question about run {Run} was not published: {Error}", runId, published.Error.Message);
                    return;
                }

                run[Key] = new JsonObject
                {
                    ["notificationId"] = published.Value.ToString("D"),
                    ["askedAt"] = Stamp(DateTimeOffset.UtcNow),
                    ["answered"] = false,
                    ["closed"] = false
                };
            }
            else if (!settled && Guid.TryParse(Text(asked!, "notificationId"), out var open))
            {
                var closed = await port.CloseAsync(open, SessionNotificationSubjects.ValidationDecidedReason, cancellationToken).ConfigureAwait(false);

                if (closed.IsFailure)
                {
                    logger.LogDebug("The validation question about run {Run} was not closed: {Error}", runId, closed.Error.Message);
                }

                asked!["closed"] = true;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Asking or closing the validation question about run {Run} failed; the stage update stands.", runId);
        }
    }

    /// <summary>
    /// Records <paramref name="answer"/> on <paramref name="run"/> as its gate decision, and
    /// says whether anything changed. Only the answer to the question the run is waiting on
    /// counts, once: a replayed answer, or one to a question the run has moved past, changes
    /// nothing.
    /// </summary>
    internal static bool Record(JsonObject run, NotificationAnswer answer)
    {
        if (run[Key] is not JsonObject asked
            || !Guid.TryParse(Text(asked, "notificationId"), out var waiting)
            || waiting != answer.NotificationId
            || Flag(asked, "answered"))
        {
            return false;
        }

        if (run["approval"] is not JsonObject approval)
        {
            approval = [];
            run["approval"] = approval;
        }

        // Decided at the desktop after the question went out: that decision stands.
        if (Instant(Text(approval, "decidedAt")) is { } decidedAt
            && Instant(Text(asked, "askedAt")) is { } askedAt
            && decidedAt > askedAt
            && !string.Equals(Text(approval, "personalValidation"), "pending", StringComparison.Ordinal))
        {
            asked["answered"] = true;
            return true;
        }

        var (decision, note) = Decision(answer);

        approval["personalValidation"] = decision;
        approval["decidedAt"] = Stamp(answer.AnsweredAt);
        approval["note"] = note;
        asked["answered"] = true;

        return true;
    }

    /// <summary>Approve approves; Reject rejects; words of the person's own are a request for
    /// changes, which the flow records as a rejection with those words.</summary>
    private static (string Decision, string Note) Decision(NotificationAnswer answer)
    {
        if (answer.ChoiceIds.Contains(SessionNotificationSubjects.ApproveChoice, StringComparer.Ordinal))
        {
            return ("approved", "Approved from a Backlog notification.");
        }

        if (!string.IsNullOrWhiteSpace(answer.Text))
        {
            return ("rejected", answer.Text.Trim());
        }

        return ("rejected", "Rejected from a Backlog notification.");
    }

    internal static NotificationDraft Draft(JsonObject run, string runId, string worktree)
    {
        var title = OneLine(Text(run, "title"));
        if (title.Length == 0) title = runId;

        var repository = Text(run, "repo");

        return new NotificationDraft(
            new NotificationSource(SessionNotificationSubjects.Context, SessionNotificationSubjects.ValidationSourceKey(runId, worktree)),
            new NotificationSubject(SessionNotificationSubjects.Context, SessionNotificationSubjects.Run, runId),
            NotificationKind.Question,
            NotificationSeverity.Info,
            Title(title),
            "The run reached Personal Validation. Your answer is recorded on the run; the pull request is still opened at the desktop.",
            $"""
            **{title}** reached Personal Validation{(string.IsNullOrWhiteSpace(repository) ? string.Empty : $" in `{repository}`")}.

            - **Approve** records the approval on the run.
            - **Reject**, or an answer in your own words, records a rejection with what you wrote.

            Opening the pull request still happens at the desktop, in the session that runs it.
            """,
            new AnswerSpec(
                [
                    new AnswerChoice(SessionNotificationSubjects.ApproveChoice, "Approve", AnswerChoiceStyle.Primary),
                    new AnswerChoice(SessionNotificationSubjects.RejectChoice, "Reject", AnswerChoiceStyle.Destructive)
                ],
                Open: new OpenAnswer("What should change?")));
    }

    /// <summary>"Approve the validation of &lt;run&gt;?", the run's title shortened to fit
    /// the title's 80 characters.</summary>
    private static string Title(string run)
    {
        const string Before = "Approve the validation of ";
        var room = MaxTitleLength - Before.Length - 1;

        return $"{Before}{(run.Length <= room ? run : run[..(room - 1)] + "…")}?";
    }

    private static string OneLine(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Decided(JsonObject run) =>
        run["approval"] is JsonObject approval ? Text(approval, "personalValidation") : null;

    private static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset? Instant(string? stamp) =>
        DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    private static bool Flag(JsonObject owner, string name) =>
        owner[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static string? Text(JsonObject owner, string name) =>
        owner[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
