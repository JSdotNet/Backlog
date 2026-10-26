namespace Backlog.UI.Components.Notifications;

/// <summary>
/// What a <see cref="NotificationCard"/> draws: one notification, already in words. The library
/// holds no clock and no device list, so <see cref="When"/> and <see cref="Outcome"/>'s device
/// arrive formatted; and it knows no module, so the host maps its own notification onto this.
/// </summary>
/// <param name="Id">The host's id for it; carried back on nothing, used for test ids.</param>
/// <param name="Kind">Alert or question.</param>
/// <param name="Severity">Info, warning or critical — a tone, never a limit on who answers.</param>
/// <param name="Title">One line of plain text.</param>
/// <param name="Summary">Plain text under the title, or empty.</param>
/// <param name="BodyMarkdown">The Markdown body, Mermaid fences included.</param>
/// <param name="Source">Who raised it, in words — "Sessions", "MCP".</param>
/// <param name="When">When it was raised, already formatted — "2 min ago".</param>
/// <param name="Answer">The answers a question takes; null on an alert.</param>
/// <param name="State">Where it stands.</param>
/// <param name="Outcome">How it was settled, or null while it is open.</param>
/// <param name="Origin">Where it came from, drawn as a line above the title; null draws the card
/// as it always was, <see cref="Source"/> beside the time.</param>
public sealed record NotificationCardModel(
    string Id,
    NotificationCardKind Kind,
    NotificationCardSeverity Severity,
    string Title,
    string Summary,
    string BodyMarkdown,
    string Source,
    string When,
    NotificationCardAnswerSpec? Answer,
    NotificationCardState State,
    NotificationCardOutcome? Outcome = null,
    NotificationCardOrigin? Origin = null)
{
    public bool IsOpen => State == NotificationCardState.Open;
}

/// <summary>
/// Where a notification came from, already in words: the system as a badge and the place beside
/// it above the title, who asked and how under the title, and "Open in &lt;System&gt; ↗" when there
/// is a link that opens it there.
/// </summary>
/// <param name="System">The system that raised it — "Claude Code", "spec-manager".</param>
/// <param name="Place">Where in that system, or null.</param>
/// <param name="AskedBy">The person who asked, or null.</param>
/// <param name="Via">How it was asked — "Agent session", "MCP ask" — or null.</param>
/// <param name="OpenLink">An absolute http or https link that opens it at its origin, or null.</param>
public sealed record NotificationCardOrigin(
    string System,
    string? Place = null,
    string? AskedBy = null,
    string? Via = null,
    string? OpenLink = null);

public enum NotificationCardKind
{
    Alert,
    Question
}

public enum NotificationCardSeverity
{
    Info,
    Warning,
    Critical
}

public enum NotificationCardState
{
    Open,
    Acknowledged,
    Answered,
    Closed
}

/// <summary>How a set choice is drawn.</summary>
public enum NotificationCardChoiceStyle
{
    Default,
    Primary,
    Destructive
}

/// <summary>One set answer: the id the host gets back, the label a person reads, its style.</summary>
public sealed record NotificationCardChoice(string Id, string Label, NotificationCardChoiceStyle Style = NotificationCardChoiceStyle.Default);

/// <summary>
/// The answers a question takes, in the four shapes a question has: single choice (buttons),
/// multiple choice (<see cref="AllowMultiple"/>, checkboxes within <see cref="MinChoices"/> and
/// <see cref="MaxChoices"/>), open (no choices, <see cref="Open"/>: a plain-text box), or choices
/// with an open answer.
/// </summary>
public sealed record NotificationCardAnswerSpec(
    IReadOnlyList<NotificationCardChoice> Choices,
    bool AllowMultiple = false,
    NotificationCardOpenAnswer? Open = null,
    int? MinChoices = null,
    int? MaxChoices = null);

/// <summary>A plain-text answer: the placeholder its box shows and the most characters it takes.</summary>
public sealed record NotificationCardOpenAnswer(string? Placeholder = null, int MaxLength = 2000);

/// <summary>
/// How a notification was settled: the answer that won and the device it came from, or why it was
/// closed. Every part is already in words.
/// </summary>
/// <param name="Answer">The winning answer as a person reads it — the chosen labels, or the text.</param>
/// <param name="Device">The device the answer came from — "this desktop", "phone".</param>
/// <param name="Reason">Why it was closed.</param>
/// <param name="When">When it settled, already formatted.</param>
public sealed record NotificationCardOutcome(string? Answer = null, string? Device = null, string? Reason = null, string? When = null);

/// <summary>What a person answered in the card: the picked choice ids, the typed text, or both
/// where the question takes both.</summary>
public sealed record NotificationCardReply(IReadOnlyList<string> ChoiceIds, string? Text);
