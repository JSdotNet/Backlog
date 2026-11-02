namespace Backlog.Modules.Sync.Abstractions;

/// <summary>
/// Where the sync surface lives. The service maps these and a client calls
/// them, so they are written down once rather than as two string literals that
/// agree until one of them is edited.
/// <para>
/// Each route is relative to <see cref="Base"/>, which is what
/// <c>MapGroup</c> wants; <see cref="Absolute"/> puts the two back together for
/// a client holding a bare <c>HttpClient</c>.
/// </para>
/// </summary>
public static class SyncRoutes
{
    /// <summary>The group every sync endpoint hangs off.</summary>
    public const string Base = "/api/sync";

    /// <summary>First device in: mints a new owner. Anonymous, because there is
    /// nothing yet to authenticate as.</summary>
    public const string RegisterDevice = "/devices/register";

    /// <summary>A paired device asks for a short code to read out. Bearer.</summary>
    public const string PairingCodes = "/devices/codes";

    /// <summary>Second device in: trades the short code for a registration
    /// credential under the same owner. Anonymous for the same reason
    /// <see cref="RegisterDevice"/> is — the code is the only thing it has.</summary>
    public const string RedeemPairingCode = "/devices/pair";

    /// <summary>Registration credential in, short-lived token out. Anonymous:
    /// the credential in the body is what authenticates the call.</summary>
    public const string DeviceToken = "/devices/token";

    /// <summary>Who the bearer of this token turned out to be. Bearer.</summary>
    public const string DeviceStatus = "/devices/me";

    /// <summary>Every device of the caller's owner, with the device that paired
    /// each one. Bearer.</summary>
    public const string Devices = "/devices";

    /// <summary>Route template for one device of the caller's owner: DELETE
    /// revokes it. Bearer. Use <see cref="DeviceFor"/> to build a concrete URL.</summary>
    public const string Device = "/devices/{id:guid}";

    /// <summary>The full path for one device.</summary>
    public static string DeviceFor(Guid id) => $"{Base}/devices/{id:D}";

    /// <summary>The captures waiting for this owner. Bearer.</summary>
    public const string Inbox = "/inbox";

    /// <summary>The task replica: POST pushes a batch, GET pulls the owner's
    /// change feed from a cursor. Bearer, and one route for both halves because
    /// they are the two directions of one exchange over one collection.</summary>
    public const string Tasks = "/tasks";

    /// <summary>The session replica: POST appends a batch of this machine's
    /// records, GET pulls the owner's session feed from a cursor. Bearer, and
    /// one route for both halves because they are the two directions of one
    /// exchange over one collection — the same shape as <see cref="Tasks"/>,
    /// over the second container .devbook/arc42/adr/0005 §Storage declares.</summary>
    public const string Sessions = "/sessions";

    /// <summary>The annotation replica: POST pushes a batch of this device's
    /// remarks on Devbook chapters, GET pulls the owner's annotation feed from a
    /// cursor. Bearer, and the same two-halves-one-route shape as
    /// <see cref="Tasks"/>, over the third replica container.</summary>
    public const string Annotations = "/annotations";

    /// <summary>The devbook subject state replica: POST pushes a batch of this
    /// device's subject states — a change proposal's, a decision record's, a debt
    /// record's — and GET pulls the owner's feed from a cursor. Bearer, and the
    /// shape of <see cref="Annotations"/>, over its own container. A service older
    /// than the client has no such route and answers 404, which the desktop reads
    /// as "not yet" rather than as a failure.</summary>
    public const string DevbookSubjects = "/devbook-subjects";

    /// <summary>The notification replica (local ADR 0039): POST pushes a batch of the
    /// calling owner instance's notifications, GET pulls the owner's notification feed from
    /// a cursor. Bearer.</summary>
    public const string Notifications = "/notifications";

    /// <summary>The replies to notifications: POST stores one reply from any device of the
    /// owner, GET lists the replies addressed to the calling owner instance. Bearer.</summary>
    public const string NotificationReplies = "/notifications/replies";

    /// <summary>Route template for one reply: DELETE removes it once its owner instance has
    /// processed it. Use <see cref="NotificationReplyFor"/> to build a concrete URL.</summary>
    public const string NotificationReply = "/notifications/replies/{id:guid}";

    /// <summary>The realtime nudge (local ADR 0039 §4): GET answers a client access URL for
    /// the Web PubSub hub that joins the caller to its owner's group, or none where the service
    /// runs without one. Bearer.</summary>
    public const string NotificationNudge = "/notifications/negotiate";

    /// <summary>The Android push (local ADR 0039 §4): PUT hands the service the calling phone's
    /// FCM token, and the service keeps one Notification Hubs installation for the device,
    /// tagged with its owner and itself from the token. Answers whether it registered — never,
    /// where the service runs without a hub. Bearer.</summary>
    public const string NotificationPush = "/notifications/push";

    /// <summary>The full path for one reply.</summary>
    public static string NotificationReplyFor(Guid id) => $"{Base}/notifications/replies/{id:D}";

    /// <summary>Route template for acknowledging one capture. Bearer. Use
    /// <see cref="AcknowledgeInboxItemFor"/> to build a concrete URL.</summary>
    public const string AcknowledgeInboxItem = "/inbox/{id:guid}/ack";

    /// <summary>Route template for one attachment: PUT uploads its bytes, GET
    /// streams them back. Bearer, and owner-scoped by the token — the route
    /// carries only the client-minted id (local ADR 0014). Use
    /// <see cref="AttachmentFor"/> to build a concrete URL.</summary>
    public const string Attachment = "/attachments/{id:guid}";

    /// <summary>The header an upload declares its bytes' SHA-256 in, as hex, and
    /// a download reports the stored digest in. The service hashes while it
    /// streams and refuses an upload whose bytes do not match it.</summary>
    public const string AttachmentSha256Header = "X-Attachment-Sha256";

    /// <summary>The full path for one attachment.</summary>
    public static string AttachmentFor(Guid id) => $"{Base}/attachments/{id:D}";

    /// <summary>The full path a client posts to.</summary>
    public static string Absolute(string route) => Base + route;

    /// <summary>The full path for acknowledging one capture.</summary>
    public static string AcknowledgeInboxItemFor(Guid id) => $"{Base}/inbox/{id:D}/ack";
}
