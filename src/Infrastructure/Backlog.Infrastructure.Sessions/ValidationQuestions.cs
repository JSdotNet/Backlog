using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The question Sessions asks when a delivery run reaches Personal Validation: "Approve the
/// validation of &lt;run&gt;?", with the gate's handoff as its body and the choices Approve,
/// Approve + follow-up, Revise and Reject
/// (<c>.devbook/domain/notifications/requirements.md#requirement-a-run-at-personal-validation-asks-to-be-approved</c>).
/// It is about the run, so the run's end closes it, and its answer is recorded on the run
/// file as the gate decision — the pull request is still opened at the desktop.
/// <para>
/// The run file remembers the question under <c>validationQuestion</c>: the notification's
/// id, when it was asked, whether it was answered or closed, and whether the gate's current
/// attempt still has to ask (<c>armed</c>). An attempt begins when the gate moves into
/// progress from another status, or is reported in progress again while the run's approval
/// is back to pending after a rejection — and only once the earlier question is settled. It
/// asks on its first report that passes links or handoff text, once: the stage still holds
/// an earlier attempt's links and output, so only what the call itself passes counts, and
/// only that reaches the body.
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

    /// <summary>The body's bound, in UTF-8 bytes
    /// (<c>.devbook/domain/notifications/domain.invariants.md#invariant-title-and-summary-stay-within-their-bounds</c>).
    /// The Notifications module keeps its own copy internal; a body over it is refused, so
    /// the handoff is cut to fit here.</summary>
    internal const int MaxBodyBytes = 32 * 1024;

    private const string TruncatedLine = "*The handoff was truncated to fit; the full text is on the run's Personal Validation stage.*";

    private const string ApprovedNote = "Approved from a Backlog notification.";
    private const string RejectedNote = "Rejected from a Backlog notification.";
    private const string RevisedNote = "Sent back to revise from a Backlog notification.";

    /// <summary>
    /// What a stage update means for the question: the gate's attempt asks it on its first
    /// report with a handoff, and the gate moving on while it is unanswered closes it,
    /// because the person decided at the desktop. Writes what it did into
    /// <paramref name="run"/>, which the caller saves. Never throws: a question that could
    /// not be asked or closed is logged, and the stage update stands.
    /// </summary>
    /// <param name="output">The handoff text this call passes, or null.</param>
    /// <param name="links">The review links this call passes, or null.</param>
    internal static async Task OnStageAsync(
        JsonObject run,
        string stageName,
        string? previousStatus,
        string status,
        string? output,
        IReadOnlyList<DeliveryStageLink>? links,
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

                if (settled && (entering || revised))
                {
                    if (asked is null)
                    {
                        asked = [];
                        run[Key] = asked;
                    }

                    asked["armed"] = true;
                }

                var handoff = string.IsNullOrWhiteSpace(output) ? null : output.Trim();
                var shown = links?.Where(link => !string.IsNullOrWhiteSpace(link.Url)).ToList() ?? [];

                if (asked is null || !Flag(asked, "armed") || (handoff is null && shown.Count == 0)) return;

                var published = await port.PublishAsync(Draft(run, runId, worktree, handoff, shown), cancellationToken).ConfigureAwait(false);

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
                    ["closed"] = false,
                    ["armed"] = false
                };
            }
            else
            {
                if (asked is not null) asked["armed"] = false;

                if (!settled && Guid.TryParse(Text(asked!, "notificationId"), out var open))
                {
                    var closed = await port.CloseAsync(open, SessionNotificationSubjects.ValidationDecidedReason, cancellationToken).ConfigureAwait(false);

                    if (closed.IsFailure)
                    {
                        logger.LogDebug("The validation question about run {Run} was not closed: {Error}", runId, closed.Error.Message);
                    }

                    asked!["closed"] = true;
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Asking or closing the validation question about run {Run} failed; the stage update stands.", runId);
        }
    }

    /// <summary>
    /// What <paramref name="answer"/> asks of the run, read without changing it: nothing
    /// (a replay, or an answer to a question the run moved past); a record; or a follow-up
    /// to file after <see cref="ValidationAnswerStep.Gated"/> first, outside the run's gate,
    /// before the record.
    /// </summary>
    internal static ValidationAnswerStep Decide(JsonObject run, NotificationAnswer answer)
    {
        if (!Waiting(run, answer, out var asked)) return ValidationAnswerStep.Nothing;
        if (DesktopDecided(run, asked)) return ValidationAnswerStep.Record;

        if (Chose(answer, SessionNotificationSubjects.ApproveFollowUpChoice)
            && !string.IsNullOrWhiteSpace(answer.Text)
            && GatedEntryOf(run) is { } gated)
        {
            return new ValidationAnswerStep(ValidationAnswerKind.FileFollowUp, gated);
        }

        return ValidationAnswerStep.Record;
    }

    /// <summary>
    /// Records <paramref name="answer"/> on <paramref name="run"/> as its gate decision, with
    /// the follow-up it filed, and says what happened. Only the answer to the question the
    /// run is waiting on counts, once: a replayed answer, or one to a question the run has
    /// moved past, changes nothing; one the desktop decided first only marks the question
    /// answered.
    /// </summary>
    internal static ValidationRecord Record(JsonObject run, NotificationAnswer answer, Guid? followUpEntryId = null)
    {
        if (!Waiting(run, answer, out var asked)) return ValidationRecord.Unchanged;

        if (DesktopDecided(run, asked))
        {
            asked["answered"] = true;
            return ValidationRecord.DesktopStands;
        }

        if (run["approval"] is not JsonObject approval)
        {
            approval = [];
            run["approval"] = approval;
        }

        var (decision, note, choice) = Decision(answer);

        approval["personalValidation"] = decision;
        approval["decidedAt"] = Stamp(answer.AnsweredAt);
        approval["note"] = note;
        approval["choice"] = choice;
        approval["followUpEntryId"] = followUpEntryId?.ToString("D");
        asked["answered"] = true;

        return ValidationRecord.Recorded;
    }

    /// <summary>
    /// Approve approves; Approve + follow-up approves with the person's words; Revise and
    /// the open answer an older build's question took are a request for changes, a
    /// rejection with those words; Reject rejects with a fixed note, which is how the
    /// orchestrator tells it from Revise — beside the <c>choice</c> it records.
    /// </summary>
    private static (string Decision, string Note, string Choice) Decision(NotificationAnswer answer)
    {
        var words = string.IsNullOrWhiteSpace(answer.Text) ? null : answer.Text.Trim();

        if (Chose(answer, SessionNotificationSubjects.ApproveFollowUpChoice))
        {
            return ("approved", words ?? ApprovedNote, SessionNotificationSubjects.ApproveFollowUpChoice);
        }

        if (Chose(answer, SessionNotificationSubjects.ApproveChoice))
        {
            return ("approved", ApprovedNote, SessionNotificationSubjects.ApproveChoice);
        }

        if (Chose(answer, SessionNotificationSubjects.RejectChoice))
        {
            return ("rejected", RejectedNote, SessionNotificationSubjects.RejectChoice);
        }

        return ("rejected", words ?? (answer.ChoiceIds.Count == 0 ? RejectedNote : RevisedNote), SessionNotificationSubjects.ReviseChoice);
    }

    /// <summary>The entry the run works, as its prompt names it: by id where the entry
    /// marker names one, else by plan and item — or null where it names neither.</summary>
    internal static GatedEntry? GatedEntryOf(JsonObject run) =>
        RunEntryMarkers.In(Text(run, "originalPrompt")) switch
        {
            { EntryId: { } id } => GatedEntry.ById(id),
            { Plan: { } plan, Item: { } item } => GatedEntry.ByPlanItem(plan, item),
            _ => null
        };

    internal static NotificationDraft Draft(
        JsonObject run,
        string runId,
        string worktree,
        string? handoff,
        IReadOnlyList<DeliveryStageLink> links)
    {
        var title = OneLine(Text(run, "title"));
        if (title.Length == 0) title = runId;

        var repository = Text(run, "repo");
        var followUp = GatedEntryOf(run) is not null;

        return new NotificationDraft(
            new NotificationSource(SessionNotificationSubjects.Context, SessionNotificationSubjects.ValidationSourceKey(runId, worktree)),
            new NotificationSubject(SessionNotificationSubjects.Context, SessionNotificationSubjects.Run, runId),
            NotificationKind.Question,
            NotificationSeverity.Info,
            Title(title),
            "The run reached Personal Validation. Your answer is recorded on the run; the pull request is still opened at the desktop.",
            Body(title, repository, followUp, handoff, links),
            new AnswerSpec(
                [
                    new AnswerChoice(SessionNotificationSubjects.ApproveChoice, "Approve", AnswerChoiceStyle.Primary),
                    .. followUp
                        ? [new AnswerChoice(SessionNotificationSubjects.ApproveFollowUpChoice, "Approve + follow-up", TakesText: true, Placeholder: "What should the follow-up do?")]
                        : Array.Empty<AnswerChoice>(),
                    new AnswerChoice(SessionNotificationSubjects.ReviseChoice, "Revise", TakesText: true, Placeholder: "What should change?"),
                    new AnswerChoice(SessionNotificationSubjects.RejectChoice, "Reject", AnswerChoiceStyle.Destructive)
                ]),
            Origin: NotificationOrigin.Fit(
                NotificationOrigins.ClaudeCode,
                string.IsNullOrWhiteSpace(repository) ? title : $"{repository.Trim()}{NotificationOrigin.Separator}{title}",
                via: NotificationOrigins.AgentSession));
    }

    /// <summary>
    /// The handoff as Markdown: what the answers do, then the review links (label, address,
    /// description) and the handoff text — cut to <see cref="MaxBodyBytes"/>, ending with a
    /// line that says so, when it is longer.
    /// </summary>
    private static string Body(string title, string? repository, bool followUp, string? handoff, IReadOnlyList<DeliveryStageLink> links)
    {
        var body = new StringBuilder();

        body.Append(CultureInfo.InvariantCulture, $"**{title}** reached Personal Validation{(string.IsNullOrWhiteSpace(repository) ? string.Empty : $" in `{repository}`")}.\n\n");
        body.Append("- **Approve** records the approval on the run.\n");
        if (followUp) body.Append("- **Approve + follow-up** approves and files your words as a follow-up entry after this one.\n");
        body.Append("- **Revise** sends it back with your words for another round.\n");
        body.Append(followUp
            ? "- **Reject** records a rejection, and the entry is blocked.\n\n"
            : "- **Reject** records a rejection and ends the run without a revise round.\n\n");
        body.Append("Opening the pull request still happens at the desktop, in the session that runs it.\n");

        if (links.Count > 0)
        {
            body.Append("\n## Review links\n\n");

            foreach (var link in links)
            {
                var label = string.IsNullOrWhiteSpace(link.Label) ? link.Url : OneLine(link.Label);
                body.Append(CultureInfo.InvariantCulture, $"- [{label}]({link.Url})");
                if (!string.IsNullOrWhiteSpace(link.Description)) body.Append(CultureInfo.InvariantCulture, $" — {OneLine(link.Description)}");
                body.Append('\n');
            }
        }

        if (handoff is not null)
        {
            body.Append("\n## Handoff\n\n").Append(handoff).Append('\n');
        }

        return Fit(body.ToString());
    }

    /// <summary>The body as it is when it fits; otherwise cut on a character boundary so
    /// that, with the truncation line, it is within <see cref="MaxBodyBytes"/>.</summary>
    private static string Fit(string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        if (bytes.Length <= MaxBodyBytes) return body;

        var tail = "\n\n" + TruncatedLine;
        var room = MaxBodyBytes - Encoding.UTF8.GetByteCount(tail);

        // Back off to the start of a UTF-8 sequence: a continuation byte is 10xxxxxx.
        while (room > 0 && (bytes[room] & 0xC0) == 0x80) room--;

        return Encoding.UTF8.GetString(bytes, 0, room).TrimEnd() + tail;
    }

    /// <summary>"Approve the validation of &lt;run&gt;?", the run's title shortened to fit
    /// the title's 80 characters.</summary>
    private static string Title(string run)
    {
        const string Before = "Approve the validation of ";
        var room = MaxTitleLength - Before.Length - 1;

        return $"{Before}{(run.Length <= room ? run : run[..(room - 1)] + "…")}?";
    }

    private static bool Waiting(JsonObject run, NotificationAnswer answer, out JsonObject asked)
    {
        asked = run[Key] as JsonObject ?? [];

        return run[Key] is JsonObject
            && Guid.TryParse(Text(asked, "notificationId"), out var waiting)
            && waiting == answer.NotificationId
            && !Flag(asked, "answered");
    }

    /// <summary>Decided at the desktop after the question went out: that decision stands.</summary>
    private static bool DesktopDecided(JsonObject run, JsonObject asked) =>
        run["approval"] is JsonObject approval
        && Instant(Text(approval, "decidedAt")) is { } decidedAt
        && Instant(Text(asked, "askedAt")) is { } askedAt
        && decidedAt > askedAt
        && !string.Equals(Text(approval, "personalValidation"), "pending", StringComparison.Ordinal);

    private static bool Chose(NotificationAnswer answer, string choice) =>
        answer.ChoiceIds.Contains(choice, StringComparer.Ordinal);

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

/// <summary>What an answer asks of the run, decided under its gate.</summary>
internal enum ValidationAnswerKind
{
    /// <summary>A replay, or an answer to a question the run moved past.</summary>
    Nothing,

    /// <summary>Record it now.</summary>
    Record,

    /// <summary>File the follow-up first, outside the run's gate, then record.</summary>
    FileFollowUp
}

/// <summary>What an answer asks of the run, and the gated entry a follow-up is filed
/// after.</summary>
internal sealed record ValidationAnswerStep(ValidationAnswerKind Kind, GatedEntry? Gated = null)
{
    internal static readonly ValidationAnswerStep Nothing = new(ValidationAnswerKind.Nothing);
    internal static readonly ValidationAnswerStep Record = new(ValidationAnswerKind.Record);
}

/// <summary>What recording an answer did to the run.</summary>
internal enum ValidationRecord
{
    /// <summary>Nothing: a replay, or a question the run moved past.</summary>
    Unchanged,

    /// <summary>The desktop decided after the question went out; the question is only
    /// marked answered.</summary>
    DesktopStands,

    /// <summary>The answer is the run's gate decision.</summary>
    Recorded
}
