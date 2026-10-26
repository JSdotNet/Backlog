namespace Backlog.Modules.Sync.DomainModels;

/// <summary>
/// One Android push for one notification (local ADR 0039 §4): the FCM data map, every value a
/// string, keyed by <c>NotificationPushData</c>. Built by <c>NotificationPushMessages.For</c>
/// and handed to the push channel, which wraps it in whatever its service wants.
/// </summary>
/// <param name="NotificationId">The notification it is about.</param>
/// <param name="Data">The data map. Never the body.</param>
public sealed record NotificationPushMessage(Guid NotificationId, IReadOnlyDictionary<string, string> Data);
