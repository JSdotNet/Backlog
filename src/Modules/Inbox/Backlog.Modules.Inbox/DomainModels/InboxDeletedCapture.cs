namespace Backlog.Modules.Inbox.DomainModels;

/// <summary>
/// What is left of a deleted replica-backed item while the phone has not yet
/// heard: enough to rebuild the capture's tombstone, and nothing the item was
/// decided into. Forgotten once the outbox drains it.
/// </summary>
public sealed record InboxDeletedCapture(
    Guid Id,
    string Title,
    DateTimeOffset CapturedAt,
    DateTimeOffset DeletedAt,
    string? Kind = null);
