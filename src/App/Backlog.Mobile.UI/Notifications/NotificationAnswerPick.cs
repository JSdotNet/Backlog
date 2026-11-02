namespace Backlog.Mobile.UI.Notifications;

/// <summary>What a person answered on a notification card: the picked choice ids, or the
/// typed text, and the note written beside either where the question takes one.</summary>
public sealed record NotificationAnswerPick(IReadOnlyList<string> ChoiceIds, string? Text, string? Note = null);
