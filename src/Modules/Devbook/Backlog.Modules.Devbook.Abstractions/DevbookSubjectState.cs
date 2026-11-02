namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// One person's decision on a change proposal — who approved or accepted it,
/// when, and at which <c>devbook.chapter.fingerprint@1</c> of the change.
/// <para>
/// The fingerprint is what lets a decision lapse without anyone withdrawing it:
/// a change edited after it was approved has another fingerprint, and the
/// approval recorded here no longer covers what the change now says. Who and
/// when come from the host's own session, never from the caller.
/// </para>
/// </summary>
public sealed record DevbookSubjectDecision(string By, DateTimeOffset At, string Fingerprint);

/// <summary>
/// The workflow state Backlog keeps for one subject in one repository — the
/// store's record, and the document that replicates.
/// <para>
/// <see cref="Id"/> is <see cref="DevbookSubject.IdFor"/> of the alias and the
/// subject, so every device files the same subject under the same id.
/// <see cref="By"/> and <see cref="At"/> say who set the current
/// <see cref="State"/> and when. <see cref="Approval"/> and
/// <see cref="Acceptance"/> are a proposal's alone. <see cref="UpdatedAt"/> is
/// what replication compares: whole-document, later wins, as a remark replicates.
/// </para>
/// <para>
/// Never written into a repository and never part of the devbook database: the
/// branch states what is true of its code, and this is the state of the work
/// around it.
/// </para>
/// </summary>
public sealed record DevbookSubjectState(
    Guid Id,
    string RepositoryAlias,
    DevbookSubject Subject,
    string State,
    string By,
    DateTimeOffset At,
    DevbookSubjectDecision? Approval,
    DevbookSubjectDecision? Acceptance,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Why a state operation was refused, per <c>devbook.decision@1</c>. A refusal
/// writes nothing, and its caller reports it rather than retrying around it.
/// </summary>
public enum DevbookSubjectRefusal
{
    /// <summary>An open question on the change blocks its approval. Wire code
    /// <c>open-question</c>. Backlog keeps no note kinds yet, so it never raises
    /// this; it is here because the contract's refusals are a closed set.</summary>
    OpenQuestion,

    /// <summary>Accept with no approval to stand on. Wire code
    /// <c>not-approved</c>.</summary>
    NotApproved,

    /// <summary>Accept at a fingerprint other than the approval's. Wire code
    /// <c>fingerprint-mismatch</c>.</summary>
    FingerprintMismatch,

    /// <summary>A state outside the subject's row, approved or accepted through
    /// <c>set_state</c>, approve or accept on anything but a proposal, or a
    /// fingerprint that is not one. Wire code <c>bad-state</c>.</summary>
    BadState
}

/// <summary>
/// What a state operation came to: the state it leaves, or the refusal and why.
/// A result rather than an exception, because a caller asking for a state the
/// contract does not allow is an expected outcome the caller reports, not a
/// fault (guideline ADR 0004).
/// <para>
/// Two kinds of refusal, kept apart. <see cref="Refused"/> is one of the
/// contract's — the closed set every member of which has a wire code.
/// <see cref="RepositoryArchived"/> is the host's own: the repository is archived,
/// so its devbook is read-only (local ADR 0029 §4). It is no member of
/// <see cref="DevbookSubjectRefusal"/> because the contract has no such refusal,
/// and a caller mapping that enum to the contract's codes must never meet one it
/// cannot spell. Both make <see cref="IsRefused"/> true, so a caller that branches
/// on that alone reports either rather than taking it for a state.
/// </para>
/// </summary>
public sealed record DevbookSubjectOutcome(DevbookSubjectState? State, DevbookSubjectRefusal? Refused, string? Message)
{
    /// <summary>Refused because the repository is archived, not by the
    /// contract's rules; <see cref="Refused"/> is null then.</summary>
    public bool RepositoryArchived { get; private init; }

    public bool IsRefused => Refused is not null || RepositoryArchived;

    public static DevbookSubjectOutcome Done(DevbookSubjectState state) => new(state, null, null);

    public static DevbookSubjectOutcome Refuse(DevbookSubjectRefusal refusal, string message) => new(null, refusal, message);

    /// <summary>The repository is archived, and nothing was written.</summary>
    public static DevbookSubjectOutcome Archived(string message) => new(null, null, message) { RepositoryArchived = true };
}
