namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// Capture pushed from a device into the sync layer.
/// <para>
/// Both fields are required and both are bounded, and the bounds live at the
/// edge that accepts one rather than here — see <c>InboxEndpoints</c>. A capture
/// becomes a task document, so an unbounded title is a document Cosmos refuses
/// with an error the person can do nothing about, and an empty one is a task
/// nobody can act on.
/// </para>
/// </summary>
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
