namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// What an owner instance lets the Android push say about a notification (local ADR 0038 §4):
/// the four fields a phone's tray shows before it has pulled anything. The owner writes it on
/// the <see cref="NotificationChange"/> envelope beside the opaque document, so the service
/// reads its own envelope and never the document.
/// <para>
/// No body, ever, and nothing a body could be read back from. A private notification's summary
/// is left out by the owner, and the service replaces it with
/// <see cref="NotificationPushData.PrivateSummary"/> whatever the owner sent.
/// </para>
/// </summary>
/// <param name="Kind">One of <see cref="NotificationKinds"/>.</param>
/// <param name="Severity">One of <see cref="NotificationSeverities"/>.</param>
/// <param name="Title">The title, at most <see cref="NotificationRecordLimits.PreviewTitleLength"/> characters.</param>
/// <param name="Summary">The summary, at most <see cref="NotificationRecordLimits.PreviewSummaryLength"/>
/// characters, or null for a private notification.</param>
/// <param name="IsPrivate">Whether the source marked it private.</param>
public sealed record NotificationPushPreview(
    string Kind,
    string Severity,
    string Title,
    string? Summary,
    bool IsPrivate);

/// <summary>The kinds a <see cref="NotificationPushPreview"/> may name, as the Notifications
/// context writes them on the wire.</summary>
public static class NotificationKinds
{
    public const string Alert = "alert";
    public const string Question = "question";

    /// <summary>Every kind a preview may carry.</summary>
    public static IReadOnlyList<string> All { get; } = [Alert, Question];

    /// <summary>Whether <paramref name="kind"/> is one of <see cref="All"/>, compared ordinally.</summary>
    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind, StringComparer.Ordinal);
}

/// <summary>The severities a <see cref="NotificationPushPreview"/> may name.</summary>
public static class NotificationSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";

    /// <summary>Every severity a preview may carry.</summary>
    public static IReadOnlyList<string> All { get; } = [Info, Warning, Critical];

    /// <summary>Whether <paramref name="severity"/> is one of <see cref="All"/>, compared ordinally.</summary>
    public static bool IsKnown(string? severity) => severity is not null && All.Contains(severity, StringComparer.Ordinal);
}

/// <summary>
/// The keys of the FCM data message a phone receives (local ADR 0038 §4). Every value is a
/// string, because FCM v1 delivers only string values in <c>data</c>. Shared by the service,
/// which writes them, and the phone, which reads them.
/// </summary>
public static class NotificationPushData
{
    /// <summary>The notification's id, as a <c>D</c>-format GUID.</summary>
    public const string Id = "id";

    /// <summary>One of <see cref="NotificationKinds"/>; absent when the owner sent no preview.</summary>
    public const string Kind = "kind";

    /// <summary>One of <see cref="NotificationSeverities"/>; absent when the owner sent no preview.</summary>
    public const string Severity = "severity";

    /// <summary>One of <see cref="NotificationStates"/>. Anything but open takes the entry out of
    /// the tray.</summary>
    public const string State = "state";

    /// <summary>The title; absent when the owner sent no preview.</summary>
    public const string Title = "title";

    /// <summary>The summary, or <see cref="PrivateSummary"/>; absent when the owner sent no preview.</summary>
    public const string Summary = "summary";

    /// <summary>What a private notification's summary reads as outside the app
    /// (<c>.devbook/domain/notifications/domain.md</c>).</summary>
    public const string PrivateSummary = "Backlog has a question for you";
}

/// <summary>
/// A phone hands the service its FCM registration token, so the service can keep the phone's
/// Notification Hubs installation (local ADR 0038 §4). Only the token: the owner and the device
/// the installation is tagged with come from the caller's device token.
/// </summary>
/// <param name="FcmToken">The token Firebase Messaging issued this app install.</param>
public sealed record RegisterPushInstallationRequest(string? FcmToken);

/// <summary>Whether the service now holds an installation for the calling device. False where
/// the service runs without a hub — under Aspire and in the endpoint tests — which the phone
/// takes as "nothing to retry", like the nudge negotiate's null URL.</summary>
public sealed record RegisterPushInstallationResponse(bool Registered);

/// <summary>The bounds a push registration is held to on the wire.</summary>
public static class NotificationPushLimits
{
    /// <summary>The longest FCM token the service takes. Tokens run to a few hundred
    /// characters; this only stops a body that is not one.</summary>
    public const int FcmTokenLength = 4_096;

    /// <summary>Why <paramref name="request"/> would be refused, or null when it would be
    /// registered.</summary>
    public static string? WhyRefused(RegisterPushInstallationRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.FcmToken)) return "A push registration needs an FCM token.";
        if (request.FcmToken.Length > FcmTokenLength) return $"An FCM token holds at most {FcmTokenLength} characters.";
        if (request.FcmToken.Any(char.IsWhiteSpace)) return "An FCM token holds no whitespace.";

        return null;
    }
}
