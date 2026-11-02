namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// One person's decision on a change proposal as it crosses the wire: who, when,
/// and at which <c>devbook.chapter.fingerprint@1</c> of the change. The desktop's
/// <c>DevbookSubjectDecision</c>, field for field.
/// </summary>
/// <param name="By">Who decided, as the deciding machine's own label — a display
/// name, never an identity.</param>
/// <param name="At">When, on the device that decided.</param>
/// <param name="Fingerprint">The change's fingerprint at the moment of the
/// decision, <c>sha256:</c> and eight lowercase hex characters.</param>
public sealed record DevbookSubjectDecisionPayload(string By, DateTimeOffset At, string Fingerprint);

/// <summary>
/// The workflow state Backlog keeps for one devbook subject — a change
/// proposal, a decision record or a debt record — as it crosses the wire: what
/// the desktop's subject state store holds, field for field, and nothing the
/// replica has to understand.
/// <para>
/// The subject is named the way <c>devbook.decision@1</c> names it: its kind by
/// the contract's own word, and a key that is the change's folder name or the
/// record's repository path, never a folder on disk — the same reason
/// <see cref="AnnotationPayload"/> gives for a chapter path. The repository is
/// its registry alias, as for a remark.
/// </para>
/// <para>
/// The kind and the state travel as strings rather than as the Devbook module's
/// enum, because this project sees nothing of that module: the wire is the
/// published language, and <see cref="DevbookSubjectRecordLimits"/> holds the
/// contract's rows for the rule both ends run.
/// </para>
/// <para>
/// No owner and no device id, for the reason <see cref="TaskChange"/> gives:
/// both come from the caller's validated token.
/// </para>
/// </summary>
/// <param name="RepositoryAlias">Which repository the subject is in, as its
/// registry alias.</param>
/// <param name="Kind"><c>proposal</c>, <c>decision</c> or <c>debt</c>.</param>
/// <param name="Key">The change's folder name for a proposal; the record's
/// repository-relative, forward-slashed path for a decision or debt
/// record.</param>
/// <param name="PullRequest">The pull request a decision record is decided in;
/// absent for the other two kinds.</param>
/// <param name="State">The subject's state, one of its kind's row.</param>
/// <param name="By">Who set <paramref name="State"/>, as a display name.</param>
/// <param name="At">When it was set, on the device that set it.</param>
/// <param name="Approval">A proposal's approval, or absent.</param>
/// <param name="Acceptance">A proposal's acceptance, or absent.</param>
public sealed record DevbookSubjectPayload(
    string RepositoryAlias,
    string Kind,
    string Key,
    int? PullRequest,
    string State,
    string By,
    DateTimeOffset At,
    DevbookSubjectDecisionPayload? Approval = null,
    DevbookSubjectDecisionPayload? Acceptance = null);

/// <summary>
/// One subject's state at one moment, as a device pushes it or pulls it.
/// Whole-document last-write-wins, as <see cref="AnnotationChange"/> is, and
/// without its tombstone: a subject's state is set and never deleted, so there is
/// no absence to tell apart from "never seen".
/// <para>
/// <see cref="Id"/> is derived from the alias and the subject rather than drawn,
/// so two devices that each set a state on the same subject before either synced
/// write one document between them, not two.
/// </para>
/// </summary>
public sealed record DevbookSubjectChange(Guid Id, DateTimeOffset UpdatedAt, DevbookSubjectPayload Subject);

/// <summary>
/// A change as it comes back out of the replica: the change itself, the device
/// that wrote it, and the store's own ordering stamp — see
/// <see cref="TaskChangeRecord"/> for why both are on the way out and neither on
/// the way in.
/// </summary>
public sealed record DevbookSubjectChangeRecord(DevbookSubjectChange Change, Guid DeviceId, long ServerTimestamp);

/// <summary>A batch of changes from one device.</summary>
public sealed record PushDevbookSubjectsRequest(IReadOnlyList<DevbookSubjectChange> Subjects);

/// <summary>How many changes the replica took — short of the batch by every
/// change that was not a later version than the one it held.</summary>
public sealed record PushDevbookSubjectsResponse(int Accepted);

/// <summary>One page of the owner's subject state feed, and where to resume —
/// <see cref="PullTasksResponse"/>'s contract: <paramref name="Since"/> is always
/// fresh, and only <paramref name="HasMore"/> being false ends a client's
/// loop.</summary>
public sealed record PullDevbookSubjectsResponse(IReadOnlyList<DevbookSubjectChangeRecord> Subjects, string Since, bool HasMore);
