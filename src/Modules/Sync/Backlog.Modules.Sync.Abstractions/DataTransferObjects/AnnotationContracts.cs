namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// One remark on a Devbook chapter as it crosses the wire — what the desktop's
/// annotation store holds, field for field, and nothing the replica has to
/// understand.
/// <para>
/// The chapter is named by repository alias and repository-relative path, never
/// by a folder on disk: a path describes one machine's clone and would name a
/// different file, or none, on the machine that reads it back. The alias is the
/// same one session records travel under (.arc42/adr/0005 §Session records),
/// so the two devices agree on which repository a remark belongs to for the
/// same reason they agree about a session's.
/// </para>
/// <para>
/// <see cref="BlockIndex"/> is the anchor the read view draws the remark
/// against — which block of the rendered chapter, from zero — and it means the
/// same thing on both devices only while both read the same version of the
/// chapter. That is a property of the repository, not of this service, and the
/// replica does not try to re-anchor: a remark whose block has moved is shown
/// at the end of the chapter rather than dropped, which is what the read view
/// already does for an index it cannot place.
/// </para>
/// <para>
/// There is no owner and no device id here, for the reason
/// <see cref="TaskChange"/> gives: both come from the caller's validated token,
/// so no batch a client composes can write into somebody else's data.
/// </para>
/// </summary>
/// <param name="RepositoryAlias">Which repository the chapter is in, as its
/// registry alias.</param>
/// <param name="ChapterPath">The chapter, repository-relative and forward-slashed
/// — <c>.domain/devbook/features.md</c>.</param>
/// <param name="BlockIndex">Which rendered block the remark hangs off.</param>
/// <param name="Body">What was said. Markdown, and possibly empty while a fresh
/// remark is still being typed — the desktop does not push an empty one.</param>
/// <param name="Author">Who said it, as the writing machine's own label. A
/// display name, never an identity: the device id is that, and the service
/// stamps it.</param>
/// <param name="CreatedAt">When the remark was first made, on the device that
/// made it.</param>
/// <param name="Resolved">Whether it has been dealt with. A resolved remark
/// stays visible and quiet rather than disappearing.</param>
public sealed record AnnotationPayload(
    string RepositoryAlias,
    string ChapterPath,
    int BlockIndex,
    string Body,
    string Author,
    DateTimeOffset CreatedAt,
    bool Resolved);

/// <summary>
/// One remark's state at one moment, as a device pushes it or pulls it. The
/// same shape as <see cref="TaskChange"/> and under the same rule: whole
/// document last-write-wins, and a deletion travels as a tombstone with
/// <see cref="DeletedAt"/> set rather than as an absence a device that has been
/// offline could not tell from "never seen".
/// </summary>
public sealed record AnnotationChange(Guid Id, DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt, AnnotationPayload Annotation);

/// <summary>
/// A change as it comes back out of the replica: the change itself, the device
/// that wrote it, and the store's own ordering stamp — see
/// <see cref="TaskChangeRecord"/> for why both are on the way out and neither
/// on the way in.
/// </summary>
public sealed record AnnotationChangeRecord(AnnotationChange Change, Guid DeviceId, long ServerTimestamp);

/// <summary>A batch of changes from one device.</summary>
public sealed record PushAnnotationsRequest(IReadOnlyList<AnnotationChange> Annotations);

/// <summary>How many changes the replica took. No per-item result, because a
/// whole-document upsert has nothing to reject.</summary>
public sealed record PushAnnotationsResponse(int Accepted);

/// <summary>One page of the owner's annotation feed, and where to resume. The
/// contract is <see cref="PullTasksResponse"/>'s: <paramref name="Since"/> is
/// always fresh, and only <paramref name="HasMore"/> being false ends a
/// client's loop.</summary>
public sealed record PullAnnotationsResponse(IReadOnlyList<AnnotationChangeRecord> Annotations, string Since, bool HasMore);
