namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// Where Backlog keeps the workflow state of a repository's devbook subjects —
/// its change proposals, decision records and debt records — now that the state
/// has left the chapters' <c>meta</c> blocks (devbook's branch-true decision).
/// <para>
/// A port beside <see cref="IDevbookAnnotationStore"/> and shaped like it: the
/// panels, the MCP tools and replication see one store, everything is synchronous
/// and in memory once loaded, and the file adapter the desktop composes is one of
/// several. Keyed by repository alias and <see cref="DevbookSubject"/>, so a state
/// set on one device names the same subject on every other.
/// </para>
/// <para>
/// Two kinds of write, stamped differently, as remarks are. The three operations
/// (<see cref="SetState"/>, <see cref="Approve"/>, <see cref="Accept"/>) apply
/// <see cref="DevbookSubjectRules"/> and stamp from the store's own clock, which is
/// what puts the change in front of the push watermark; a refusal writes nothing
/// and raises nothing. <see cref="Apply"/> writes a document exactly as it arrived
/// from another device, stamps included, when it is later than the one held.
/// </para>
/// <para>
/// Nothing here is a Markdown write, so nothing here asks whether a folder can be
/// edited: a subject on a branch snapshot takes a state as readily as one in a
/// clone. Whether the repository is archived is the caller's to refuse, as it is
/// for a remark (local ADR 0029 §4).
/// </para>
/// </summary>
public interface IDevbookSubjectStateStore
{
    /// <summary>Raised after any write, on whichever thread wrote. Never raised
    /// for a refusal.</summary>
    event Action? Changed;

    /// <summary>One subject's state, or null when the store keeps none — which
    /// for a proposal means it has not been set to <c>draft</c> or approved yet.
    /// An unscoped subject — no alias — is answered under the empty alias.</summary>
    DevbookSubjectState? Get(string? repositoryAlias, DevbookSubject subject);

    /// <summary>Every subject state kept for one repository.</summary>
    IReadOnlyList<DevbookSubjectState> List(string? repositoryAlias);

    /// <summary>One state document by id, or null. Whether an arriving version
    /// is newer is <see cref="Apply"/>'s to decide, not a caller's on what this
    /// answered.</summary>
    DevbookSubjectState? Find(Guid id);

    /// <summary>Sets a subject's state, per
    /// <see cref="DevbookSubjectRules.SetState"/>.</summary>
    /// <param name="by">Who is setting it — the host's author, never a
    /// caller's.</param>
    DevbookSubjectOutcome SetState(string? repositoryAlias, DevbookSubject subject, string state, string by);

    /// <summary>Approves a proposal at a fingerprint, per
    /// <see cref="DevbookSubjectRules.Approve"/>.</summary>
    DevbookSubjectOutcome Approve(string? repositoryAlias, DevbookSubject subject, string fingerprint, string by);

    /// <summary>Accepts a proposal at a fingerprint, per
    /// <see cref="DevbookSubjectRules.Accept"/>.</summary>
    DevbookSubjectOutcome Accept(string? repositoryAlias, DevbookSubject subject, string fingerprint, string by);

    /// <summary>
    /// Writes a document as replication hands it over, stamps and all, when it
    /// supersedes the copy held (<see cref="DevbookSubjectRules.Supersedes"/>), and
    /// answers whether it wrote. Nothing is written or raised otherwise.
    /// <para>
    /// The check is the store's, made under the same lock as the write, rather
    /// than the merge's on a copy it read first: between that read and the write
    /// a person can set a state here, and a write that did not look again would
    /// put the older copy over it — lost on this device and, since the store then
    /// holds the older stamp, never pushed either.
    /// </para>
    /// </summary>
    bool Apply(DevbookSubjectState replicated);

    /// <summary>Every state this machine holds that changed strictly after
    /// <paramref name="watermark"/>, ordered by <c>UpdatedAt</c> — what a push
    /// selects. Across every repository, because the watermark is the
    /// device's.</summary>
    IReadOnlyList<DevbookSubjectState> ListChangedSince(DateTimeOffset watermark);
}
