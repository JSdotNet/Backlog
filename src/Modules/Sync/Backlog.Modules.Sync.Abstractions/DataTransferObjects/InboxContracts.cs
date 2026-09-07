namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>Capture pushed from a device into the sync layer.</summary>
public sealed record CaptureRequest(string Title, string Source);

/// <summary>
/// An unsynced capture awaiting pickup by the desktop.
/// <para>
/// The record carries no owner id, and that is deliberate: the owner is never
/// something a caller states, it is something the service reads out of the
/// caller's token. See <see cref="SyncClaims"/>.
/// </para>
/// </summary>
public sealed record InboxItem(Guid Id, string Title, string Source, DateTimeOffset CapturedAt);
