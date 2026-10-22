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

    /// <summary>Why a notification about a pairing closes: its code expired unredeemed, or
    /// the question about the device that redeemed it was settled.</summary>
    public const string EndedReason = "Pairing finished";

    /// <summary>The choice that keeps the device that redeemed a pairing code.</summary>
    public const string AllowChoice = "allow";

    /// <summary>The choice that revokes the device that redeemed a pairing code.</summary>
    public const string RefuseChoice = "refuse";

    /// <summary>A pairing's subject id.</summary>
    public static string Id(Guid pairingId) => pairingId.ToString("D");

    /// <summary>
    /// The source key of the question asked about the device that redeemed a pairing:
    /// the pairing and the device, so the answer can be acted on after a restart, when
    /// nothing in memory remembers either.
    /// </summary>
    public static string SourceKey(Guid pairingId, Guid deviceId) => $"{pairingId:D}/{deviceId:D}";

    /// <summary>The pairing and device a <see cref="SourceKey"/> names, or null for a key
    /// that is not one.</summary>
    public static (Guid PairingId, Guid DeviceId)? ParseSourceKey(string? key)
    {
        var slash = key?.IndexOf('/', StringComparison.Ordinal) ?? -1;
        if (slash <= 0) return null;

        return Guid.TryParse(key![..slash], out var pairing) && Guid.TryParse(key[(slash + 1)..], out var device)
            ? (pairing, device)
            : null;
    }
}
