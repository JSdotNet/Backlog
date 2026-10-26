namespace Backlog.Modules.Notifications.Abstractions;

/// <summary>What a notification is (<c>.devbook/domain/notifications/domain.md#notification-kind</c>).</summary>
public enum NotificationKind
{
    /// <summary>Informs. It can be acknowledged, but nothing waits on it.</summary>
    Alert,

    /// <summary>Waits for an answer, and carries an <see cref="AnswerSpec"/> and a subject.</summary>
    Question
}

/// <summary>How urgent a notification is (<c>.devbook/domain/notifications/domain.md#severity</c>).
/// Independent of the kind, and never a limit on who may answer.</summary>
public enum NotificationSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>Where a notification stands (<c>.devbook/domain/notifications/domain.md#notification-state</c>).
/// It starts Open and moves once; only the owner instance moves it.</summary>
public enum NotificationState
{
    /// <summary>Published, and not yet settled.</summary>
    Open,

    /// <summary>An alert a person acknowledged.</summary>
    Acknowledged,

    /// <summary>A question whose owner accepted a reply as its answer.</summary>
    Answered,

    /// <summary>The publisher closed it, or its subject ended.</summary>
    Closed
}

/// <summary>How a set choice is drawn (<c>.devbook/domain/notifications/domain.md#answer-spec</c>).</summary>
public enum AnswerChoiceStyle
{
    Default,
    Primary,
    Destructive
}

/// <summary>Which instances a question takes a reply from
/// (<c>.devbook/domain/notifications/domain.md#answer-spec</c>, <c>answerOn</c>).</summary>
public enum AnswerOn
{
    /// <summary>Any paired viewer, and the owner — the default, critical questions included.</summary>
    AnyInstance,

    /// <summary>Only a reply made on the owner instance.</summary>
    Owner
}
