using System.Globalization;

using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Modules.Notifications.Toasts;

/// <summary>
/// What an operating-system notification shows for a notification on its owner instance, and what
/// a press in one asks the centre to do (<c>.devbook/domain/notifications/context.md</c>, the
/// proposal's "Windows app notifications with action buttons").
/// <para>
/// Platform-free on purpose: the Windows head draws <see cref="NotificationToastContent"/> with the
/// Windows App SDK and hands what comes back to <see cref="Read"/>, and every rule about which
/// buttons appear is tested here, where no notification server is needed.
/// </para>
/// <list type="bullet">
/// <item>Set answers become action buttons, at most <see cref="MaxButtons"/>.</item>
/// <item>An open answer gets an inline text box and a Send button beside it.</item>
/// <item>A question whose answers do not fit — several to pick, more choices than buttons, or a
/// choice that takes text — offers "Open in Backlog", which opens it in the centre.</item>
/// <item>An alert offers Acknowledge.</item>
/// <item>Only the title, the summary and the origin's "System · Place" leave the app, and a
/// private notification's summary is replaced by <see cref="PrivateSummary"/>. The origin is the
/// attribution line.</item>
/// </list>
/// </summary>
public static class NotificationToast
{
    /// <summary>The most buttons one notification carries.</summary>
    public const int MaxButtons = 3;

    /// <summary>What a private notification says in place of its summary.</summary>
    public const string PrivateSummary = "Backlog has a question for you";

    /// <summary>The id of the inline text box an open answer is typed in.</summary>
    public const string TextBoxId = "answer";

    public const string NotificationArgument = "notification";
    public const string ActionArgument = "action";
    public const string ChoiceArgument = "choice";

    public const string AnswerAction = "answer";
    public const string AcknowledgeAction = "acknowledge";
    public const string OpenAction = "open";

    /// <summary>The tag the platform keeps a notification's toast under, so a settled one can be
    /// taken down again.</summary>
    public static string TagFor(Guid notificationId) => notificationId.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>The notification to raise for <paramref name="notification"/>, or null when it is
    /// no longer open and so raises none.</summary>
    public static NotificationToastContent? For(NotificationView notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!notification.IsOpen) return null;

        var summary = notification.IsPrivate ? PrivateSummary : notification.Summary;
        var buttons = new List<NotificationToastButton>();
        NotificationToastTextBox? textBox = null;

        if (notification.Kind == NotificationKind.Alert)
        {
            buttons.Add(Button(notification.Id, "Acknowledge", AcknowledgeAction));
        }
        else if (notification.AnswerSpec is { } spec)
        {
            var choices = spec.Choices ?? [];
            var open = spec.Open;
            var room = MaxButtons - (open is null ? 0 : 1);
            // A toast button cannot carry the words a text-taking choice needs, so such a
            // question is answered in the centre.
            var choicesFit = !spec.AllowMultiple && choices.Count > 0 && choices.Count <= room
                && !choices.Any(choice => choice.TakesText);

            if (choicesFit)
            {
                buttons.AddRange(choices.Select(choice => new NotificationToastButton(
                    choice.Label,
                    Arguments(notification.Id, AnswerAction, choice.Id),
                    choice.Style,
                    UsesTextBox: false)));
            }

            if (open is not null)
            {
                textBox = new NotificationToastTextBox(TextBoxId, open.Placeholder ?? "Write an answer…");
                buttons.Add(new NotificationToastButton("Send", Arguments(notification.Id, AnswerAction), AnswerChoiceStyle.Primary, UsesTextBox: true));
            }

            if (!choicesFit && choices.Count > 0)
            {
                buttons.Add(Button(notification.Id, "Open in Backlog", OpenAction));
            }
        }

        return new NotificationToastContent(
            notification.Id,
            notification.Title,
            summary,
            notification.Severity,
            Arguments(notification.Id, OpenAction),
            buttons,
            textBox,
            notification.Origin?.Line);
    }

    /// <summary>
    /// What a press asks for, read from the arguments the pressed button — or the body — carried
    /// and the text the person typed. Null when the arguments name no notification. A Send with an
    /// empty box opens the notification rather than answering nothing.
    /// </summary>
    public static NotificationToastAction? Read(
        IReadOnlyDictionary<string, string> arguments,
        IReadOnlyDictionary<string, string> userInput)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(userInput);

        if (!arguments.TryGetValue(NotificationArgument, out var rawId)
            || !Guid.TryParse(rawId, CultureInfo.InvariantCulture, out var id))
            return null;

        arguments.TryGetValue(ActionArgument, out var action);

        switch (action)
        {
            case AcknowledgeAction:
                return new NotificationToastAction(id, NotificationToastActionKind.Acknowledge, [], null);

            case AnswerAction when arguments.TryGetValue(ChoiceArgument, out var choice) && !string.IsNullOrEmpty(choice):
                return new NotificationToastAction(id, NotificationToastActionKind.Answer, [choice], null);

            case AnswerAction when userInput.TryGetValue(TextBoxId, out var text) && !string.IsNullOrWhiteSpace(text):
                return new NotificationToastAction(id, NotificationToastActionKind.Answer, [], text.Trim());

            default:
                return new NotificationToastAction(id, NotificationToastActionKind.Open, [], null);
        }
    }

    private static NotificationToastButton Button(Guid id, string label, string action) =>
        new(label, Arguments(id, action), AnswerChoiceStyle.Default, UsesTextBox: false);

    private static IReadOnlyDictionary<string, string> Arguments(Guid id, string action, string? choice = null)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NotificationArgument] = id.ToString("D", CultureInfo.InvariantCulture),
            [ActionArgument] = action
        };

        if (choice is not null) arguments[ChoiceArgument] = choice;
        return arguments;
    }
}

/// <summary>One operating-system notification: its text, what a press on its body carries, its
/// buttons in order, the text box an open answer is typed in, and the attribution line that says
/// where it came from — "System · Place", or null for a notification that names no origin.</summary>
public sealed record NotificationToastContent(
    Guid NotificationId,
    string Title,
    string Summary,
    NotificationSeverity Severity,
    IReadOnlyDictionary<string, string> Arguments,
    IReadOnlyList<NotificationToastButton> Buttons,
    NotificationToastTextBox? TextBox,
    string? Attribution = null)
{
    /// <summary>The tag the platform keeps the notification under, so a settled one can be
    /// taken down again.</summary>
    public string Tag => NotificationToast.TagFor(NotificationId);
}

/// <summary>One action button: its label, the arguments a press carries, how it is drawn, and
/// whether it sends the text box's content.</summary>
public sealed record NotificationToastButton(
    string Label,
    IReadOnlyDictionary<string, string> Arguments,
    AnswerChoiceStyle Style,
    bool UsesTextBox);

/// <summary>The inline text box an open answer is typed in.</summary>
public sealed record NotificationToastTextBox(string Id, string Placeholder);

/// <summary>What a press in a notification asks for.</summary>
public enum NotificationToastActionKind
{
    /// <summary>Answer the question with <see cref="NotificationToastAction.ChoiceIds"/> or
    /// <see cref="NotificationToastAction.Text"/>, through the centre.</summary>
    Answer,

    /// <summary>Acknowledge the alert, through the centre.</summary>
    Acknowledge,

    /// <summary>Bring the app forward on the notification in the centre.</summary>
    Open
}

/// <summary>A press read back: which notification, what to do, and the answer it carries.</summary>
public sealed record NotificationToastAction(
    Guid NotificationId,
    NotificationToastActionKind Kind,
    IReadOnlyList<string> ChoiceIds,
    string? Text);
