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

    /// <summary>
    /// The alert raised when the person answered Refuse but did not confirm it with Windows
    /// Hello (<see cref="PairingAnswers"/>): the device is still paired, and the way to refuse it
    /// again is Revoke on its row in Settings → Devices. About no subject, so the pairing ending
    /// does not close it; it stays until it is acknowledged.
    /// <para>
    /// Worded by <paramref name="code"/>, because each asks something different of the person:
    /// cancelled, they refuse again; expired, a prompt still on screen no longer does anything;
    /// unavailable, Revoke asks for Windows Hello too, so it has to be set up first.
    /// </para>
    /// </summary>
    /// <param name="pairingId">The pairing the refusal answered.</param>
    /// <param name="deviceId">The device that stays paired.</param>
    /// <param name="deviceName">Its name, or null when nothing here knows it.</param>
    /// <param name="code">The step-up's code: <see cref="DeviceStepUp.DeclinedCode"/>,
    /// <see cref="DeviceStepUp.ExpiredCode"/> or <see cref="DeviceStepUp.UnavailableCode"/>. Any
    /// other is worded as cancelled.</param>
    /// <param name="reason">The step-up's own sentence: why nothing was sent.</param>
    internal static NotificationDraft NotRefused(Guid pairingId, Guid deviceId, string? deviceName, string code, string reason)
    {
        var name = OneLine(deviceName);
        var named = name.Length > 0;
        if (!named) name = "A device";

        const int Frame = 16; // " was not refused"
        var title = $"{NotificationText.Cut(name, MaxTitleLength - Frame)} was not refused";
        var who = named ? $"**{name}**" : "The device that redeemed this desktop's pairing code";

        return new NotificationDraft(
            new NotificationSource(SyncNotificationSubjects.Context, SyncNotificationSubjects.SourceKey(pairingId, deviceId)),
            Subject: null,
            NotificationKind.Alert,
            NotificationSeverity.Warning,
            title,
            NotRefusedSummary(code),
            $"""
            You answered **Refuse**, and nothing was revoked. {OneLine(reason)}

            {who} is still paired with your account and can sync your backlog.

            {NotRefusedNextStep(code)}
            """,
            Origin: NotificationOrigin.Fit(NotificationOrigins.Backlog, "Pairing"));
    }

    private const string RevokeOnItsRow =
        "open **Settings → Devices** on this desktop and choose **Revoke** on its row. Windows Hello asks you to confirm.";

    private static string NotRefusedSummary(string code) => code switch
    {
        DeviceStepUp.UnavailableCode =>
            "Windows Hello is not set up on this PC, so it is still paired. Set up Windows Hello, then Revoke it in Settings → Devices.",
        DeviceStepUp.ExpiredCode =>
            "Windows Hello was not confirmed in time, so it is still paired. Refuse it again: Revoke it in Settings → Devices.",
        _ =>
            "Windows Hello was cancelled, so it is still paired. Refuse it again: Revoke it in Settings → Devices.",
    };

    private static string NotRefusedNextStep(string code) => code switch
    {
        DeviceStepUp.UnavailableCode =>
            "Revoke asks for Windows Hello too, so it fails the same way until Windows Hello is set up. Set it up first, in Windows Settings → Accounts → Sign-in options; then "
            + RevokeOnItsRow,
        DeviceStepUp.ExpiredCode =>
            "A Windows Hello prompt still on screen from this refusal has no effect now: confirming it revokes nothing, so close it. To refuse the device, "
            + RevokeOnItsRow,
        _ => "To refuse it again, " + RevokeOnItsRow,
    };

    /// <summary>"Pair &lt;name&gt;?", the name shortened to fit the title's 80 characters.</summary>
    private static string Title(string name)
    {
        const int Frame = 6; // "Pair " and "?"
        var room = MaxTitleLength - Frame;

        return $"Pair {NotificationText.Cut(name, room)}?";
    }

    private static string OneLine(string? text) => NotificationText.OneLine(text);
}
