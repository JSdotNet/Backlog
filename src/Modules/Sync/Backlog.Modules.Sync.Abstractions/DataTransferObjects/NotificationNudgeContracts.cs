namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// What the negotiate endpoint answers (local ADR 0038 §4): the client access URL a desktop
/// opens its WebSocket on, or <see langword="null"/> where the service runs without a Web
/// PubSub hub — under Aspire and in every endpoint test — and the desktop keeps polling.
/// <para>
/// The URL carries a short-lived token the service minted for this device. It joins the
/// connection to its owner's group and grants nothing else: the desktop can only listen, never
/// send or join another group.
/// </para>
/// </summary>
/// <param name="Url">The <c>wss://</c> URL to connect to, or null when there is no hub.</param>
public sealed record NotificationNudgeNegotiateResponse(Uri? Url);

/// <summary>
/// The one message the hub ever carries: "pull now", with the kind of change that caused it
/// and nothing of the change itself — no id, no title, no owner. The desktop runs its
/// notification exchange on any of them; the kind is there for the log.
/// </summary>
/// <param name="Type">Always <see cref="NotificationNudgeMessage.Pull"/>.</param>
/// <param name="Kind">One of <see cref="NotificationNudgeKinds"/>.</param>
public sealed record NotificationNudgeMessage(string Type, string Kind)
{
    /// <summary>The only <see cref="Type"/> there is.</summary>
    public const string Pull = "pull";
}

/// <summary>What landed on the service to cause a nudge.</summary>
public static class NotificationNudgeKinds
{
    /// <summary>An owner instance pushed notifications — new ones or a state change.</summary>
    public const string Notification = "notification";

    /// <summary>A device stored a reply to one of the owner's questions.</summary>
    public const string Reply = "reply";
}
