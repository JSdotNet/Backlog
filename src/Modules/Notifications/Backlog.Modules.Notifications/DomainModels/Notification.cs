using System.Text;

using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Notifications.DomainModels;

/// <summary>
/// One message a source raises on an owner instance
/// (<c>.devbook/domain/notifications/domain.md#notification</c>, local ADR 0039): its kind,
/// severity, content and lifecycle, and the consistency boundary for its answer.
/// <para>
/// Its rules are <c>.devbook/domain/notifications/domain.invariants.md</c>, each with a unit
/// test in <c>Backlog.Modules.Notifications.UnitTests</c>: only the owner instance changes it;
/// a question has an answer spec and a subject; kind and severity come from their fixed sets;
/// there is no deadline and no default answer; the state moves once, forward from Open; a
/// question has at most one answer; the content stays within its bounds; and a reply is
/// accepted only when it fits the spec, the question is open, it comes from the owner when
/// the question says so, and it is addressed to this owner instance.
/// </para>
/// <para>
/// <c>reply.foreign_owner</c> — a reply from a device under another account — is the sync
/// service's check first (ADR 0039 §2), and the owner's second check needs the account the
/// replica carries. Neither exists before the sync exchange does, so this step checks only that
/// the reply is addressed to this instance (<c>reply.wrong_instance</c>).
/// </para>
/// <para>
/// Every change takes the acting instance's id and is refused for any other than
/// <see cref="OwnerInstanceId"/> (<c>notification.not_owner</c>): one writer per document is
/// what lets the replica carry it without merge rules.
/// </para>
/// </summary>
public sealed class Notification
{
    /// <summary>The most characters a title holds.</summary>
    public const int MaxTitleLength = 80;

    /// <summary>The most characters a summary holds.</summary>
    public const int MaxSummaryLength = 180;

    /// <summary>The most bytes of UTF-8 Markdown a body holds: 32 KB.</summary>
    public const int MaxBodyBytes = 32 * 1024;

    private Notification(
        Guid id,
        Guid ownerInstanceId,
        NotificationSource source,
        NotificationSubject? subject,
        NotificationKind kind,
        NotificationSeverity severity,
        string title,
        string summary,
        string bodyMarkdown,
        AnswerSpec? answerSpec,
        string? deepLink,
        bool isPrivate,
        DateTimeOffset createdAt)
    {
        Id = id;
        OwnerInstanceId = ownerInstanceId;
        Source = source;
        Subject = subject;
        Kind = kind;
        Severity = severity;
        Title = title;
        Summary = summary;
        BodyMarkdown = bodyMarkdown;
        AnswerSpec = answerSpec;
        DeepLink = deepLink;
        IsPrivate = isPrivate;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    /// <summary>The device that published it, its only writer.</summary>
    public Guid OwnerInstanceId { get; }

    public NotificationSource Source { get; }

    public NotificationSubject? Subject { get; }

    public NotificationKind Kind { get; }

    public NotificationSeverity Severity { get; }

    public string Title { get; }

    public string Summary { get; }

    public string BodyMarkdown { get; }

    /// <summary>The answers a question accepts; null on an alert.</summary>
    public AnswerSpec? AnswerSpec { get; }

    public string? DeepLink { get; }

    public bool IsPrivate { get; }

    public DateTimeOffset CreatedAt { get; }

    public NotificationState State { get; private set; } = NotificationState.Open;

    /// <summary>When it left Open, or null while it is open.</summary>
    public DateTimeOffset? SettledAt { get; private set; }

    /// <summary>Why it was closed, or null.</summary>
    public string? CloseReason { get; private set; }

    /// <summary>The accepted answer, or null.</summary>
    public NotificationAnswer? Answer { get; private set; }

    /// <summary>When the source's handler took the answer, or null while it still has to be
    /// handed over — the replay's marker.</summary>
    public DateTimeOffset? AnswerDeliveredAt { get; private set; }

    public bool IsOpen => State == NotificationState.Open;

    /// <summary>Whether an accepted answer is still waiting for its handler.</summary>
    public bool AwaitsDelivery => Answer is not null && AnswerDeliveredAt is null;

    /// <summary>
    /// A new Open notification owned by <paramref name="ownerInstanceId"/>. Refused when the
    /// draft breaks a rule: an unknown kind or severity, a question without an answer spec or a
    /// subject, an alert with an answer spec, an answer spec of no allowed shape, a missing
    /// source or title, or content past its bounds.
    /// </summary>
    public static Result<Notification> Publish(Guid id, Guid ownerInstanceId, NotificationDraft draft, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (!Enum.IsDefined(draft.Kind)) return NotificationsErrors.UnknownKind();
        if (!Enum.IsDefined(draft.Severity)) return NotificationsErrors.UnknownSeverity();

        if (draft.Source is null || string.IsNullOrWhiteSpace(draft.Source.Context) || string.IsNullOrWhiteSpace(draft.Source.Key))
            return NotificationsErrors.SourceRequired();

        if (draft.Subject is { } named
            && (string.IsNullOrWhiteSpace(named.Context) || string.IsNullOrWhiteSpace(named.Type) || string.IsNullOrWhiteSpace(named.Id)))
            return NotificationsErrors.SubjectIncomplete();

        if (draft.Kind == NotificationKind.Question)
        {
            if (draft.Answer is null || draft.Subject is null) return NotificationsErrors.QuestionIncomplete();

            var shape = AnswerSpecRules.Validate(draft.Answer);
            if (shape.IsFailure) return shape.Error;
        }
        else if (draft.Answer is not null)
        {
            return NotificationsErrors.AlertWithAnswerSpec();
        }

        var title = draft.Title?.Trim() ?? string.Empty;
        if (title.Length == 0 || title.Contains('\n', StringComparison.Ordinal) || title.Contains('\r', StringComparison.Ordinal))
            return NotificationsErrors.TitleRequired();
        if (title.Length > MaxTitleLength) return NotificationsErrors.ContentTooLong("title", MaxTitleLength, "characters");

        var summary = draft.Summary?.Trim() ?? string.Empty;
        if (summary.Length > MaxSummaryLength) return NotificationsErrors.ContentTooLong("summary", MaxSummaryLength, "characters");

        var body = draft.BodyMarkdown ?? string.Empty;
        if (Encoding.UTF8.GetByteCount(body) > MaxBodyBytes) return NotificationsErrors.ContentTooLong("body", MaxBodyBytes, "bytes of Markdown");

        return new Notification(
            id,
            ownerInstanceId,
            draft.Source with { Context = draft.Source.Context.Trim(), Key = draft.Source.Key.Trim() },
            draft.Subject,
            draft.Kind,
            draft.Severity,
            title,
            summary,
            body,
            draft.Answer,
            string.IsNullOrWhiteSpace(draft.DeepLink) ? null : draft.DeepLink.Trim(),
            draft.IsPrivate,
            createdAt);
    }

    /// <summary>Rehydration from storage. Trusted, as every stored aggregate is: the rules held
    /// when it was written.</summary>
    public static Notification Restore(
        Guid id,
        Guid ownerInstanceId,
        NotificationSource source,
        NotificationSubject? subject,
        NotificationKind kind,
        NotificationSeverity severity,
        string title,
        string summary,
        string bodyMarkdown,
        AnswerSpec? answerSpec,
        string? deepLink,
        bool isPrivate,
        DateTimeOffset createdAt,
        NotificationState state,
        DateTimeOffset? settledAt,
        string? closeReason,
        NotificationAnswer? answer,
        DateTimeOffset? answerDeliveredAt) =>
        new(id, ownerInstanceId, source, subject, kind, severity, title, summary, bodyMarkdown, answerSpec, deepLink, isPrivate, createdAt)
        {
            State = state,
            SettledAt = settledAt,
            CloseReason = closeReason,
            Answer = answer,
            AnswerDeliveredAt = answerDeliveredAt
        };

    /// <summary>Acknowledges an open alert. Refused for a question
    /// (<c>notification.wrong_kind</c>) and for one already settled.</summary>
    public Result Acknowledge(Guid actingInstanceId, DateTimeOffset at)
    {
        if (actingInstanceId != OwnerInstanceId) return Result.Failure(NotificationsErrors.NotOwner());
        if (Kind != NotificationKind.Alert) return Result.Failure(NotificationsErrors.WrongKind(Kind));
        if (!IsOpen) return Result.Failure(NotificationsErrors.AlreadySettled(State));

        State = NotificationState.Acknowledged;
        SettledAt = at;
        return Result.Success();
    }

    /// <summary>
    /// Accepts <paramref name="reply"/> as the question's one answer. Refused, in this order,
    /// for an acting instance other than the owner (<c>notification.not_owner</c>), a reply to
    /// another notification or addressed to another owner instance (<c>reply.invalid</c>,
    /// <c>reply.wrong_instance</c>), an
    /// alert (<c>notification.wrong_kind</c>), a question already answered
    /// (<c>notification.already_answered</c>) or closed (<c>reply.closed</c>), a viewer's reply
    /// to an owner-only question (<c>reply.owner_only</c>), and a reply that does not fit the
    /// answer spec (<c>reply.invalid</c>).
    /// </summary>
    public Result<NotificationAnswer> Accept(NotificationReply reply, Guid actingInstanceId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(reply);

        if (actingInstanceId != OwnerInstanceId) return NotificationsErrors.NotOwner();
        if (reply.NotificationId != Id) return NotificationsErrors.ReplyInvalid("The reply answers another notification.");
        if (reply.OwnerInstanceId != OwnerInstanceId) return NotificationsErrors.ReplyWrongInstance();
        if (Kind != NotificationKind.Question || AnswerSpec is null) return NotificationsErrors.WrongKind(Kind);

        switch (State)
        {
            case NotificationState.Answered:
                return NotificationsErrors.AlreadyAnswered();
            case NotificationState.Closed or NotificationState.Acknowledged:
                return NotificationsErrors.ReplyClosed();
            case NotificationState.Open:
                break;
        }

        if (AnswerSpec.AnswerOn == AnswerOn.Owner && reply.FromDeviceId != OwnerInstanceId)
            return NotificationsErrors.ReplyOwnerOnly();

        var fits = ReplyValidation.Validate(AnswerSpec, reply.ChoiceIds, reply.Text);
        if (fits.IsFailure) return fits.Error;

        var answer = new NotificationAnswer(
            Id,
            Source,
            reply.Id,
            reply.FromDeviceId,
            [.. reply.ChoiceIds ?? []],
            string.IsNullOrWhiteSpace(reply.Text) ? null : reply.Text,
            at);

        Answer = answer;
        State = NotificationState.Answered;
        SettledAt = at;
        return answer;
    }

    /// <summary>Closes an open notification for the publisher. Refused for one already
    /// settled (<c>notification.already_settled</c>).</summary>
    public Result Close(Guid actingInstanceId, string? reason, DateTimeOffset at)
    {
        if (actingInstanceId != OwnerInstanceId) return Result.Failure(NotificationsErrors.NotOwner());
        if (!IsOpen) return Result.Failure(NotificationsErrors.AlreadySettled(State));

        State = NotificationState.Closed;
        SettledAt = at;
        CloseReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return Result.Success();
    }

    /// <summary>Whether it is about <paramref name="subject"/>: all three parts equal.</summary>
    public bool IsAbout(NotificationSubject subject) =>
        Subject is { } own
        && string.Equals(own.Context, subject.Context, StringComparison.Ordinal)
        && string.Equals(own.Type, subject.Type, StringComparison.Ordinal)
        && string.Equals(own.Id, subject.Id, StringComparison.Ordinal);

    /// <summary>Closes an open notification because its subject ended. Refused for one about
    /// another subject, and for one already settled.</summary>
    public Result CloseForSubject(NotificationSubject subject, string reason, Guid actingInstanceId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (actingInstanceId != OwnerInstanceId) return Result.Failure(NotificationsErrors.NotOwner());
        if (!IsAbout(subject)) return Result.Failure(NotificationsErrors.OtherSubject());

        return Close(actingInstanceId, reason, at);
    }

    /// <summary>Records that the source's handler took the answer, so the replay skips it.</summary>
    public void MarkAnswerDelivered(DateTimeOffset at)
    {
        if (Answer is not null && AnswerDeliveredAt is null) AnswerDeliveredAt = at;
    }
}
