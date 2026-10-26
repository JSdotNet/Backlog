using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Notifications;

/// <summary>
/// One notification as the JSON document its row holds — the shape local ADR 0039 §3 gives the
/// travelling <c>notification</c> document, kept here as the owner's local copy. Versioned so a
/// later shape can be told from this one.
/// </summary>
internal sealed record NotificationDocument(
    int Version,
    Guid Id,
    Guid OwnerInstanceId,
    NotificationSource Source,
    NotificationSubject? Subject,
    NotificationKind Kind,
    NotificationSeverity Severity,
    string Title,
    string Summary,
    string BodyMd,
    AnswerSpec? Answer,
    string? DeepLink,
    bool IsPrivate,
    DateTimeOffset CreatedAt,
    NotificationState State,
    DateTimeOffset? SettledAt,
    string? CloseReason,
    NotificationAnswer? Accepted,
    DateTimeOffset? AnswerDeliveredAt)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>The only shape written so far.</summary>
    public const int CurrentVersion = 1;

    public static NotificationDocument From(Notification notification) => new(
        CurrentVersion,
        notification.Id,
        notification.OwnerInstanceId,
        notification.Source,
        notification.Subject,
        notification.Kind,
        notification.Severity,
        notification.Title,
        notification.Summary,
        notification.BodyMarkdown,
        notification.AnswerSpec,
        notification.DeepLink,
        notification.IsPrivate,
        notification.CreatedAt,
        notification.State,
        notification.SettledAt,
        notification.CloseReason,
        notification.Answer,
        notification.AnswerDeliveredAt);

    /// <summary>The notification this document holds. Throws <see cref="JsonException"/> for one
    /// that parsed but does not read as a notification — a missing owner, source or title — so the
    /// store skips the row rather than handing the module a notification without them.</summary>
    public Notification ToNotification()
    {
        if (Version != CurrentVersion) throw new JsonException($"A notification document of version {Version} is not one this build reads.");
        if (Id == Guid.Empty || OwnerInstanceId == Guid.Empty) throw new JsonException("A notification document names its id and its owner instance.");
        if (Source is null || string.IsNullOrWhiteSpace(Source.Context) || string.IsNullOrWhiteSpace(Source.Key))
            throw new JsonException("A notification document names its source.");
        if (string.IsNullOrWhiteSpace(Title)) throw new JsonException("A notification document has a title.");
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Severity) || !Enum.IsDefined(State))
            throw new JsonException("A notification document's kind, severity and state come from their fixed sets.");
        if (Kind == NotificationKind.Question && Answer is null) throw new JsonException("A question document carries its answer spec.");
        if (Accepted is not null && (Accepted.NotificationId != Id || Accepted.Source is null))
            throw new JsonException("A notification document's accepted answer names the notification and its source.");

        return Restore();
    }

    private Notification Restore() => Notification.Restore(
        Id,
        OwnerInstanceId,
        Source,
        Subject,
        Kind,
        Severity,
        Title,
        Summary ?? string.Empty,
        BodyMd ?? string.Empty,
        Answer is null ? null : Answer with { Choices = Answer.Choices ?? [] },
        DeepLink,
        IsPrivate,
        CreatedAt,
        State,
        SettledAt,
        CloseReason,
        Accepted is null ? null : Accepted with { ChoiceIds = Accepted.ChoiceIds ?? [] },
        AnswerDeliveredAt);
}
