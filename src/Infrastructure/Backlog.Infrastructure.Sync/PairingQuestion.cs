using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The question the Sync context asks on the desktop whose pairing code a device redeemed:
/// "Pair &lt;device name&gt;?", Allow or Refuse, answered on that desktop only
/// (<c>answerOn: owner</c>), about the pairing. Built in one place so the draft
/// <see cref="PairingSubjects"/> publishes and the answer <see cref="PairingAnswers"/> reads
/// agree on every name.
/// </summary>
internal static class PairingQuestion
{
    private const int MaxTitleLength = 80;

    internal static NotificationSubject Subject(Guid pairingId) =>
        new(SyncNotificationSubjects.Context, SyncNotificationSubjects.Pairing, SyncNotificationSubjects.Id(pairingId));

    internal static NotificationDraft Draft(Guid pairingId, Guid deviceId, string deviceName)
    {
        var name = OneLine(deviceName);
        if (name.Length == 0) name = "a new device";

        return new NotificationDraft(
            new NotificationSource(SyncNotificationSubjects.Context, SyncNotificationSubjects.SourceKey(pairingId, deviceId)),
            Subject(pairingId),
            NotificationKind.Question,
            NotificationSeverity.Warning,
            Title(name),
            "A device redeemed the pairing code shown on this desktop. Allow keeps it; Refuse revokes it.",
            $"""
            **{name}** redeemed the pairing code this desktop showed, and is now paired with your account.

            - **Allow** keeps it paired.
            - **Refuse** revokes it: its credential stops working, and it can no longer sync.

            Only this desktop can answer. If you did not pair a device just now, refuse.
            """,
            new AnswerSpec(
                [
                    new AnswerChoice(SyncNotificationSubjects.AllowChoice, "Allow", AnswerChoiceStyle.Primary),
                    new AnswerChoice(SyncNotificationSubjects.RefuseChoice, "Refuse", AnswerChoiceStyle.Destructive)
                ],
                AnswerOn: AnswerOn.Owner),
            Origin: NotificationOrigin.Fit(NotificationOrigins.Backlog, "Pairing"));
    }

    /// <summary>"Pair &lt;name&gt;?", the name shortened to fit the title's 80 characters.</summary>
    private static string Title(string name)
    {
        const int Frame = 6; // "Pair " and "?"
        var room = MaxTitleLength - Frame;

        return $"Pair {(name.Length <= room ? name : name[..(room - 1)] + "…")}?";
    }

    private static string OneLine(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
