namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// A capture as a channel hands it to the module — the sync client with a
/// replica document, or a source monitor with a feed entry — reduced to what
/// the Inbox needs to know about it.
/// <para>
/// <paramref name="Id"/> is the capture's own id, and the item the module
/// creates reuses it. That is what makes intake idempotent by primary key: a
/// replayed page, the desktop's own echo and a feed read twice all arrive as an
/// id the store already holds and cost nothing.
/// </para>
/// <para>
/// <paramref name="WithdrawnAt"/> is the replica tombstone stamp. A capture the
/// phone dismissed — or that this desktop acknowledged and is now hearing its
/// own tombstone for — arrives with it set, and the intake decides by id and
/// status whether that means anything here.
/// </para>
/// <para>
/// <paramref name="SourceUrl"/> and <paramref name="BodyMd"/> are the
/// <c>source_url</c> and <c>body_md</c> of <c>.devbook/domain/capture/domain.md#itemcaptured</c>.
/// A channel that knows the link — a feed entry has one — says so here rather
/// than hiding it in the title for the intake to find again; one that does not
/// leaves both null and the intake reads the title as it always has. Last and
/// defaulted so the sync client's call, which knows neither, is unchanged.
/// </para>
/// <para>
/// <paramref name="ReplicaBacked"/> says whether the replica holds a document
/// under this id — true for a pulled capture, and what the sync client's call
/// gets by default. A source monitor hands over a capture the replica has
/// never seen and says false, so that routing or archiving the item does not
/// push a tombstone for a document that was never there.
/// </para>
/// <para>
/// <paramref name="Tags"/> and <paramref name="Person"/> are what the channel
/// said about the capture beyond its text: tag names, with or without the
/// <c>#</c>, and the person as <c>@name</c> or bare. The intake stores the tags
/// bare and de-duplicated and the person as the item's source person — never
/// among the tags. Defaulted like the two before them, so a channel that knows
/// neither says nothing.
/// </para>
/// </summary>
public sealed record InboxCaptureDto(
    Guid Id,
    string Title,
    string Channel,
    DateTimeOffset CapturedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? WithdrawnAt,
    string? SourceUrl = null,
    string? BodyMd = null,
    bool ReplicaBacked = true,
    IReadOnlyList<string>? Tags = null,
    string? Person = null);
