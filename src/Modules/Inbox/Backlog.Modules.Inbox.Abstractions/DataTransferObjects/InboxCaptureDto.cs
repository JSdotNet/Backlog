namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// A capture as the sync client hands it to the module: the replica document
/// reduced to what the Inbox needs to know about it.
/// <para>
/// <paramref name="Id"/> is the replica capture's own id, and the item the
/// module creates reuses it. That is what makes intake idempotent by primary
/// key: a replayed page and the desktop's own echo both arrive as an id the
/// store already holds and cost nothing.
/// </para>
/// <para>
/// <paramref name="WithdrawnAt"/> is the replica tombstone stamp. A capture the
/// phone dismissed — or that this desktop acknowledged and is now hearing its
/// own tombstone for — arrives with it set, and the intake decides by id and
/// status whether that means anything here.
/// </para>
/// </summary>
public sealed record InboxCaptureDto(
    Guid Id,
    string Title,
    string Channel,
    DateTimeOffset CapturedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? WithdrawnAt);
