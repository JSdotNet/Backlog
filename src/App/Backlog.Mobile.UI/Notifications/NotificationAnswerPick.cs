namespace Backlog.Mobile.UI.Notifications;

/// <summary>What a person answered on a notification card: the picked choice ids, or the
/// typed text.</summary>
public sealed record NotificationAnswerPick(IReadOnlyList<string> ChoiceIds, string? Text);
