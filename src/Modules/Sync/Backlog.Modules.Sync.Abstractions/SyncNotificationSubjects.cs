namespace Backlog.Modules.Sync.Abstractions;

/// <summary>
/// How the Sync context names a pairing a notification can be about, and the reason it
/// closes with when the pairing ends (<c>.devbook/domain/notifications/domain.md#subject</c>).
/// <para>
/// A pairing is one pairing code this desktop issued, named by an id the desktop gives it
/// when the code is issued — never by the code, which is a secret for its ten minutes.
/// Plain strings, because this project may not see the Notifications context's
/// abstractions (<c>ModuleBoundaryTests</c>); <c>PairingSubjects</c> in
/// <c>Backlog.Infrastructure.Sync</c> builds the subject from these.
/// </para>
/// </summary>
public static class SyncNotificationSubjects
{
    /// <summary>The subject context a pairing is named under.</summary>
    public const string Context = "sync";

    /// <summary>A pairing, by the id its code was given on issue, in the <c>D</c> format.</summary>
    public const string Pairing = "pairing";

    /// <summary>Why a notification about a pairing closes: its code was redeemed or expired.</summary>
    public const string EndedReason = "Pairing finished";

    /// <summary>A pairing's subject id.</summary>
    public static string Id(Guid pairingId) => pairingId.ToString("D");
}
