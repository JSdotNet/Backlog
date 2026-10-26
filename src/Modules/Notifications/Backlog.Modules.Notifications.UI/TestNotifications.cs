using System.Globalization;

using global::Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Modules.Notifications.UI;

/// <summary>
/// The two notifications the Notifications settings page sends to try the system out — an info
/// alert and a question — and how the page knows one of its own again. Both are raised by the
/// <see cref="SourceContext"/> source under <see cref="SourceKey"/>, so the page finds the last
/// one in the centre after the settings screen closed and opened again.
/// </summary>
public static class TestNotifications
{
    /// <summary>The source context a test notification is raised by, and the subject context it
    /// is about: Notifications itself.</summary>
    public const string SourceContext = "notifications";

    /// <summary>The key inside <see cref="SourceContext"/> every test notification carries.</summary>
    public const string SourceKey = "settings-test";

    /// <summary>The subject type of a test question — <c>notifications / test / &lt;id&gt;</c>.</summary>
    public const string SubjectType = "test";

    /// <summary>The set answer that says the test arrived.</summary>
    public const string ItWorksChoiceId = "it-works";

    /// <summary>The set answer that says it did not, yet.</summary>
    public const string NotYetChoiceId = "not-yet";

    /// <summary>The reason Clear test closes a test with.</summary>
    public const string ClearedReason = "Test cleared";

    /// <summary>The reason a test still open is closed with when the next one is sent.</summary>
    public const string ReplacedReason = "Replaced by a new test";

    /// <summary>The source every test notification is raised by.</summary>
    public static NotificationSource Source { get; } = new(SourceContext, SourceKey);

    /// <summary>Whether <paramref name="source"/> is the test's own.</summary>
    public static bool IsTest(NotificationSource source) =>
        source is not null
        && string.Equals(source.Context, SourceContext, StringComparison.Ordinal)
        && string.Equals(source.Key, SourceKey, StringComparison.Ordinal);

    /// <summary>A fresh test's subject: <c>notifications / test / &lt;id&gt;</c>, the id eight hex
    /// characters, enough to tell one test from the next in the centre.</summary>
    public static NotificationSubject NewSubject() =>
        new(SourceContext, SubjectType, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8]);

    /// <summary>The test alert: info, with a Markdown body that carries a small Mermaid diagram,
    /// so a device that shows it proves it renders both.</summary>
    public static NotificationDraft Alert(NotificationSubject subject) => new(
        Source,
        subject,
        NotificationKind.Alert,
        NotificationSeverity.Info,
        "Test alert from Settings",
        "If you can read this, notifications reach this device.",
        """
        **This is a test alert**, sent from *Settings › Notifications* on this desktop.

        Nothing waits on it. It shows how a notification's body renders:

        - Markdown, with **bold**, *italics* and a list
        - a Mermaid diagram:

        ```mermaid
        flowchart LR
            Settings[Settings] --> Desktop[This desktop]
            Desktop --> Phone[Your phone]
        ```
        """);

    /// <summary>The test question: two set answers, "It works" and "Not yet", and an open answer
    /// beside them.</summary>
    public static NotificationDraft Question(NotificationSubject subject) => new(
        Source,
        subject,
        NotificationKind.Question,
        NotificationSeverity.Info,
        "Test question from Settings",
        "Did this question reach you? Answer it from any paired device.",
        """
        **This is a test question**, sent from *Settings › Notifications* on this desktop.

        Pick an answer, or write one. The desktop that asked takes the first answer it receives,
        and the Settings page shows which one it was and the device it came from.
        """,
        new AnswerSpec(
            [
                new AnswerChoice(ItWorksChoiceId, "It works", AnswerChoiceStyle.Primary),
                new AnswerChoice(NotYetChoiceId, "Not yet")
            ],
            Open: new OpenAnswer("Anything else to say?")));
}

/// <summary>
/// The <see cref="INotificationAnswerHandler"/> for the test's source: it takes every answer and
/// does nothing with it, because the Settings page reads the stored answer off the centre. Registered
/// all the same, so a test answer is marked delivered rather than replayed on every start, as
/// <c>McpNotificationAnswers</c> does for the MCP <c>ask</c> tool.
/// <para>
/// Answers are handed over by source context alone, so this handler takes every answer to a
/// question the <c>notifications</c> context raises. The test is the only one today; a later
/// question of the context's own needs a handler that tells them apart by
/// <see cref="NotificationSource.Key"/>.
/// </para>
/// </summary>
public sealed class TestNotificationAnswers : INotificationAnswerHandler
{
    public string Source => TestNotifications.SourceContext;

    public Task HandleAnswerAsync(NotificationAnswer answer, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
